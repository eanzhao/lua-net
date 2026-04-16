using Lua.Runtime.Execution;
using Lua.Runtime.Values;

namespace Lua.Runtime.Objects;

public sealed class LuaClosure
{
    public LuaClosure(
        string? debugName = null,
        int upvalueCount = 0,
        ILuaClosureBody? body = null,
        LuaUpvalue[]? upvalues = null,
        string?[]? upvalueNames = null,
        string? sourceName = null,
        int lineDefined = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(upvalueCount);
        ArgumentOutOfRangeException.ThrowIfNegative(lineDefined);

        if (upvalues is not null && upvalues.Length != upvalueCount)
        {
            throw new ArgumentException("The upvalue array length must match the declared upvalue count.", nameof(upvalues));
        }

        if (upvalueNames is not null && upvalueNames.Length != upvalueCount)
        {
            throw new ArgumentException("The upvalue name array length must match the declared upvalue count.", nameof(upvalueNames));
        }

        DebugName = debugName;
        UpvalueCount = upvalueCount;
        Body = body;
        Upvalues = upvalues ?? CreateEmptyUpvalues(upvalueCount);
        UpvalueNames = upvalueNames;
        SourceName = sourceName;
        LineDefined = lineDefined;
    }

    public string? DebugName { get; }

    public int UpvalueCount { get; }

    public ILuaClosureBody? Body { get; }

    public LuaUpvalue[] Upvalues { get; }

    public string?[]? UpvalueNames { get; }

    public string? SourceName { get; }

    public int LineDefined { get; }

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
