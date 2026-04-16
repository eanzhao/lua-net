using Lua.Syntax.Lexing;
using Shouldly;

namespace Lua.Syntax.Tests;

public class LuaLexerTests
{
    [Fact]
    public void Tokenize_ShouldRecognizeKeywordsOperatorsAndIdentifiers()
    {
        var tokens = new LuaLexer("global answer = foo // 2 << 1 ~= nil and true or false").Tokenize();

        tokens.Select(token => token.Kind).ShouldBe(
        [
            LuaTokenKind.Global,
            LuaTokenKind.Identifier,
            LuaTokenKind.Assign,
            LuaTokenKind.Identifier,
            LuaTokenKind.IntegerDivision,
            LuaTokenKind.Number,
            LuaTokenKind.LeftShift,
            LuaTokenKind.Number,
            LuaTokenKind.NotEqual,
            LuaTokenKind.Nil,
            LuaTokenKind.And,
            LuaTokenKind.True,
            LuaTokenKind.Or,
            LuaTokenKind.False,
            LuaTokenKind.EndOfFile
        ]);

        tokens[0].Lexeme.ShouldBe("global");
        tokens[1].Lexeme.ShouldBe("answer");
        tokens[3].Lexeme.ShouldBe("foo");
    }

    [Fact]
    public void Tokenize_ShouldTrackLineAndColumnAcrossComments()
    {
        const string source = "local foo\n-- skip me\n  global bar";

        var tokens = new LuaLexer(source, "sample.lua").Tokenize();

        tokens[0].Range.Start.Line.ShouldBe(1);
        tokens[0].Range.Start.Column.ShouldBe(1);
        tokens[0].Range.End.Line.ShouldBe(1);
        tokens[0].Range.End.Column.ShouldBe(6);

        tokens[2].Kind.ShouldBe(LuaTokenKind.Global);
        tokens[2].Range.Start.Line.ShouldBe(3);
        tokens[2].Range.Start.Column.ShouldBe(3);
        tokens[3].Lexeme.ShouldBe("bar");
        tokens[^1].Range.Start.Line.ShouldBe(3);
    }

    [Fact]
    public void Tokenize_ShouldReadNumericLiterals()
    {
        var tokens = new LuaLexer("42 3.14e-2 0xff 0x1.fp+2 0xF0.0 .5").Tokenize();

        tokens.Where(token => token.Kind == LuaTokenKind.Number)
            .Select(token => token.Lexeme)
            .ShouldBe(["42", "3.14e-2", "0xff", "0x1.fp+2", "0xF0.0", ".5"]);
    }

    [Fact]
    public void Tokenize_ShouldDecodeShortStringEscapes()
    {
        const string source = "'a\\n\\x41\\u{03C0}\\122\\z  \ntrim'";

        var token = new LuaLexer(source).Tokenize().Single(token => token.Kind == LuaTokenKind.String);

        token.StringValue.ShouldBe("a\nAπztrim");
    }

    [Fact]
    public void Tokenize_ShouldReadLongStringsAndSkipLongComments()
    {
        const string source = """
--[==[
comment
]==]
local s = [=[
alpha
beta]=]
""";

        var tokens = new LuaLexer(source).Tokenize();

        tokens.Select(token => token.Kind).ShouldBe(
        [
            LuaTokenKind.Local,
            LuaTokenKind.Identifier,
            LuaTokenKind.Assign,
            LuaTokenKind.String,
            LuaTokenKind.EndOfFile
        ]);

        tokens[0].Range.Start.Line.ShouldBe(4);
        tokens[3].StringValue.ShouldBe("alpha\nbeta");
    }

    [Fact]
    public void Tokenize_ShouldSkipUtf8BomAndHashPreamble()
    {
        const string source = "\uFEFF# testing special comment on first line\nreturn 1";

        var tokens = new LuaLexer(source).Tokenize();

        tokens.Select(token => token.Kind).ShouldBe(
        [
            LuaTokenKind.Return,
            LuaTokenKind.Number,
            LuaTokenKind.EndOfFile
        ]);

        tokens[0].Range.Start.Line.ShouldBe(2);
        tokens[0].Range.Start.Column.ShouldBe(1);
    }

    [Fact]
    public void Tokenize_InvalidLongStringDelimiter_ShouldThrow()
    {
        var exception = Should.Throw<LuaSyntaxException>(() => new LuaLexer("[=oops").Tokenize());

        exception.Message.ShouldContain("invalid long string delimiter");
    }

    [Fact]
    public void Tokenize_MalformedNumber_ShouldThrow()
    {
        var exception = Should.Throw<LuaSyntaxException>(() => new LuaLexer("123abc").Tokenize());

        exception.Message.ShouldContain("malformed number");
    }

    [Fact]
    public void Tokenize_TooLargeUnicodeEscape_ShouldThrow()
    {
        var exception = Should.Throw<LuaSyntaxException>(() => new LuaLexer("'\\u{110000}'").Tokenize());

        exception.Message.ShouldContain("UTF-8 value too large");
    }

    [Fact]
    public void Tokenize_UnfinishedShortString_ShouldThrow()
    {
        Should.Throw<LuaSyntaxException>(() => new LuaLexer("'hello").Tokenize())
            .Message.ShouldContain("unfinished string");

        Should.Throw<LuaSyntaxException>(() => new LuaLexer("\"hello\n\"").Tokenize())
            .Message.ShouldContain("unfinished string");
    }

    [Fact]
    public void Tokenize_InvalidEscapeSequence_ShouldThrow()
    {
        Should.Throw<LuaSyntaxException>(() => new LuaLexer("'\\q'").Tokenize())
            .Message.ShouldContain("escape sequence");
    }

    [Fact]
    public void Tokenize_DecimalEscapeOverflow_ShouldThrow()
    {
        Should.Throw<LuaSyntaxException>(() => new LuaLexer("'\\999'").Tokenize())
            .Message.ShouldContain("decimal escape too large");
    }

    [Fact]
    public void Tokenize_HexFloatWithoutExponent_ShouldThrow()
    {
        Should.Throw<LuaSyntaxException>(() => new LuaLexer("0x1.8").Tokenize())
            .Message.ShouldContain("malformed number");
    }

    [Fact]
    public void Tokenize_UnfinishedLongString_ShouldThrow()
    {
        Should.Throw<LuaSyntaxException>(() => new LuaLexer("[[ unterminated").Tokenize())
            .Message.ShouldContain("unfinished long string");
    }

    [Fact]
    public void Tokenize_ShouldRecognizeAllSingleCharOperators()
    {
        var tokens = new LuaLexer("+ * % ^ # & ~ | = ( ) { } [ ] : ; ,").Tokenize();

        var kinds = tokens.Where(t => t.Kind != LuaTokenKind.EndOfFile).Select(t => t.Kind).ToArray();
        kinds.ShouldContain(LuaTokenKind.Plus);
        kinds.ShouldContain(LuaTokenKind.Star);
        kinds.ShouldContain(LuaTokenKind.Percent);
        kinds.ShouldContain(LuaTokenKind.Caret);
        kinds.ShouldContain(LuaTokenKind.Hash);
        kinds.ShouldContain(LuaTokenKind.Ampersand);
        kinds.ShouldContain(LuaTokenKind.Tilde);
        kinds.ShouldContain(LuaTokenKind.Pipe);
        kinds.ShouldContain(LuaTokenKind.Assign);
        kinds.ShouldContain(LuaTokenKind.Semicolon);
        kinds.ShouldContain(LuaTokenKind.Comma);
    }
}
