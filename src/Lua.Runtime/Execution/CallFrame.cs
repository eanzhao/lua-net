using Lua.Runtime.Objects;
using Lua.Runtime.Values;

namespace Lua.Runtime.Execution;

public sealed class CallFrame
{
    private readonly Dictionary<int, LuaUpvalue> _openUpvalues = [];
    private readonly List<int> _toBeClosedRegisters = [];

    public CallFrame(
        LuaClosure closure,
        int baseIndex,
        int expectedResults,
        int programCounter = 0,
        int registerTop = 0,
        IReadOnlyList<LuaValue>? varargs = null)
    {
        ArgumentNullException.ThrowIfNull(closure);
        ArgumentOutOfRangeException.ThrowIfNegative(baseIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedResults);
        ArgumentOutOfRangeException.ThrowIfNegative(programCounter);
        ArgumentOutOfRangeException.ThrowIfNegative(registerTop);

        Closure = closure;
        BaseIndex = baseIndex;
        ExpectedResults = expectedResults;
        ProgramCounter = programCounter;
        RegisterTop = registerTop;
        Varargs = varargs ?? Array.Empty<LuaValue>();
    }

    public LuaClosure Closure { get; }

    public int BaseIndex { get; }

    public int ExpectedResults { get; }

    public int ProgramCounter { get; private set; }

    public int RegisterTop { get; private set; }

    public IReadOnlyList<LuaValue> Varargs { get; }

    public void SetRegisterTop(int registerTop)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(registerTop);
        RegisterTop = registerTop;
    }

    public void RegisterToBeClosed(int registerIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(registerIndex);

        if (!_toBeClosedRegisters.Contains(registerIndex))
        {
            _toBeClosedRegisters.Add(registerIndex);
        }
    }

    public IReadOnlyList<int> ConsumeToBeClosedRegistersFrom(int registerIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(registerIndex);

        if (_toBeClosedRegisters.Count == 0)
        {
            return Array.Empty<int>();
        }

        var registers = new List<int>();
        for (var index = _toBeClosedRegisters.Count - 1; index >= 0; index--)
        {
            var trackedRegister = _toBeClosedRegisters[index];
            if (trackedRegister < registerIndex)
            {
                continue;
            }

            registers.Add(trackedRegister);
            _toBeClosedRegisters.RemoveAt(index);
        }

        return registers;
    }

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
        CloseOpenUpvaluesFrom(state, 0);
    }

    public void CloseOpenUpvaluesFrom(LuaState state, int registerIndex)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentOutOfRangeException.ThrowIfNegative(registerIndex);

        if (_openUpvalues.Count == 0)
        {
            return;
        }

        var keysToClose = new List<int>();
        foreach (var (key, upvalue) in _openUpvalues)
        {
            if (key < registerIndex)
            {
                continue;
            }

            upvalue.Close(state);
            keysToClose.Add(key);
        }

        foreach (var key in keysToClose)
        {
            _openUpvalues.Remove(key);
        }
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
