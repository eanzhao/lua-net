namespace Lua.Runtime.Objects;

public sealed class LuaUserData
{
    public LuaUserData(object? value = null)
    {
        Value = value;
    }

    public object? Value { get; }
}
