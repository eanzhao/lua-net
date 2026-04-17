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
                info.SetValue(LuaValue.FromString("name"), target.DebugName is not null
                    ? LuaValue.FromString(target.DebugName)
                    : LuaValue.Nil);
                info.SetValue(LuaValue.FromString("namewhat"), LuaValue.FromString(""));
            }

            if (what.Contains('S'))
            {
                var isMain = target.DebugName == "main";
                info.SetValue(LuaValue.FromString("source"), LuaValue.FromString("=?"));
                info.SetValue(LuaValue.FromString("short_src"), LuaValue.FromString("?"));
                info.SetValue(LuaValue.FromString("what"),
                    target.Body is LuaNativeClosureBody
                        ? LuaValue.FromString("C")
                        : isMain
                            ? LuaValue.FromString("main")
                            : LuaValue.FromString("Lua"));
            }

            if (what.Contains('l') && frameIndex >= 0)
            {
                var currentLine = ResolveCurrentLine(state.CurrentThread.Frames[frameIndex]);
                info.SetValue(LuaValue.FromString("currentline"), LuaValue.FromInteger(currentLine));
            }

            if (what.Contains('u'))
            {
                info.SetValue(LuaValue.FromString("nups"), LuaValue.FromInteger(target.UpvalueCount));
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
