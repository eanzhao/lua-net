using Lua.Runtime.Execution;
using Lua.Runtime.Values;

namespace Lua.Runtime.Objects;

public delegate LuaValue[] LuaNativeFunction(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments);

public sealed class LuaNativeClosureBody : ILuaClosureBody
{
    public LuaNativeClosureBody(LuaNativeFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);
        Function = function;
    }

    public LuaNativeFunction Function { get; }
}
