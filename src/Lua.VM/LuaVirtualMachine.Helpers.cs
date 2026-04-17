using System.Runtime.ExceptionServices;
using Lua.Bytecode.Chunks;
using Lua.Bytecode.Instructions;
using Lua.Runtime.Execution;
using Lua.Runtime.Objects;
using Lua.Runtime.Values;
using Lua.VM.Closures;
using static Lua.Runtime.Values.LuaValueHelper;

namespace Lua.VM;

public sealed partial class LuaVirtualMachine
{
    private LuaValue GetRegister(CallFrame frame, int registerIndex)
    {
        return State.Stack[frame.BaseIndex + registerIndex];
    }

    private void SetRegister(CallFrame frame, int registerIndex, LuaValue value)
    {
        EnsureRegisterExists(frame, registerIndex);
        State.Stack[frame.BaseIndex + registerIndex] = value;
    }

    private LuaValue GetUpvalue(CallFrame frame, int upvalueIndex)
    {
        return frame.Closure.Upvalues[upvalueIndex].GetValue(State);
    }

    private void SetUpvalue(CallFrame frame, int upvalueIndex, LuaValue value)
    {
        frame.Closure.Upvalues[upvalueIndex].SetValue(State, value);
    }

    private static LuaPrototype GetCurrentPrototype(CallFrame frame)
    {
        return GetBytecodeBody(frame.Closure).Prototype;
    }

    private static LuaBytecodeClosureBody GetBytecodeBody(LuaClosure closure)
    {
        if (closure.Body is not LuaBytecodeClosureBody body)
        {
            throw new InvalidOperationException("The closure does not contain a bytecode body.");
        }

        return body;
    }

    private static LuaValue ConvertConstant(LuaConstant constant)
    {
        return constant.Kind switch
        {
            LuaConstantKind.Nil => LuaValue.Nil,
            LuaConstantKind.Boolean => LuaValue.FromBoolean(constant.AsBoolean()),
            LuaConstantKind.Integer => LuaValue.FromInteger(constant.AsInteger()),
            LuaConstantKind.Float => LuaValue.FromFloat(constant.AsFloat()),
            LuaConstantKind.String => LuaValue.FromString(constant.AsString()),
            _ => throw new NotImplementedException($"Constant kind '{constant.Kind}' is not implemented yet.")
        };
    }

    private static string? GetConstantString(LuaConstant constant)
    {
        return constant.Kind == LuaConstantKind.String ? constant.AsString() : null;
    }

    private LuaValue GetRkValue(CallFrame frame, LuaPrototype prototype, int operand, int isConstant)
    {
        return isConstant != 0
            ? ConvertConstant(prototype.Constants[operand])
            : GetRegister(frame, operand);
    }

    private void EnsureRegisterExists(CallFrame frame, int registerIndex)
    {
        var absoluteIndex = frame.BaseIndex + registerIndex;
        if (absoluteIndex >= State.Stack.Count)
        {
            State.Stack.SetTop(absoluteIndex + 1);
        }
    }

    private void InitializeRegisters(int baseIndex, IReadOnlyList<LuaValue> arguments, int fixedArgumentCount)
    {
        for (var index = 0; index < fixedArgumentCount; index++)
        {
            State.Stack[baseIndex + index] = arguments[index];
        }
    }

    private LuaValue[] ReadArguments(CallFrame frame, int functionRegister, int functionAndArgumentCount)
    {
        var argumentCount = functionAndArgumentCount == 0
            ? GetOpenValueCount(frame, functionRegister + 1)
            : functionAndArgumentCount - 1;
        var arguments = new LuaValue[argumentCount];

        for (var index = 0; index < argumentCount; index++)
        {
            arguments[index] = GetRegister(frame, functionRegister + index + 1);
        }

        return arguments;
    }

    private void WriteResults(CallFrame frame, int registerIndex, int resultCount, IReadOnlyList<LuaValue> results)
    {
        for (var index = 0; index < resultCount; index++)
        {
            var value = index < results.Count ? results[index] : LuaValue.Nil;
            SetRegister(frame, registerIndex + index, value);
        }
    }

    private void WriteOpenResults(CallFrame frame, int registerIndex, IReadOnlyList<LuaValue> results)
    {
        for (var index = 0; index < results.Count; index++)
        {
            SetRegister(frame, registerIndex + index, results[index]);
        }

        frame.SetRegisterTop(registerIndex + results.Count);
    }

    private LuaValue[] ReadOpenResults(CallFrame frame, int registerIndex)
    {
        var resultCount = GetOpenValueCount(frame, registerIndex);
        var results = new LuaValue[resultCount];

        for (var index = 0; index < resultCount; index++)
        {
            results[index] = GetRegister(frame, registerIndex + index);
        }

        return results;
    }

    private static int GetOpenValueCount(CallFrame frame, int registerIndex)
    {
        return Math.Max(0, frame.RegisterTop - registerIndex);
    }

    private static void JumpRelative(CallFrame frame, int offset)
    {
        frame.Jump(frame.ProgramCounter + offset);
    }

    private static LuaValue[] GetVarargs(
        LuaPrototype prototype,
        IReadOnlyList<LuaValue> arguments,
        int fixedArgumentCount)
    {
        if (!IsVarArgFunction(prototype) || arguments.Count <= fixedArgumentCount)
        {
            return Array.Empty<LuaValue>();
        }

        var varargs = new LuaValue[arguments.Count - fixedArgumentCount];
        for (var index = 0; index < varargs.Length; index++)
        {
            varargs[index] = arguments[fixedArgumentCount + index];
        }

        return varargs;
    }

    private static LuaTable CreateVarArgTable(IReadOnlyList<LuaValue> varargs)
    {
        var table = new LuaTable("vararg", arrayCapacity: varargs.Count);
        for (var index = 0; index < varargs.Count; index++)
        {
            table.SetValue(LuaValue.FromInteger(index + 1), varargs[index]);
        }

        table.SetValue(LuaValue.FromString("n"), LuaValue.FromInteger(varargs.Count));
        return table;
    }

    private static LuaValue GetVarArgValue(CallFrame frame, LuaValue key)
    {
        if (TryGetInteger(key, out var integerKey))
        {
            if (integerKey >= 1 && integerKey <= frame.Varargs.Count)
            {
                return frame.Varargs[(int)integerKey - 1];
            }

            return LuaValue.Nil;
        }

        if (key.Kind == LuaValueKind.String && string.Equals(key.AsString(), "n", StringComparison.Ordinal))
        {
            return LuaValue.FromInteger(frame.Varargs.Count);
        }

        return LuaValue.Nil;
    }

    private static int GetVarArgCount(LuaTable table)
    {
        var countValue = table.GetValue(LuaValue.FromString("n"));
        if (countValue.Kind != LuaValueKind.Integer)
        {
            throw new InvalidOperationException("vararg table has no proper 'n'");
        }

        var count = countValue.AsInteger();
        if (count < 0 || count > int.MaxValue)
        {
            throw new InvalidOperationException("vararg table has no proper 'n'");
        }

        return (int)count;
    }

    private LuaUpvalue[] BuildUpvalues(LuaPrototype prototype, CallFrame? parentFrame, LuaValue? rootEnvironment)
    {
        var upvalues = new LuaUpvalue[prototype.Upvalues.Length];

        for (var index = 0; index < prototype.Upvalues.Length; index++)
        {
            var descriptor = prototype.Upvalues[index];

            if (parentFrame is null)
            {
                upvalues[index] = new LuaUpvalue(
                    string.Equals(descriptor.Name, "_ENV", StringComparison.Ordinal)
                        ? rootEnvironment ?? LuaValue.FromTable(State.GlobalEnvironment)
                        : LuaValue.Nil);

                continue;
            }

            if (descriptor.InStack != 0)
            {
                upvalues[index] = parentFrame.GetOrCreateOpenUpvalue(State.Stack, descriptor.Index);
            }
            else
            {
                if (descriptor.Index >= parentFrame.Closure.Upvalues.Length)
                {
                    throw new InvalidOperationException(
                        $"Upvalue index {descriptor.Index} out of range (parent has {parentFrame.Closure.Upvalues.Length} upvalues).");
                }

                upvalues[index] = parentFrame.Closure.Upvalues[descriptor.Index];
            }
        }

        return upvalues;
    }

    private static string GetDebugName(LuaPrototype prototype)
    {
        if (!string.IsNullOrEmpty(prototype.DebugName))
        {
            return prototype.DebugName;
        }

        if (prototype.LineDefined == 0)
        {
            return "main";
        }

        return $"function@{prototype.LineDefined}";
    }

    private static (string? Name, string NameWhat) GetCallSiteDebugInfo(LuaPrototype prototype, int instructionIndex)
    {
        if (prototype.CallSiteNames is null ||
            prototype.CallSiteNameWhats is null ||
            (uint)instructionIndex >= (uint)prototype.CallSiteNames.Length ||
            (uint)instructionIndex >= (uint)prototype.CallSiteNameWhats.Length)
        {
            return (null, string.Empty);
        }

        return (prototype.CallSiteNames[instructionIndex], prototype.CallSiteNameWhats[instructionIndex]);
    }

    private static bool IsVarArgFunction(LuaPrototype prototype)
    {
        return (prototype.Flags & VarArgFlagMask) != 0;
    }

    private static bool UsesVarArgTable(LuaPrototype prototype)
    {
        return (prototype.Flags & VarArgTableFlag) != 0;
    }

    private LuaValue GetCloseMethod(LuaValue value)
    {
        return State.TryGetMetamethod(value, "__close", out var metamethod)
            ? metamethod
            : LuaValue.Nil;
    }

    private void RegisterToBeClosed(CallFrame frame, int registerIndex, string? variableName = null)
    {
        var value = GetRegister(frame, registerIndex);
        if (value.IsNil || (value.Kind == LuaValueKind.Boolean && !value.AsBoolean()))
        {
            return;
        }

        EnsureCloseMethodExists(value, variableName);
        frame.RegisterToBeClosed(registerIndex);
    }

    private Exception? CloseResourcesFrom(CallFrame frame, int registerIndex, Exception? pendingException = null)
    {
        try
        {
            var currentException = pendingException;
            foreach (var trackedRegister in frame.ConsumeToBeClosedRegistersFrom(registerIndex))
            {
                try
                {
                    CloseToBeClosedValue(frame, trackedRegister, currentException);
                }
                catch (Exception ex)
                {
                    currentException = AnnotateCloseError(ex);
                }
            }

            return currentException;
        }
        finally
        {
            frame.CloseOpenUpvaluesFrom(registerIndex);
        }
    }

    private bool StartCloseContinuation(
        CallFrame frame,
        int registerIndex,
        LuaPendingCloseContinuationKind continuationKind,
        IReadOnlyList<LuaValue>? returnResults,
        Exception? pendingException = null)
    {
        if (State.CurrentThread.IsMainThread)
        {
            return false;
        }

        if (frame.PendingClose is not null)
        {
            return true;
        }

        var registers = frame.ConsumeToBeClosedRegistersFrom(registerIndex);
        if (registers.Count == 0)
        {
            if (pendingException is null)
            {
                frame.CloseOpenUpvaluesFrom(registerIndex);
                return false;
            }

            RethrowIfNeeded(CloseResourcesFrom(frame, registerIndex, pendingException));
            return false;
        }

        frame.CloseOpenUpvaluesFrom(registerIndex);
        frame.SetPendingClose(new LuaPendingClose(registers, continuationKind, returnResults, pendingException));
        return true;
    }

    private bool TryStartErrorCloseContinuation(Exception exception)
    {
        var frame = State.CurrentFrame;
        if (frame is null ||
            frame.PendingClose is not null ||
            !frame.HasToBeClosedRegistersFrom(0))
        {
            return false;
        }

        return StartCloseContinuation(
            frame,
            registerIndex: 0,
            LuaPendingCloseContinuationKind.Error,
            returnResults: null,
            pendingException: exception);
    }

    private bool TryResumePendingClose(
        CallFrame frame,
        int hostCallId,
        out bool completedFrame,
        out LuaValue[] completedResults)
    {
        completedFrame = false;
        completedResults = [];

        var pendingClose = frame.PendingClose;
        if (pendingClose is null)
        {
            return false;
        }

        if (pendingClose.AwaitingResumeValues)
        {
            if (!State.CurrentThread.HasResumeValues())
            {
                return false;
            }

            State.CurrentThread.ConsumeResumeValues();
            pendingClose.ConsumeResumeValues();
        }

        while (pendingClose.HasRemainingRegisters)
        {
            var registerIndex = pendingClose.DequeueNextRegister();

            try
            {
                if (TryStartCloseCall(frame, registerIndex, pendingClose))
                {
                    return true;
                }
            }
            catch (LuaYieldException ex) when (ReferenceEquals(ex.Thread, State.CurrentThread))
            {
                if (pendingClose.HasCloseError)
                {
                    pendingClose.CanReplaceCloseError = false;
                }

                pendingClose.WaitForResumeValues();
                throw;
            }
            catch (Exception ex)
            {
                if (!pendingClose.HasCloseError || pendingClose.CanReplaceCloseError)
                {
                    pendingClose.PendingException = AnnotateCloseError(ex);
                    pendingClose.HasCloseError = true;
                    pendingClose.CanReplaceCloseError = true;
                }
            }
        }

        frame.ClearPendingClose();
        if (pendingClose.ContinuationKind == LuaPendingCloseContinuationKind.Error)
        {
            var pendingException = pendingClose.PendingException;
            State.PopFrame();
            State.Stack.SetTop(frame.BaseIndex);
            RethrowIfNeeded(pendingException);
            return true;
        }

        if (pendingClose.PendingException is not null)
        {
            ExceptionDispatchInfo.Capture(pendingClose.PendingException).Throw();
        }

        if (pendingClose.ContinuationKind == LuaPendingCloseContinuationKind.Return)
        {
            completedFrame = CompleteFrameAfterClose(frame, pendingClose.ReturnResults, hostCallId, out completedResults);
            return true;
        }

        return true;
    }

    private bool TryResumePendingProtectedCall(
        CallFrame frame,
        int hostCallId,
        out bool completedFrame,
        out LuaValue[] completedResults)
    {
        completedFrame = false;
        completedResults = [];

        var pendingProtectedCall = frame.PendingProtectedCall;
        if (pendingProtectedCall is null)
        {
            return false;
        }

        if (pendingProtectedCall.AwaitingResumeValues)
        {
            if (!State.CurrentThread.HasResumeValues())
            {
                return false;
            }

            var resumeValues = State.CurrentThread.ConsumeResumeValues();
            pendingProtectedCall.ConsumeResumeValues();
            pendingProtectedCall.SetPhaseResults(resumeValues);
        }

        while (true)
        {
            if (pendingProtectedCall.HasPhaseResults)
            {
                var phaseResults = pendingProtectedCall.ConsumePhaseResults();
                pendingProtectedCall.SetFinalResults(
                    pendingProtectedCall.Phase == LuaPendingProtectedCallPhase.Function
                        ? CreateProtectedSuccessResults(phaseResults)
                        : CreateProtectedFailureResults(phaseResults.Length == 0 ? LuaValue.Nil : phaseResults[0]));
                continue;
            }

            if (pendingProtectedCall.HasPendingErrorObject)
            {
                if (pendingProtectedCall.Phase == LuaPendingProtectedCallPhase.MessageHandler ||
                    pendingProtectedCall.MessageHandler is null)
                {
                    var finalErrorObject = pendingProtectedCall.Phase == LuaPendingProtectedCallPhase.MessageHandler
                        ? LuaValue.FromString("error in error handling")
                        : pendingProtectedCall.ConsumePendingErrorObject();
                    pendingProtectedCall.SetFinalResults(CreateProtectedFailureResults(finalErrorObject));
                    continue;
                }

                var errorObject = pendingProtectedCall.ConsumePendingErrorObject();
                pendingProtectedCall.BeginMessageHandler();

                try
                {
                    var handledResults = State.InvokeCallable(pendingProtectedCall.MessageHandler.Value, [errorObject]);
                    pendingProtectedCall.SetPhaseResults(handledResults);
                    continue;
                }
                catch (LuaYieldException ex) when (ReferenceEquals(ex.Thread, State.CurrentThread))
                {
                    if (pendingProtectedCall.ActiveHostCallId == 0)
                    {
                        pendingProtectedCall.WaitForResumeValues();
                    }

                    throw;
                }
                catch
                {
                    pendingProtectedCall.SetFinalResults(
                        CreateProtectedFailureResults(LuaValue.FromString("error in error handling")));
                    continue;
                }
            }

            if (!pendingProtectedCall.HasFinalResults)
            {
                return false;
            }

            var finalResults = pendingProtectedCall.ConsumeFinalResults();
            frame.ClearPendingProtectedCall();
            ExecuteReturnHook(frame);
            State.PopFrame();
            State.Stack.SetTop(frame.BaseIndex);

            if (State.CurrentFrame is not null)
            {
                State.CurrentThread.SetResumeValues(finalResults);
                return true;
            }

            if (!State.CurrentThread.IsMainThread)
            {
                State.CurrentThread.MarkCompleted();
            }

            completedFrame = true;
            completedResults = finalResults;
            return true;
        }
    }

    private bool TryCompleteProtectedHostCall(int hostCallId, IReadOnlyList<LuaValue> results)
    {
        var frames = State.CurrentThread.Frames;
        for (var index = frames.Count - 1; index >= 0; index--)
        {
            var pendingProtectedCall = frames[index].PendingProtectedCall;
            if (pendingProtectedCall is null ||
                !pendingProtectedCall.IsSuspended ||
                pendingProtectedCall.ActiveHostCallId != hostCallId)
            {
                continue;
            }

            pendingProtectedCall.SetPhaseResults(results);
            return true;
        }

        return false;
    }

    private bool TryHandlePendingProtectedCallException(Exception exception)
    {
        var frames = State.CurrentThread.Frames;
        for (var index = frames.Count - 1; index >= 0; index--)
        {
            var pendingProtectedCall = frames[index].PendingProtectedCall;
            if (pendingProtectedCall is null || !pendingProtectedCall.IsSuspended)
            {
                continue;
            }

            var frameDepth = index + 1;
            var cleanedException = CleanupFramesToDepth(frameDepth, exception) ?? exception;
            pendingProtectedCall.SetPendingErrorObject(GetErrorObject(cleanedException));
            return true;
        }

        return false;
    }

    private bool TryStartCloseCall(CallFrame frame, int registerIndex, LuaPendingClose pendingClose)
    {
        var value = GetRegister(frame, registerIndex);
        if (value.IsNil || (value.Kind == LuaValueKind.Boolean && !value.AsBoolean()))
        {
            return false;
        }

        var closeMethod = GetCloseMethod(value);
        if (closeMethod.Kind != LuaValueKind.Function)
        {
            throw CreateCloseMethodRuntimeException(closeMethod);
        }

        var closeClosure = GetCloseCallable(closeMethod.AsFunction());
        var closeArguments = pendingClose.PendingException is null
            ? new[] { value }
            : new[] { value, GetErrorObject(pendingClose.PendingException) };

        switch (closeClosure.Body)
        {
            case LuaBytecodeClosureBody body:
                PushBytecodeFrame(
                    closeClosure,
                    body.Prototype,
                    closeArguments,
                    LuaCallReturnTarget.ForCloseContinuation(frame));
                return true;
            case LuaNativeClosureBody body:
                ExecuteNativeClosure(closeClosure, body, closeArguments);
                return false;
            case null:
                throw new InvalidOperationException("The closure does not contain an executable body.");
            default:
                throw new InvalidOperationException($"Unsupported closure body type '{closeClosure.Body.GetType().Name}'.");
        }
    }

    private void CloseToBeClosedValue(CallFrame frame, int registerIndex, Exception? pendingException)
    {
        var value = GetRegister(frame, registerIndex);
        if (value.IsNil || (value.Kind == LuaValueKind.Boolean && !value.AsBoolean()))
        {
            return;
        }

        var closeMethod = GetCloseMethod(value);
        if (closeMethod.Kind != LuaValueKind.Function)
        {
            throw CreateCloseMethodRuntimeException(closeMethod);
        }

        var closeClosure = GetCloseCallable(closeMethod.AsFunction());
        if (pendingException is null)
        {
            Call(closeClosure, [value], invocationName: "close");
            return;
        }

        Call(closeClosure, [value, GetErrorObject(pendingException)], invocationName: "close");
    }

    private static LuaClosure GetCloseCallable(LuaClosure closeClosure)
    {
        if (!string.IsNullOrEmpty(closeClosure.DebugName) &&
            !closeClosure.DebugName.StartsWith("function@", StringComparison.Ordinal))
        {
            return closeClosure;
        }

        return new LuaClosure(
            "close",
            closeClosure.UpvalueCount,
            closeClosure.Body,
            closeClosure.Upvalues,
            closeClosure.UpvalueNames,
            closeClosure.SourceName,
            closeClosure.LineDefined);
    }

    private static LuaValue GetErrorObject(Exception? exception)
    {
        return exception switch
        {
            null => LuaValue.Nil,
            LuaRuntimeException runtimeException => runtimeException.ErrorObject,
            _ => LuaValue.FromString(exception.Message)
        };
    }

    private static LuaValue[] CreateProtectedSuccessResults(IReadOnlyList<LuaValue> results)
    {
        var values = new LuaValue[results.Count + 1];
        values[0] = LuaValue.FromBoolean(true);
        for (var index = 0; index < results.Count; index++)
        {
            values[index + 1] = results[index];
        }

        return values;
    }

    private static LuaValue[] CreateProtectedFailureResults(LuaValue errorObject)
    {
        return [LuaValue.FromBoolean(false), errorObject];
    }

    private static void RethrowIfNeeded(Exception? exception)
    {
        if (exception is null)
        {
            return;
        }

        ExceptionDispatchInfo.Capture(exception).Throw();
    }

    private void EnsureCloseMethodExists(LuaValue value, string? variableName = null)
    {
        var closeMethod = GetCloseMethod(value);
        if (closeMethod.Kind != LuaValueKind.Function)
        {
            if (!string.IsNullOrEmpty(variableName))
            {
                throw new LuaRuntimeException(
                    LuaValue.FromString($"variable '{variableName}' got a non-closable value"));
            }

            throw CreateCloseMethodRuntimeException(closeMethod);
        }
    }

    private static LuaRuntimeException CreateCloseMethodRuntimeException(LuaValue closeMethod)
    {
        return closeMethod.IsNil
            ? new LuaRuntimeException(LuaValue.FromString("no metamethod 'close'"))
            : new LuaRuntimeException(
                LuaValue.FromString(
                    $"attempt to call a {GetTypeName(closeMethod)} value (metamethod 'close')"));
    }

    private static Exception AnnotateCloseError(Exception exception)
    {
        if (exception is not LuaRuntimeException runtimeException || runtimeException.ErrorObject.Kind != LuaValueKind.String)
        {
            return exception;
        }

        var message = runtimeException.ErrorObject.AsString();
        if (message.Contains("in metamethod 'close'", StringComparison.Ordinal))
        {
            return exception;
        }

        return new LuaRuntimeException(
            LuaValue.FromString($"{message}\nin metamethod 'close'"),
            runtimeException);
    }

    private void ExecuteReturnHook(CallFrame frame)
    {
        var thread = State.CurrentThread;
        if (thread.IsExecutingHook || !thread.HasHookEvent('r') || thread.HookFunction is null)
        {
            return;
        }

        thread.EnterHookInvocation();

        try
        {
            Call(thread.HookFunction, [LuaValue.FromString("return")], invocationName: "hook", invocationNameWhat: "hook");
        }
        finally
        {
            thread.ExitHookInvocation();
        }
    }

    private void ExecuteCallHook(CallFrame frame)
    {
        var thread = State.CurrentThread;
        if (thread.IsExecutingHook || !thread.HasHookEvent('c') || thread.HookFunction is null)
        {
            return;
        }

        thread.EnterHookInvocation();

        try
        {
            Call(thread.HookFunction, [LuaValue.FromString("call")], invocationName: "hook", invocationNameWhat: "hook");
        }
        finally
        {
            thread.ExitHookInvocation();
        }
    }

    private void ExecuteLineHook(CallFrame frame, LuaPrototype prototype, LuaInstruction instruction)
    {
        var thread = State.CurrentThread;
        if (thread.IsExecutingHook || !thread.HasHookEvent('l') || thread.HookFunction is null)
        {
            return;
        }

        if (instruction.Opcode is LuaOpcode.VarArgPrep)
        {
            return;
        }

        var instructionIndex = frame.ProgramCounter - 1;
        if ((uint)instructionIndex >= (uint)prototype.Code.Length)
        {
            return;
        }

        var line = frame.Closure.ResolveLine(instructionIndex);
        if (line <= 0 || frame.LastLineHookLine == line)
        {
            return;
        }

        frame.SetLastLineHookLine(line);
        thread.EnterHookInvocation();

        try
        {
            Call(
                thread.HookFunction,
                [LuaValue.FromString("line"), LuaValue.FromInteger(line)],
                invocationName: "hook",
                invocationNameWhat: "hook");
        }
        finally
        {
            thread.ExitHookInvocation();
        }
    }

    private static LuaRuntimeException CreateTypeError(LuaValue value, string operation)
    {
        return new LuaRuntimeException(LuaValue.FromString($"attempt to {operation} a {GetTypeName(value)} value"));
    }

    private static LuaRuntimeException CreateRuntimeException(string message)
    {
        return new LuaRuntimeException(LuaValue.FromString(message));
    }

    private static LuaRuntimeException CreateIntegerRepresentationError()
    {
        return CreateRuntimeException("number has no integer representation");
    }

    private static int ToSignedB(int value)
    {
        return value - LuaInstructionLayout.OffsetSC;
    }

    private static int ToSignedC(int value)
    {
        return value - LuaInstructionLayout.OffsetSC;
    }

    private static int SaturateCapacity(long value)
    {
        if (value <= 0)
        {
            return 0;
        }

        return value >= int.MaxValue ? int.MaxValue : (int)value;
    }

    private static int ReadFollowingExtraArgument(CallFrame frame, LuaPrototype prototype, LuaOpcode owner)
    {
        if (frame.ProgramCounter >= prototype.Code.Length)
        {
            throw new InvalidOperationException($"Opcode '{owner}' is missing the following EXTRAARG.");
        }

        var extraInstruction = LuaInstruction.FromRaw(prototype.Code[frame.ProgramCounter]);
        if (extraInstruction.Opcode != LuaOpcode.ExtraArg)
        {
            throw new InvalidOperationException($"Opcode '{owner}' must be followed by EXTRAARG.");
        }

        frame.Advance();
        return extraInstruction.Ax;
    }

    private static LuaInstruction ReadPreviousInstruction(CallFrame frame, LuaPrototype prototype, LuaOpcode owner)
    {
        if (frame.ProgramCounter < 2)
        {
            throw new InvalidOperationException($"Opcode '{owner}' is missing the preceding arithmetic instruction.");
        }

        return LuaInstruction.FromRaw(prototype.Code[frame.ProgramCounter - 2]);
    }
}
