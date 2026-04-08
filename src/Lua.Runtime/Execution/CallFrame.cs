using Lua.Runtime.Objects;

namespace Lua.Runtime.Execution;

public sealed class CallFrame
{
    private readonly Dictionary<int, LuaUpvalue> _openUpvalues = [];

    public CallFrame(LuaClosure closure, int baseIndex, int expectedResults, int programCounter = 0)
    {
        ArgumentNullException.ThrowIfNull(closure);
        ArgumentOutOfRangeException.ThrowIfNegative(baseIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedResults);
        ArgumentOutOfRangeException.ThrowIfNegative(programCounter);

        Closure = closure;
        BaseIndex = baseIndex;
        ExpectedResults = expectedResults;
        ProgramCounter = programCounter;
    }

    public LuaClosure Closure { get; }

    public int BaseIndex { get; }

    public int ExpectedResults { get; }

    public int ProgramCounter { get; private set; }

    public LuaUpvalue GetOrCreateOpenUpvalue(int registerIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(registerIndex);

        if (_openUpvalues.TryGetValue(registerIndex, out var upvalue))
        {
            return upvalue;
        }

        upvalue = new LuaUpvalue(this, registerIndex);
        _openUpvalues.Add(registerIndex, upvalue);
        return upvalue;
    }

    public void CloseOpenUpvalues(LuaState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        foreach (var upvalue in _openUpvalues.Values)
        {
            upvalue.Close(state);
        }

        _openUpvalues.Clear();
    }

    public void Advance(int amount = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        ProgramCounter += amount;
    }

    public void Jump(int targetProgramCounter)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(targetProgramCounter);
        ProgramCounter = targetProgramCounter;
    }
}
