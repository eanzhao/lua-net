namespace Lua.Bytecode.Chunks;

public sealed class LuaLocalVariable
{
    public required string? Name { get; init; }

    public required int StartProgramCounter { get; init; }

    public required int EndProgramCounter { get; init; }
}
