using Lua.Runtime.Execution;
using Lua.Runtime.Values;

namespace Lua.Runtime.Objects;

public sealed class LuaThread
{
    private readonly List<CallFrame> _frames = [];
    private LuaClosure? _entryClosure;
    private LuaValue[] _resumeValues = [];
    private bool _hasResumeValues;
    private int _nonYieldableCallDepth;
    private int _hookInvocationDepth;

    public LuaThread(string? debugName = null, bool isMainThread = false)
    {
        DebugName = debugName;
        IsMainThread = isMainThread;
        Stack = new LuaStack();
    }

    public string? DebugName { get; }

    public bool IsMainThread { get; }

    public LuaStack Stack { get; }

    public IReadOnlyList<CallFrame> Frames => _frames;

    public CallFrame? CurrentFrame => _frames.Count == 0 ? null : _frames[^1];

    public LuaClosure? EntryClosure => _entryClosure;

    public bool HasStarted { get; private set; }

    public bool IsDead { get; private set; }

    public bool IsYieldSuspended { get; private set; }

    public bool IsResumingChild { get; set; }

    public LuaThread? ResumeParent { get; set; }

    public LuaValue ErrorObject { get; private set; } = LuaValue.Nil;

    public int NonYieldableCallDepth => _nonYieldableCallDepth;

    public LuaClosure? HookFunction { get; private set; }

    public string HookMask { get; private set; } = string.Empty;

    public int HookCount { get; private set; }

    public bool IsExecutingHook => _hookInvocationDepth > 0;

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

    public void SetEntryClosure(LuaClosure closure)
    {
        ArgumentNullException.ThrowIfNull(closure);
        _entryClosure = closure;
        HasStarted = false;
        IsDead = false;
        IsYieldSuspended = false;
        ErrorObject = LuaValue.Nil;
    }

    public void MarkStarted()
    {
        if (_entryClosure is null)
        {
            throw new InvalidOperationException("Coroutine entry closure is not configured.");
        }

        HasStarted = true;
        IsDead = false;
        IsYieldSuspended = false;
        ErrorObject = LuaValue.Nil;
        _hasResumeValues = false;
    }

    public void MarkYieldSuspended()
    {
        IsYieldSuspended = true;
    }

    public void MarkRunning()
    {
        IsYieldSuspended = false;
    }

    public void MarkCompleted()
    {
        HasStarted = true;
        IsDead = true;
        IsYieldSuspended = false;
        ErrorObject = LuaValue.Nil;
        _resumeValues = [];
        _hasResumeValues = false;
    }

    public void MarkErrored(LuaValue errorObject)
    {
        HasStarted = true;
        IsDead = true;
        IsYieldSuspended = false;
        ErrorObject = errorObject;
        _resumeValues = [];
        _hasResumeValues = false;
    }

    public void ResetToDead()
    {
        HasStarted = true;
        IsDead = true;
        IsYieldSuspended = false;
        ErrorObject = LuaValue.Nil;
        ResumeParent = null;
        IsResumingChild = false;
        _resumeValues = [];
        _hasResumeValues = false;
        _frames.Clear();
        Stack.Clear();
    }

    public void SetResumeValues(IReadOnlyList<LuaValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _resumeValues = values.ToArray();
        _hasResumeValues = true;
    }

    public LuaValue[] ConsumeResumeValues()
    {
        var values = _resumeValues;
        _resumeValues = [];
        _hasResumeValues = false;
        return values;
    }

    public bool HasResumeValues()
    {
        return _hasResumeValues;
    }

    public void EnterNonYieldableCall()
    {
        _nonYieldableCallDepth += 1;
    }

    public void ExitNonYieldableCall()
    {
        if (_nonYieldableCallDepth == 0)
        {
            throw new InvalidOperationException("Non-yieldable call depth is already zero.");
        }

        _nonYieldableCallDepth -= 1;
    }

    public void SetHook(LuaClosure? hookFunction, string hookMask, int hookCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(hookCount);

        HookFunction = hookFunction;
        HookMask = hookMask ?? string.Empty;
        HookCount = hookCount;
    }

    public void ClearHook()
    {
        HookFunction = null;
        HookMask = string.Empty;
        HookCount = 0;
    }

    public bool HasHookEvent(char eventMask)
    {
        return HookFunction is not null && HookMask.Contains(eventMask, StringComparison.Ordinal);
    }

    public void EnterHookInvocation()
    {
        _hookInvocationDepth += 1;
    }

    public void ExitHookInvocation()
    {
        if (_hookInvocationDepth == 0)
        {
            throw new InvalidOperationException("Hook invocation depth is already zero.");
        }

        _hookInvocationDepth -= 1;
    }
}
