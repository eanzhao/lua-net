using Lua.Runtime.Values;

namespace Lua.Runtime.Execution;

public sealed class LuaUpvalue
{
    private CallFrame? _openFrame;
    private int _registerIndex;
    private LuaValue _closedValue;

    public LuaUpvalue(LuaValue value)
    {
        _closedValue = value;
    }

    public LuaUpvalue(CallFrame frame, int registerIndex)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentOutOfRangeException.ThrowIfNegative(registerIndex);

        _openFrame = frame;
        _registerIndex = registerIndex;
    }

    public bool IsOpen => _openFrame is not null;

    public LuaValue GetValue(LuaState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return _openFrame is null
            ? _closedValue
            : state.Stack[_openFrame.BaseIndex + _registerIndex];
    }

    public void SetValue(LuaState state, LuaValue value)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (_openFrame is null)
        {
            _closedValue = value;
            return;
        }

        state.Stack[_openFrame.BaseIndex + _registerIndex] = value;
    }

    public void Close(LuaState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (_openFrame is null)
        {
            return;
        }

        _closedValue = state.Stack[_openFrame.BaseIndex + _registerIndex];
        _openFrame = null;
        _registerIndex = 0;
    }
}
