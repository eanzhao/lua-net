using Lua.Runtime.Objects;
using Lua.Runtime.Values;

namespace Lua.Runtime.Execution;

public sealed partial class LuaState
{
    private static LuaValue[] CoroutineCreate(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var function = RequireArgument(arguments, 0, "coroutine.create");
        if (function.Kind != LuaValueKind.Function)
        {
            throw CreateArgumentTypeError("coroutine.create", 1, "function", function);
        }

        var thread = new LuaThread(function.AsFunction().DebugName);
        thread.SetEntryClosure(function.AsFunction());
        return [LuaValue.FromThread(thread)];
    }

    private static LuaValue[] CoroutineResume(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var thread = RequireCoroutineThread(arguments, 0, "coroutine.resume");
        if (state._coroutineResumer is null)
        {
            throw CreateRuntimeError("coroutine resumer is not configured");
        }

        var resumeArguments = arguments.Count > 1 ? arguments.Skip(1).ToArray() : Array.Empty<LuaValue>();
        return state._coroutineResumer(thread, resumeArguments);
    }

    private static LuaValue[] CoroutineYield(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var thread = state.CurrentThread;
        if (!state.IsYieldable(thread))
        {
            throw CreateRuntimeError("attempt to yield across a C-call boundary");
        }

        if (thread.IsMainThread)
        {
            throw CreateRuntimeError("attempt to yield from outside a coroutine");
        }

        throw new LuaYieldException(thread, arguments);
    }

    private static LuaValue[] CoroutineWrap(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var created = CoroutineCreate(state, closure, arguments);
        var thread = created[0].AsThread();
        var wrapped = new LuaClosure(
            "coroutine.wrap",
            body: new LuaNativeClosureBody((innerState, _, wrapArguments) =>
            {
                if (innerState._coroutineResumer is null)
                {
                    throw CreateRuntimeError("coroutine resumer is not configured");
                }

                var results = innerState._coroutineResumer(thread, wrapArguments);
                var success = results.Length != 0 && results[0].Kind == LuaValueKind.Boolean && results[0].AsBoolean();
                if (success)
                {
                    return results.Skip(1).ToArray();
                }

                if (innerState._coroutineCloser is not null)
                {
                    innerState._coroutineCloser(thread);
                }

                var errorObject = results.Length > 1 ? results[1] : LuaValue.Nil;
                throw new LuaRuntimeException(errorObject);
            }));

        return [LuaValue.FromFunction(wrapped)];
    }

    private static LuaValue[] CoroutineStatus(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var thread = RequireCoroutineThread(arguments, 0, "coroutine.status");
        return [LuaValue.FromString(state.GetCoroutineStatus(thread))];
    }

    private static LuaValue[] CoroutineIsYieldable(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var thread = arguments.Count == 0 || arguments[0].IsNil
            ? state.CurrentThread
            : RequireCoroutineThread(arguments, 0, "coroutine.isyieldable");
        return [LuaValue.FromBoolean(state.IsYieldable(thread))];
    }

    private static LuaValue[] CoroutineClose(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var thread = arguments.Count == 0 || arguments[0].IsNil
            ? state.CurrentThread
            : RequireCoroutineThread(arguments, 0, "coroutine.close");
        var status = state.GetCoroutineStatus(thread);

        switch (status)
        {
            case "dead":
            case "suspended":
                if (state._coroutineCloser is null)
                {
                    throw CreateRuntimeError("coroutine closer is not configured");
                }

                return state._coroutineCloser(thread);
            case "normal":
                throw CreateRuntimeError("cannot close a normal coroutine");
            case "running":
                if (thread.IsMainThread)
                {
                    throw CreateRuntimeError("cannot close main thread");
                }

                throw new LuaThreadCloseException(thread);
            default:
                throw new InvalidOperationException($"Unknown coroutine status '{status}'.");
        }
    }

    private static LuaValue[] CoroutineRunning(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        return [LuaValue.FromThread(state.CurrentThread), LuaValue.FromBoolean(state.CurrentThread.IsMainThread)];
    }

    public bool IsYieldable(LuaThread? thread = null)
    {
        var actualThread = thread ?? CurrentThread;
        if (actualThread.IsMainThread)
        {
            return false;
        }

        if (!ReferenceEquals(actualThread, CurrentThread))
        {
            return !actualThread.IsDead;
        }

        return actualThread.NonYieldableCallDepth == 0;
    }

    public string GetCoroutineStatus(LuaThread thread)
    {
        ArgumentNullException.ThrowIfNull(thread);

        if (ReferenceEquals(thread, CurrentThread))
        {
            return "running";
        }

        if (thread.IsDead)
        {
            return "dead";
        }

        if (thread.IsMainThread)
        {
            return "normal";
        }

        if (!thread.HasStarted || thread.IsYieldSuspended)
        {
            return "suspended";
        }

        if (thread.IsResumingChild || thread.ResumeParent is not null)
        {
            return "normal";
        }

        return "suspended";
    }

    private static LuaThread RequireCoroutineThread(IReadOnlyList<LuaValue> arguments, int index, string functionName)
    {
        var value = RequireArgument(arguments, index, functionName);
        if (value.Kind == LuaValueKind.Thread)
        {
            return value.AsThread();
        }

        throw CreateArgumentTypeError(functionName, index + 1, "thread", value);
    }
}
