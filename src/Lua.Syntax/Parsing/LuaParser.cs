using Lua.Syntax.Ast;
using Lua.Syntax.Lexing;

namespace Lua.Syntax.Parsing;

public sealed class LuaParser
{
    private const int UnaryPriority = 12;

    private readonly IReadOnlyList<LuaToken> _tokens;
    private readonly string _sourceName;
    private int _position;
    private int _loopDepth;
    private bool _allowVararg = true;

    public LuaParser(string source, string? sourceName = null)
        : this(new LuaLexer(source, sourceName).Tokenize(), sourceName)
    {
    }

    public LuaParser(IReadOnlyList<LuaToken> tokens, string? sourceName = null)
    {
        if (tokens is null)
        {
            throw new ArgumentNullException(nameof(tokens));
        }

        if (tokens.Count == 0)
        {
            throw new ArgumentException("Token list cannot be empty.", nameof(tokens));
        }

        _tokens = tokens;
        _sourceName = string.IsNullOrWhiteSpace(sourceName) ? "<input>" : sourceName;
    }

    public static LuaChunkSyntax Parse(string source, string? sourceName = null)
    {
        return new LuaParser(source, sourceName).ParseChunk();
    }

    public LuaChunkSyntax ParseChunk()
    {
        _loopDepth = 0;
        _allowVararg = true;

        var block = ParseBlock();
        Expect(LuaTokenKind.EndOfFile);

        return new LuaChunkSyntax(block.Range, block);
    }

    private LuaToken Current => _tokens[Math.Min(_position, _tokens.Count - 1)];

    private LuaToken Peek(int offset)
    {
        var index = _position + offset;
        if (index < 0)
        {
            return _tokens[0];
        }

        return _tokens[Math.Min(index, _tokens.Count - 1)];
    }

    private LuaToken Advance()
    {
        var token = Current;
        if (_position < _tokens.Count - 1)
        {
            _position++;
        }

        return token;
    }

    private bool Match(LuaTokenKind kind)
    {
        if (Current.Kind != kind)
        {
            return false;
        }

        Advance();
        return true;
    }

    private LuaToken Expect(LuaTokenKind kind)
    {
        if (Current.Kind != kind)
        {
            throw CreateSyntaxException($"expected {FormatToken(kind)}");
        }

        return Advance();
    }

    private LuaSyntaxException CreateSyntaxException(string message)
    {
        return new LuaSyntaxException(message, Current.Range.Start, _sourceName);
    }

    private static LuaSourceRange Combine(LuaSyntaxNode start, LuaSyntaxNode end)
    {
        return new LuaSourceRange(start.Range.Start, end.Range.End);
    }

    private static LuaSourceRange Combine(LuaToken start, LuaToken end)
    {
        return new LuaSourceRange(start.Range.Start, end.Range.End);
    }

    private static LuaSourceRange Combine(LuaToken start, LuaSyntaxNode end)
    {
        return new LuaSourceRange(start.Range.Start, end.Range.End);
    }

    private static LuaSourceRange Combine(LuaSyntaxNode start, LuaToken end)
    {
        return new LuaSourceRange(start.Range.Start, end.Range.End);
    }

    private LuaBlockSyntax ParseBlock()
    {
        var start = Current.Range.Start;
        var statements = new List<LuaStatementSyntax>();

        while (!IsBlockTerminator(Current.Kind))
        {
            if (Current.Kind == LuaTokenKind.Return)
            {
                statements.Add(ParseReturnStatement());
                break;
            }

            statements.Add(ParseStatement());
        }

        var end = statements.Count > 0
            ? statements[^1].Range.End
            : Current.Range.Start;

        return new LuaBlockSyntax(new LuaSourceRange(start, end), statements);
    }

    private static bool IsBlockTerminator(LuaTokenKind kind)
    {
        return kind is LuaTokenKind.Else or LuaTokenKind.ElseIf or LuaTokenKind.End or LuaTokenKind.EndOfFile or LuaTokenKind.Until;
    }

    private LuaStatementSyntax ParseStatement()
    {
        return Current.Kind switch
        {
            LuaTokenKind.Semicolon => ParseEmptyStatement(),
            LuaTokenKind.Break => ParseBreakStatement(),
            LuaTokenKind.Goto => ParseGotoStatement(),
            LuaTokenKind.DoubleColon => ParseLabelStatement(),
            LuaTokenKind.Do => ParseDoStatement(),
            LuaTokenKind.While => ParseWhileStatement(),
            LuaTokenKind.Repeat => ParseRepeatStatement(),
            LuaTokenKind.If => ParseIfStatement(),
            LuaTokenKind.For => ParseForStatement(),
            LuaTokenKind.Function => ParseFunctionDeclarationStatement(),
            LuaTokenKind.Local => ParseLocalStatement(),
            LuaTokenKind.Global => ParseGlobalStatement(),
            _ => ParseAssignmentOrCallStatement()
        };
    }

    private LuaEmptyStatementSyntax ParseEmptyStatement()
    {
        var semicolon = Expect(LuaTokenKind.Semicolon);
        return new LuaEmptyStatementSyntax(semicolon.Range);
    }

    private LuaBreakStatementSyntax ParseBreakStatement()
    {
        if (_loopDepth == 0)
        {
            throw CreateSyntaxException("break outside loop");
        }

        var breakToken = Expect(LuaTokenKind.Break);
        return new LuaBreakStatementSyntax(breakToken.Range);
    }

    private LuaGotoStatementSyntax ParseGotoStatement()
    {
        var gotoToken = Expect(LuaTokenKind.Goto);
        var name = ParseName();
        return new LuaGotoStatementSyntax(Combine(gotoToken, name), name);
    }

    private LuaLabelStatementSyntax ParseLabelStatement()
    {
        var start = Expect(LuaTokenKind.DoubleColon);
        var name = ParseName();
        var end = Expect(LuaTokenKind.DoubleColon);
        return new LuaLabelStatementSyntax(Combine(start, end), name);
    }

    private LuaDoStatementSyntax ParseDoStatement()
    {
        var doToken = Expect(LuaTokenKind.Do);
        var block = ParseBlock();
        var endToken = Expect(LuaTokenKind.End);
        return new LuaDoStatementSyntax(Combine(doToken, endToken), block);
    }

    private LuaWhileStatementSyntax ParseWhileStatement()
    {
        var whileToken = Expect(LuaTokenKind.While);
        var condition = ParseExpression();
        Expect(LuaTokenKind.Do);

        _loopDepth++;
        var block = ParseBlock();
        _loopDepth--;

        var endToken = Expect(LuaTokenKind.End);
        return new LuaWhileStatementSyntax(Combine(whileToken, endToken), condition, block);
    }

    private LuaRepeatStatementSyntax ParseRepeatStatement()
    {
        var repeatToken = Expect(LuaTokenKind.Repeat);

        _loopDepth++;
        var block = ParseBlock();
        _loopDepth--;

        Expect(LuaTokenKind.Until);
        var condition = ParseExpression();
        return new LuaRepeatStatementSyntax(
            new LuaSourceRange(repeatToken.Range.Start, condition.Range.End),
            block,
            condition);
    }

    private LuaIfStatementSyntax ParseIfStatement()
    {
        var ifToken = Expect(LuaTokenKind.If);
        var ifClause = ParseConditionalClause(ifToken);
        var elseIfClauses = new List<LuaConditionalClauseSyntax>();

        while (Current.Kind == LuaTokenKind.ElseIf)
        {
            var elseIfToken = Expect(LuaTokenKind.ElseIf);
            elseIfClauses.Add(ParseConditionalClause(elseIfToken));
        }

        LuaElseClauseSyntax? elseClause = null;
        if (Match(LuaTokenKind.Else))
        {
            var elseStart = _tokens[_position - 1];
            var elseBlock = ParseBlock();
            elseClause = new LuaElseClauseSyntax(
                new LuaSourceRange(elseStart.Range.Start, elseBlock.Range.End),
                elseBlock);
        }

        var endToken = Expect(LuaTokenKind.End);
        return new LuaIfStatementSyntax(
            Combine(ifToken, endToken),
            ifClause,
            elseIfClauses,
            elseClause);
    }

    private LuaConditionalClauseSyntax ParseConditionalClause(LuaToken keyword)
    {
        var condition = ParseExpression();
        Expect(LuaTokenKind.Then);
        var block = ParseBlock();
        return new LuaConditionalClauseSyntax(
            new LuaSourceRange(keyword.Range.Start, block.Range.End),
            condition,
            block);
    }

    private LuaStatementSyntax ParseForStatement()
    {
        var forToken = Expect(LuaTokenKind.For);
        var name = ParseName();

        if (Match(LuaTokenKind.Assign))
        {
            return ParseNumericForStatement(forToken, name);
        }

        return ParseGenericForStatement(forToken, name);
    }

    private LuaNumericForStatementSyntax ParseNumericForStatement(LuaToken forToken, LuaNameSyntax name)
    {
        var initialValue = ParseExpression();
        Expect(LuaTokenKind.Comma);
        var limit = ParseExpression();

        LuaExpressionSyntax? step = null;
        if (Match(LuaTokenKind.Comma))
        {
            step = ParseExpression();
        }

        Expect(LuaTokenKind.Do);

        _loopDepth++;
        var block = ParseBlock();
        _loopDepth--;

        var endToken = Expect(LuaTokenKind.End);
        return new LuaNumericForStatementSyntax(
            Combine(forToken, endToken),
            name,
            initialValue,
            limit,
            step,
            block);
    }

    private LuaGenericForStatementSyntax ParseGenericForStatement(LuaToken forToken, LuaNameSyntax firstName)
    {
        var names = new List<LuaNameSyntax> { firstName };
        while (Match(LuaTokenKind.Comma))
        {
            names.Add(ParseName());
        }

        Expect(LuaTokenKind.In);
        var expressions = ParseExpressionList();
        Expect(LuaTokenKind.Do);

        _loopDepth++;
        var block = ParseBlock();
        _loopDepth--;

        var endToken = Expect(LuaTokenKind.End);
        return new LuaGenericForStatementSyntax(
            Combine(forToken, endToken),
            names,
            expressions,
            block);
    }

    private LuaFunctionDeclarationStatementSyntax ParseFunctionDeclarationStatement()
    {
        var functionToken = Expect(LuaTokenKind.Function);
        var name = ParseFunctionName();
        var body = ParseFunctionBody();
        return new LuaFunctionDeclarationStatementSyntax(
            Combine(functionToken, body),
            name,
            body);
    }

    private LuaStatementSyntax ParseLocalStatement()
    {
        var localToken = Expect(LuaTokenKind.Local);
        if (Match(LuaTokenKind.Function))
        {
            var name = ParseName();
            var body = ParseFunctionBody();
            return new LuaLocalFunctionStatementSyntax(
                new LuaSourceRange(localToken.Range.Start, body.Range.End),
                name,
                body);
        }

        var leadingAttribute = Current.Kind == LuaTokenKind.LessThan
            ? ParseAttribute(allowClose: true)
            : null;

        var names = ParseDeclarationNameList(allowClose: true, leadingAttribute);
        var initializers = Match(LuaTokenKind.Assign)
            ? ParseExpressionList()
            : [];

        var end = initializers.Count > 0
            ? initializers[^1].Range.End
            : names.Range.End;

        return new LuaLocalDeclarationStatementSyntax(
            new LuaSourceRange(localToken.Range.Start, end),
            names,
            initializers);
    }

    private LuaStatementSyntax ParseGlobalStatement()
    {
        var globalToken = Expect(LuaTokenKind.Global);
        if (Match(LuaTokenKind.Function))
        {
            var name = ParseName();
            var body = ParseFunctionBody();
            return new LuaGlobalFunctionStatementSyntax(
                new LuaSourceRange(globalToken.Range.Start, body.Range.End),
                name,
                body);
        }

        var leadingAttribute = Current.Kind == LuaTokenKind.LessThan
            ? ParseAttribute(allowClose: false)
            : null;

        if (Match(LuaTokenKind.Star))
        {
            var starToken = _tokens[_position - 1];
            return new LuaGlobalWildcardStatementSyntax(
                new LuaSourceRange(globalToken.Range.Start, starToken.Range.End),
                leadingAttribute);
        }

        var names = ParseDeclarationNameList(allowClose: false, leadingAttribute);
        var initializers = Match(LuaTokenKind.Assign)
            ? ParseExpressionList()
            : [];

        var end = initializers.Count > 0
            ? initializers[^1].Range.End
            : names.Range.End;

        return new LuaGlobalDeclarationStatementSyntax(
            new LuaSourceRange(globalToken.Range.Start, end),
            names,
            initializers);
    }

    private LuaAssignmentStatementSyntax ParseAssignmentStatement(LuaExpressionSyntax firstExpression)
    {
        var firstVariable = RequireVariable(firstExpression);
        var variables = new List<LuaVariableExpressionSyntax> { firstVariable };

        while (Match(LuaTokenKind.Comma))
        {
            variables.Add(RequireVariable(ParsePrefixExpression()));
        }

        Expect(LuaTokenKind.Assign);
        var values = ParseExpressionList();

        return new LuaAssignmentStatementSyntax(
            new LuaSourceRange(variables[0].Range.Start, values[^1].Range.End),
            variables,
            values);
    }

    private LuaStatementSyntax ParseAssignmentOrCallStatement()
    {
        var expression = ParsePrefixExpression();

        if (Current.Kind is LuaTokenKind.Assign or LuaTokenKind.Comma)
        {
            return ParseAssignmentStatement(expression);
        }

        if (expression is LuaFunctionCallExpressionSyntax call)
        {
            return new LuaFunctionCallStatementSyntax(call.Range, call);
        }

        throw CreateSyntaxException("expected assignment or function call");
    }

    private LuaReturnStatementSyntax ParseReturnStatement()
    {
        var returnToken = Expect(LuaTokenKind.Return);
        var expressions = IsReturnTerminator(Current.Kind)
            ? []
            : ParseExpressionList();

        LuaSourcePosition end = expressions.Count > 0
            ? expressions[^1].Range.End
            : returnToken.Range.End;

        if (Match(LuaTokenKind.Semicolon))
        {
            end = _tokens[_position - 1].Range.End;
        }

        return new LuaReturnStatementSyntax(
            new LuaSourceRange(returnToken.Range.Start, end),
            expressions);
    }

    private static bool IsReturnTerminator(LuaTokenKind kind)
    {
        return kind is LuaTokenKind.End or LuaTokenKind.Else or LuaTokenKind.ElseIf or LuaTokenKind.EndOfFile or LuaTokenKind.Semicolon or LuaTokenKind.Until;
    }

    private LuaDeclarationNameListSyntax ParseDeclarationNameList(bool allowClose, LuaVariableAttributeSyntax? defaultAttribute)
    {
        var start = defaultAttribute?.Range.Start ?? Current.Range.Start;
        var names = new List<LuaDeclaredNameSyntax>();
        var toCloseCount = 0;

        do
        {
            var name = ParseName();
            var attribute = Current.Kind == LuaTokenKind.LessThan
                ? ParseAttribute(allowClose)
                : null;

            var effectiveAttribute = attribute?.Kind ?? defaultAttribute?.Kind;
            if (effectiveAttribute == LuaVariableAttributeKind.Close)
            {
                toCloseCount++;
                if (toCloseCount > 1)
                {
                    throw CreateSyntaxException("multiple to-be-closed variables in declaration");
                }
            }

            var end = attribute?.Range.End ?? name.Range.End;
            names.Add(new LuaDeclaredNameSyntax(
                new LuaSourceRange(name.Range.Start, end),
                name,
                attribute));
        }
        while (Match(LuaTokenKind.Comma));

        return new LuaDeclarationNameListSyntax(
            new LuaSourceRange(start, names[^1].Range.End),
            defaultAttribute,
            names);
    }

    private LuaVariableAttributeSyntax ParseAttribute(bool allowClose)
    {
        var lessThan = Expect(LuaTokenKind.LessThan);
        var name = ParseName();
        var greaterThan = Expect(LuaTokenKind.GreaterThan);

        var kind = name.Identifier switch
        {
            "const" => LuaVariableAttributeKind.Const,
            "close" when allowClose => LuaVariableAttributeKind.Close,
            "close" => throw CreateSyntaxException("global variables cannot be to-be-closed"),
            _ => throw CreateSyntaxException($"unknown attribute '{name.Identifier}'")
        };

        return new LuaVariableAttributeSyntax(
            Combine(lessThan, greaterThan),
            kind);
    }

    private LuaFunctionNameSyntax ParseFunctionName()
    {
        var segments = new List<LuaNameSyntax> { ParseName() };
        while (Match(LuaTokenKind.Dot))
        {
            segments.Add(ParseName());
        }

        LuaNameSyntax? methodName = null;
        if (Match(LuaTokenKind.Colon))
        {
            methodName = ParseName();
        }

        var end = methodName?.Range.End ?? segments[^1].Range.End;
        return new LuaFunctionNameSyntax(
            new LuaSourceRange(segments[0].Range.Start, end),
            segments,
            methodName);
    }

    private LuaFunctionBodySyntax ParseFunctionBody()
    {
        var leftParen = Expect(LuaTokenKind.LeftParen);
        var parameters = new List<LuaNameSyntax>();
        LuaVarargParameterSyntax? varargParameter = null;

        if (Current.Kind != LuaTokenKind.RightParen)
        {
            if (Current.Kind == LuaTokenKind.Vararg)
            {
                varargParameter = ParseVarargParameter();
            }
            else
            {
                parameters.Add(ParseName());
                while (Match(LuaTokenKind.Comma))
                {
                    if (Current.Kind == LuaTokenKind.Vararg)
                    {
                        varargParameter = ParseVarargParameter();
                        break;
                    }

                    parameters.Add(ParseName());
                }
            }
        }

        Expect(LuaTokenKind.RightParen);

        var previousAllowVararg = _allowVararg;
        var previousLoopDepth = _loopDepth;
        _allowVararg = varargParameter is not null;
        _loopDepth = 0;

        var block = ParseBlock();

        _allowVararg = previousAllowVararg;
        _loopDepth = previousLoopDepth;

        var endToken = Expect(LuaTokenKind.End);
        return new LuaFunctionBodySyntax(
            Combine(leftParen, endToken),
            parameters,
            varargParameter,
            block);
    }

    private LuaVarargParameterSyntax ParseVarargParameter()
    {
        var dots = Expect(LuaTokenKind.Vararg);
        LuaNameSyntax? name = null;
        if (Current.Kind == LuaTokenKind.Identifier)
        {
            name = ParseName();
        }

        var end = name?.Range.End ?? dots.Range.End;
        return new LuaVarargParameterSyntax(
            new LuaSourceRange(dots.Range.Start, end),
            name);
    }

    private IReadOnlyList<LuaExpressionSyntax> ParseExpressionList()
    {
        var expressions = new List<LuaExpressionSyntax> { ParseExpression() };
        while (Match(LuaTokenKind.Comma))
        {
            expressions.Add(ParseExpression());
        }

        return expressions;
    }

    private LuaExpressionSyntax ParseExpression()
    {
        return ParseSubExpression(0);
    }

    private LuaExpressionSyntax ParseSubExpression(int limit)
    {
        LuaExpressionSyntax expression;

        if (TryGetUnaryOperator(Current.Kind, out var unaryOperator))
        {
            var operatorToken = Advance();
            var operand = ParseSubExpression(UnaryPriority);
            expression = new LuaUnaryExpressionSyntax(
                new LuaSourceRange(operatorToken.Range.Start, operand.Range.End),
                unaryOperator,
                operand);
        }
        else
        {
            expression = ParseSimpleExpression();
        }

        while (TryGetBinaryOperator(Current.Kind, out var binaryInfo) && binaryInfo.LeftPriority > limit)
        {
            Advance();
            var right = ParseSubExpression(binaryInfo.RightPriority);
            expression = new LuaBinaryExpressionSyntax(
                new LuaSourceRange(expression.Range.Start, right.Range.End),
                expression,
                binaryInfo.Operator,
                right);
        }

        return expression;
    }

    private LuaExpressionSyntax ParseSimpleExpression()
    {
        return Current.Kind switch
        {
            LuaTokenKind.Nil => ParseNilLiteral(),
            LuaTokenKind.False => ParseBooleanLiteral(false),
            LuaTokenKind.True => ParseBooleanLiteral(true),
            LuaTokenKind.Number => ParseNumberLiteral(),
            LuaTokenKind.String => ParseStringLiteral(),
            LuaTokenKind.Vararg => ParseVarargExpression(),
            LuaTokenKind.Function => ParseFunctionExpression(),
            LuaTokenKind.LeftBrace => ParseTableConstructor(),
            _ => ParsePrefixExpression()
        };
    }

    private LuaNilLiteralExpressionSyntax ParseNilLiteral()
    {
        var token = Expect(LuaTokenKind.Nil);
        return new LuaNilLiteralExpressionSyntax(token.Range);
    }

    private LuaBooleanLiteralExpressionSyntax ParseBooleanLiteral(bool value)
    {
        var token = Expect(value ? LuaTokenKind.True : LuaTokenKind.False);
        return new LuaBooleanLiteralExpressionSyntax(token.Range, value);
    }

    private LuaNumberLiteralExpressionSyntax ParseNumberLiteral()
    {
        var token = Expect(LuaTokenKind.Number);
        return new LuaNumberLiteralExpressionSyntax(token.Range, token.Lexeme);
    }

    private LuaStringLiteralExpressionSyntax ParseStringLiteral()
    {
        var token = Expect(LuaTokenKind.String);
        return new LuaStringLiteralExpressionSyntax(token.Range, token.Lexeme, token.StringValue ?? string.Empty);
    }

    private LuaVarargExpressionSyntax ParseVarargExpression()
    {
        if (!_allowVararg)
        {
            throw CreateSyntaxException("cannot use '...' outside a vararg function");
        }

        var token = Expect(LuaTokenKind.Vararg);
        return new LuaVarargExpressionSyntax(token.Range);
    }

    private LuaFunctionExpressionSyntax ParseFunctionExpression()
    {
        var functionToken = Expect(LuaTokenKind.Function);
        var body = ParseFunctionBody();
        return new LuaFunctionExpressionSyntax(
            Combine(functionToken, body),
            body);
    }

    private LuaTableConstructorExpressionSyntax ParseTableConstructor()
    {
        var leftBrace = Expect(LuaTokenKind.LeftBrace);
        var fields = new List<LuaTableFieldSyntax>();

        while (Current.Kind != LuaTokenKind.RightBrace)
        {
            fields.Add(ParseTableField());

            if (!Match(LuaTokenKind.Comma) && !Match(LuaTokenKind.Semicolon))
            {
                break;
            }

            if (Current.Kind == LuaTokenKind.RightBrace)
            {
                break;
            }
        }

        var rightBrace = Expect(LuaTokenKind.RightBrace);
        return new LuaTableConstructorExpressionSyntax(
            Combine(leftBrace, rightBrace),
            fields);
    }

    private LuaTableFieldSyntax ParseTableField()
    {
        if (Match(LuaTokenKind.LeftBracket))
        {
            var leftBracket = _tokens[_position - 1];
            var key = ParseExpression();
            Expect(LuaTokenKind.RightBracket);
            Expect(LuaTokenKind.Assign);
            var value = ParseExpression();
            return new LuaKeyTableFieldSyntax(
                new LuaSourceRange(leftBracket.Range.Start, value.Range.End),
                key,
                value);
        }

        if (Current.Kind == LuaTokenKind.Identifier && Peek(1).Kind == LuaTokenKind.Assign)
        {
            var name = ParseName();
            Expect(LuaTokenKind.Assign);
            var value = ParseExpression();
            return new LuaNameTableFieldSyntax(
                new LuaSourceRange(name.Range.Start, value.Range.End),
                name,
                value);
        }

        var expression = ParseExpression();
        return new LuaExpressionTableFieldSyntax(expression.Range, expression);
    }

    private LuaExpressionSyntax ParsePrefixExpression()
    {
        LuaExpressionSyntax expression = Current.Kind switch
        {
            LuaTokenKind.Identifier => ParseNameExpression(),
            LuaTokenKind.LeftParen => ParseParenthesizedExpression(),
            _ => throw CreateSyntaxException("expected expression")
        };

        while (true)
        {
            switch (Current.Kind)
            {
                case LuaTokenKind.LeftBracket:
                {
                    Advance();
                    var index = ParseExpression();
                    var rightBracket = Expect(LuaTokenKind.RightBracket);
                    expression = new LuaIndexExpressionSyntax(
                        new LuaSourceRange(expression.Range.Start, rightBracket.Range.End),
                        expression,
                        index);
                    break;
                }
                case LuaTokenKind.Dot:
                {
                    Advance();
                    var name = ParseName();
                    expression = new LuaMemberAccessExpressionSyntax(
                        new LuaSourceRange(expression.Range.Start, name.Range.End),
                        expression,
                        name);
                    break;
                }
                case LuaTokenKind.Colon:
                {
                    Advance();
                    var methodName = ParseName();
                    var arguments = ParseCallArguments();
                    expression = new LuaFunctionCallExpressionSyntax(
                        new LuaSourceRange(expression.Range.Start, arguments.Range.End),
                        expression,
                        methodName,
                        arguments);
                    break;
                }
                default:
                    if (!IsCallArgumentStart(Current.Kind))
                    {
                        return expression;
                    }

                    var callArguments = ParseCallArguments();
                    expression = new LuaFunctionCallExpressionSyntax(
                        new LuaSourceRange(expression.Range.Start, callArguments.Range.End),
                        expression,
                        null,
                        callArguments);
                    break;
            }
        }
    }

    private LuaNameExpressionSyntax ParseNameExpression()
    {
        var name = ParseName();
        return new LuaNameExpressionSyntax(name.Range, name);
    }

    private LuaParenthesizedExpressionSyntax ParseParenthesizedExpression()
    {
        var leftParen = Expect(LuaTokenKind.LeftParen);
        var expression = ParseExpression();
        var rightParen = Expect(LuaTokenKind.RightParen);
        return new LuaParenthesizedExpressionSyntax(
            Combine(leftParen, rightParen),
            expression);
    }

    private LuaCallArgumentsSyntax ParseCallArguments()
    {
        return Current.Kind switch
        {
            LuaTokenKind.LeftParen => ParseParenthesizedCallArguments(),
            LuaTokenKind.LeftBrace => ParseSingleExpressionCallArguments(LuaCallArgumentStyle.TableConstructor, ParseTableConstructor()),
            LuaTokenKind.String => ParseSingleExpressionCallArguments(LuaCallArgumentStyle.LiteralString, ParseStringLiteral()),
            _ => throw CreateSyntaxException("expected function arguments")
        };
    }

    private LuaCallArgumentsSyntax ParseParenthesizedCallArguments()
    {
        var leftParen = Expect(LuaTokenKind.LeftParen);
        IReadOnlyList<LuaExpressionSyntax> arguments = Current.Kind == LuaTokenKind.RightParen
            ? []
            : ParseExpressionList();
        var rightParen = Expect(LuaTokenKind.RightParen);

        return new LuaCallArgumentsSyntax(
            Combine(leftParen, rightParen),
            LuaCallArgumentStyle.Parenthesized,
            arguments);
    }

    private static LuaCallArgumentsSyntax ParseSingleExpressionCallArguments(LuaCallArgumentStyle style, LuaExpressionSyntax expression)
    {
        return new LuaCallArgumentsSyntax(expression.Range, style, [expression]);
    }

    private LuaNameSyntax ParseName()
    {
        var token = Expect(LuaTokenKind.Identifier);
        return new LuaNameSyntax(token.Range, token.Lexeme);
    }

    private LuaVariableExpressionSyntax RequireVariable(LuaExpressionSyntax expression)
    {
        if (expression is LuaVariableExpressionSyntax variable)
        {
            return variable;
        }

        throw CreateSyntaxException("expected variable");
    }

    private static bool IsCallArgumentStart(LuaTokenKind kind)
    {
        return kind is LuaTokenKind.LeftParen or LuaTokenKind.LeftBrace or LuaTokenKind.String;
    }

    private static bool TryGetUnaryOperator(LuaTokenKind kind, out LuaUnaryOperatorKind op)
    {
        switch (kind)
        {
            case LuaTokenKind.Minus:
                op = LuaUnaryOperatorKind.Negate;
                return true;
            case LuaTokenKind.Not:
                op = LuaUnaryOperatorKind.Not;
                return true;
            case LuaTokenKind.Hash:
                op = LuaUnaryOperatorKind.Length;
                return true;
            case LuaTokenKind.Tilde:
                op = LuaUnaryOperatorKind.BitwiseNot;
                return true;
            default:
                op = default;
                return false;
        }
    }

    private static bool TryGetBinaryOperator(LuaTokenKind kind, out BinaryOperatorInfo info)
    {
        switch (kind)
        {
            case LuaTokenKind.Plus:
                info = new BinaryOperatorInfo(LuaBinaryOperatorKind.Add, 10, 10);
                return true;
            case LuaTokenKind.Minus:
                info = new BinaryOperatorInfo(LuaBinaryOperatorKind.Subtract, 10, 10);
                return true;
            case LuaTokenKind.Star:
                info = new BinaryOperatorInfo(LuaBinaryOperatorKind.Multiply, 11, 11);
                return true;
            case LuaTokenKind.Percent:
                info = new BinaryOperatorInfo(LuaBinaryOperatorKind.Modulo, 11, 11);
                return true;
            case LuaTokenKind.Caret:
                info = new BinaryOperatorInfo(LuaBinaryOperatorKind.Power, 14, 13);
                return true;
            case LuaTokenKind.Slash:
                info = new BinaryOperatorInfo(LuaBinaryOperatorKind.Divide, 11, 11);
                return true;
            case LuaTokenKind.IntegerDivision:
                info = new BinaryOperatorInfo(LuaBinaryOperatorKind.IntegerDivide, 11, 11);
                return true;
            case LuaTokenKind.Ampersand:
                info = new BinaryOperatorInfo(LuaBinaryOperatorKind.BitwiseAnd, 6, 6);
                return true;
            case LuaTokenKind.Pipe:
                info = new BinaryOperatorInfo(LuaBinaryOperatorKind.BitwiseOr, 4, 4);
                return true;
            case LuaTokenKind.Tilde:
                info = new BinaryOperatorInfo(LuaBinaryOperatorKind.BitwiseXor, 5, 5);
                return true;
            case LuaTokenKind.LeftShift:
                info = new BinaryOperatorInfo(LuaBinaryOperatorKind.LeftShift, 7, 7);
                return true;
            case LuaTokenKind.RightShift:
                info = new BinaryOperatorInfo(LuaBinaryOperatorKind.RightShift, 7, 7);
                return true;
            case LuaTokenKind.Concat:
                info = new BinaryOperatorInfo(LuaBinaryOperatorKind.Concat, 9, 8);
                return true;
            case LuaTokenKind.Equal:
                info = new BinaryOperatorInfo(LuaBinaryOperatorKind.Equal, 3, 3);
                return true;
            case LuaTokenKind.NotEqual:
                info = new BinaryOperatorInfo(LuaBinaryOperatorKind.NotEqual, 3, 3);
                return true;
            case LuaTokenKind.LessThan:
                info = new BinaryOperatorInfo(LuaBinaryOperatorKind.LessThan, 3, 3);
                return true;
            case LuaTokenKind.LessEqual:
                info = new BinaryOperatorInfo(LuaBinaryOperatorKind.LessEqual, 3, 3);
                return true;
            case LuaTokenKind.GreaterThan:
                info = new BinaryOperatorInfo(LuaBinaryOperatorKind.GreaterThan, 3, 3);
                return true;
            case LuaTokenKind.GreaterEqual:
                info = new BinaryOperatorInfo(LuaBinaryOperatorKind.GreaterEqual, 3, 3);
                return true;
            case LuaTokenKind.And:
                info = new BinaryOperatorInfo(LuaBinaryOperatorKind.And, 2, 2);
                return true;
            case LuaTokenKind.Or:
                info = new BinaryOperatorInfo(LuaBinaryOperatorKind.Or, 1, 1);
                return true;
            default:
                info = default;
                return false;
        }
    }

    private static string FormatToken(LuaTokenKind kind)
    {
        return kind switch
        {
            LuaTokenKind.Identifier => "identifier",
            LuaTokenKind.Number => "number",
            LuaTokenKind.String => "string",
            LuaTokenKind.EndOfFile => "end of file",
            LuaTokenKind.Plus => "'+'",
            LuaTokenKind.Minus => "'-'",
            LuaTokenKind.Star => "'*'",
            LuaTokenKind.Slash => "'/'",
            LuaTokenKind.IntegerDivision => "'//'",
            LuaTokenKind.Percent => "'%'",
            LuaTokenKind.Caret => "'^'",
            LuaTokenKind.Hash => "'#'",
            LuaTokenKind.Ampersand => "'&'",
            LuaTokenKind.Tilde => "'~'",
            LuaTokenKind.Pipe => "'|'",
            LuaTokenKind.LessThan => "'<'",
            LuaTokenKind.LessEqual => "'<='",
            LuaTokenKind.LeftShift => "'<<'",
            LuaTokenKind.GreaterThan => "'>'",
            LuaTokenKind.GreaterEqual => "'>='",
            LuaTokenKind.RightShift => "'>>'",
            LuaTokenKind.Assign => "'='",
            LuaTokenKind.Equal => "'=='",
            LuaTokenKind.NotEqual => "'~='",
            LuaTokenKind.LeftParen => "'('",
            LuaTokenKind.RightParen => "')'",
            LuaTokenKind.LeftBrace => "'{'",
            LuaTokenKind.RightBrace => "'}'",
            LuaTokenKind.LeftBracket => "'['",
            LuaTokenKind.RightBracket => "']'",
            LuaTokenKind.Colon => "':'",
            LuaTokenKind.DoubleColon => "'::'",
            LuaTokenKind.Semicolon => "';'",
            LuaTokenKind.Comma => "','",
            LuaTokenKind.Dot => "'.'",
            LuaTokenKind.Concat => "'..'",
            LuaTokenKind.Vararg => "'...'",
            _ => $"'{CurrentLexeme(kind)}'"
        };

        static string CurrentLexeme(LuaTokenKind tokenKind)
        {
            return tokenKind switch
            {
                LuaTokenKind.And => "and",
                LuaTokenKind.Break => "break",
                LuaTokenKind.Do => "do",
                LuaTokenKind.Else => "else",
                LuaTokenKind.ElseIf => "elseif",
                LuaTokenKind.End => "end",
                LuaTokenKind.False => "false",
                LuaTokenKind.For => "for",
                LuaTokenKind.Function => "function",
                LuaTokenKind.Global => "global",
                LuaTokenKind.Goto => "goto",
                LuaTokenKind.If => "if",
                LuaTokenKind.In => "in",
                LuaTokenKind.Local => "local",
                LuaTokenKind.Nil => "nil",
                LuaTokenKind.Not => "not",
                LuaTokenKind.Or => "or",
                LuaTokenKind.Repeat => "repeat",
                LuaTokenKind.Return => "return",
                LuaTokenKind.Then => "then",
                LuaTokenKind.True => "true",
                LuaTokenKind.Until => "until",
                LuaTokenKind.While => "while",
                _ => tokenKind.ToString()
            };
        }
    }

    private readonly record struct BinaryOperatorInfo(
        LuaBinaryOperatorKind Operator,
        int LeftPriority,
        int RightPriority);
}
