namespace Lua.Runtime.Objects;

public sealed class LuaClosure
{
    public LuaClosure(string? debugName = null, int upvalueCount = 0, ILuaClosureBody? body = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(upvalueCount);
        DebugName = debugName;
        UpvalueCount = upvalueCount;
        Body = body;
    }

    public string? DebugName { get; }

    public int UpvalueCount { get; }

    public ILuaClosureBody? Body { get; }
}
