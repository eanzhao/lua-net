namespace Lua.Runtime.Execution;

public enum LuaPendingCallKind
{
    Registers,
    TailReturn
}

public sealed class LuaPendingCall
{
    private LuaPendingCall(LuaPendingCallKind kind, int registerIndex, int resultCount)
    {
        if (kind == LuaPendingCallKind.Registers)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(registerIndex);
        }

        Kind = kind;
        RegisterIndex = registerIndex;
        ResultCount = resultCount;
    }

    public LuaPendingCallKind Kind { get; }

    public int RegisterIndex { get; }

    public int ResultCount { get; }

    public static LuaPendingCall ForRegisters(int registerIndex, int resultCount)
    {
        return new LuaPendingCall(LuaPendingCallKind.Registers, registerIndex, resultCount);
    }

    public static LuaPendingCall ForTailReturn()
    {
        return new LuaPendingCall(LuaPendingCallKind.TailReturn, 0, 0);
    }
}
