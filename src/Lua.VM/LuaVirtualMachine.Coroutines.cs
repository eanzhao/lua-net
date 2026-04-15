using Lua.Bytecode.Chunks;
using Lua.Runtime.Execution;
using Lua.Runtime.Objects;
using Lua.Runtime.Values;
using Lua.VM.Closures;

namespace Lua.VM;

public sealed partial class LuaVirtualMachine
{
    private LuaValue[] ResumeCoroutine(LuaThread thread, IReadOnlyList<LuaValue> arguments)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(arguments);

        if (thread.IsDead)
        {
            return [LuaValue.FromBoolean(false), LuaValue.FromString("cannot resume dead coroutine")];
        }

        if (ReferenceEquals(thread, State.CurrentThread) ||
            thread.ResumeParent is not null ||
            thread.IsResumingChild)
        {
            return [LuaValue.FromBoolean(false), LuaValue.FromString("cannot resume non-suspended coroutine")];
        }

        var resumer = State.CurrentThread;
        var previousThread = State.SwitchCurrentThread(thread);
        thread.ResumeParent = resumer;
        resumer.IsResumingChild = true;

        try
        {
            LuaValue[] results;
            if (!thread.HasStarted)
            {
                results = StartCoroutineThread(thread, arguments);
            }
            else
            {
                if (!thread.IsYieldSuspended)
                {
                    return [LuaValue.FromBoolean(false), LuaValue.FromString("cannot resume non-suspended coroutine")];
                }

                thread.MarkRunning();
                thread.SetResumeValues(arguments);
                results = RunInterpreter(hostCallId: 0);
            }

            return PrependResumeStatus(success: true, results);
        }
        catch (LuaYieldException ex) when (ReferenceEquals(ex.Thread, thread))
        {
            thread.MarkYieldSuspended();
            return PrependResumeStatus(success: true, ex.Values);
        }
        catch (LuaThreadCloseException ex) when (ReferenceEquals(ex.Thread, thread))
        {
            var closeResults = CloseRunningCoroutine(thread);
            return closeResults.Length == 1 && closeResults[0].Kind == LuaValueKind.Boolean && closeResults[0].AsBoolean()
                ? [LuaValue.FromBoolean(true)]
                : [LuaValue.FromBoolean(false), closeResults[1]];
        }
        catch (Exception ex)
        {
            var cleanedException = CleanupFramesToDepth(0, ex) ?? ex;
            var errorObject = GetErrorObject(cleanedException);
            thread.MarkErrored(errorObject);
            return [LuaValue.FromBoolean(false), errorObject];
        }
        finally
        {
            thread.ResumeParent = null;
            resumer.IsResumingChild = false;
            State.SwitchCurrentThread(previousThread);
        }
    }

    private LuaValue[] CloseCoroutine(LuaThread thread)
    {
        ArgumentNullException.ThrowIfNull(thread);

        if (ReferenceEquals(thread, State.CurrentThread) && !thread.IsDead)
        {
            return CloseRunningCoroutine(thread);
        }

        if (thread.IsDead)
        {
            if (!thread.ErrorObject.IsNil)
            {
                var errorObject = thread.ErrorObject;
                thread.ResetToDead();
                return [LuaValue.FromBoolean(false), errorObject];
            }

            thread.ResetToDead();
            return [LuaValue.FromBoolean(true)];
        }

        var previousThread = State.SwitchCurrentThread(thread);
        try
        {
            var pendingException = CleanupFramesToDepth(0);
            if (pendingException is null)
            {
                thread.ResetToDead();
                return [LuaValue.FromBoolean(true)];
            }

            var errorObject = GetErrorObject(pendingException);
            thread.MarkErrored(errorObject);
            return [LuaValue.FromBoolean(false), errorObject];
        }
        finally
        {
            State.SwitchCurrentThread(previousThread);
        }
    }

    private LuaValue[] StartCoroutineThread(LuaThread thread, IReadOnlyList<LuaValue> arguments)
    {
        var entryClosure = thread.EntryClosure
            ?? throw new InvalidOperationException("Coroutine entry closure is not configured.");
        thread.MarkStarted();

        return entryClosure.Body switch
        {
            LuaBytecodeClosureBody body => StartBytecodeCoroutine(thread, entryClosure, body.Prototype, arguments),
            LuaNativeClosureBody body => FinishNativeCoroutine(thread, entryClosure, body, arguments),
            null => throw new InvalidOperationException("The closure does not contain an executable body."),
            _ => throw new InvalidOperationException($"Unsupported closure body type '{entryClosure.Body.GetType().Name}'.")
        };
    }

    private LuaValue[] StartBytecodeCoroutine(
        LuaThread thread,
        LuaClosure closure,
        LuaPrototype prototype,
        IReadOnlyList<LuaValue> arguments)
    {
        PushBytecodeFrame(closure, prototype, arguments, LuaCallReturnTarget.ForThreadRoot());
        return RunInterpreter(hostCallId: 0);
    }

    private LuaValue[] FinishNativeCoroutine(
        LuaThread thread,
        LuaClosure closure,
        LuaNativeClosureBody body,
        IReadOnlyList<LuaValue> arguments)
    {
        var results = ExecuteNativeClosure(closure, body, arguments);
        thread.ResetToDead();
        return results;
    }

    private LuaValue[] CloseRunningCoroutine(LuaThread thread)
    {
        var pendingException = CleanupFramesToDepth(0);
        if (pendingException is null)
        {
            thread.ResetToDead();
            return [LuaValue.FromBoolean(true)];
        }

        var errorObject = GetErrorObject(pendingException);
        thread.MarkErrored(errorObject);
        return [LuaValue.FromBoolean(false), errorObject];
    }

    private static LuaValue[] PrependResumeStatus(bool success, IReadOnlyList<LuaValue> results)
    {
        var values = new LuaValue[results.Count + 1];
        values[0] = LuaValue.FromBoolean(success);
        for (var index = 0; index < results.Count; index++)
        {
            values[index + 1] = results[index];
        }

        return values;
    }
}
