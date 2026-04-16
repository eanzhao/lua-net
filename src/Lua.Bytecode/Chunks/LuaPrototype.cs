namespace Lua.Bytecode.Chunks;

public sealed class LuaPrototype
{
    public string? DebugName { get; init; }

    public required int LineDefined { get; init; }

    public required int LastLineDefined { get; init; }

    public required byte NumberOfParameters { get; init; }

    public required byte Flags { get; init; }

    public required byte MaxStackSize { get; init; }

    public required uint[] Code { get; init; }

    public required LuaConstant[] Constants { get; init; }

    public required LuaUpvalueDescriptor[] Upvalues { get; init; }

    public required LuaPrototype[] NestedPrototypes { get; init; }

    public required string? Source { get; init; }

    public required sbyte[] LineInfo { get; init; }

    public required LuaAbsoluteLineInfo[] AbsoluteLineInfo { get; init; }

    public required LuaLocalVariable[] LocalVariables { get; init; }

    public string?[]? ToBeClosedNames { get; init; }

    public byte[]? RegisterTopHints { get; init; }
}
