namespace Lua.Runtime.Objects;

public sealed class LuaClosure
{
    public LuaClosure(string? debugName = null, int upvalueCount = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(upvalueCount);
        DebugName = debugName;
        UpvalueCount = upvalueCount;
    }

    public string? DebugName { get; }

    public int UpvalueCount { get; }
}
