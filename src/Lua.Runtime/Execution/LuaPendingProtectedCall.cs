using Lua.Runtime.Values;

namespace Lua.Runtime.Execution;

public enum LuaPendingProtectedCallPhase
{
    Function,
    MessageHandler
}

public sealed class LuaPendingProtectedCall
{
    private LuaValue[] _phaseResults = [];
    private LuaValue[] _finalResults = [];

    public LuaPendingProtectedCall(LuaValue? messageHandler)
    {
        MessageHandler = messageHandler;
    }

    public LuaValue? MessageHandler { get; }

    public LuaPendingProtectedCallPhase Phase { get; private set; }

    public int ActiveHostCallId { get; private set; }

    public bool IsSuspended { get; private set; }

    public bool AwaitingResumeValues { get; private set; }

    public bool HasPhaseResults { get; private set; }

    public bool HasPendingErrorObject { get; private set; }

    public bool HasFinalResults { get; private set; }

    public LuaValue PendingErrorObject { get; private set; } = LuaValue.Nil;

    public void SetActiveHostCallId(int hostCallId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hostCallId);

        ActiveHostCallId = hostCallId;
        AwaitingResumeValues = false;
    }

    public void WaitForResumeValues()
    {
        ActiveHostCallId = 0;
        IsSuspended = true;
        AwaitingResumeValues = true;
    }

    public void MarkSuspended()
    {
        IsSuspended = true;
    }

    public void ConsumeResumeValues()
    {
        AwaitingResumeValues = false;
    }

    public void SetPhaseResults(IReadOnlyList<LuaValue> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        _phaseResults = results.ToArray();
        HasPhaseResults = true;
        ActiveHostCallId = 0;
        AwaitingResumeValues = false;
        HasPendingErrorObject = false;
        PendingErrorObject = LuaValue.Nil;
    }

    public LuaValue[] ConsumePhaseResults()
    {
        var results = _phaseResults;
        _phaseResults = [];
        HasPhaseResults = false;
        return results;
    }

    public void SetPendingErrorObject(LuaValue errorObject)
    {
        PendingErrorObject = errorObject;
        HasPendingErrorObject = true;
        ActiveHostCallId = 0;
        AwaitingResumeValues = false;
        HasPhaseResults = false;
        _phaseResults = [];
    }

    public LuaValue ConsumePendingErrorObject()
    {
        var errorObject = PendingErrorObject;
        PendingErrorObject = LuaValue.Nil;
        HasPendingErrorObject = false;
        return errorObject;
    }

    public void BeginMessageHandler()
    {
        Phase = LuaPendingProtectedCallPhase.MessageHandler;
    }

    public void SetFinalResults(IReadOnlyList<LuaValue> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        _finalResults = results.ToArray();
        HasFinalResults = true;
        ActiveHostCallId = 0;
        AwaitingResumeValues = false;
        HasPhaseResults = false;
        _phaseResults = [];
        HasPendingErrorObject = false;
        PendingErrorObject = LuaValue.Nil;
    }

    public LuaValue[] ConsumeFinalResults()
    {
        var results = _finalResults;
        _finalResults = [];
        HasFinalResults = false;
        return results;
    }
}
