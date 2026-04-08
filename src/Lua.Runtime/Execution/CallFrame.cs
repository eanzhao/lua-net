using Lua.Runtime.Objects;

namespace Lua.Runtime.Execution;

public sealed class CallFrame
{
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
