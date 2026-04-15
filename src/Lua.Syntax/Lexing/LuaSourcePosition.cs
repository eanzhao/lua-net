namespace Lua.Syntax.Lexing;

public readonly record struct LuaSourcePosition(int Offset, int Line, int Column);
