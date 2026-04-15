using System.Text;

namespace Lua.Syntax.Lexing;

public sealed class LuaLexer
{
    private static readonly Dictionary<string, LuaTokenKind> Keywords = new(StringComparer.Ordinal)
    {
        ["and"] = LuaTokenKind.And,
        ["break"] = LuaTokenKind.Break,
        ["do"] = LuaTokenKind.Do,
        ["else"] = LuaTokenKind.Else,
        ["elseif"] = LuaTokenKind.ElseIf,
        ["end"] = LuaTokenKind.End,
        ["false"] = LuaTokenKind.False,
        ["for"] = LuaTokenKind.For,
        ["function"] = LuaTokenKind.Function,
        ["global"] = LuaTokenKind.Global,
        ["goto"] = LuaTokenKind.Goto,
        ["if"] = LuaTokenKind.If,
        ["in"] = LuaTokenKind.In,
        ["local"] = LuaTokenKind.Local,
        ["nil"] = LuaTokenKind.Nil,
        ["not"] = LuaTokenKind.Not,
        ["or"] = LuaTokenKind.Or,
        ["repeat"] = LuaTokenKind.Repeat,
        ["return"] = LuaTokenKind.Return,
        ["then"] = LuaTokenKind.Then,
        ["true"] = LuaTokenKind.True,
        ["until"] = LuaTokenKind.Until,
        ["while"] = LuaTokenKind.While
    };

    private readonly string _source;
    private readonly string _sourceName;
    private int _index;
    private int _line = 1;
    private int _column = 1;

    public LuaLexer(string source, string? sourceName = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _sourceName = string.IsNullOrWhiteSpace(sourceName) ? "<input>" : sourceName;
    }

    public IReadOnlyList<LuaToken> Tokenize()
    {
        SkipPreamble();

        var tokens = new List<LuaToken>();
        while (true)
        {
            SkipTrivia();

            var token = ReadToken();
            tokens.Add(token);

            if (token.Kind == LuaTokenKind.EndOfFile)
            {
                return tokens;
            }
        }
    }

    private LuaSourcePosition CurrentPosition => new(_index, _line, _column);

    private bool IsAtEnd => _index >= _source.Length;

    private char Current => Peek(0);

    private char Peek(int offset)
    {
        var index = _index + offset;
        return index >= 0 && index < _source.Length ? _source[index] : '\0';
    }

    private void SkipPreamble()
    {
        if (!IsAtEnd && Current == '\uFEFF')
        {
            _index++;
        }

        if (Current == '#' && Peek(1) == '!')
        {
            while (!IsAtEnd && !IsNewLine(Current))
            {
                AdvanceSingle();
            }

            if (!IsAtEnd)
            {
                ConsumeNewLine();
            }
        }
    }

    private void SkipTrivia()
    {
        while (!IsAtEnd)
        {
            if (IsNewLine(Current))
            {
                ConsumeNewLine();
                continue;
            }

            if (IsSpace(Current))
            {
                AdvanceSingle();
                continue;
            }

            if (Current == '-' && Peek(1) == '-')
            {
                AdvanceSingle();
                AdvanceSingle();

                if (Current == '[' && TryGetLongBracketLevel(out var level, out _))
                {
                    ReadLongBracket(level, captureValue: false, _line);
                }
                else
                {
                    while (!IsAtEnd && !IsNewLine(Current))
                    {
                        AdvanceSingle();
                    }
                }

                continue;
            }

            break;
        }
    }

    private LuaToken ReadToken()
    {
        var startIndex = _index;
        var start = CurrentPosition;

        if (IsAtEnd)
        {
            return new LuaToken
            {
                Kind = LuaTokenKind.EndOfFile,
                Lexeme = string.Empty,
                Range = new LuaSourceRange(start, start)
            };
        }

        if (IsIdentifierStart(Current))
        {
            return ReadIdentifierOrKeyword(startIndex, start);
        }

        if (IsDigit(Current) || (Current == '.' && IsDigit(Peek(1))))
        {
            return ReadNumber(startIndex, start);
        }

        return Current switch
        {
            '"' or '\'' => ReadShortString(startIndex, start),
            '[' => ReadBracketToken(startIndex, start),
            '.' => ReadDotToken(startIndex, start),
            '=' => ReadCompoundToken(startIndex, start, LuaTokenKind.Assign, '=', LuaTokenKind.Equal),
            '<' => ReadComparisonToken(startIndex, start, LuaTokenKind.LessThan, '=', LuaTokenKind.LessEqual, '<', LuaTokenKind.LeftShift),
            '>' => ReadComparisonToken(startIndex, start, LuaTokenKind.GreaterThan, '=', LuaTokenKind.GreaterEqual, '>', LuaTokenKind.RightShift),
            '/' => ReadCompoundToken(startIndex, start, LuaTokenKind.Slash, '/', LuaTokenKind.IntegerDivision),
            '~' => ReadCompoundToken(startIndex, start, LuaTokenKind.Tilde, '=', LuaTokenKind.NotEqual),
            ':' => ReadCompoundToken(startIndex, start, LuaTokenKind.Colon, ':', LuaTokenKind.DoubleColon),
            '+' => ReadSingleCharToken(startIndex, start, LuaTokenKind.Plus),
            '-' => ReadSingleCharToken(startIndex, start, LuaTokenKind.Minus),
            '*' => ReadSingleCharToken(startIndex, start, LuaTokenKind.Star),
            '%' => ReadSingleCharToken(startIndex, start, LuaTokenKind.Percent),
            '^' => ReadSingleCharToken(startIndex, start, LuaTokenKind.Caret),
            '#' => ReadSingleCharToken(startIndex, start, LuaTokenKind.Hash),
            '&' => ReadSingleCharToken(startIndex, start, LuaTokenKind.Ampersand),
            '|' => ReadSingleCharToken(startIndex, start, LuaTokenKind.Pipe),
            '(' => ReadSingleCharToken(startIndex, start, LuaTokenKind.LeftParen),
            ')' => ReadSingleCharToken(startIndex, start, LuaTokenKind.RightParen),
            '{' => ReadSingleCharToken(startIndex, start, LuaTokenKind.LeftBrace),
            '}' => ReadSingleCharToken(startIndex, start, LuaTokenKind.RightBrace),
            ']' => ReadSingleCharToken(startIndex, start, LuaTokenKind.RightBracket),
            ';' => ReadSingleCharToken(startIndex, start, LuaTokenKind.Semicolon),
            ',' => ReadSingleCharToken(startIndex, start, LuaTokenKind.Comma),
            _ => throw CreateSyntaxException($"unexpected symbol '{Current}'", start)
        };
    }

    private LuaToken ReadIdentifierOrKeyword(int startIndex, LuaSourcePosition start)
    {
        AdvanceSingle();
        while (IsIdentifierPart(Current))
        {
            AdvanceSingle();
        }

        var lexeme = _source[startIndex.._index];
        var kind = Keywords.GetValueOrDefault(lexeme, LuaTokenKind.Identifier);

        return CreateToken(kind, startIndex, start);
    }

    private LuaToken ReadNumber(int startIndex, LuaSourcePosition start)
    {
        var sawExponent = false;
        var isHex = false;

        if (Current == '.')
        {
            AdvanceSingle();
        }
        else
        {
            AdvanceSingle();

            if (_source[startIndex] == '0' && (Current == 'x' || Current == 'X'))
            {
                isHex = true;
                AdvanceSingle();
            }
        }

        while (!IsAtEnd)
        {
            if (isHex)
            {
                if (IsHexDigit(Current) || Current == '.')
                {
                    AdvanceSingle();
                    continue;
                }

                if (!sawExponent && (Current == 'p' || Current == 'P'))
                {
                    sawExponent = true;
                    AdvanceSingle();
                    if (Current == '+' || Current == '-')
                    {
                        AdvanceSingle();
                    }

                    continue;
                }

                break;
            }

            if (IsDigit(Current) || Current == '.')
            {
                AdvanceSingle();
                continue;
            }

            if (!sawExponent && (Current == 'e' || Current == 'E'))
            {
                sawExponent = true;
                AdvanceSingle();
                if (Current == '+' || Current == '-')
                {
                    AdvanceSingle();
                }

                continue;
            }

            break;
        }

        if (IsIdentifierStart(Current))
        {
            AdvanceSingle();
            while (IsIdentifierPart(Current))
            {
                AdvanceSingle();
            }
        }

        var lexeme = _source[startIndex.._index];
        if (!IsValidNumber(lexeme.AsSpan()))
        {
            throw CreateSyntaxException("malformed number", start);
        }

        return CreateToken(LuaTokenKind.Number, startIndex, start);
    }

    private LuaToken ReadShortString(int startIndex, LuaSourcePosition start)
    {
        var delimiter = Current;
        var value = new StringBuilder();

        AdvanceSingle();

        while (!IsAtEnd)
        {
            if (Current == delimiter)
            {
                AdvanceSingle();
                return CreateToken(LuaTokenKind.String, startIndex, start, value.ToString());
            }

            if (IsNewLine(Current))
            {
                throw CreateSyntaxException("unfinished string", start);
            }

            if (Current != '\\')
            {
                value.Append(Current);
                AdvanceSingle();
                continue;
            }

            AdvanceSingle();
            if (IsAtEnd)
            {
                throw CreateSyntaxException("unfinished string", start);
            }

            switch (Current)
            {
                case 'a':
                    value.Append('\a');
                    AdvanceSingle();
                    break;
                case 'b':
                    value.Append('\b');
                    AdvanceSingle();
                    break;
                case 'f':
                    value.Append('\f');
                    AdvanceSingle();
                    break;
                case 'n':
                    value.Append('\n');
                    AdvanceSingle();
                    break;
                case 'r':
                    value.Append('\r');
                    AdvanceSingle();
                    break;
                case 't':
                    value.Append('\t');
                    AdvanceSingle();
                    break;
                case 'v':
                    value.Append('\v');
                    AdvanceSingle();
                    break;
                case '\\':
                case '"':
                case '\'':
                    value.Append(Current);
                    AdvanceSingle();
                    break;
                case 'x':
                    value.Append((char)ReadHexEscape());
                    break;
                case 'u':
                    value.Append(ReadUnicodeEscape());
                    break;
                case 'z':
                    AdvanceSingle();
                    while (!IsAtEnd && IsZapWhitespace(Current))
                    {
                        if (IsNewLine(Current))
                        {
                            ConsumeNewLine();
                        }
                        else
                        {
                            AdvanceSingle();
                        }
                    }

                    break;
                case '\n':
                case '\r':
                    ConsumeNewLine();
                    value.Append('\n');
                    break;
                default:
                    if (!IsDigit(Current))
                    {
                        throw CreateSyntaxException("invalid escape sequence", CurrentPosition);
                    }

                    value.Append((char)ReadDecimalEscape());
                    break;
            }
        }

        throw CreateSyntaxException("unfinished string", start);
    }

    private LuaToken ReadBracketToken(int startIndex, LuaSourcePosition start)
    {
        if (TryGetLongBracketLevel(out var level, out var invalidDelimiter))
        {
            var value = ReadLongBracket(level, captureValue: true, start.Line);
            return CreateToken(LuaTokenKind.String, startIndex, start, value);
        }

        if (invalidDelimiter)
        {
            throw CreateSyntaxException("invalid long string delimiter", start);
        }

        return ReadSingleCharToken(startIndex, start, LuaTokenKind.LeftBracket);
    }

    private LuaToken ReadDotToken(int startIndex, LuaSourcePosition start)
    {
        if (Peek(1) == '.')
        {
            if (Peek(2) == '.')
            {
                AdvanceSingle();
                AdvanceSingle();
                AdvanceSingle();
                return CreateToken(LuaTokenKind.Vararg, startIndex, start);
            }

            AdvanceSingle();
            AdvanceSingle();
            return CreateToken(LuaTokenKind.Concat, startIndex, start);
        }

        return ReadSingleCharToken(startIndex, start, LuaTokenKind.Dot);
    }

    private LuaToken ReadComparisonToken(
        int startIndex,
        LuaSourcePosition start,
        LuaTokenKind singleKind,
        char firstCompoundChar,
        LuaTokenKind firstCompoundKind,
        char secondCompoundChar,
        LuaTokenKind secondCompoundKind)
    {
        AdvanceSingle();
        if (Current == firstCompoundChar)
        {
            AdvanceSingle();
            return CreateToken(firstCompoundKind, startIndex, start);
        }

        if (Current == secondCompoundChar)
        {
            AdvanceSingle();
            return CreateToken(secondCompoundKind, startIndex, start);
        }

        return CreateToken(singleKind, startIndex, start);
    }

    private LuaToken ReadCompoundToken(
        int startIndex,
        LuaSourcePosition start,
        LuaTokenKind singleKind,
        char compoundChar,
        LuaTokenKind compoundKind)
    {
        AdvanceSingle();
        if (Current == compoundChar)
        {
            AdvanceSingle();
            return CreateToken(compoundKind, startIndex, start);
        }

        return CreateToken(singleKind, startIndex, start);
    }

    private LuaToken ReadSingleCharToken(int startIndex, LuaSourcePosition start, LuaTokenKind kind)
    {
        AdvanceSingle();
        return CreateToken(kind, startIndex, start);
    }

    private string ReadLongBracket(int level, bool captureValue, int startLine)
    {
        ConsumeLongBracketStart(level);

        if (!IsAtEnd && IsNewLine(Current))
        {
            ConsumeNewLine();
        }

        var value = captureValue ? new StringBuilder() : null;
        while (true)
        {
            if (IsAtEnd)
            {
                var kind = captureValue ? "string" : "comment";
                throw CreateSyntaxException($"unfinished long {kind} (starting at line {startLine})", CurrentPosition);
            }

            if (TryConsumeLongBracketEnd(level))
            {
                return value?.ToString() ?? string.Empty;
            }

            if (IsNewLine(Current))
            {
                ConsumeNewLine();
                value?.Append('\n');
                continue;
            }

            value?.Append(Current);
            AdvanceSingle();
        }
    }

    private bool TryGetLongBracketLevel(out int level, out bool invalidDelimiter)
    {
        invalidDelimiter = false;
        level = -1;

        if (Current != '[' && Current != ']')
        {
            return false;
        }

        var delimiter = Current;
        var index = _index + 1;
        var equalsCount = 0;

        while (index < _source.Length && _source[index] == '=')
        {
            equalsCount++;
            index++;
        }

        if (index < _source.Length && _source[index] == delimiter)
        {
            level = equalsCount;
            return true;
        }

        invalidDelimiter = equalsCount > 0;
        return false;
    }

    private void ConsumeLongBracketStart(int level)
    {
        AdvanceSingle();
        for (var i = 0; i < level; i++)
        {
            AdvanceSingle();
        }

        AdvanceSingle();
    }

    private bool TryConsumeLongBracketEnd(int level)
    {
        if (Current != ']')
        {
            return false;
        }

        var index = _index + 1;
        var equalsCount = 0;
        while (index < _source.Length && _source[index] == '=')
        {
            equalsCount++;
            index++;
        }

        if (equalsCount != level || index >= _source.Length || _source[index] != ']')
        {
            return false;
        }

        AdvanceSingle();
        for (var i = 0; i < level; i++)
        {
            AdvanceSingle();
        }

        AdvanceSingle();
        return true;
    }

    private int ReadHexEscape()
    {
        AdvanceSingle();

        var high = ReadHexDigit("hexadecimal digit expected");
        var low = ReadHexDigit("hexadecimal digit expected");
        return (high << 4) + low;
    }

    private string ReadUnicodeEscape()
    {
        AdvanceSingle();

        if (Current != '{')
        {
            throw CreateSyntaxException("missing '{'", CurrentPosition);
        }

        AdvanceSingle();

        if (!IsHexDigit(Current))
        {
            throw CreateSyntaxException("hexadecimal digit expected", CurrentPosition);
        }

        uint codePoint = 0;
        while (IsHexDigit(Current))
        {
            var nextCodePoint = ((ulong)codePoint * 16UL) + (uint)HexValue(Current);
            if (nextCodePoint > 0x10FFFFUL)
            {
                throw CreateSyntaxException("UTF-8 value too large", CurrentPosition);
            }

            codePoint = (uint)nextCodePoint;
            AdvanceSingle();
        }

        if (Current != '}')
        {
            throw CreateSyntaxException("missing '}'", CurrentPosition);
        }

        AdvanceSingle();

        if (codePoint is >= 0xD800u and <= 0xDFFFu)
        {
            throw CreateSyntaxException("UTF-8 value too large", CurrentPosition);
        }

        return char.ConvertFromUtf32((int)codePoint);
    }

    private int ReadDecimalEscape()
    {
        var value = 0;
        var count = 0;

        while (count < 3 && IsDigit(Current))
        {
            value = (value * 10) + (Current - '0');
            AdvanceSingle();
            count++;
        }

        if (value > byte.MaxValue)
        {
            throw CreateSyntaxException("decimal escape too large", CurrentPosition);
        }

        return value;
    }

    private int ReadHexDigit(string message)
    {
        if (!IsHexDigit(Current))
        {
            throw CreateSyntaxException(message, CurrentPosition);
        }

        var value = HexValue(Current);
        AdvanceSingle();
        return value;
    }

    private void AdvanceSingle()
    {
        if (IsAtEnd)
        {
            return;
        }

        _index++;
        _column++;
    }

    private void ConsumeNewLine()
    {
        var current = Current;
        AdvanceSingle();

        if (!IsAtEnd && IsNewLine(Current) && Current != current)
        {
            AdvanceSingle();
        }

        _line++;
        _column = 1;
    }

    private LuaToken CreateToken(LuaTokenKind kind, int startIndex, LuaSourcePosition start, string? stringValue = null)
    {
        return new LuaToken
        {
            Kind = kind,
            Lexeme = _source[startIndex.._index],
            Range = new LuaSourceRange(start, CurrentPosition),
            StringValue = stringValue
        };
    }

    private LuaSyntaxException CreateSyntaxException(string message, LuaSourcePosition position)
    {
        return new LuaSyntaxException(message, position, _sourceName);
    }

    private static bool IsIdentifierStart(char c)
    {
        return c == '_' || IsAsciiLetter(c);
    }

    private static bool IsIdentifierPart(char c)
    {
        return IsIdentifierStart(c) || IsDigit(c);
    }

    private static bool IsAsciiLetter(char c)
    {
        return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
    }

    private static bool IsDigit(char c)
    {
        return c >= '0' && c <= '9';
    }

    private static bool IsHexDigit(char c)
    {
        return IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
    }

    private static int HexValue(char c)
    {
        return c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            >= 'A' and <= 'F' => c - 'A' + 10,
            _ => throw new ArgumentOutOfRangeException(nameof(c))
        };
    }

    private static bool IsNewLine(char c)
    {
        return c is '\n' or '\r';
    }

    private static bool IsSpace(char c)
    {
        return c is ' ' or '\f' or '\t' or '\v';
    }

    private static bool IsZapWhitespace(char c)
    {
        return IsSpace(c) || IsNewLine(c);
    }

    private static bool IsValidNumber(ReadOnlySpan<char> text)
    {
        return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? IsValidHexNumber(text)
            : IsValidDecimalNumber(text);
    }

    private static bool IsValidDecimalNumber(ReadOnlySpan<char> text)
    {
        var index = 0;
        var digitsBeforeDot = ConsumeDigits(text, ref index);
        var digitsAfterDot = false;

        if (index < text.Length && text[index] == '.')
        {
            index++;
            digitsAfterDot = ConsumeDigits(text, ref index);
        }

        if (!digitsBeforeDot && !digitsAfterDot)
        {
            return false;
        }

        if (index < text.Length && (text[index] == 'e' || text[index] == 'E'))
        {
            index++;
            if (index < text.Length && (text[index] == '+' || text[index] == '-'))
            {
                index++;
            }

            if (!ConsumeDigits(text, ref index))
            {
                return false;
            }
        }

        return index == text.Length;
    }

    private static bool IsValidHexNumber(ReadOnlySpan<char> text)
    {
        if (text.Length < 3)
        {
            return false;
        }

        var index = 2;
        var digitsBeforeDot = ConsumeHexDigits(text, ref index);
        var digitsAfterDot = false;
        var hasDot = false;

        if (index < text.Length && text[index] == '.')
        {
            hasDot = true;
            index++;
            digitsAfterDot = ConsumeHexDigits(text, ref index);
        }

        if (!digitsBeforeDot && !digitsAfterDot)
        {
            return false;
        }

        if (index == text.Length)
        {
            return !hasDot;
        }

        if (text[index] != 'p' && text[index] != 'P')
        {
            return false;
        }

        index++;
        if (index < text.Length && (text[index] == '+' || text[index] == '-'))
        {
            index++;
        }

        return ConsumeDigits(text, ref index) && index == text.Length;
    }

    private static bool ConsumeDigits(ReadOnlySpan<char> text, ref int index)
    {
        var start = index;
        while (index < text.Length && IsDigit(text[index]))
        {
            index++;
        }

        return index > start;
    }

    private static bool ConsumeHexDigits(ReadOnlySpan<char> text, ref int index)
    {
        var start = index;
        while (index < text.Length && IsHexDigit(text[index]))
        {
            index++;
        }

        return index > start;
    }
}
