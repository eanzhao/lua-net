namespace Lua.Bytecode.Chunks;

public sealed class LuaChunk
{
    public required LuaChunkHeader Header { get; init; }

    public required byte MainUpvalueCount { get; init; }

    public required LuaPrototype MainFunction { get; init; }
}
