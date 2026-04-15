using Lua.Runtime.Objects;

namespace Lua.Runtime.Execution;

public sealed class LuaThreadCloseException : Exception
{
    public LuaThreadCloseException(LuaThread thread)
    {
        ArgumentNullException.ThrowIfNull(thread);
        Thread = thread;
    }

    public LuaThread Thread { get; }
}
