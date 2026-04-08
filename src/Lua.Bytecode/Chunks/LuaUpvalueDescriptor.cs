namespace Lua.Bytecode.Chunks;

public sealed class LuaUpvalueDescriptor
{
    public required byte InStack { get; init; }

    public required byte Index { get; init; }

    public required byte Kind { get; init; }

    public string? Name { get; set; }
}
