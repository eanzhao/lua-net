using System.Text;
using Lua.Bytecode.Chunks;
using Lua.Bytecode.Instructions;
using Lua.Runtime.Execution;
using Lua.Runtime.Values;
using static Lua.Runtime.Values.LuaValueHelper;

namespace Lua.VM;

public sealed partial class LuaVirtualMachine
{
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
            if (HasFollowingMetamethodInstruction(frame, prototype))
            {
                return;
            }

            throw new NotImplementedException("Arithmetic metamethod dispatch is not implemented yet.");
        }

        SetRegister(frame, instruction.A, result);
        SkipMetamethodInstructionIfPresent(frame, prototype);
    }

    private void ExecuteUnaryArithmetic(
        CallFrame frame,
        LuaInstruction instruction,
        Func<LuaValue, (bool Success, LuaValue Result)> operation,
        int metamethodEvent)
    {
        var operand = GetRegister(frame, instruction.B);
        var (success, result) = operation(operand);
        if (!success)
        {
            result = CallBinaryMetamethodResult(operand, operand, metamethodEvent);
        }

        SetRegister(frame, instruction.A, result);
    }

    private void ExecuteLength(CallFrame frame, LuaInstruction instruction)
    {
        var value = GetRegister(frame, instruction.B);

        if (value.Kind == LuaValueKind.String)
        {
            SetRegister(frame, instruction.A, LuaValue.FromInteger(Encoding.UTF8.GetByteCount(value.AsString())));
            return;
        }

        if (value.Kind == LuaValueKind.Table)
        {
            if (TryGetMetamethod(value, GetMetamethodName(LengthMetamethodEvent), out var metamethod))
            {
                SetRegister(frame, instruction.A, CallMetamethodResult(metamethod.AsFunction(), value, value));
                return;
            }

            SetRegister(frame, instruction.A, LuaValue.FromInteger(value.AsTable().GetSequenceLength()));
            return;
        }

        if (TryGetMetamethod(value, GetMetamethodName(LengthMetamethodEvent), out var dynamicMetamethod))
        {
            SetRegister(frame, instruction.A, CallMetamethodResult(dynamicMetamethod.AsFunction(), value, value));
            return;
        }

        throw CreateTypeError(value, "get length of");
    }

    private void ExecuteConcat(CallFrame frame, LuaInstruction instruction)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(instruction.B);

        var values = new LuaValue[instruction.B];
        for (var index = 0; index < instruction.B; index++)
        {
            values[index] = GetRegister(frame, instruction.A + index);
        }

        var total = values.Length;
        while (total > 1)
        {
            var left = values[total - 2];
            var right = values[total - 1];

            if (!TryConcatenateValues(left, right, out var result))
            {
                result = CallBinaryMetamethodResult(left, right, ConcatMetamethodEvent);
            }

            values[total - 2] = result;
            total -= 1;
        }

        SetRegister(frame, instruction.A, values[0]);
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

    private static bool TryGetConcatenationString(LuaValue value, out string result)
    {
        switch (value.Kind)
        {
            case LuaValueKind.String:
                result = value.AsString();
                return true;
            case LuaValueKind.Integer:
                result = value.AsInteger().ToString(System.Globalization.CultureInfo.InvariantCulture);
                return true;
            case LuaValueKind.Float:
                result = value.AsFloat().ToString("G17", System.Globalization.CultureInfo.InvariantCulture);
                return true;
            default:
                result = string.Empty;
                return false;
        }
    }

    private static bool TryConcatenateValues(LuaValue left, LuaValue right, out LuaValue result)
    {
        if (TryGetConcatenationString(left, out var leftText) &&
            TryGetConcatenationString(right, out var rightText))
        {
            result = LuaValue.FromString(leftText + rightText);
            return true;
        }

        result = LuaValue.Nil;
        return false;
    }
}
