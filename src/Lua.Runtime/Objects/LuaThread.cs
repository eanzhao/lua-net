namespace Lua.Runtime.Objects;

public sealed class LuaThread
{
    public LuaThread(string? debugName = null)
    {
        DebugName = debugName;
    }

    public string? DebugName { get; }
}
