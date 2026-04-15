using Lua.Runtime.Execution;
using Lua.Runtime.Values;

namespace Lua.Runtime.Objects;

public sealed class LuaTable : IMetatableOwner
{
    private readonly Dictionary<LuaValue, LuaValue> _entries;
    private readonly List<LuaValue> _iterationKeys;

    public LuaTable(string? debugName = null, int arrayCapacity = 0, int hashCapacity = 0)
    {
        DebugName = debugName;
        _entries = new Dictionary<LuaValue, LuaValue>(Math.Max(arrayCapacity, 0) + Math.Max(hashCapacity, 0));
        _iterationKeys = [];
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
        var exists = _entries.ContainsKey(normalizedKey);
        if (value.IsNil)
        {
            if (_entries.Remove(normalizedKey))
            {
                _iterationKeys.Remove(normalizedKey);
            }

            return;
        }

        _entries[normalizedKey] = value;
        if (!exists)
        {
            _iterationKeys.Add(normalizedKey);
        }
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

    public bool TryGetNextEntry(LuaValue currentKey, out LuaValue nextKey, out LuaValue nextValue)
    {
        if (currentKey.IsNil)
        {
            if (_iterationKeys.Count == 0)
            {
                nextKey = LuaValue.Nil;
                nextValue = LuaValue.Nil;
                return false;
            }

            nextKey = _iterationKeys[0];
            nextValue = _entries[nextKey];
            return true;
        }

        var normalizedKey = NormalizeNextKey(currentKey);
        var currentIndex = _iterationKeys.IndexOf(normalizedKey);
        if (currentIndex < 0)
        {
            throw new LuaRuntimeException(LuaValue.FromString("invalid key to 'next'"));
        }

        var nextIndex = currentIndex + 1;
        if (nextIndex >= _iterationKeys.Count)
        {
            nextKey = LuaValue.Nil;
            nextValue = LuaValue.Nil;
            return false;
        }

        nextKey = _iterationKeys[nextIndex];
        nextValue = _entries[nextKey];
        return true;
    }

    private static LuaValue NormalizeKey(LuaValue key)
    {
        if (key.IsNil)
        {
            throw new LuaRuntimeException(LuaValue.FromString("table index is nil"));
        }

        if (key.Kind == LuaValueKind.Float)
        {
            var number = key.AsFloat();
            if (double.IsNaN(number))
            {
                throw new LuaRuntimeException(LuaValue.FromString("table index is NaN"));
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

    private static LuaValue NormalizeNextKey(LuaValue key)
    {
        if (key.Kind == LuaValueKind.Float)
        {
            var number = key.AsFloat();
            if (double.IsNaN(number))
            {
                throw new LuaRuntimeException(LuaValue.FromString("invalid key to 'next'"));
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
