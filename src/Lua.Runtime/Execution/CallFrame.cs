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
        IReadOnlyList<LuaValue>? varargs = null,
        LuaCallReturnTarget? returnTarget = null,
        string? invocationName = null,
        string invocationNameWhat = "")
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
        ReturnTarget = returnTarget ?? LuaCallReturnTarget.None;
        InvocationName = invocationName;
        InvocationNameWhat = invocationNameWhat;
    }

    public LuaClosure Closure { get; }

    public int BaseIndex { get; }

    public int ExpectedResults { get; }

    public int ProgramCounter { get; private set; }

    public int RegisterTop { get; private set; }

    public int LiveRegisterTop { get; private set; }

    public IReadOnlyList<LuaValue> Varargs { get; }

    public LuaCallReturnTarget ReturnTarget { get; }

    public string? InvocationName { get; }

    public string InvocationNameWhat { get; }

    public LuaPendingCall? PendingCall { get; private set; }

    public LuaPendingClose? PendingClose { get; private set; }

    public LuaPendingProtectedCall? PendingProtectedCall { get; private set; }

    public int LastLineHookLine { get; private set; } = -1;

    public void SetPendingCall(int registerIndex, int resultCount)
    {
        PendingCall = LuaPendingCall.ForRegisters(registerIndex, resultCount);
    }

    public void SetPendingTailReturn()
    {
        PendingCall = LuaPendingCall.ForTailReturn();
    }

    public void ClearPendingCall()
    {
        PendingCall = null;
    }

    public void SetPendingClose(LuaPendingClose pendingClose)
    {
        ArgumentNullException.ThrowIfNull(pendingClose);
        PendingClose = pendingClose;
    }

    public void ClearPendingClose()
    {
        PendingClose = null;
    }

    public void SetPendingProtectedCall(LuaPendingProtectedCall pendingProtectedCall)
    {
        ArgumentNullException.ThrowIfNull(pendingProtectedCall);
        PendingProtectedCall = pendingProtectedCall;
    }

    public void ClearPendingProtectedCall()
    {
        PendingProtectedCall = null;
    }

    public void SetRegisterTop(int registerTop)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(registerTop);
        RegisterTop = registerTop;
    }

    public void SetLiveRegisterTop(int registerTop)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(registerTop);
        LiveRegisterTop = registerTop;
    }

    public void SetLastLineHookLine(int line)
    {
        LastLineHookLine = line;
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
                break;
            }

            registers.Add(trackedRegister);
            _toBeClosedRegisters.RemoveAt(index);
        }

        registers.Sort();
        registers.Reverse();

        return registers;
    }

    public bool HasToBeClosedRegistersFrom(int registerIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(registerIndex);

        foreach (var trackedRegister in _toBeClosedRegisters)
        {
            if (trackedRegister >= registerIndex)
            {
                return true;
            }
        }

        return false;
    }

    public LuaUpvalue GetOrCreateOpenUpvalue(LuaStack stack, int registerIndex)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentOutOfRangeException.ThrowIfNegative(registerIndex);

        if (_openUpvalues.TryGetValue(registerIndex, out var upvalue))
        {
            return upvalue;
        }

        upvalue = new LuaUpvalue(stack, BaseIndex + registerIndex);
        _openUpvalues.Add(registerIndex, upvalue);
        return upvalue;
    }

    public LuaUpvalue GetOrCreateOpenUpvalue(LuaState state, int registerIndex)
    {
        ArgumentNullException.ThrowIfNull(state);
        return GetOrCreateOpenUpvalue(state.Stack, registerIndex);
    }

    public LuaUpvalue GetOrCreateOpenUpvalue(int registerIndex)
    {
        throw new InvalidOperationException("An explicit stack or state is required to create an open upvalue.");
    }

    public void CloseOpenUpvalues()
    {
        CloseOpenUpvaluesFrom(0);
    }

    public void CloseOpenUpvalues(LuaState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        CloseOpenUpvalues();
    }

    public void CloseOpenUpvaluesFrom(int registerIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(registerIndex);

        if (_openUpvalues.Count == 0)
        {
            return;
        }

        var keysToClose = new List<int>();
        foreach (var key in _openUpvalues.Keys)
        {
            if (key >= registerIndex)
            {
                keysToClose.Add(key);
            }
        }

        keysToClose.Sort();
        keysToClose.Reverse();

        foreach (var key in keysToClose)
        {
            _openUpvalues[key].Close();
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
