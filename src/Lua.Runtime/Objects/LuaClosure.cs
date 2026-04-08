using Lua.Runtime.Execution;
using Lua.Runtime.Values;

namespace Lua.Runtime.Objects;

public sealed class LuaClosure
{
    public LuaClosure(
        string? debugName = null,
        int upvalueCount = 0,
        ILuaClosureBody? body = null,
        LuaUpvalue[]? upvalues = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(upvalueCount);

        if (upvalues is not null && upvalues.Length != upvalueCount)
        {
            throw new ArgumentException("The upvalue array length must match the declared upvalue count.", nameof(upvalues));
        }

        DebugName = debugName;
        UpvalueCount = upvalueCount;
        Body = body;
        Upvalues = upvalues ?? CreateEmptyUpvalues(upvalueCount);
    }

    public string? DebugName { get; }

    public int UpvalueCount { get; }

    public ILuaClosureBody? Body { get; }

    public LuaUpvalue[] Upvalues { get; }

    private static LuaUpvalue[] CreateEmptyUpvalues(int upvalueCount)
    {
        var upvalues = new LuaUpvalue[upvalueCount];
        for (var index = 0; index < upvalueCount; index++)
        {
            upvalues[index] = new LuaUpvalue(LuaValue.Nil);
        }

        return upvalues;
    }
}
