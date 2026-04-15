using Lua.Runtime.Values;

namespace Lua.Runtime.Execution;

public sealed class LuaUpvalue
{
    private LuaStack? _openStack;
    private int _openIndex;
    private LuaValue _closedValue;

    public LuaUpvalue(LuaValue value)
    {
        _closedValue = value;
    }

    public LuaUpvalue(LuaStack stack, int absoluteIndex)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentOutOfRangeException.ThrowIfNegative(absoluteIndex);

        _openStack = stack;
        _openIndex = absoluteIndex;
    }

    public bool IsOpen => _openStack is not null;

    public LuaValue GetValue(LuaState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return _openStack is null
            ? _closedValue
            : _openStack[_openIndex];
    }

    public void SetValue(LuaState state, LuaValue value)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (_openStack is null)
        {
            _closedValue = value;
            return;
        }

        _openStack[_openIndex] = value;
    }

    public void Close()
    {
        if (_openStack is null)
        {
            return;
        }

        _closedValue = _openStack[_openIndex];
        _openStack = null;
        _openIndex = 0;
    }
}
