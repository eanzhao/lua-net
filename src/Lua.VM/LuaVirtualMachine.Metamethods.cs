using Lua.Bytecode.Chunks;
using Lua.Bytecode.Instructions;
using Lua.Runtime.Execution;
using Lua.Runtime.Objects;
using Lua.Runtime.Values;
using static Lua.Runtime.Values.LuaValueHelper;

namespace Lua.VM;

public sealed partial class LuaVirtualMachine
{
    private void ExecuteMetamethodBinary(CallFrame frame, LuaPrototype prototype, LuaInstruction instruction)
    {
        var resultRegister = ReadPreviousInstruction(frame, prototype, instruction.Opcode).A;
        ExecuteBinaryMetamethod(
            frame,
            resultRegister,
            GetRegister(frame, instruction.A),
            GetRegister(frame, instruction.B),
            instruction.C);
    }

    private void ExecuteMetamethodBinaryImmediate(CallFrame frame, LuaPrototype prototype, LuaInstruction instruction)
    {
        var resultRegister = ReadPreviousInstruction(frame, prototype, instruction.Opcode).A;
        var registerOperand = GetRegister(frame, instruction.A);
        var immediateOperand = LuaValue.FromInteger(ToSignedB(instruction.B));

        if (instruction.K != 0)
        {
            ExecuteBinaryMetamethod(frame, resultRegister, immediateOperand, registerOperand, instruction.C);
            return;
        }

        ExecuteBinaryMetamethod(frame, resultRegister, registerOperand, immediateOperand, instruction.C);
    }

    private void ExecuteMetamethodBinaryConstant(CallFrame frame, LuaPrototype prototype, LuaInstruction instruction)
    {
        var resultRegister = ReadPreviousInstruction(frame, prototype, instruction.Opcode).A;
        var registerOperand = GetRegister(frame, instruction.A);
        var constantOperand = ConvertConstant(prototype.Constants[instruction.B]);

        if (instruction.K != 0)
        {
            ExecuteBinaryMetamethod(frame, resultRegister, constantOperand, registerOperand, instruction.C);
            return;
        }

        ExecuteBinaryMetamethod(frame, resultRegister, registerOperand, constantOperand, instruction.C);
    }

    private void ExecuteBinaryMetamethod(
        CallFrame frame,
        int resultRegister,
        LuaValue left,
        LuaValue right,
        int eventIndex)
    {
        var metamethod = ResolveBinaryMetamethod(left, right, eventIndex).AsFunction();
        var results = Call(metamethod, [left, right]);
        SetRegister(frame, resultRegister, results.Length == 0 ? LuaValue.Nil : results[0]);
    }

    private LuaValue ResolveBinaryMetamethod(LuaValue left, LuaValue right, int eventIndex)
    {
        var metamethodName = GetMetamethodName(eventIndex);

        if (State.TryGetMetamethod(left, metamethodName, out var metamethod) ||
            State.TryGetMetamethod(right, metamethodName, out metamethod))
        {
            return metamethod;
        }

        if (IsBitwiseMetamethodEvent(eventIndex) &&
            (IsNumericWithoutIntegerRepresentation(left) || IsNumericWithoutIntegerRepresentation(right)))
        {
            throw CreateIntegerRepresentationError();
        }

        if (IsArithmeticMetamethodEvent(eventIndex))
        {
            var operand = IsArithmeticOperandCompatible(left) ? right : left;
            throw new LuaRuntimeException(
                LuaValue.FromString($"attempt to perform arithmetic on a {GetTypeName(operand)} value"));
        }

        throw new LuaRuntimeException(
            LuaValue.FromString($"no metamethod '{metamethodName}' for {GetTypeName(left)} and {GetTypeName(right)}"));
    }

    private LuaValue CallBinaryMetamethodResult(LuaValue left, LuaValue right, int eventIndex)
    {
        return CallMetamethodResult(ResolveBinaryMetamethod(left, right, eventIndex).AsFunction(), left, right);
    }

    private bool CallBinaryMetamethodBoolean(LuaValue left, LuaValue right, int eventIndex)
    {
        return IsTruthy(CallBinaryMetamethodResult(left, right, eventIndex));
    }

    private LuaValue CallMetamethodResult(LuaClosure metamethod, LuaValue left, LuaValue right)
    {
        var results = Call(metamethod, [left, right]);
        return results.Length == 0 ? LuaValue.Nil : results[0];
    }

    private (LuaClosure Closure, IReadOnlyList<LuaValue> Arguments) ResolveCallable(
        LuaValue callable,
        IReadOnlyList<LuaValue> arguments)
    {
        var currentCallable = callable;
        var currentArguments = arguments;

        for (var depth = 0; depth < MaxCallMetamethodDepth; depth++)
        {
            if (currentCallable.Kind == LuaValueKind.Function)
            {
                return (currentCallable.AsFunction(), currentArguments);
            }

            if (!State.TryGetMetamethod(currentCallable, GetMetamethodName(CallMetamethodEvent), out var metamethod))
            {
                throw CreateTypeError(currentCallable, "call");
            }

            currentArguments = PrependArgument(currentCallable, currentArguments);
            currentCallable = metamethod;
        }

        throw new LuaRuntimeException(LuaValue.FromString("'__call' chain too long"));
    }

    private static LuaValue[] PrependArgument(LuaValue head, IReadOnlyList<LuaValue> tail)
    {
        var arguments = new LuaValue[tail.Count + 1];
        arguments[0] = head;
        for (var index = 0; index < tail.Count; index++)
        {
            arguments[index + 1] = tail[index];
        }

        return arguments;
    }

    private static string GetMetamethodName(int eventIndex)
    {
        if (eventIndex < 0 || eventIndex >= MetamethodNames.Length)
        {
            throw new InvalidOperationException($"Unknown metamethod event index '{eventIndex}'.");
        }

        return MetamethodNames[eventIndex];
    }

    private static bool IsArithmeticMetamethodEvent(int eventIndex)
    {
        return eventIndex >= AddMetamethodEvent && eventIndex <= ShiftRightMetamethodEvent;
    }

    private static bool IsBitwiseMetamethodEvent(int eventIndex)
    {
        return eventIndex >= BitwiseAndMetamethodEvent && eventIndex <= ShiftRightMetamethodEvent;
    }

    private static bool IsArithmeticOperandCompatible(LuaValue value)
    {
        return TryGetNumber(value, out _) ||
               (value.Kind == LuaValueKind.String && TryParseLuaStringNumber(value.AsString(), out _));
    }

    private static bool HasFollowingMetamethodInstruction(CallFrame frame, LuaPrototype prototype)
    {
        if (frame.ProgramCounter >= prototype.Code.Length)
        {
            return false;
        }

        var next = LuaInstruction.FromRaw(prototype.Code[frame.ProgramCounter]).Opcode;
        return next is LuaOpcode.MmBin or LuaOpcode.MmBinI or LuaOpcode.MmBinK;
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
