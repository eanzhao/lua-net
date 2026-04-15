using Lua.Bytecode.Instructions;
using Lua.Runtime.Execution;
using Lua.Runtime.Values;
using static Lua.Runtime.Values.LuaValueHelper;

namespace Lua.VM;

public sealed partial class LuaVirtualMachine
{
    private void ExecuteEqualityComparison(CallFrame frame, LuaValue left, LuaValue right, int expected)
    {
        ExecuteConditionalJump(frame, AreEqualWithMetamethod(left, right), expected);
    }

    private void ExecuteRegisterComparison(
        CallFrame frame,
        LuaValue left,
        LuaValue right,
        int expected,
        Func<int, bool> accept,
        int metamethodEvent)
    {
        if (!TryCompareOrdered(left, right, out var comparison))
        {
            ExecuteConditionalJump(frame, CallBinaryMetamethodBoolean(left, right, metamethodEvent), expected);
            return;
        }

        ExecuteConditionalJump(frame, accept(comparison), expected);
    }

    private void ExecuteImmediateComparison(
        CallFrame frame,
        LuaInstruction instruction,
        Func<double, double, bool> comparison,
        int metamethodEvent = -1,
        bool flipOperands = false,
        bool allowNonNumericAsFalse = false)
    {
        var value = GetRegister(frame, instruction.A);
        var immediateValue = GetImmediateComparisonValue(instruction);

        if (TryGetNumber(value, out var numericValue) && TryGetNumber(immediateValue, out var numericImmediate))
        {
            ExecuteConditionalJump(frame, comparison(numericValue, numericImmediate), instruction.K);
            return;
        }

        if (allowNonNumericAsFalse)
        {
            ExecuteConditionalJump(frame, false, instruction.K);
            return;
        }

        if (metamethodEvent >= 0)
        {
            var left = flipOperands ? immediateValue : value;
            var right = flipOperands ? value : immediateValue;
            ExecuteConditionalJump(frame, CallBinaryMetamethodBoolean(left, right, metamethodEvent), instruction.K);
            return;
        }

        throw new NotImplementedException("Comparison semantics beyond numeric immediates are not implemented yet.");
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

        frame.Jump(frame.ProgramCounter + jumpInstruction.SJ + 1);
    }

    private static bool AreEqual(LuaValue left, LuaValue right)
    {
        if (left.Kind == right.Kind)
        {
            if (left == right)
            {
                return true;
            }

            if (left.Kind == LuaValueKind.Table &&
                TryGetMetamethod(left, GetMetamethodName(EqualityMetamethodEvent), out _))
            {
                return false;
            }

            return left == right;
        }

        if (TryGetNumber(left, out var leftNumber) && TryGetNumber(right, out var rightNumber))
        {
            return leftNumber.Equals(rightNumber);
        }

        if (left.Kind == right.Kind &&
            (left.Kind == LuaValueKind.Table || left.Kind == LuaValueKind.UserData))
        {
            return false;
        }

        return false;
    }

    private bool AreEqualWithMetamethod(LuaValue left, LuaValue right)
    {
        if (left.Kind != right.Kind)
        {
            return TryGetNumber(left, out var leftNumber) &&
                   TryGetNumber(right, out var rightNumber) &&
                   leftNumber.Equals(rightNumber);
        }

        if (left == right)
        {
            return true;
        }

        if (left.Kind is LuaValueKind.Table or LuaValueKind.UserData)
        {
            if (TryGetMetamethod(left, GetMetamethodName(EqualityMetamethodEvent), out var metamethod) ||
                TryGetMetamethod(right, GetMetamethodName(EqualityMetamethodEvent), out metamethod))
            {
                return IsTruthy(CallMetamethodResult(metamethod.AsFunction(), left, right));
            }
        }

        return AreEqual(left, right);
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

    private static LuaValue GetImmediateComparisonValue(LuaInstruction instruction)
    {
        var immediate = ToSignedB(instruction.B);
        return instruction.C != 0
            ? LuaValue.FromFloat(immediate)
            : LuaValue.FromInteger(immediate);
    }
}
