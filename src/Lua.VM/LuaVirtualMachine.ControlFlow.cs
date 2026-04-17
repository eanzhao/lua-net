using Lua.Bytecode.Chunks;
using Lua.Bytecode.Instructions;
using Lua.Runtime.Execution;
using Lua.Runtime.Objects;
using Lua.Runtime.Values;
using Lua.VM.Closures;
using System.Runtime.ExceptionServices;
using static Lua.Runtime.Values.LuaValueHelper;

namespace Lua.VM;

public sealed partial class LuaVirtualMachine
{
    private void ExecuteCall(CallFrame frame, LuaInstruction instruction)
    {
        var prototype = GetCurrentPrototype(frame);
        var callable = GetRegister(frame, instruction.A);
        var arguments = ReadArguments(frame, instruction.A, instruction.B);
        var resolved = ResolveCallable(callable, arguments);
        var resultCount = instruction.C == 0 ? -1 : instruction.C - 1;
        var (invocationName, invocationNameWhat) = GetCallSiteDebugInfo(prototype, frame.ProgramCounter - 1);

        switch (resolved.Closure.Body)
        {
            case LuaBytecodeClosureBody body:
                PushBytecodeFrame(
                    resolved.Closure,
                    body.Prototype,
                    resolved.Arguments,
                    LuaCallReturnTarget.ForRegisters(frame, instruction.A, resultCount),
                    invocationName,
                    invocationNameWhat);
                return;
            case LuaNativeClosureBody body:
                frame.SetPendingCall(instruction.A, resultCount);
                try
                {
                    var results = ExecuteNativeClosure(resolved.Closure, body, resolved.Arguments, invocationName, invocationNameWhat);
                    WriteCallResults(frame, instruction.A, resultCount, results);
                    frame.ClearPendingCall();
                    return;
                }
                catch (LuaYieldException ex) when (ReferenceEquals(ex.Thread, State.CurrentThread))
                {
                    throw;
                }
                catch
                {
                    if (frame.PendingCall?.Kind == LuaPendingCallKind.Registers)
                    {
                        frame.ClearPendingCall();
                    }

                    throw;
                }
            case null:
                throw new InvalidOperationException("The closure does not contain an executable body.");
            default:
                throw new InvalidOperationException($"Unsupported closure body type '{resolved.Closure.Body.GetType().Name}'.");
        }
    }

    private bool ExecuteTailCall(CallFrame frame, LuaInstruction instruction, int hostCallId, out LuaValue[] completedResults)
    {
        completedResults = [];

        var prototype = GetCurrentPrototype(frame);
        var callable = GetRegister(frame, instruction.A);
        var arguments = ReadArguments(frame, instruction.A, instruction.B);
        var resolved = ResolveCallable(callable, arguments);
        var (invocationName, invocationNameWhat) = GetCallSiteDebugInfo(prototype, frame.ProgramCounter - 1);

        switch (resolved.Closure.Body)
        {
            case LuaBytecodeClosureBody body:
                var returnTarget = frame.ReturnTarget;
                var pendingException = CloseResourcesFrom(frame, 0);
                if (pendingException is not null)
                {
                    ExceptionDispatchInfo.Capture(pendingException).Throw();
                }

                State.PopFrame();
                State.Stack.SetTop(frame.BaseIndex);
                PushBytecodeFrame(resolved.Closure, body.Prototype, resolved.Arguments, returnTarget, invocationName, invocationNameWhat);
                return false;
            case LuaNativeClosureBody nativeBody:
                frame.SetPendingTailReturn();
                try
                {
                    var results = ExecuteNativeClosure(resolved.Closure, nativeBody, resolved.Arguments, invocationName, invocationNameWhat);
                    frame.ClearPendingCall();
                    return TryCompleteFrame(frame, results, hostCallId, out completedResults);
                }
                catch (LuaYieldException ex) when (ReferenceEquals(ex.Thread, State.CurrentThread))
                {
                    throw;
                }
                catch
                {
                    if (frame.PendingCall?.Kind == LuaPendingCallKind.TailReturn)
                    {
                        frame.ClearPendingCall();
                    }

                    throw;
                }
            case null:
                throw new InvalidOperationException("The closure does not contain an executable body.");
            default:
                throw new InvalidOperationException($"Unsupported closure body type '{resolved.Closure.Body.GetType().Name}'.");
        }
    }

    private LuaValue[] ExecuteReturn(CallFrame frame, LuaInstruction instruction)
    {
        if (instruction.B == 0)
        {
            return ReadOpenResults(frame, instruction.A);
        }

        var resultCount = instruction.B - 1;
        var results = new LuaValue[resultCount];

        for (var index = 0; index < resultCount; index++)
        {
            results[index] = GetRegister(frame, instruction.A + index);
        }

        return results;
    }

    private void ExecuteVarArg(CallFrame frame, LuaInstruction instruction)
    {
        if (instruction.K != 0)
        {
            ExecuteVarArgFromTable(frame, instruction);
            return;
        }

        var requestedResultCount = instruction.C - 1;
        if (requestedResultCount < 0)
        {
            WriteOpenResults(frame, instruction.A, frame.Varargs);
            return;
        }

        WriteResults(frame, instruction.A, requestedResultCount, frame.Varargs);
    }

    private void ExecuteGetVarArg(CallFrame frame, LuaInstruction instruction)
    {
        SetRegister(frame, instruction.A, GetVarArgValue(frame, GetRegister(frame, instruction.C)));
    }

    private void ExecuteVarArgFromTable(CallFrame frame, LuaInstruction instruction)
    {
        var varargTable = GetRegister(frame, instruction.B).AsTable();
        var requestedResultCount = instruction.C - 1;
        var availableCount = GetVarArgCount(varargTable);

        if (requestedResultCount < 0)
        {
            var results = new LuaValue[availableCount];
            for (var index = 0; index < availableCount; index++)
            {
                results[index] = varargTable.GetValue(LuaValue.FromInteger(index + 1));
            }

            WriteOpenResults(frame, instruction.A, results);
            return;
        }

        var values = new LuaValue[requestedResultCount];
        for (var index = 0; index < requestedResultCount; index++)
        {
            values[index] = index < availableCount
                ? varargTable.GetValue(LuaValue.FromInteger(index + 1))
                : LuaValue.Nil;
        }

        WriteResults(frame, instruction.A, requestedResultCount, values);
    }

    private void ExecuteErrNNil(LuaPrototype prototype, CallFrame frame, LuaInstruction instruction)
    {
        if (GetRegister(frame, instruction.A).IsNil)
        {
            return;
        }

        var globalName = instruction.Bx == 0
            ? "?"
            : GetConstantString(prototype.Constants[instruction.Bx - 1]) ?? "?";

        throw new LuaRuntimeException(LuaValue.FromString($"global '{globalName}' already defined"));
    }

    private void ExecuteIntegerForPrep(
        CallFrame frame,
        LuaInstruction instruction,
        long initialValue,
        LuaValue limitValue,
        long stepValue)
    {
        if (stepValue == 0)
        {
            throw CreateRuntimeException("'for' step is zero");
        }

        var limit = GetIntegerForLimit(limitValue, initialValue, stepValue);
        if (ShouldSkipIntegerForLoop(initialValue, limit, stepValue))
        {
            frame.Advance(instruction.Bx + 1);
            return;
        }

        SetRegister(frame, instruction.A, LuaValue.FromInteger(ComputeIntegerForLoopCount(initialValue, limit, stepValue)));
        SetRegister(frame, instruction.A + 1, LuaValue.FromInteger(stepValue));
        SetRegister(frame, instruction.A + 2, LuaValue.FromInteger(initialValue));
    }

    private void ExecuteFloatForPrep(
        CallFrame frame,
        LuaInstruction instruction,
        LuaValue initialValue,
        LuaValue limitValue,
        LuaValue stepValue)
    {
        if (!TryGetNumber(limitValue, out var numericLimit))
        {
            throw CreateRuntimeException("'for' limit must be a number");
        }

        if (!TryGetNumber(stepValue, out var numericStep))
        {
            throw CreateRuntimeException("'for' step must be a number");
        }

        if (!TryGetNumber(initialValue, out var numericInitial))
        {
            throw CreateRuntimeException("'for' initial value must be a number");
        }

        if (numericStep == 0d)
        {
            throw CreateRuntimeException("'for' step is zero");
        }

        if (ShouldSkipFloatForLoop(numericInitial, numericLimit, numericStep))
        {
            frame.Advance(instruction.Bx + 1);
            return;
        }

        SetRegister(frame, instruction.A, LuaValue.FromFloat(numericLimit));
        SetRegister(frame, instruction.A + 1, LuaValue.FromFloat(numericStep));
        SetRegister(frame, instruction.A + 2, LuaValue.FromFloat(numericInitial));
    }

    private void ExecuteIntegerForLoop(CallFrame frame, LuaInstruction instruction)
    {
        var remaining = unchecked((ulong)GetRegister(frame, instruction.A).AsInteger());
        if (remaining == 0)
        {
            return;
        }

        var step = GetRegister(frame, instruction.A + 1).AsInteger();
        var index = GetRegister(frame, instruction.A + 2).AsInteger();

        SetRegister(frame, instruction.A, LuaValue.FromInteger(unchecked((long)(remaining - 1))));
        SetRegister(frame, instruction.A + 2, LuaValue.FromInteger(index + step));
        JumpRelative(frame, -instruction.Bx);
    }

    private void ExecuteFloatForLoop(CallFrame frame, LuaInstruction instruction)
    {
        var step = GetRegister(frame, instruction.A + 1).AsFloat();
        var limit = GetRegister(frame, instruction.A).AsFloat();
        var index = GetRegister(frame, instruction.A + 2).AsFloat() + step;

        if (ShouldContinueFloatForLoop(index, limit, step))
        {
            SetRegister(frame, instruction.A + 2, LuaValue.FromFloat(index));
            JumpRelative(frame, -instruction.Bx);
        }
    }

    private void ExecuteForPrep(CallFrame frame, LuaInstruction instruction)
    {
        var initialValue = GetRegister(frame, instruction.A);
        var limitValue = GetRegister(frame, instruction.A + 1);
        var stepValue = GetRegister(frame, instruction.A + 2);

        if (initialValue.Kind == LuaValueKind.Integer && stepValue.Kind == LuaValueKind.Integer)
        {
            ExecuteIntegerForPrep(frame, instruction, initialValue.AsInteger(), limitValue, stepValue.AsInteger());
            return;
        }

        ExecuteFloatForPrep(frame, instruction, initialValue, limitValue, stepValue);
    }

    private void ExecuteForLoop(CallFrame frame, LuaInstruction instruction)
    {
        if (GetRegister(frame, instruction.A + 1).Kind == LuaValueKind.Integer)
        {
            ExecuteIntegerForLoop(frame, instruction);
            return;
        }

        ExecuteFloatForLoop(frame, instruction);
    }

    private void ExecuteTForPrep(CallFrame frame, LuaInstruction instruction)
    {
        var controlValue = GetRegister(frame, instruction.A + 2);
        var closeValue = GetRegister(frame, instruction.A + 3);

        SetRegister(frame, instruction.A + 2, closeValue);
        SetRegister(frame, instruction.A + 3, controlValue);
        RegisterToBeClosed(frame, instruction.A + 2);
        frame.Advance(instruction.Bx);
    }

    private void ExecuteTForCall(CallFrame frame, LuaInstruction instruction)
    {
        var prototype = GetCurrentPrototype(frame);
        SetRegister(frame, instruction.A + 5, GetRegister(frame, instruction.A + 3));
        SetRegister(frame, instruction.A + 4, GetRegister(frame, instruction.A + 1));
        SetRegister(frame, instruction.A + 3, GetRegister(frame, instruction.A));

        var iterator = GetRegister(frame, instruction.A + 3);
        LuaValue[] arguments =
        [
            GetRegister(frame, instruction.A + 4),
            GetRegister(frame, instruction.A + 5)
        ];
        var resolved = ResolveCallable(iterator, arguments);

        switch (resolved.Closure.Body)
        {
            case LuaBytecodeClosureBody body:
                PushBytecodeFrame(
                    resolved.Closure,
                    body.Prototype,
                    resolved.Arguments,
                    LuaCallReturnTarget.ForRegisters(frame, instruction.A + 3, instruction.C),
                    GetCallSiteDebugInfo(prototype, frame.ProgramCounter - 1).Name ?? "for iterator",
                    GetCallSiteDebugInfo(prototype, frame.ProgramCounter - 1).NameWhat);
                return;
            case LuaNativeClosureBody nativeBody:
                var results = ExecuteNativeClosure(
                    resolved.Closure,
                    nativeBody,
                    resolved.Arguments,
                    invocationName: "for iterator",
                    invocationNameWhat: string.Empty);
                WriteResults(frame, instruction.A + 3, instruction.C, results);
                return;
            case null:
                throw new InvalidOperationException("The closure does not contain an executable body.");
            default:
                throw new InvalidOperationException($"Unsupported closure body type '{resolved.Closure.Body.GetType().Name}'.");
        }
    }

    private void ExecuteTForLoop(CallFrame frame, LuaInstruction instruction)
    {
        if (!GetRegister(frame, instruction.A + 3).IsNil)
        {
            JumpRelative(frame, -instruction.Bx);
        }
    }

    private void ExecuteVarArgPrep(CallFrame frame, LuaPrototype prototype)
    {
        if (!UsesVarArgTable(prototype))
        {
            return;
        }

        SetRegister(frame, prototype.NumberOfParameters, LuaValue.FromTable(CreateVarArgTable(frame.Varargs)));
    }

    private void ExecuteJump(CallFrame frame, LuaInstruction instruction)
    {
        JumpRelative(frame, instruction.SJ);
    }

    private static long GetIntegerForLimit(LuaValue value, long initialValue, long stepValue)
    {
        if (value.Kind == LuaValueKind.Integer)
        {
            return value.AsInteger();
        }

        if (!TryGetNumber(value, out var numericLimit))
        {
            throw CreateRuntimeException("'for' limit must be a number");
        }

        if (!double.IsFinite(numericLimit))
        {
            return stepValue > 0 ? long.MaxValue : long.MinValue;
        }

        if (stepValue > 0)
        {
            if (numericLimit < initialValue)
            {
                return long.MinValue;
            }

            if (numericLimit >= long.MaxValue)
            {
                return long.MaxValue;
            }

            return (long)Math.Floor(numericLimit);
        }

        if (numericLimit > initialValue)
        {
            return long.MaxValue;
        }

        if (numericLimit <= long.MinValue)
        {
            return long.MinValue;
        }

        return (long)Math.Ceiling(numericLimit);
    }

    private static bool ShouldSkipIntegerForLoop(long initialValue, long limit, long stepValue)
    {
        return stepValue > 0 ? limit < initialValue : initialValue < limit;
    }

    private static long ComputeIntegerForLoopCount(long initialValue, long limit, long stepValue)
    {
        ulong count;
        if (stepValue > 0)
        {
            count = unchecked((ulong)limit) - unchecked((ulong)initialValue);
            if (stepValue != 1)
            {
                count /= (ulong)stepValue;
            }
        }
        else
        {
            count = unchecked((ulong)initialValue) - unchecked((ulong)limit);
            count /= GetUnsignedAbs(stepValue);
        }

        return unchecked((long)count);
    }

    private static ulong GetUnsignedAbs(long value)
    {
        return value >= 0
            ? (ulong)value
            : unchecked((ulong)(-(value + 1))) + 1UL;
    }

    private static bool ShouldSkipFloatForLoop(double initialValue, double limit, double stepValue)
    {
        return stepValue > 0d ? limit < initialValue : initialValue < limit;
    }

    private static bool ShouldContinueFloatForLoop(double value, double limit, double stepValue)
    {
        return stepValue > 0d ? value <= limit : limit <= value;
    }
}
