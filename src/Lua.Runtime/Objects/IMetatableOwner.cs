using Lua.Runtime.Values;

namespace Lua.Runtime.Objects;

public interface IMetatableOwner
{
    LuaTable? Metatable { get; }
}

public static class MetatableOwnerExtensions
{
    public static bool TryGetMetamethod(this IMetatableOwner owner, string name, out LuaValue value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        if (owner.Metatable is null)
        {
            value = LuaValue.Nil;
            return false;
        }

        value = owner.Metatable.GetValue(LuaValue.FromString(name));
        return !value.IsNil;
    }
}
