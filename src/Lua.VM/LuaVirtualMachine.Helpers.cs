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
        if (!TryGetInteger(countValue, out var count) || count < 0 || count > int.MaxValue)
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

        if (pendingException is null)
        {
            Call(closeMethod.AsFunction(), [value]);
            return;
        }

        Call(closeMethod.AsFunction(), [value, GetErrorObject(pendingException)]);
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

    private static LuaRuntimeException CreateTypeError(LuaValue value, string operation)
    {
        return new LuaRuntimeException(LuaValue.FromString($"attempt to {operation} a {GetTypeName(value)} value"));
    }

    private static LuaRuntimeException CreateRuntimeException(string message)
    {
        return new LuaRuntimeException(LuaValue.FromString(message));
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
