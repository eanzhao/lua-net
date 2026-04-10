using Lua.Runtime.Values;

namespace Lua.Runtime.Objects;

public sealed class LuaUserData
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

    public bool TryGetMetamethod(string name, out LuaValue value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        if (Metatable is null)
        {
            value = LuaValue.Nil;
            return false;
        }

        value = Metatable.GetValue(LuaValue.FromString(name));
        return !value.IsNil;
    }
}
