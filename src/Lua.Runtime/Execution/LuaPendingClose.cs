using Lua.Runtime.Values;

namespace Lua.Runtime.Execution;

public enum LuaPendingCloseContinuationKind
{
    ContinueExecution,
    Return,
    Error
}

public sealed class LuaPendingClose
{
    private readonly List<int> _remainingRegisters;

    public LuaPendingClose(
        IReadOnlyList<int> registers,
        LuaPendingCloseContinuationKind continuationKind,
        IReadOnlyList<LuaValue>? returnResults = null,
        Exception? pendingException = null)
    {
        ArgumentNullException.ThrowIfNull(registers);

        _remainingRegisters = [.. registers];
        ContinuationKind = continuationKind;
        ReturnResults = returnResults?.ToArray() ?? [];
        PendingException = pendingException;
    }

    public LuaPendingCloseContinuationKind ContinuationKind { get; }

    public LuaValue[] ReturnResults { get; }

    public Exception? PendingException { get; set; }

    public bool HasCloseError { get; set; }

    public bool CanReplaceCloseError { get; set; } = true;

    public bool AwaitingResumeValues { get; private set; }

    public bool HasRemainingRegisters => _remainingRegisters.Count != 0;

    public int DequeueNextRegister()
    {
        if (_remainingRegisters.Count == 0)
        {
            throw new InvalidOperationException("There are no pending registers to close.");
        }

        var register = _remainingRegisters[0];
        _remainingRegisters.RemoveAt(0);
        return register;
    }

    public void WaitForResumeValues()
    {
        AwaitingResumeValues = true;
    }

    public void ConsumeResumeValues()
    {
        AwaitingResumeValues = false;
    }
}
