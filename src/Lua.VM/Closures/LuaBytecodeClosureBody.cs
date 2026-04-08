using Lua.Bytecode.Chunks;
using Lua.Runtime.Objects;

namespace Lua.VM.Closures;

public sealed class LuaBytecodeClosureBody : ILuaClosureBody
{
    public LuaBytecodeClosureBody(LuaPrototype prototype)
    {
        ArgumentNullException.ThrowIfNull(prototype);
        Prototype = prototype;
    }

    public LuaPrototype Prototype { get; }
}
