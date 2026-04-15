using Lua.Bytecode.Chunks;
using Lua.Bytecode.Instructions;
using Lua.Runtime.Execution;
using Lua.Runtime.Objects;
using Lua.Runtime.Values;
using static Lua.Runtime.Values.LuaValueHelper;

namespace Lua.VM;

public sealed partial class LuaVirtualMachine
{
    private void ExecuteTableGet(CallFrame frame, int targetRegister, LuaValue tableValue, LuaValue key)
    {
        SetRegister(frame, targetRegister, ResolveTableGet(tableValue, key));
    }

    private void ExecuteTableSet(CallFrame frame, LuaValue tableValue, LuaValue key, LuaValue value)
    {
        AssignTableValue(tableValue, key, value);
    }

    private LuaValue ResolveTableGet(LuaValue target, LuaValue key)
    {
        var currentTarget = target;
        var metamethodName = GetMetamethodName(IndexMetamethodEvent);

        for (var depth = 0; depth < MaxTableAccessMetamethodDepth; depth++)
        {
            if (currentTarget.Kind == LuaValueKind.Table)
            {
                var table = currentTarget.AsTable();
                if (table.TryGetValue(key, out var value))
                {
                    return value;
                }

                if (!table.TryGetMetamethod(metamethodName, out var metamethod))
                {
                    return LuaValue.Nil;
                }

                if (metamethod.Kind == LuaValueKind.Function)
                {
                    return CallMetamethodResult(metamethod.AsFunction(), currentTarget, key);
                }

                currentTarget = metamethod;
                continue;
            }

            if (!TryGetMetamethod(currentTarget, metamethodName, out var nextMetamethod))
            {
                throw CreateTypeError(currentTarget, "index");
            }

            if (nextMetamethod.Kind == LuaValueKind.Function)
            {
                return CallMetamethodResult(nextMetamethod.AsFunction(), currentTarget, key);
            }

            currentTarget = nextMetamethod;
        }

        throw new LuaRuntimeException(LuaValue.FromString($"'{metamethodName}' chain too long; possible loop"));
    }

    private void AssignTableValue(LuaValue target, LuaValue key, LuaValue value)
    {
        var currentTarget = target;
        var metamethodName = GetMetamethodName(NewIndexMetamethodEvent);

        for (var depth = 0; depth < MaxTableAccessMetamethodDepth; depth++)
        {
            if (currentTarget.Kind == LuaValueKind.Table)
            {
                var table = currentTarget.AsTable();
                if (table.TryGetValue(key, out _))
                {
                    table.SetValue(key, value);
                    return;
                }

                if (!table.TryGetMetamethod(metamethodName, out var metamethod))
                {
                    table.SetValue(key, value);
                    return;
                }

                if (metamethod.Kind == LuaValueKind.Function)
                {
                    Call(metamethod.AsFunction(), [currentTarget, key, value]);
                    return;
                }

                currentTarget = metamethod;
                continue;
            }

            if (!TryGetMetamethod(currentTarget, metamethodName, out var nextMetamethod))
            {
                throw CreateTypeError(currentTarget, "index");
            }

            if (nextMetamethod.Kind == LuaValueKind.Function)
            {
                Call(nextMetamethod.AsFunction(), [currentTarget, key, value]);
                return;
            }

            currentTarget = nextMetamethod;
        }

        throw new LuaRuntimeException(LuaValue.FromString($"'{metamethodName}' chain too long; possible loop"));
    }

    private void ExecuteSetList(CallFrame frame, LuaPrototype prototype, LuaInstruction instruction)
    {
        var tableValue = GetRegister(frame, instruction.A);
        if (tableValue.Kind != LuaValueKind.Table)
        {
            throw new NotImplementedException("SETLIST currently supports tables only.");
        }

        var elementCount = instruction.VB;
        if (elementCount == 0)
        {
            elementCount = GetOpenValueCount(frame, instruction.A + 1);
        }

        var startIndex = instruction.VC;
        if (instruction.K != 0)
        {
            startIndex += ReadFollowingExtraArgument(frame, prototype, instruction.Opcode) * (LuaInstructionLayout.MaxArgVC + 1);
        }

        var currentIndex = startIndex + elementCount;
        var table = tableValue.AsTable();

        for (var offset = elementCount; offset > 0; offset--)
        {
            table.SetValue(LuaValue.FromInteger(currentIndex), GetRegister(frame, instruction.A + offset));
            currentIndex -= 1;
        }
    }

    private void ExecuteNewTable(CallFrame frame, LuaPrototype prototype, LuaInstruction instruction)
    {
        var extraArgument = ReadFollowingExtraArgument(frame, prototype, instruction.Opcode);
        var hashCapacity = instruction.VB > 0 ? 1L << (instruction.VB - 1) : 0L;
        var arrayCapacity = (long)instruction.VC;

        if (instruction.K != 0)
        {
            arrayCapacity += (long)extraArgument * (LuaInstructionLayout.MaxArgVC + 1L);
        }

        var table = new LuaTable(
            arrayCapacity: SaturateCapacity(arrayCapacity),
            hashCapacity: SaturateCapacity(hashCapacity));

        SetRegister(frame, instruction.A, LuaValue.FromTable(table));
    }

    private void ExecuteSelf(CallFrame frame, LuaPrototype prototype, LuaInstruction instruction)
    {
        var receiver = GetRegister(frame, instruction.B);
        var key = ConvertConstant(prototype.Constants[instruction.C]);

        SetRegister(frame, instruction.A + 1, receiver);
        ExecuteTableGet(frame, instruction.A, receiver, key);
    }
}
