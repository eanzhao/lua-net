using Lua.Runtime.Values;

namespace Lua.Runtime.Objects;

public sealed class LuaUserData : IMetatableOwner
{
    private readonly LuaValue[] _userValues;

    public LuaUserData(object? value = null, int userValueCount = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(userValueCount);
        Value = value;
        _userValues = new LuaValue[userValueCount];
    }

    public object? Value { get; }

    public LuaTable? Metatable { get; private set; }

    public int UserValueCount => _userValues.Length;

    public void SetMetatable(LuaTable? metatable)
    {
        Metatable = metatable;
    }

    public bool TryGetUserValue(int slot, out LuaValue value)
    {
        if ((uint)(slot - 1) >= (uint)_userValues.Length)
        {
            value = LuaValue.Nil;
            return false;
        }

        value = _userValues[slot - 1];
        return true;
    }

    public bool TrySetUserValue(int slot, LuaValue value)
    {
        if ((uint)(slot - 1) >= (uint)_userValues.Length)
        {
            return false;
        }

        _userValues[slot - 1] = value;
        return true;
    }
}
