using Lua.Runtime.Values;

namespace Lua.Runtime.Execution;

public sealed class LuaStack
{
    private readonly List<LuaValue> _slots = [];

    public int Count => _slots.Count;

    public LuaValue this[int index]
    {
        get => _slots[index];
        set => _slots[index] = value;
    }

    public void Push(LuaValue value)
    {
        _slots.Add(value);
    }

    public LuaValue Pop()
    {
        if (_slots.Count == 0)
        {
            throw new InvalidOperationException("Cannot pop from an empty stack.");
        }

        var lastIndex = _slots.Count - 1;
        var value = _slots[lastIndex];
        _slots.RemoveAt(lastIndex);
        return value;
    }

    public LuaValue Peek(int depth = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(depth);

        if (depth >= _slots.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(depth), "Peek depth is outside the stack.");
        }

        return _slots[_slots.Count - 1 - depth];
    }

    public void SetTop(int newCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(newCount);

        if (newCount < _slots.Count)
        {
            _slots.RemoveRange(newCount, _slots.Count - newCount);
            return;
        }

        while (_slots.Count < newCount)
        {
            _slots.Add(LuaValue.Nil);
        }
    }

    public void Clear()
    {
        _slots.Clear();
    }
}
