using System.Diagnostics;
using System.Globalization;
using Lua.Runtime.Objects;
using Lua.Runtime.Values;

namespace Lua.Runtime.Execution;

public sealed partial class LuaState
{
    private static LuaValue[] OsClock(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        return [LuaValue.FromFloat(Process.GetCurrentProcess().TotalProcessorTime.TotalSeconds)];
    }

    private static LuaValue[] OsTime(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        return [LuaValue.FromInteger(DateTimeOffset.UtcNow.ToUnixTimeSeconds())];
    }

    private static LuaValue[] OsDiffTime(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var t2Value = RequireArgument(arguments, 0, "os.difftime");
        var t1Value = RequireArgument(arguments, 1, "os.difftime");

        if (!LuaValueHelper.TryGetNumber(t2Value, out var t2) || !LuaValueHelper.TryGetNumber(t1Value, out var t1))
        {
            throw CreateArgumentTypeError("os.difftime", 1, "number", t2Value);
        }

        return [LuaValue.FromFloat(t2 - t1)];
    }

    private static LuaValue[] OsDate(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var format = arguments.Count >= 1 && arguments[0].Kind == LuaValueKind.String
            ? arguments[0].AsString()
            : "%c";

        var useUtc = format.StartsWith('!');
        if (useUtc)
        {
            format = format[1..];
        }

        var now = useUtc ? DateTime.UtcNow : DateTime.Now;

        if (format == "*t")
        {
            return [LuaValue.FromTable(CreateDateTable(now))];
        }

        return [LuaValue.FromString(FormatStrftime(format, now))];
    }

    private static LuaValue[] OsGetEnv(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var name = RequireArgument(arguments, 0, "os.getenv");
        if (name.Kind != LuaValueKind.String)
        {
            throw CreateArgumentTypeError("os.getenv", 1, "string", name);
        }

        var value = Environment.GetEnvironmentVariable(name.AsString());
        return value is not null ? [LuaValue.FromString(value)] : [LuaValue.Nil];
    }

    private static LuaValue[] OsRemove(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var filename = RequireStringArgument(arguments, 0, "os.remove");
        try
        {
            File.Delete(state.ResolveFilePath(filename));
            return [LuaValue.FromBoolean(true)];
        }
        catch (Exception ex)
        {
            return [LuaValue.Nil, LuaValue.FromString(ex.Message)];
        }
    }

    private static LuaValue[] OsRename(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var oldName = RequireStringArgument(arguments, 0, "os.rename");
        var newName = RequireStringArgument(arguments, 1, "os.rename");
        try
        {
            File.Move(state.ResolveFilePath(oldName), state.ResolveFilePath(newName));
            return [LuaValue.FromBoolean(true)];
        }
        catch (Exception ex)
        {
            return [LuaValue.Nil, LuaValue.FromString(ex.Message)];
        }
    }

    private static LuaValue[] OsExecute(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        if (arguments.Count == 0 || arguments[0].IsNil)
        {
            return [LuaValue.FromBoolean(true)];
        }

        var command = arguments[0].AsString();
        try
        {
            var isWindows = OperatingSystem.IsWindows();
            var psi = new ProcessStartInfo
            {
                FileName = isWindows ? "cmd.exe" : "/bin/sh",
                Arguments = isWindows ? $"/c {command}" : $"-c \"{command.Replace("\"", "\\\"")}\"",
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };

            using var process = Process.Start(psi);
            process?.WaitForExit();
            var exitCode = process?.ExitCode ?? -1;

            return exitCode == 0
                ? [LuaValue.FromBoolean(true), LuaValue.FromString("exit"), LuaValue.FromInteger(exitCode)]
                : [LuaValue.Nil, LuaValue.FromString("exit"), LuaValue.FromInteger(exitCode)];
        }
        catch (Exception ex)
        {
            return [LuaValue.Nil, LuaValue.FromString(ex.Message)];
        }
    }

    private static LuaValue[] OsTmpName(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        return [LuaValue.FromString(Path.GetTempFileName())];
    }

    private static LuaValue[] OsExit(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var code = 0;
        if (arguments.Count >= 1)
        {
            if (arguments[0].Kind == LuaValueKind.Boolean)
            {
                code = arguments[0].AsBoolean() ? 0 : 1;
            }
            else if (LuaValueHelper.TryGetInteger(arguments[0], out var intCode))
            {
                code = (int)intCode;
            }
        }

        Environment.Exit(code);
        return [];
    }

    private static LuaTable CreateDateTable(DateTime dt)
    {
        var table = new LuaTable("date");
        table.SetValue(LuaValue.FromString("year"), LuaValue.FromInteger(dt.Year));
        table.SetValue(LuaValue.FromString("month"), LuaValue.FromInteger(dt.Month));
        table.SetValue(LuaValue.FromString("day"), LuaValue.FromInteger(dt.Day));
        table.SetValue(LuaValue.FromString("hour"), LuaValue.FromInteger(dt.Hour));
        table.SetValue(LuaValue.FromString("min"), LuaValue.FromInteger(dt.Minute));
        table.SetValue(LuaValue.FromString("sec"), LuaValue.FromInteger(dt.Second));
        table.SetValue(LuaValue.FromString("wday"), LuaValue.FromInteger(((int)dt.DayOfWeek) + 1));
        table.SetValue(LuaValue.FromString("yday"), LuaValue.FromInteger(dt.DayOfYear));
        table.SetValue(LuaValue.FromString("isdst"), LuaValue.FromBoolean(dt.IsDaylightSavingTime()));
        return table;
    }

    private static string FormatStrftime(string format, DateTime dt)
    {
        var result = new System.Text.StringBuilder();
        for (var i = 0; i < format.Length; i++)
        {
            if (format[i] != '%' || i + 1 >= format.Length)
            {
                result.Append(format[i]);
                continue;
            }

            i++;
            result.Append(format[i] switch
            {
                'a' => dt.ToString("ddd", CultureInfo.InvariantCulture),
                'A' => dt.ToString("dddd", CultureInfo.InvariantCulture),
                'b' or 'h' => dt.ToString("MMM", CultureInfo.InvariantCulture),
                'B' => dt.ToString("MMMM", CultureInfo.InvariantCulture),
                'c' => dt.ToString("ddd MMM dd HH:mm:ss yyyy", CultureInfo.InvariantCulture),
                'd' => dt.Day.ToString("D2"),
                'H' => dt.Hour.ToString("D2"),
                'I' => (dt.Hour % 12 == 0 ? 12 : dt.Hour % 12).ToString("D2"),
                'j' => dt.DayOfYear.ToString("D3"),
                'm' => dt.Month.ToString("D2"),
                'M' => dt.Minute.ToString("D2"),
                'p' => dt.Hour < 12 ? "AM" : "PM",
                'S' => dt.Second.ToString("D2"),
                'w' => ((int)dt.DayOfWeek).ToString(),
                'x' => dt.ToString("MM/dd/yy", CultureInfo.InvariantCulture),
                'X' => dt.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                'y' => (dt.Year % 100).ToString("D2"),
                'Y' => dt.Year.ToString("D4"),
                'Z' => TimeZoneInfo.Local.StandardName,
                '%' => "%",
                var c => $"%{c}"
            });
        }

        return result.ToString();
    }

}
