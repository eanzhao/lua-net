namespace Lua.Runtime.Execution;

public enum LuaCallReturnTargetKind
{
    None,
    HostCall,
    Registers,
    ThreadRoot,
    CloseContinuation
}

public sealed class LuaCallReturnTarget
{
    private LuaCallReturnTarget(
        LuaCallReturnTargetKind kind,
        int hostCallId,
        CallFrame? callerFrame,
        int registerIndex,
        int resultCount)
    {
        Kind = kind;
        HostCallId = hostCallId;
        CallerFrame = callerFrame;
        RegisterIndex = registerIndex;
        ResultCount = resultCount;
    }

    public static LuaCallReturnTarget None { get; } = new(LuaCallReturnTargetKind.None, 0, null, 0, 0);

    public LuaCallReturnTargetKind Kind { get; }

    public int HostCallId { get; }

    public CallFrame? CallerFrame { get; }

    public int RegisterIndex { get; }

    public int ResultCount { get; }

    public static LuaCallReturnTarget ForHostCall(int hostCallId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hostCallId);
        return new LuaCallReturnTarget(LuaCallReturnTargetKind.HostCall, hostCallId, null, 0, 0);
    }

    public static LuaCallReturnTarget ForRegisters(CallFrame callerFrame, int registerIndex, int resultCount)
    {
        ArgumentNullException.ThrowIfNull(callerFrame);
        ArgumentOutOfRangeException.ThrowIfNegative(registerIndex);
        return new LuaCallReturnTarget(LuaCallReturnTargetKind.Registers, 0, callerFrame, registerIndex, resultCount);
    }

    public static LuaCallReturnTarget ForThreadRoot()
    {
        return new LuaCallReturnTarget(LuaCallReturnTargetKind.ThreadRoot, 0, null, 0, 0);
    }

    public static LuaCallReturnTarget ForCloseContinuation(CallFrame callerFrame)
    {
        ArgumentNullException.ThrowIfNull(callerFrame);
        return new LuaCallReturnTarget(LuaCallReturnTargetKind.CloseContinuation, 0, callerFrame, 0, 0);
    }
}
