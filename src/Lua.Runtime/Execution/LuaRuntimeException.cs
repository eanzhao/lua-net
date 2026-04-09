using Lua.Runtime.Values;

namespace Lua.Runtime.Execution;

public sealed class LuaRuntimeException : Exception
{
    public LuaRuntimeException(LuaValue errorObject)
        : base(errorObject.ToString())
    {
        ErrorObject = errorObject;
    }

    public LuaRuntimeException(LuaValue errorObject, Exception innerException)
        : base(errorObject.ToString(), innerException)
    {
        ErrorObject = errorObject;
    }

    public LuaValue ErrorObject { get; }
}
