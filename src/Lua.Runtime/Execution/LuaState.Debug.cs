using Lua.Runtime.Objects;
using Lua.Runtime.Values;
using static Lua.Runtime.Values.LuaValueHelper;

namespace Lua.Runtime.Execution;

public sealed partial class LuaState
{
    private static LuaValue[] DebugGetMetatable(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var value = RequireArgument(arguments, 0, "debug.getmetatable");
        return state.TryGetRawMetatable(value, out var metatable) && metatable is not null
            ? [LuaValue.FromTable(metatable)]
            : [LuaValue.Nil];
    }

    private static LuaValue[] DebugGetInfo(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var level = RequireArgument(arguments, 0, "debug.getinfo");
        var what = arguments.Count >= 2 && arguments[1].Kind == LuaValueKind.String
            ? arguments[1].AsString()
            : "flnStu";
        ValidateGetInfoOptions(level, what);

        LuaClosure? target = null;
        int frameIndex = -1;

        if (level.Kind == LuaValueKind.Function)
        {
            target = level.AsFunction();
        }
        else if (TryGetInteger(level, out var levelInt))
        {
            var frames = state.CurrentThread.Frames;
            var resolvedIndex = frames.Count - 1 - (int)levelInt;
            if (resolvedIndex >= 0 && resolvedIndex < frames.Count)
            {
                frameIndex = resolvedIndex;
                target = frames[resolvedIndex].Closure;
            }
            else
            {
                return [LuaValue.Nil];
            }
        }
        else
        {
            throw CreateArgumentTypeError("debug.getinfo", 1, "function or integer", level);
        }

        var info = new LuaTable("debug.getinfo");

        if (target is not null)
        {
            if (what.Contains('n'))
            {
                var (name, nameWhat) = frameIndex >= 0
                    ? ResolveFrameName(state.CurrentThread.Frames[frameIndex])
                    : ResolveFunctionName(target);
                info.SetValue(LuaValue.FromString("name"), name);
                info.SetValue(LuaValue.FromString("namewhat"), LuaValue.FromString(nameWhat));
            }

            if (what.Contains('S'))
            {
                var isMain = target.LineDefined == 0;
                var source = GetClosureSource(target);
                info.SetValue(LuaValue.FromString("source"), LuaValue.FromString("=?"));
                info.SetValue(LuaValue.FromString("source"), LuaValue.FromString(source));
                info.SetValue(LuaValue.FromString("short_src"), LuaValue.FromString(GetShortSource(source)));
                info.SetValue(LuaValue.FromString("what"),
                    target.Body is LuaNativeClosureBody
                        ? LuaValue.FromString("C")
                        : isMain
                            ? LuaValue.FromString("main")
                            : LuaValue.FromString("Lua"));
                info.SetValue(LuaValue.FromString("linedefined"), LuaValue.FromInteger(target.Body is LuaNativeClosureBody ? -1 : target.LineDefined));
                info.SetValue(LuaValue.FromString("lastlinedefined"), LuaValue.FromInteger(target.Body is LuaNativeClosureBody ? -1 : target.LastLineDefined));
            }

            if (what.Contains('l') && frameIndex >= 0)
            {
                var currentLine = ResolveCurrentLine(state.CurrentThread.Frames[frameIndex]);
                info.SetValue(LuaValue.FromString("currentline"), LuaValue.FromInteger(currentLine));
            }

            if (what.Contains('f'))
            {
                info.SetValue(LuaValue.FromString("func"), LuaValue.FromFunction(target));
            }

            if (what.Contains('L') && target.Body is not LuaNativeClosureBody)
            {
                info.SetValue(LuaValue.FromString("activelines"), LuaValue.FromTable(CreateActiveLinesTable(target)));
            }

            if (what.Contains('u'))
            {
                info.SetValue(LuaValue.FromString("nups"), LuaValue.FromInteger(target.UpvalueCount));
                info.SetValue(LuaValue.FromString("nparams"), LuaValue.FromInteger(target.ParameterCount));
                info.SetValue(LuaValue.FromString("isvararg"), LuaValue.FromBoolean(target.IsVarArg));
            }
        }

        return [LuaValue.FromTable(info)];
    }

    private static int ResolveCurrentLine(CallFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var programCounter = Math.Max(0, frame.ProgramCounter - 1);
        return frame.Closure.ResolveLine(programCounter);
    }

    private static (LuaValue Name, string NameWhat) ResolveFrameName(CallFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (string.IsNullOrEmpty(frame.InvocationName) && string.IsNullOrEmpty(frame.InvocationNameWhat))
        {
            return (LuaValue.Nil, string.Empty);
        }

        return string.IsNullOrEmpty(frame.InvocationName)
            ? (LuaValue.Nil, frame.InvocationNameWhat)
            : (LuaValue.FromString(frame.InvocationName), frame.InvocationNameWhat);
    }

    private static (LuaValue Name, string NameWhat) ResolveFunctionName(LuaClosure closure)
    {
        ArgumentNullException.ThrowIfNull(closure);

        return string.IsNullOrEmpty(closure.DebugName)
            ? (LuaValue.Nil, string.Empty)
            : (LuaValue.FromString(closure.DebugName), string.Empty);
    }

    private static void ValidateGetInfoOptions(LuaValue level, string what)
    {
        foreach (var option in what)
        {
            if (option is not ('>' or 'S' or 'l' or 'u' or 'f' or 'L' or 'n' or 'r' or 't'))
            {
                throw CreateArgumentError("debug.getinfo", 2, "invalid option");
            }
        }

        if (what.Contains('>') && level.Kind != LuaValueKind.Function)
        {
            throw CreateArgumentError("debug.getinfo", 2, "invalid option");
        }
    }

    private static string GetClosureSource(LuaClosure closure)
    {
        if (closure.Body is LuaNativeClosureBody)
        {
            return "=[C]";
        }

        return closure.SourceName is null ? "=?" : closure.SourceName;
    }

    private static string GetShortSource(string source)
    {
        if (source.Length == 0)
        {
            return "[string \"\"]";
        }

        if (source == "?")
        {
            return "?";
        }

        if (source == "=[C]")
        {
            return "[C]";
        }

        if (source[0] == '=')
        {
            return source[1..];
        }

        if (source[0] == '@')
        {
            const int maxLength = 60;
            var fileName = source[1..];
            return fileName.Length <= maxLength
                ? fileName
                : "..." + fileName[(fileName.Length - (maxLength - 3))..];
        }

        const int maxStringLength = 60;
        var newlineIndex = source.IndexOf('\n');
        var visible = newlineIndex >= 0 ? source[..newlineIndex] : source;
        if (visible.Length > maxStringLength)
        {
            visible = visible[..maxStringLength];
        }

        var truncated = newlineIndex >= 0 || visible.Length < source.Length;
        if (string.IsNullOrEmpty(visible))
        {
            visible = "...";
            truncated = false;
        }
        else if (truncated)
        {
            visible += "...";
        }

        return $"[string \"{visible}\"]";
    }

    private static LuaTable CreateActiveLinesTable(LuaClosure closure)
    {
        var activeLines = new LuaTable("debug.getinfo.activelines");
        for (var programCounter = 0; programCounter < closure.InstructionCount; programCounter++)
        {
            var line = closure.ResolveLine(programCounter);
            if (line > 0)
            {
                activeLines.SetValue(LuaValue.FromInteger(line), LuaValue.FromBoolean(true));
            }
        }

        if (closure.LineDefined > 0)
        {
            activeLines.SetValue(LuaValue.FromInteger(closure.LineDefined), LuaValue.Nil);
        }

        if (closure.LastLineDefined > closure.LineDefined)
        {
            activeLines.SetValue(LuaValue.FromInteger(closure.LastLineDefined), LuaValue.FromBoolean(true));
        }

        return activeLines;
    }

    private static LuaValue[] DebugTraceback(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var message = arguments.Count >= 1 && !arguments[0].IsNil ? arguments[0] : LuaValue.Nil;
        if (!message.IsNil && message.Kind != LuaValueKind.String)
        {
            return [message];
        }

        var level = arguments.Count >= 2 && TryGetInteger(arguments[1], out var levelInt) ? (int)levelInt : 1;

        var sb = new System.Text.StringBuilder();
        if (!message.IsNil)
        {
            sb.AppendLine(message.Kind == LuaValueKind.String ? message.AsString() : FormatLuaValue(message));
        }

        sb.Append("stack traceback:");

        var frames = state.CurrentThread.Frames;
        var startIndex = level == 0
            ? frames.Count - 1
            : frames.Count - 1 - level;
        for (var i = startIndex; i >= 0 && i < frames.Count; i--)
        {
            var frame = frames[i];
            var name = frame.Closure.DebugName ?? "?";
            sb.Append($"\n\t[C]: in function '{name}'");
        }

        return [LuaValue.FromString(sb.ToString())];
    }

    private static LuaValue[] DebugSetMetatable(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var value = RequireArgument(arguments, 0, "debug.setmetatable");
        var metatableValue = RequireArgument(arguments, 1, "debug.setmetatable");

        if (metatableValue.IsNil)
        {
            state.SetRawMetatable(value, metatable: null);
            return [value];
        }

        if (metatableValue.Kind != LuaValueKind.Table)
        {
            throw CreateArgumentTypeError("debug.setmetatable", 2, "nil or table", metatableValue);
        }

        state.SetRawMetatable(value, metatableValue.AsTable());
        return [value];
    }

    private static LuaValue[] DebugGetLocal(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var level = RequireArgument(arguments, 0, "debug.getlocal");
        if (!TryGetInteger(level, out var levelInt))
        {
            throw CreateArgumentTypeError("debug.getlocal", 1, "integer", level);
        }

        var localIndex = RequireArgument(arguments, 1, "debug.getlocal");
        if (!TryGetInteger(localIndex, out var localInt))
        {
            throw CreateArgumentTypeError("debug.getlocal", 2, "integer", localIndex);
        }

        var frames = state.CurrentThread.Frames;
        var frameIndex = frames.Count - 1 - (int)levelInt;
        if (frameIndex < 0 || frameIndex >= frames.Count)
        {
            return [LuaValue.Nil];
        }

        var frame = frames[frameIndex];
        var registerIndex = (int)localInt - 1;
        if (registerIndex < 0)
        {
            return [LuaValue.Nil];
        }

        var absoluteIndex = frame.BaseIndex + registerIndex;
        if (absoluteIndex >= state.Stack.Count)
        {
            return [LuaValue.Nil];
        }

        var localName = $"(local {localInt})";
        return [LuaValue.FromString(localName), state.Stack[absoluteIndex]];
    }

    private static LuaValue[] DebugSetLocal(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var level = RequireArgument(arguments, 0, "debug.setlocal");
        if (!TryGetInteger(level, out var levelInt))
        {
            throw CreateArgumentTypeError("debug.setlocal", 1, "integer", level);
        }

        var localIndex = RequireArgument(arguments, 1, "debug.setlocal");
        if (!TryGetInteger(localIndex, out var localInt))
        {
            throw CreateArgumentTypeError("debug.setlocal", 2, "integer", localIndex);
        }

        var value = arguments.Count >= 3 ? arguments[2] : LuaValue.Nil;

        var frames = state.CurrentThread.Frames;
        var frameIndex = frames.Count - 1 - (int)levelInt;
        if (frameIndex < 0 || frameIndex >= frames.Count)
        {
            return [LuaValue.Nil];
        }

        var frame = frames[frameIndex];
        var registerIndex = (int)localInt - 1;
        var absoluteIndex = frame.BaseIndex + registerIndex;
        if (registerIndex < 0 || absoluteIndex >= state.Stack.Count)
        {
            return [LuaValue.Nil];
        }

        state.Stack[absoluteIndex] = value;
        return [LuaValue.FromString($"(local {localInt})")];
    }

    private static LuaValue[] DebugGetUpvalue(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var funcValue = RequireArgument(arguments, 0, "debug.getupvalue");
        if (funcValue.Kind != LuaValueKind.Function)
        {
            throw CreateArgumentTypeError("debug.getupvalue", 1, "function", funcValue);
        }

        var upIndex = RequireArgument(arguments, 1, "debug.getupvalue");
        if (!TryGetInteger(upIndex, out var upInt))
        {
            throw CreateArgumentTypeError("debug.getupvalue", 2, "integer", upIndex);
        }

        var func = funcValue.AsFunction();
        var index = (int)upInt - 1;
        if (index < 0 || index >= func.Upvalues.Length)
        {
            return [LuaValue.Nil];
        }

        var name = GetUpvalueName(func, index) ?? $"(upvalue {upInt})";
        return [LuaValue.FromString(name), func.Upvalues[index].GetValue(state)];
    }

    private static LuaValue[] DebugSetUpvalue(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var funcValue = RequireArgument(arguments, 0, "debug.setupvalue");
        if (funcValue.Kind != LuaValueKind.Function)
        {
            throw CreateArgumentTypeError("debug.setupvalue", 1, "function", funcValue);
        }

        var upIndex = RequireArgument(arguments, 1, "debug.setupvalue");
        if (!TryGetInteger(upIndex, out var upInt))
        {
            throw CreateArgumentTypeError("debug.setupvalue", 2, "integer", upIndex);
        }

        var value = arguments.Count >= 3 ? arguments[2] : LuaValue.Nil;

        var func = funcValue.AsFunction();
        var index = (int)upInt - 1;
        if (index < 0 || index >= func.Upvalues.Length)
        {
            return [LuaValue.Nil];
        }

        func.Upvalues[index].SetValue(state, value);
        return [LuaValue.FromString(GetUpvalueName(func, index) ?? $"(upvalue {upInt})")];
    }

    private static string? GetUpvalueName(LuaClosure closure, int index)
    {
        if (closure.UpvalueNames is null || index >= closure.UpvalueNames.Length)
        {
            return null;
        }

        return string.IsNullOrEmpty(closure.UpvalueNames[index])
            ? null
            : closure.UpvalueNames[index];
    }

    private static LuaValue[] DebugSetHook(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var argumentIndex = 0;
        var targetThread = ResolveHookThread(state, arguments, ref argumentIndex);
        if (argumentIndex >= arguments.Count || arguments[argumentIndex].IsNil)
        {
            targetThread.ClearHook();
            return [];
        }

        var hookValue = arguments[argumentIndex];
        if (hookValue.Kind != LuaValueKind.Function)
        {
            throw CreateArgumentTypeError("debug.sethook", argumentIndex + 1, "function", hookValue);
        }

        var mask = GetHookMask(arguments, argumentIndex + 1);
        var count = GetHookCount(arguments, argumentIndex + 2);
        targetThread.SetHook(hookValue.AsFunction(), mask, count);
        if (mask.Contains('l') && ReferenceEquals(targetThread, state.CurrentThread) && targetThread.Frames.Count >= 2)
        {
            var callerFrame = targetThread.Frames[^2];
            callerFrame.SetLastLineHookLine(ResolveCurrentLine(callerFrame));
        }

        return [];
    }

    private static LuaValue[] DebugGetHook(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var argumentIndex = 0;
        var targetThread = ResolveHookThread(state, arguments, ref argumentIndex);
        if (argumentIndex < arguments.Count && arguments[argumentIndex].Kind != LuaValueKind.Thread)
        {
            throw CreateArgumentTypeError("debug.gethook", argumentIndex + 1, "thread", arguments[argumentIndex]);
        }

        return targetThread.HookFunction is null
            ? [LuaValue.Nil]
            : [LuaValue.FromFunction(targetThread.HookFunction), LuaValue.FromString(targetThread.HookMask), LuaValue.FromInteger(targetThread.HookCount)];
    }

    private static LuaThread ResolveHookThread(LuaState state, IReadOnlyList<LuaValue> arguments, ref int argumentIndex)
    {
        if (argumentIndex < arguments.Count && arguments[argumentIndex].Kind == LuaValueKind.Thread)
        {
            return arguments[argumentIndex++].AsThread();
        }

        return state.CurrentThread;
    }

    private static string GetHookMask(IReadOnlyList<LuaValue> arguments, int index)
    {
        if (index >= arguments.Count || arguments[index].IsNil)
        {
            return string.Empty;
        }

        if (arguments[index].Kind != LuaValueKind.String)
        {
            throw CreateArgumentTypeError("debug.sethook", index + 1, "string", arguments[index]);
        }

        var mask = arguments[index].AsString();
        foreach (var option in mask)
        {
            if (option is not ('c' or 'r' or 'l'))
            {
                throw CreateArgumentError("debug.sethook", index + 1, "invalid hook mask");
            }
        }

        return mask;
    }

    private static int GetHookCount(IReadOnlyList<LuaValue> arguments, int index)
    {
        if (index >= arguments.Count || arguments[index].IsNil)
        {
            return 0;
        }

        if (!TryGetInteger(arguments[index], out var count))
        {
            throw CreateArgumentTypeError("debug.sethook", index + 1, "integer", arguments[index]);
        }

        if (count < 0 || count > int.MaxValue)
        {
            throw CreateArgumentError("debug.sethook", index + 1, "count out of range");
        }

        return (int)count;
    }

    private static LuaValue[] DebugGetUserValue(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var ud = RequireArgument(arguments, 0, "debug.getuservalue");
        var slot = 1;
        if (arguments.Count >= 2 && !arguments[1].IsNil)
        {
            if (!TryGetInteger(arguments[1], out var slotInt))
            {
                throw CreateArgumentTypeError("debug.getuservalue", 2, "integer", arguments[1]);
            }

            if (slotInt < int.MinValue || slotInt > int.MaxValue)
            {
                return [LuaValue.Nil];
            }

            slot = (int)slotInt;
        }

        if (ud.Kind != LuaValueKind.UserData)
        {
            return [LuaValue.Nil];
        }

        var userdata = ud.AsUserData();
        return userdata.TryGetUserValue(slot, out var value)
            ? [value, LuaValue.FromBoolean(true)]
            : [LuaValue.Nil];
    }

    private static LuaValue[] DebugSetUserValue(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var ud = RequireArgument(arguments, 0, "debug.setuservalue");
        var slot = 1;
        if (arguments.Count >= 3 && !arguments[2].IsNil)
        {
            if (!TryGetInteger(arguments[2], out var slotInt))
            {
                throw CreateArgumentTypeError("debug.setuservalue", 3, "integer", arguments[2]);
            }

            if (slotInt < int.MinValue || slotInt > int.MaxValue)
            {
                return [LuaValue.Nil];
            }

            slot = (int)slotInt;
        }

        if (ud.Kind != LuaValueKind.UserData)
        {
            throw CreateArgumentTypeError("debug.setuservalue", 1, "userdata", ud);
        }

        var value = RequireArgument(arguments, 1, "debug.setuservalue");
        var userdata = ud.AsUserData();
        return userdata.TrySetUserValue(slot, value)
            ? [ud]
            : [LuaValue.Nil];
    }

    private static LuaValue[] DebugUpvalueId(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var funcValue = RequireArgument(arguments, 0, "debug.upvalueid");
        if (funcValue.Kind != LuaValueKind.Function)
        {
            throw CreateArgumentTypeError("debug.upvalueid", 1, "function", funcValue);
        }

        var upIndex = RequireArgument(arguments, 1, "debug.upvalueid");
        if (!TryGetInteger(upIndex, out var upInt))
        {
            throw CreateArgumentTypeError("debug.upvalueid", 2, "integer", upIndex);
        }

        var func = funcValue.AsFunction();
        var index = (int)upInt - 1;
        if (index < 0 || index >= func.Upvalues.Length)
        {
            throw CreateRuntimeError("invalid upvalue index");
        }

        return [LuaValue.FromUserData(new LuaUserData(func.Upvalues[index], userValueCount: 0))];
    }

    private static LuaValue[] DebugUpvalueJoin(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var f1 = RequireArgument(arguments, 0, "debug.upvaluejoin").AsFunction();
        var n1 = (int)RequireArgument(arguments, 1, "debug.upvaluejoin").AsInteger() - 1;
        var f2 = RequireArgument(arguments, 2, "debug.upvaluejoin").AsFunction();
        var n2 = (int)RequireArgument(arguments, 3, "debug.upvaluejoin").AsInteger() - 1;

        if (n1 < 0 || n1 >= f1.Upvalues.Length || n2 < 0 || n2 >= f2.Upvalues.Length)
        {
            throw CreateRuntimeError("invalid upvalue index");
        }

        f1.Upvalues[n1] = f2.Upvalues[n2];
        return [];
    }
}
