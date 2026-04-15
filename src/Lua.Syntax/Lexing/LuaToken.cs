namespace Lua.Syntax.Lexing;

public sealed record LuaToken
{
    public required LuaTokenKind Kind { get; init; }

    public required string Lexeme { get; init; }

    public required LuaSourceRange Range { get; init; }

    public string? StringValue { get; init; }
}
