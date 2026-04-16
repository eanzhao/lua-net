using System.Text;
using Lua.Runtime.Objects;
using Lua.Runtime.Values;
using static Lua.Runtime.Values.LuaValueHelper;

namespace Lua.Runtime.Execution;

public sealed partial class LuaState
{
    private static readonly UTF8Encoding Utf8FileEncoding = new(encoderShouldEmitUTF8Identifier: false);
    private LuaTable? _ioDefaultInput;
    private LuaTable? _ioDefaultOutput;

    private LuaTable CreateFileHandle(StreamReader? reader, StreamWriter? writer, string name)
    {
        var handle = new LuaTable(name);
        var metatable = new LuaTable("file_metatable");

        handle.SetValue(LuaValue.FromString("__reader"), reader is not null
            ? LuaValue.FromUserData(new LuaUserData(reader, userValueCount: 0))
            : LuaValue.Nil);
        handle.SetValue(LuaValue.FromString("__writer"), writer is not null
            ? LuaValue.FromUserData(new LuaUserData(writer, userValueCount: 0))
            : LuaValue.Nil);
        handle.SetValue(LuaValue.FromString("__closed"), LuaValue.FromBoolean(false));

        RegisterLibraryFunction(metatable, "read", FileRead, "file:read");
        RegisterLibraryFunction(metatable, "write", FileWrite, "file:write");
        RegisterLibraryFunction(metatable, "close", FileClose, "file:close");
        RegisterLibraryFunction(metatable, "flush", FileFlush, "file:flush");
        RegisterLibraryFunction(metatable, "lines", FileLines, "file:lines");
        metatable.SetValue(LuaValue.FromString("__index"), LuaValue.FromTable(metatable));
        metatable.SetValue(LuaValue.FromString("__tostring"), LuaValue.FromFunction(
            new LuaClosure("file:__tostring", body: new LuaNativeClosureBody(static (_, _, args) =>
            {
                var h = args[0].AsTable();
                var closed = h.GetValue(LuaValue.FromString("__closed"));
                return [LuaValue.FromString(closed.Kind == LuaValueKind.Boolean && closed.AsBoolean()
                    ? "file (closed)" : "file")];
            }))));

        handle.SetMetatable(metatable);
        return handle;
    }

    private LuaValue[] IoOpen(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var filename = RequireStringArgument(arguments, 0, "io.open");
        var resolvedFilename = state.ResolveFilePath(filename);
        var mode = arguments.Count >= 2 && arguments[1].Kind == LuaValueKind.String
            ? arguments[1].AsString()
            : "r";

        try
        {
            StreamReader? reader = null;
            StreamWriter? writer = null;

            switch (mode)
            {
                case "r":
                    reader = new StreamReader(resolvedFilename, Utf8FileEncoding);
                    break;
                case "w":
                    writer = new StreamWriter(resolvedFilename, false, Utf8FileEncoding);
                    break;
                case "a":
                    writer = new StreamWriter(resolvedFilename, true, Utf8FileEncoding);
                    break;
                case "r+":
                    var rStream = new FileStream(resolvedFilename, FileMode.Open, FileAccess.ReadWrite);
                    reader = new StreamReader(rStream, Utf8FileEncoding, leaveOpen: true);
                    writer = new StreamWriter(rStream, Utf8FileEncoding, leaveOpen: true);
                    break;
                case "w+":
                    var wStream = new FileStream(resolvedFilename, FileMode.Create, FileAccess.ReadWrite);
                    reader = new StreamReader(wStream, Utf8FileEncoding, leaveOpen: true);
                    writer = new StreamWriter(wStream, Utf8FileEncoding, leaveOpen: true);
                    break;
                case "a+":
                    var aStream = new FileStream(resolvedFilename, FileMode.Append, FileAccess.ReadWrite);
                    reader = new StreamReader(aStream, Utf8FileEncoding, leaveOpen: true);
                    writer = new StreamWriter(aStream, Utf8FileEncoding, leaveOpen: true);
                    break;
                default:
                    return [LuaValue.Nil, LuaValue.FromString($"invalid mode '{mode}'")];
            }

            return [LuaValue.FromTable(CreateFileHandle(reader, writer, filename))];
        }
        catch (Exception ex)
        {
            return [LuaValue.Nil, LuaValue.FromString(ex.Message)];
        }
    }

    private static LuaValue[] IoClose(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        if (arguments.Count == 0 || arguments[0].IsNil)
        {
            return [LuaValue.FromBoolean(true)];
        }

        return FileClose(state, closure, arguments);
    }

    private static LuaValue[] IoType(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var value = RequireArgument(arguments, 0, "io.type");

        if (value.Kind != LuaValueKind.Table)
        {
            return [LuaValue.FromBoolean(false)];
        }

        var table = value.AsTable();
        var readerField = table.GetValue(LuaValue.FromString("__reader"));
        var writerField = table.GetValue(LuaValue.FromString("__writer"));

        if (readerField.IsNil && writerField.IsNil)
        {
            return [LuaValue.FromBoolean(false)];
        }

        var closed = table.GetValue(LuaValue.FromString("__closed"));
        if (closed.Kind == LuaValueKind.Boolean && closed.AsBoolean())
        {
            return [LuaValue.FromString("closed file")];
        }

        return [LuaValue.FromString("file")];
    }

    private LuaValue[] IoInput(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        if (arguments.Count >= 1 && !arguments[0].IsNil)
        {
            if (arguments[0].Kind == LuaValueKind.String)
            {
                var result = IoOpen(state, closure, arguments);
                if (result[0].IsNil)
                {
                    throw new LuaRuntimeException(result[1]);
                }

                _ioDefaultInput = result[0].AsTable();
            }
            else if (arguments[0].Kind == LuaValueKind.Table)
            {
                _ioDefaultInput = arguments[0].AsTable();
            }
        }

        _ioDefaultInput ??= CreateFileHandle(
            new StreamReader(Console.OpenStandardInput(), Utf8FileEncoding), null, "stdin");

        return [LuaValue.FromTable(_ioDefaultInput)];
    }

    private LuaValue[] IoOutput(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        if (arguments.Count >= 1 && !arguments[0].IsNil)
        {
            if (arguments[0].Kind == LuaValueKind.String)
            {
                var result = IoOpen(state, closure,
                    [arguments[0], LuaValue.FromString("w")]);
                if (result[0].IsNil)
                {
                    throw new LuaRuntimeException(result[1]);
                }

                _ioDefaultOutput = result[0].AsTable();
            }
            else if (arguments[0].Kind == LuaValueKind.Table)
            {
                _ioDefaultOutput = arguments[0].AsTable();
            }
        }

        _ioDefaultOutput ??= CreateFileHandle(
            null, new StreamWriter(Console.OpenStandardOutput(), Utf8FileEncoding) { AutoFlush = true }, "stdout");

        return [LuaValue.FromTable(_ioDefaultOutput)];
    }

    private LuaValue[] IoRead(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var input = IoInput(state, closure, []);
        return FileRead(state, closure, [input[0], ..arguments]);
    }

    private LuaValue[] IoWrite(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var output = IoOutput(state, closure, []);
        return FileWrite(state, closure, [output[0], ..arguments]);
    }

    private LuaValue[] IoFlush(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var output = IoOutput(state, closure, []);
        return FileFlush(state, closure, [output[0]]);
    }

    private LuaValue[] IoLines(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        if (arguments.Count >= 1 && arguments[0].Kind == LuaValueKind.String)
        {
            var result = IoOpen(state, closure, [arguments[0], LuaValue.FromString("r")]);
            if (result[0].IsNil)
            {
                throw new LuaRuntimeException(result[1]);
            }

            return FileLines(state, closure, [result[0]]);
        }

        var input = IoInput(state, closure, []);
        return FileLines(state, closure, [input[0]]);
    }

    private static LuaValue[] FileRead(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var handle = arguments[0].AsTable();
        EnsureFileOpen(handle);

        var readerValue = handle.GetValue(LuaValue.FromString("__reader"));
        if (readerValue.IsNil)
        {
            throw CreateRuntimeError("file is not readable");
        }

        var reader = (StreamReader)readerValue.AsUserData().Value!;
        var format = arguments.Count >= 2 ? arguments[1] : LuaValue.FromString("l");

        if (format.Kind == LuaValueKind.String)
        {
            var fmt = format.AsString();
            return fmt switch
            {
                "l" or "*l" => ReadLine(reader, false),
                "L" or "*L" => ReadLine(reader, true),
                "a" or "*a" => ReadAll(reader),
                "n" or "*n" => ReadNumber(reader),
                _ => [LuaValue.Nil]
            };
        }

        if (TryGetInteger(format, out var count))
        {
            return ReadBytes(reader, (int)count);
        }

        return [LuaValue.Nil];
    }

    private static LuaValue[] FileWrite(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var handle = arguments[0].AsTable();
        EnsureFileOpen(handle);

        var writerValue = handle.GetValue(LuaValue.FromString("__writer"));
        if (writerValue.IsNil)
        {
            throw CreateRuntimeError("file is not writable");
        }

        var writer = (StreamWriter)writerValue.AsUserData().Value!;

        for (var i = 1; i < arguments.Count; i++)
        {
            var arg = arguments[i];
            if (arg.Kind == LuaValueKind.String)
            {
                writer.Write(arg.AsString());
            }
            else if (arg.Kind == LuaValueKind.Integer)
            {
                writer.Write(arg.AsInteger().ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else if (arg.Kind == LuaValueKind.Float)
            {
                writer.Write(arg.AsFloat().ToString("G17", System.Globalization.CultureInfo.InvariantCulture));
            }
            else
            {
                throw CreateArgumentTypeError("file:write", i + 1, "string or number", arg);
            }
        }

        return [arguments[0]];
    }

    private static LuaValue[] FileClose(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var handle = arguments[0].AsTable();
        var readerValue = handle.GetValue(LuaValue.FromString("__reader"));
        var writerValue = handle.GetValue(LuaValue.FromString("__writer"));

        try
        {
            if (!readerValue.IsNil && readerValue.AsUserData().Value is StreamReader reader)
            {
                reader.Dispose();
            }

            if (!writerValue.IsNil && writerValue.AsUserData().Value is StreamWriter writer)
            {
                writer.Dispose();
            }

            handle.SetValue(LuaValue.FromString("__closed"), LuaValue.FromBoolean(true));
            return [LuaValue.FromBoolean(true)];
        }
        catch (Exception ex)
        {
            return [LuaValue.Nil, LuaValue.FromString(ex.Message)];
        }
    }

    private static LuaValue[] FileFlush(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var handle = arguments[0].AsTable();
        EnsureFileOpen(handle);

        var writerValue = handle.GetValue(LuaValue.FromString("__writer"));
        if (!writerValue.IsNil && writerValue.AsUserData().Value is StreamWriter writer)
        {
            writer.Flush();
        }

        return [arguments[0]];
    }

    private static LuaValue[] FileLines(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var handle = arguments[0].AsTable();
        var iterator = new LuaClosure(
            "file:lines iterator",
            body: new LuaNativeClosureBody((s, c, args) =>
            {
                var readerValue = handle.GetValue(LuaValue.FromString("__reader"));
                if (readerValue.IsNil)
                {
                    return [LuaValue.Nil];
                }

                var reader = (StreamReader)readerValue.AsUserData().Value!;
                var line = reader.ReadLine();
                return line is not null ? [LuaValue.FromString(line)] : [LuaValue.Nil];
            }));

        return [LuaValue.FromFunction(iterator)];
    }

    private static void EnsureFileOpen(LuaTable handle)
    {
        var closed = handle.GetValue(LuaValue.FromString("__closed"));
        if (closed.Kind == LuaValueKind.Boolean && closed.AsBoolean())
        {
            throw CreateRuntimeError("attempt to use a closed file");
        }
    }

    private static LuaValue[] ReadLine(StreamReader reader, bool keepNewline)
    {
        var line = reader.ReadLine();
        if (line is null)
        {
            return [LuaValue.Nil];
        }

        return [LuaValue.FromString(keepNewline ? line + "\n" : line)];
    }

    private static LuaValue[] ReadAll(StreamReader reader)
    {
        return [LuaValue.FromString(reader.ReadToEnd())];
    }

    private static LuaValue[] ReadNumber(StreamReader reader)
    {
        var sb = new StringBuilder();
        while (reader.Peek() >= 0)
        {
            var c = (char)reader.Peek();
            if (char.IsWhiteSpace(c) && sb.Length == 0)
            {
                reader.Read();
                continue;
            }

            if (char.IsDigit(c) || c is '.' or '-' or '+' or 'e' or 'E' or 'x' or 'X'
                || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))
            {
                sb.Append((char)reader.Read());
                continue;
            }

            break;
        }

        if (sb.Length == 0)
        {
            return [LuaValue.Nil];
        }

        if (long.TryParse(sb.ToString(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var intResult))
        {
            return [LuaValue.FromInteger(intResult)];
        }

        if (double.TryParse(sb.ToString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var floatResult))
        {
            return [LuaValue.FromFloat(floatResult)];
        }

        return [LuaValue.Nil];
    }

    private static LuaValue[] ReadBytes(StreamReader reader, int count)
    {
        if (count == 0)
        {
            return reader.EndOfStream ? [LuaValue.Nil] : [LuaValue.FromString("")];
        }

        var buffer = new char[count];
        var read = reader.Read(buffer, 0, count);
        return read > 0 ? [LuaValue.FromString(new string(buffer, 0, read))] : [LuaValue.Nil];
    }
}
