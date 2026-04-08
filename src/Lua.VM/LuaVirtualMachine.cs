using Lua.Bytecode.Chunks;
using Lua.Bytecode.Instructions;
using Lua.Runtime.Execution;
using Lua.Runtime.Objects;
using Lua.Runtime.Values;
using Lua.VM.Closures;

namespace Lua.VM;

public sealed class LuaVirtualMachine
{
    public LuaVirtualMachine()
    {
        State = new LuaState();
    }

    public LuaState State { get; }

    public LuaValue[] Execute(LuaChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        var closure = CreateClosure(chunk.MainFunction);
        return Call(closure);
    }

    public LuaValue[] Call(LuaClosure closure, IReadOnlyList<LuaValue>? arguments = null)
    {
        ArgumentNullException.ThrowIfNull(closure);

        var body = GetBytecodeBody(closure);
        return ExecuteClosure(closure, body.Prototype, arguments ?? Array.Empty<LuaValue>());
    }

    public LuaClosure CreateClosure(LuaPrototype prototype, string? debugName = null)
    {
        ArgumentNullException.ThrowIfNull(prototype);

        return new LuaClosure(
            debugName ?? GetDebugName(prototype),
            prototype.Upvalues.Length,
            new LuaBytecodeClosureBody(prototype));
    }

    private LuaValue[] ExecuteClosure(LuaClosure closure, LuaPrototype prototype, IReadOnlyList<LuaValue> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var baseIndex = State.Stack.Count;
        var frameSize = Math.Max(prototype.MaxStackSize, arguments.Count);

        State.Stack.SetTop(baseIndex + frameSize);
        InitializeRegisters(baseIndex, arguments);

        var frame = new CallFrame(closure, baseIndex, expectedResults: 0);
        State.PushFrame(frame);

        try
        {
            while (frame.ProgramCounter < prototype.Code.Length)
            {
                var instruction = LuaInstruction.FromRaw(prototype.Code[frame.ProgramCounter]);
                frame.Advance();

                switch (instruction.Opcode)
                {
                    case LuaOpcode.Move:
                        SetRegister(frame, instruction.A, GetRegister(frame, instruction.B));
                        break;
                    case LuaOpcode.LoadFalse:
                        SetRegister(frame, instruction.A, LuaValue.FromBoolean(false));
                        break;
                    case LuaOpcode.LoadTrue:
                        SetRegister(frame, instruction.A, LuaValue.FromBoolean(true));
                        break;
                    case LuaOpcode.LoadNil:
                        ExecuteLoadNil(frame, instruction);
                        break;
                    case LuaOpcode.LoadI:
                        SetRegister(frame, instruction.A, LuaValue.FromInteger(instruction.SBx));
                        break;
                    case LuaOpcode.LoadK:
                        SetRegister(frame, instruction.A, ConvertConstant(prototype.Constants[instruction.Bx]));
                        break;
                    case LuaOpcode.Add:
                        ExecuteAdd(frame, prototype, instruction);
                        break;
                    case LuaOpcode.Call:
                        ExecuteCall(frame, instruction);
                        break;
                    case LuaOpcode.TailCall:
                        return ExecuteTailCall(frame, instruction);
                    case LuaOpcode.Return:
                        return ExecuteReturn(frame, instruction);
                    case LuaOpcode.Return0:
                        return [];
                    case LuaOpcode.Return1:
                        return [GetRegister(frame, instruction.A)];
                    case LuaOpcode.Closure:
                        ExecuteClosureInstruction(frame, prototype, instruction);
                        break;
                    case LuaOpcode.VarArgPrep:
                        break;
                    case LuaOpcode.Jmp:
                        ExecuteJump(frame, instruction);
                        break;
                    case LuaOpcode.EqI:
                        ExecuteImmediateComparison(frame, instruction, static (left, right) => left == right, allowNonNumericAsFalse: true);
                        break;
                    case LuaOpcode.LtI:
                        ExecuteImmediateComparison(frame, instruction, static (left, right) => left < right);
                        break;
                    case LuaOpcode.LeI:
                        ExecuteImmediateComparison(frame, instruction, static (left, right) => left <= right);
                        break;
                    case LuaOpcode.GtI:
                        ExecuteImmediateComparison(frame, instruction, static (left, right) => left > right);
                        break;
                    case LuaOpcode.GeI:
                        ExecuteImmediateComparison(frame, instruction, static (left, right) => left >= right);
                        break;
                    case LuaOpcode.Test:
                        ExecuteConditionalJump(frame, IsTruthy(GetRegister(frame, instruction.A)), instruction.K);
                        break;
                    case LuaOpcode.MmBin:
                    case LuaOpcode.MmBinI:
                    case LuaOpcode.MmBinK:
                        throw new NotSupportedException("Metamethod dispatch is not implemented yet.");
                    default:
                        throw new NotSupportedException($"Opcode '{instruction.Name}' is not implemented yet.");
                }
            }

            return [];
        }
        finally
        {
            State.PopFrame();
            State.Stack.SetTop(baseIndex);
        }
    }

    private void ExecuteAdd(CallFrame frame, LuaPrototype prototype, LuaInstruction instruction)
    {
        var left = GetRegister(frame, instruction.B);
        var right = GetRegister(frame, instruction.C);

        if (!TryAdd(left, right, out var result))
        {
            throw new NotSupportedException("Arithmetic metamethod dispatch is not implemented yet.");
        }

        SetRegister(frame, instruction.A, result);

        if (frame.ProgramCounter >= prototype.Code.Length)
        {
            return;
        }

        var next = LuaInstruction.FromRaw(prototype.Code[frame.ProgramCounter]).Opcode;
        if (next is LuaOpcode.MmBin or LuaOpcode.MmBinI or LuaOpcode.MmBinK)
        {
            frame.Advance();
        }
    }

    private void ExecuteLoadNil(CallFrame frame, LuaInstruction instruction)
    {
        for (var index = 0; index <= instruction.B; index++)
        {
            SetRegister(frame, instruction.A + index, LuaValue.Nil);
        }
    }

    private void ExecuteCall(CallFrame frame, LuaInstruction instruction)
    {
        if (instruction.B == 0)
        {
            throw new NotSupportedException("Open argument CALL is not implemented yet.");
        }

        if (instruction.C == 0)
        {
            throw new NotSupportedException("Open result CALL is not implemented yet.");
        }

        var closure = GetRegister(frame, instruction.A).AsFunction();
        var arguments = ReadArguments(frame, instruction.A, instruction.B);
        var results = Call(closure, arguments);

        WriteResults(frame, instruction.A, instruction.C - 1, results);
    }

    private LuaValue[] ExecuteTailCall(CallFrame frame, LuaInstruction instruction)
    {
        if (instruction.B == 0)
        {
            throw new NotSupportedException("Open argument TAILCALL is not implemented yet.");
        }

        var closure = GetRegister(frame, instruction.A).AsFunction();
        var arguments = ReadArguments(frame, instruction.A, instruction.B);
        return Call(closure, arguments);
    }

    private LuaValue[] ExecuteReturn(CallFrame frame, LuaInstruction instruction)
    {
        if (instruction.B == 0)
        {
            throw new NotSupportedException("Open result RETURN is not implemented yet.");
        }

        var resultCount = instruction.B - 1;
        var results = new LuaValue[resultCount];

        for (var index = 0; index < resultCount; index++)
        {
            results[index] = GetRegister(frame, instruction.A + index);
        }

        return results;
    }

    private void ExecuteClosureInstruction(CallFrame frame, LuaPrototype prototype, LuaInstruction instruction)
    {
        var nestedPrototype = prototype.NestedPrototypes[instruction.Bx];
        var nestedClosure = CreateClosure(nestedPrototype);

        SetRegister(frame, instruction.A, LuaValue.FromFunction(nestedClosure));
    }

    private void ExecuteJump(CallFrame frame, LuaInstruction instruction)
    {
        frame.Advance(instruction.SJ);
    }

    private void ExecuteImmediateComparison(
        CallFrame frame,
        LuaInstruction instruction,
        Func<double, double, bool> comparison,
        bool allowNonNumericAsFalse = false)
    {
        var value = GetRegister(frame, instruction.A);
        var immediate = ToSignedB(instruction.B);

        if (!TryGetNumber(value, out var numericValue))
        {
            if (allowNonNumericAsFalse)
            {
                ExecuteConditionalJump(frame, false, instruction.K);
                return;
            }

            throw new NotSupportedException("Comparison metamethod dispatch is not implemented yet.");
        }

        ExecuteConditionalJump(frame, comparison(numericValue, immediate), instruction.K);
    }

    private void ExecuteConditionalJump(CallFrame frame, bool condition, int expected)
    {
        if (condition != (expected != 0))
        {
            frame.Advance();
            return;
        }

        if (frame.ProgramCounter >= GetCurrentPrototype(frame).Code.Length)
        {
            throw new InvalidOperationException("Conditional instruction is missing the following jump.");
        }

        var jumpInstruction = LuaInstruction.FromRaw(GetCurrentPrototype(frame).Code[frame.ProgramCounter]);
        if (jumpInstruction.Opcode != LuaOpcode.Jmp)
        {
            throw new InvalidOperationException("Conditional instruction must be followed by JMP.");
        }

        frame.Advance(jumpInstruction.SJ + 1);
    }

    private static LuaBytecodeClosureBody GetBytecodeBody(LuaClosure closure)
    {
        if (closure.Body is not LuaBytecodeClosureBody body)
        {
            throw new InvalidOperationException("The closure does not contain a bytecode body.");
        }

        return body;
    }

    private void InitializeRegisters(int baseIndex, IReadOnlyList<LuaValue> arguments)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            State.Stack[baseIndex + index] = arguments[index];
        }
    }

    private LuaValue[] ReadArguments(CallFrame frame, int functionRegister, int functionAndArgumentCount)
    {
        var argumentCount = functionAndArgumentCount - 1;
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

    private LuaValue GetRegister(CallFrame frame, int registerIndex)
    {
        return State.Stack[frame.BaseIndex + registerIndex];
    }

    private void SetRegister(CallFrame frame, int registerIndex, LuaValue value)
    {
        State.Stack[frame.BaseIndex + registerIndex] = value;
    }

    private static LuaPrototype GetCurrentPrototype(CallFrame frame)
    {
        return GetBytecodeBody(frame.Closure).Prototype;
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
            _ => throw new NotSupportedException($"Constant kind '{constant.Kind}' is not implemented yet.")
        };
    }

    private static bool TryAdd(LuaValue left, LuaValue right, out LuaValue result)
    {
        if (left.Kind == LuaValueKind.Integer && right.Kind == LuaValueKind.Integer)
        {
            result = LuaValue.FromInteger(left.AsInteger() + right.AsInteger());
            return true;
        }

        if (TryGetNumber(left, out var leftNumber) && TryGetNumber(right, out var rightNumber))
        {
            result = LuaValue.FromFloat(leftNumber + rightNumber);
            return true;
        }

        result = LuaValue.Nil;
        return false;
    }

    private static bool TryGetNumber(LuaValue value, out double result)
    {
        switch (value.Kind)
        {
            case LuaValueKind.Integer:
                result = value.AsInteger();
                return true;
            case LuaValueKind.Float:
                result = value.AsFloat();
                return true;
            default:
                result = default;
                return false;
        }
    }

    private static string GetDebugName(LuaPrototype prototype)
    {
        if (prototype.LineDefined == 0)
        {
            return "main";
        }

        return $"function@{prototype.LineDefined}";
    }

    private static bool IsTruthy(LuaValue value)
    {
        return value.Kind switch
        {
            LuaValueKind.Nil => false,
            LuaValueKind.Boolean => value.AsBoolean(),
            _ => true
        };
    }

    private static int ToSignedB(int value)
    {
        return value - LuaInstructionLayout.OffsetSC;
    }
}
