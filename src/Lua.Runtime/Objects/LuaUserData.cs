namespace Lua.Runtime.Objects;

public sealed class LuaUserData : IMetatableOwner
{
    public LuaUserData(object? value = null)
    {
        Value = value;
    }

    public object? Value { get; }

    public LuaTable? Metatable { get; private set; }

    public void SetMetatable(LuaTable? metatable)
    {
        Metatable = metatable;
    }
}
