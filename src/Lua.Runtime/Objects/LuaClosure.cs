using Lua.Runtime.Execution;
using Lua.Runtime.Values;

namespace Lua.Runtime.Objects;

public sealed class LuaClosure
{
    private static readonly object RegistrySync = new();
    private static readonly List<WeakReference<LuaClosure>> RegisteredClosures = [];

    public LuaClosure(
        string? debugName = null,
        int upvalueCount = 0,
        ILuaClosureBody? body = null,
        LuaUpvalue[]? upvalues = null,
        string?[]? upvalueNames = null,
        string? sourceName = null,
        int lineDefined = 0,
        Func<int, int>? lineResolver = null,
        int lastLineDefined = 0,
        int parameterCount = 0,
        bool isVarArg = true,
        int instructionCount = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(upvalueCount);
        ArgumentOutOfRangeException.ThrowIfNegative(lineDefined);
        ArgumentOutOfRangeException.ThrowIfNegative(lastLineDefined);
        ArgumentOutOfRangeException.ThrowIfNegative(parameterCount);
        ArgumentOutOfRangeException.ThrowIfNegative(instructionCount);

        if (upvalues is not null && upvalues.Length != upvalueCount)
        {
            throw new ArgumentException("The upvalue array length must match the declared upvalue count.", nameof(upvalues));
        }

        if (upvalueNames is not null && upvalueNames.Length != upvalueCount)
        {
            throw new ArgumentException("The upvalue name array length must match the declared upvalue count.", nameof(upvalueNames));
        }

        DebugName = debugName;
        UpvalueCount = upvalueCount;
        Body = body;
        Upvalues = upvalues ?? CreateEmptyUpvalues(upvalueCount);
        UpvalueNames = upvalueNames;
        SourceName = sourceName;
        LineDefined = lineDefined;
        LineResolver = lineResolver;
        LastLineDefined = lastLineDefined;
        ParameterCount = parameterCount;
        IsVarArg = isVarArg;
        InstructionCount = instructionCount;

        lock (RegistrySync)
        {
            RegisteredClosures.Add(new WeakReference<LuaClosure>(this));
        }
    }

    public string? DebugName { get; }

    public int UpvalueCount { get; }

    public ILuaClosureBody? Body { get; }

    public LuaUpvalue[] Upvalues { get; }

    public string?[]? UpvalueNames { get; }

    public string? SourceName { get; }

    public int LineDefined { get; }

    public int LastLineDefined { get; }

    public int ParameterCount { get; }

    public bool IsVarArg { get; }

    public int InstructionCount { get; }

    public Func<int, int>? LineResolver { get; }

    public int ResolveLine(int programCounter)
    {
        return LineResolver?.Invoke(programCounter) ?? -1;
    }

    internal static List<LuaClosure> GetRegisteredClosuresSnapshot()
    {
        lock (RegistrySync)
        {
            var snapshot = new List<LuaClosure>(RegisteredClosures.Count);
            for (var index = RegisteredClosures.Count - 1; index >= 0; index--)
            {
                if (!RegisteredClosures[index].TryGetTarget(out var closure))
                {
                    RegisteredClosures.RemoveAt(index);
                    continue;
                }

                snapshot.Add(closure);
            }

            snapshot.Reverse();
            return snapshot;
        }
    }

    internal int GetApproximateMemorySize()
    {
        return 64 + (Upvalues.Length * 24);
    }

    internal void VisitReferencedStrings(Action<string> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        if (DebugName is not null)
        {
            visitor(DebugName);
        }

        if (SourceName is not null)
        {
            visitor(SourceName);
        }

        if (UpvalueNames is null)
        {
            return;
        }

        foreach (var upvalueName in UpvalueNames)
        {
            if (upvalueName is not null)
            {
                visitor(upvalueName);
            }
        }
    }

    private static LuaUpvalue[] CreateEmptyUpvalues(int upvalueCount)
    {
        var upvalues = new LuaUpvalue[upvalueCount];
        for (var index = 0; index < upvalueCount; index++)
        {
            upvalues[index] = new LuaUpvalue(LuaValue.Nil);
        }

        return upvalues;
    }
}
