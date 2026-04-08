namespace Lua.Bytecode.Chunks;

public sealed class LuaChunkHeader
{
    public required byte Version { get; init; }

    public required byte Format { get; init; }

    public required byte IntSize { get; init; }

    public required int IntFormatMarker { get; init; }

    public required byte InstructionSize { get; init; }

    public required uint InstructionFormatMarker { get; init; }

    public required byte LuaIntegerSize { get; init; }

    public required long LuaIntegerFormatMarker { get; init; }

    public required byte LuaNumberSize { get; init; }

    public required double LuaNumberFormatMarker { get; init; }
}
