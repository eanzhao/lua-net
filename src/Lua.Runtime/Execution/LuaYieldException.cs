using Lua.Runtime.Objects;
using Lua.Runtime.Values;

namespace Lua.Runtime.Execution;

public sealed class LuaYieldException : Exception
{
    public LuaYieldException(LuaThread thread, IReadOnlyList<LuaValue> values)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(values);

        Thread = thread;
        Values = values.ToArray();
    }

    public LuaThread Thread { get; }

    public LuaValue[] Values { get; }
}
