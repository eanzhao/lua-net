using Lua.Runtime.Values;

namespace Lua.Runtime.Objects;

public sealed class LuaTable : IMetatableOwner
{
    private readonly Dictionary<LuaValue, LuaValue> _entries;

    public LuaTable(string? debugName = null, int arrayCapacity = 0, int hashCapacity = 0)
    {
        DebugName = debugName;
        _entries = new Dictionary<LuaValue, LuaValue>(Math.Max(arrayCapacity, 0) + Math.Max(hashCapacity, 0));
    }

    public string? DebugName { get; }

    public LuaTable? Metatable { get; private set; }

    public LuaValue GetValue(LuaValue key)
    {
        return TryGetValue(key, out var value) ? value : LuaValue.Nil;
    }

    public bool TryGetValue(LuaValue key, out LuaValue value)
    {
        var normalizedKey = NormalizeKey(key);
        return _entries.TryGetValue(normalizedKey, out value);
    }

    public void SetValue(LuaValue key, LuaValue value)
    {
        var normalizedKey = NormalizeKey(key);
        if (value.IsNil)
        {
            _entries.Remove(normalizedKey);
            return;
        }

        _entries[normalizedKey] = value;
    }

    public void SetMetatable(LuaTable? metatable)
    {
        Metatable = metatable;
    }

    public long GetSequenceLength()
    {
        long length = 0;
        while (_entries.TryGetValue(LuaValue.FromInteger(length + 1), out var value) && !value.IsNil)
        {
            length += 1;
        }

        return length;
    }

    private static LuaValue NormalizeKey(LuaValue key)
    {
        if (key.IsNil)
        {
            throw new ArgumentException("Table index is nil.", nameof(key));
        }

        if (key.Kind == LuaValueKind.Float)
        {
            var number = key.AsFloat();
            if (double.IsNaN(number))
            {
                throw new ArgumentException("Table index is NaN.", nameof(key));
            }

            if (double.IsFinite(number) &&
                number >= long.MinValue &&
                number <= long.MaxValue &&
                Math.Truncate(number) == number)
            {
                return LuaValue.FromInteger((long)number);
            }
        }

        return key;
    }
}
