namespace Lua.Runtime.Objects;

public sealed class LuaTable
{
    public LuaTable(string? debugName = null)
    {
        DebugName = debugName;
    }

    public string? DebugName { get; }
}
