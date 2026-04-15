namespace Lua.Syntax.Lexing;

public sealed class LuaSyntaxException : Exception
{
    public LuaSyntaxException(string message, LuaSourcePosition position, string? sourceName = null)
        : base(FormatMessage(message, position, sourceName))
    {
        Position = position;
        SourceName = string.IsNullOrWhiteSpace(sourceName) ? "<input>" : sourceName;
    }

    public LuaSourcePosition Position { get; }

    public string SourceName { get; }

    private static string FormatMessage(string message, LuaSourcePosition position, string? sourceName)
    {
        var name = string.IsNullOrWhiteSpace(sourceName) ? "<input>" : sourceName;
        return $"{name}:{position.Line}:{position.Column}: {message}";
    }
}
