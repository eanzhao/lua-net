using System.Globalization;
using System.Text;
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
                    case LuaOpcode.AddI:
                        ExecuteAddImmediate(frame, prototype, instruction);
                        break;
                    case LuaOpcode.AddK:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), ConvertConstant(prototype.Constants[instruction.C]), TryAdd);
                        break;
                    case LuaOpcode.SubK:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), ConvertConstant(prototype.Constants[instruction.C]), TrySubtract);
                        break;
                    case LuaOpcode.MulK:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), ConvertConstant(prototype.Constants[instruction.C]), TryMultiply);
                        break;
                    case LuaOpcode.ModK:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), ConvertConstant(prototype.Constants[instruction.C]), TryModulo);
                        break;
                    case LuaOpcode.PowK:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), ConvertConstant(prototype.Constants[instruction.C]), TryPower);
                        break;
                    case LuaOpcode.DivK:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), ConvertConstant(prototype.Constants[instruction.C]), TryDivide);
                        break;
                    case LuaOpcode.IDivK:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), ConvertConstant(prototype.Constants[instruction.C]), TryIntegerDivide);
                        break;
                    case LuaOpcode.BandK:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), ConvertConstant(prototype.Constants[instruction.C]), TryBitwiseAnd);
                        break;
                    case LuaOpcode.BorK:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), ConvertConstant(prototype.Constants[instruction.C]), TryBitwiseOr);
                        break;
                    case LuaOpcode.BXorK:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), ConvertConstant(prototype.Constants[instruction.C]), TryBitwiseXor);
                        break;
                    case LuaOpcode.ShlI:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, LuaValue.FromInteger(ToSignedC(instruction.C)), GetRegister(frame, instruction.B), TryShiftLeft);
                        break;
                    case LuaOpcode.ShrI:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), LuaValue.FromInteger(ToSignedC(instruction.C)), TryShiftRight);
                        break;
                    case LuaOpcode.Add:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), GetRegister(frame, instruction.C), TryAdd);
                        break;
                    case LuaOpcode.Sub:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), GetRegister(frame, instruction.C), TrySubtract);
                        break;
                    case LuaOpcode.Mul:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), GetRegister(frame, instruction.C), TryMultiply);
                        break;
                    case LuaOpcode.Mod:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), GetRegister(frame, instruction.C), TryModulo);
                        break;
                    case LuaOpcode.Pow:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), GetRegister(frame, instruction.C), TryPower);
                        break;
                    case LuaOpcode.Div:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), GetRegister(frame, instruction.C), TryDivide);
                        break;
                    case LuaOpcode.IDiv:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), GetRegister(frame, instruction.C), TryIntegerDivide);
                        break;
                    case LuaOpcode.Band:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), GetRegister(frame, instruction.C), TryBitwiseAnd);
                        break;
                    case LuaOpcode.Bor:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), GetRegister(frame, instruction.C), TryBitwiseOr);
                        break;
                    case LuaOpcode.BXor:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), GetRegister(frame, instruction.C), TryBitwiseXor);
                        break;
                    case LuaOpcode.Shl:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), GetRegister(frame, instruction.C), TryShiftLeft);
                        break;
                    case LuaOpcode.Shr:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), GetRegister(frame, instruction.C), TryShiftRight);
                        break;
                    case LuaOpcode.Unm:
                        ExecuteUnaryArithmetic(frame, instruction, TryUnaryMinus);
                        break;
                    case LuaOpcode.BNot:
                        ExecuteUnaryArithmetic(frame, instruction, TryBitwiseNot);
                        break;
                    case LuaOpcode.Not:
                        SetRegister(frame, instruction.A, LuaValue.FromBoolean(!IsTruthy(GetRegister(frame, instruction.B))));
                        break;
                    case LuaOpcode.Len:
                        ExecuteLength(frame, instruction);
                        break;
                    case LuaOpcode.Concat:
                        ExecuteConcat(frame, instruction);
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
                    case LuaOpcode.Eq:
                        ExecuteEqualityComparison(frame, GetRegister(frame, instruction.A), GetRegister(frame, instruction.B), instruction.K);
                        break;
                    case LuaOpcode.Lt:
                        ExecuteRegisterComparison(frame, GetRegister(frame, instruction.A), GetRegister(frame, instruction.B), instruction.K, static comparison => comparison < 0);
                        break;
                    case LuaOpcode.Le:
                        ExecuteRegisterComparison(frame, GetRegister(frame, instruction.A), GetRegister(frame, instruction.B), instruction.K, static comparison => comparison <= 0);
                        break;
                    case LuaOpcode.EqK:
                        ExecuteEqualityComparison(frame, GetRegister(frame, instruction.A), ConvertConstant(prototype.Constants[instruction.B]), instruction.K);
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
                    case LuaOpcode.TestSet:
                        ExecuteTestSet(frame, instruction);
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

    private void ExecuteAddImmediate(CallFrame frame, LuaPrototype prototype, LuaInstruction instruction)
    {
        var left = GetRegister(frame, instruction.B);
        var right = LuaValue.FromInteger(ToSignedC(instruction.C));

        ExecuteBinaryArithmetic(frame, prototype, instruction, left, right, TryAdd);
    }

    private void ExecuteBinaryArithmetic(
        CallFrame frame,
        LuaPrototype prototype,
        LuaInstruction instruction,
        LuaValue left,
        LuaValue right,
        Func<LuaValue, LuaValue, (bool Success, LuaValue Result)> operation)
    {
        var (success, result) = operation(left, right);
        if (!success)
        {
            throw new NotSupportedException("Arithmetic metamethod dispatch is not implemented yet.");
        }

        SetRegister(frame, instruction.A, result);
        SkipMetamethodInstructionIfPresent(frame, prototype);
    }

    private void ExecuteUnaryArithmetic(
        CallFrame frame,
        LuaInstruction instruction,
        Func<LuaValue, (bool Success, LuaValue Result)> operation)
    {
        var (success, result) = operation(GetRegister(frame, instruction.B));
        if (!success)
        {
            throw new NotSupportedException("Arithmetic metamethod dispatch is not implemented yet.");
        }

        SetRegister(frame, instruction.A, result);
    }

    private void ExecuteLoadNil(CallFrame frame, LuaInstruction instruction)
    {
        for (var index = 0; index <= instruction.B; index++)
        {
            SetRegister(frame, instruction.A + index, LuaValue.Nil);
        }
    }

    private void ExecuteLength(CallFrame frame, LuaInstruction instruction)
    {
        var value = GetRegister(frame, instruction.B);

        if (value.Kind == LuaValueKind.String)
        {
            SetRegister(frame, instruction.A, LuaValue.FromInteger(Encoding.UTF8.GetByteCount(value.AsString())));
            return;
        }

        throw new NotSupportedException("Length semantics beyond strings are not implemented yet.");
    }

    private void ExecuteConcat(CallFrame frame, LuaInstruction instruction)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(instruction.B);

        var builder = new StringBuilder();
        for (var index = 0; index < instruction.B; index++)
        {
            if (!TryGetConcatenationString(GetRegister(frame, instruction.A + index), out var text))
            {
                throw new NotSupportedException("Concatenation metamethod dispatch is not implemented yet.");
            }

            builder.Append(text);
        }

        SetRegister(frame, instruction.A, LuaValue.FromString(builder.ToString()));
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

    private void ExecuteEqualityComparison(CallFrame frame, LuaValue left, LuaValue right, int expected)
    {
        ExecuteConditionalJump(frame, AreEqual(left, right), expected);
    }

    private void ExecuteRegisterComparison(
        CallFrame frame,
        LuaValue left,
        LuaValue right,
        int expected,
        Func<int, bool> accept)
    {
        if (!TryCompareOrdered(left, right, out var comparison))
        {
            throw new NotSupportedException("Comparison metamethod dispatch is not implemented yet.");
        }

        ExecuteConditionalJump(frame, accept(comparison), expected);
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

    private void ExecuteTestSet(CallFrame frame, LuaInstruction instruction)
    {
        var value = GetRegister(frame, instruction.B);

        if (IsTruthy(value) != (instruction.K != 0))
        {
            frame.Advance();
            return;
        }

        SetRegister(frame, instruction.A, value);
        ExecuteNextJump(frame);
    }

    private void ExecuteConditionalJump(CallFrame frame, bool condition, int expected)
    {
        if (condition != (expected != 0))
        {
            frame.Advance();
            return;
        }

        ExecuteNextJump(frame);
    }

    private void ExecuteNextJump(CallFrame frame)
    {
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

    private static string GetDebugName(LuaPrototype prototype)
    {
        if (prototype.LineDefined == 0)
        {
            return "main";
        }

        return $"function@{prototype.LineDefined}";
    }

    private static (bool Success, LuaValue Result) TryAdd(LuaValue left, LuaValue right)
    {
        if (left.Kind == LuaValueKind.Integer && right.Kind == LuaValueKind.Integer)
        {
            return (true, LuaValue.FromInteger(left.AsInteger() + right.AsInteger()));
        }

        if (TryGetNumber(left, out var leftNumber) && TryGetNumber(right, out var rightNumber))
        {
            return (true, LuaValue.FromFloat(leftNumber + rightNumber));
        }

        return (false, LuaValue.Nil);
    }

    private static (bool Success, LuaValue Result) TrySubtract(LuaValue left, LuaValue right)
    {
        if (left.Kind == LuaValueKind.Integer && right.Kind == LuaValueKind.Integer)
        {
            return (true, LuaValue.FromInteger(left.AsInteger() - right.AsInteger()));
        }

        if (TryGetNumber(left, out var leftNumber) && TryGetNumber(right, out var rightNumber))
        {
            return (true, LuaValue.FromFloat(leftNumber - rightNumber));
        }

        return (false, LuaValue.Nil);
    }

    private static (bool Success, LuaValue Result) TryMultiply(LuaValue left, LuaValue right)
    {
        if (left.Kind == LuaValueKind.Integer && right.Kind == LuaValueKind.Integer)
        {
            return (true, LuaValue.FromInteger(left.AsInteger() * right.AsInteger()));
        }

        if (TryGetNumber(left, out var leftNumber) && TryGetNumber(right, out var rightNumber))
        {
            return (true, LuaValue.FromFloat(leftNumber * rightNumber));
        }

        return (false, LuaValue.Nil);
    }

    private static (bool Success, LuaValue Result) TryPower(LuaValue left, LuaValue right)
    {
        if (TryGetNumber(left, out var leftNumber) && TryGetNumber(right, out var rightNumber))
        {
            return (true, LuaValue.FromFloat(Math.Pow(leftNumber, rightNumber)));
        }

        return (false, LuaValue.Nil);
    }

    private static (bool Success, LuaValue Result) TryDivide(LuaValue left, LuaValue right)
    {
        if (TryGetNumber(left, out var leftNumber) && TryGetNumber(right, out var rightNumber))
        {
            return (true, LuaValue.FromFloat(leftNumber / rightNumber));
        }

        return (false, LuaValue.Nil);
    }

    private static (bool Success, LuaValue Result) TryIntegerDivide(LuaValue left, LuaValue right)
    {
        if (left.Kind == LuaValueKind.Integer && right.Kind == LuaValueKind.Integer)
        {
            return (true, LuaValue.FromInteger(LuaIntegerFloorDivide(left.AsInteger(), right.AsInteger())));
        }

        if (TryGetNumber(left, out var leftNumber) && TryGetNumber(right, out var rightNumber))
        {
            return (true, LuaValue.FromFloat(Math.Floor(leftNumber / rightNumber)));
        }

        return (false, LuaValue.Nil);
    }

    private static (bool Success, LuaValue Result) TryModulo(LuaValue left, LuaValue right)
    {
        if (left.Kind == LuaValueKind.Integer && right.Kind == LuaValueKind.Integer)
        {
            return (true, LuaValue.FromInteger(LuaIntegerModulo(left.AsInteger(), right.AsInteger())));
        }

        if (TryGetNumber(left, out var leftNumber) && TryGetNumber(right, out var rightNumber))
        {
            var quotient = Math.Floor(leftNumber / rightNumber);
            return (true, LuaValue.FromFloat(leftNumber - quotient * rightNumber));
        }

        return (false, LuaValue.Nil);
    }

    private static (bool Success, LuaValue Result) TryUnaryMinus(LuaValue value)
    {
        return value.Kind switch
        {
            LuaValueKind.Integer => (true, LuaValue.FromInteger(-value.AsInteger())),
            LuaValueKind.Float => (true, LuaValue.FromFloat(-value.AsFloat())),
            _ => (false, LuaValue.Nil)
        };
    }

    private static (bool Success, LuaValue Result) TryBitwiseAnd(LuaValue left, LuaValue right)
    {
        return TryBinaryIntegerOperation(left, right, static (x, y) => x & y);
    }

    private static (bool Success, LuaValue Result) TryBitwiseOr(LuaValue left, LuaValue right)
    {
        return TryBinaryIntegerOperation(left, right, static (x, y) => x | y);
    }

    private static (bool Success, LuaValue Result) TryBitwiseXor(LuaValue left, LuaValue right)
    {
        return TryBinaryIntegerOperation(left, right, static (x, y) => x ^ y);
    }

    private static (bool Success, LuaValue Result) TryShiftLeft(LuaValue left, LuaValue right)
    {
        return TryBinaryIntegerOperation(left, right, static (x, y) => LuaShiftLeft(x, y));
    }

    private static (bool Success, LuaValue Result) TryShiftRight(LuaValue left, LuaValue right)
    {
        return TryBinaryIntegerOperation(left, right, static (x, y) => LuaShiftLeft(x, -y));
    }

    private static (bool Success, LuaValue Result) TryBitwiseNot(LuaValue value)
    {
        if (!TryGetInteger(value, out var integer))
        {
            return (false, LuaValue.Nil);
        }

        return (true, LuaValue.FromInteger(~integer));
    }

    private static bool AreEqual(LuaValue left, LuaValue right)
    {
        if (left.Kind == right.Kind)
        {
            return left == right;
        }

        if (TryGetNumber(left, out var leftNumber) && TryGetNumber(right, out var rightNumber))
        {
            return leftNumber.Equals(rightNumber);
        }

        return false;
    }

    private static bool TryCompareOrdered(LuaValue left, LuaValue right, out int comparison)
    {
        if (left.Kind == LuaValueKind.String && right.Kind == LuaValueKind.String)
        {
            comparison = StringComparer.Ordinal.Compare(left.AsString(), right.AsString());
            return true;
        }

        if (TryGetNumber(left, out var leftNumber) && TryGetNumber(right, out var rightNumber))
        {
            comparison = leftNumber.CompareTo(rightNumber);
            return true;
        }

        comparison = default;
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

    private static bool TryGetInteger(LuaValue value, out long result)
    {
        switch (value.Kind)
        {
            case LuaValueKind.Integer:
                result = value.AsInteger();
                return true;
            case LuaValueKind.Float:
            {
                var number = value.AsFloat();
                if (double.IsFinite(number) &&
                    number >= long.MinValue &&
                    number <= long.MaxValue &&
                    Math.Truncate(number) == number)
                {
                    result = (long)number;
                    return true;
                }

                break;
            }
        }

        result = default;
        return false;
    }

    private static bool TryGetConcatenationString(LuaValue value, out string result)
    {
        switch (value.Kind)
        {
            case LuaValueKind.String:
                result = value.AsString();
                return true;
            case LuaValueKind.Integer:
                result = value.AsInteger().ToString(CultureInfo.InvariantCulture);
                return true;
            case LuaValueKind.Float:
                result = value.AsFloat().ToString("G17", CultureInfo.InvariantCulture);
                return true;
            default:
                result = string.Empty;
                return false;
        }
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

    private static int ToSignedC(int value)
    {
        return value - LuaInstructionLayout.OffsetSC;
    }

    private static long LuaIntegerFloorDivide(long left, long right)
    {
        if (right == 0)
        {
            throw new DivideByZeroException("attempt to divide by zero");
        }

        if (right == -1 && left == long.MinValue)
        {
            return -left;
        }

        var quotient = left / right;
        if ((left ^ right) < 0 && left % right != 0)
        {
            quotient -= 1;
        }

        return quotient;
    }

    private static long LuaIntegerModulo(long left, long right)
    {
        if (right == 0)
        {
            throw new DivideByZeroException("attempt to perform 'n%0'");
        }

        if (right == -1)
        {
            return 0;
        }

        var remainder = left % right;
        if (remainder != 0 && (remainder ^ right) < 0)
        {
            remainder += right;
        }

        return remainder;
    }

    private static (bool Success, LuaValue Result) TryBinaryIntegerOperation(
        LuaValue left,
        LuaValue right,
        Func<long, long, long> operation)
    {
        if (!TryGetInteger(left, out var leftInteger) || !TryGetInteger(right, out var rightInteger))
        {
            return (false, LuaValue.Nil);
        }

        return (true, LuaValue.FromInteger(operation(leftInteger, rightInteger)));
    }

    private static long LuaShiftLeft(long value, long shift)
    {
        if (shift < 0)
        {
            if (shift <= -64)
            {
                return 0;
            }

            return value >> (int)(-shift);
        }

        if (shift >= 64)
        {
            return 0;
        }

        return value << (int)shift;
    }

    private static void SkipMetamethodInstructionIfPresent(CallFrame frame, LuaPrototype prototype)
    {
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
}
