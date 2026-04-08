using Lua.Runtime.Objects;
using Lua.Runtime.Values;

namespace Lua.Runtime.Execution;

public sealed class LuaState
{
    private readonly List<CallFrame> _frames = [];

    public LuaState()
    {
        Stack = new LuaStack();
        GlobalEnvironment = new LuaTable("_ENV");
        RegisterBaseFunctions();
    }

    public LuaStack Stack { get; }

    public LuaTable GlobalEnvironment { get; }

    public IReadOnlyList<CallFrame> Frames => _frames;

    public CallFrame? CurrentFrame => _frames.Count == 0 ? null : _frames[^1];

    private void RegisterBaseFunctions()
    {
        var setMetatable = new LuaClosure(
            "setmetatable",
            body: new LuaNativeClosureBody(SetMetatable));

        GlobalEnvironment.SetValue(
            LuaValue.FromString("setmetatable"),
            LuaValue.FromFunction(setMetatable));
    }

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

    private static LuaValue[] SetMetatable(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        _ = state;
        _ = closure;

        if (arguments.Count < 2 || arguments[0].Kind != LuaValueKind.Table)
        {
            throw new InvalidOperationException("setmetatable expects a table as the first argument.");
        }

        var table = arguments[0].AsTable();
        var metatableValue = arguments[1];

        if (metatableValue.IsNil)
        {
            table.SetMetatable(null);
            return [arguments[0]];
        }

        if (metatableValue.Kind != LuaValueKind.Table)
        {
            throw new InvalidOperationException("setmetatable expects a table or nil as the second argument.");
        }

        table.SetMetatable(metatableValue.AsTable());
        return [arguments[0]];
    }
}
