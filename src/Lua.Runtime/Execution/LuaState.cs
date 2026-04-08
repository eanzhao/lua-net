using Lua.Runtime.Objects;

namespace Lua.Runtime.Execution;

public sealed class LuaState
{
    private readonly List<CallFrame> _frames = [];

    public LuaState()
    {
        Stack = new LuaStack();
        GlobalEnvironment = new LuaTable("_ENV");
    }

    public LuaStack Stack { get; }

    public LuaTable GlobalEnvironment { get; }

    public IReadOnlyList<CallFrame> Frames => _frames;

    public CallFrame? CurrentFrame => _frames.Count == 0 ? null : _frames[^1];

    public void PushFrame(CallFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _frames.Add(frame);
    }

    public CallFrame PopFrame()
    {
        if (_frames.Count == 0)
        {
            throw new InvalidOperationException("Cannot pop from an empty frame stack.");
        }

        var lastIndex = _frames.Count - 1;
        var frame = _frames[lastIndex];
        _frames.RemoveAt(lastIndex);
        return frame;
    }
}
