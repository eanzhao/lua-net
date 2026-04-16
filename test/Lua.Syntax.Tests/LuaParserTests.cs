using Lua.Syntax.Ast;
using Lua.Syntax.Lexing;
using Lua.Syntax.Parsing;
using Shouldly;

namespace Lua.Syntax.Tests;

public class LuaParserTests
{
    [Fact]
    public void ParseChunk_ShouldRespectRightAssociativityAndUnaryPrecedence()
    {
        var chunk = LuaParser.Parse("return a .. b .. c, -a^b");

        var returnStatement = chunk.Block.Statements.Single().ShouldBeOfType<LuaReturnStatementSyntax>();
        returnStatement.Expressions.Count.ShouldBe(2);

        var concat = returnStatement.Expressions[0].ShouldBeOfType<LuaBinaryExpressionSyntax>();
        concat.Operator.ShouldBe(LuaBinaryOperatorKind.Concat);
        concat.Left.ShouldBeOfType<LuaNameExpressionSyntax>().Name.Identifier.ShouldBe("a");

        var nestedConcat = concat.Right.ShouldBeOfType<LuaBinaryExpressionSyntax>();
        nestedConcat.Operator.ShouldBe(LuaBinaryOperatorKind.Concat);
        nestedConcat.Left.ShouldBeOfType<LuaNameExpressionSyntax>().Name.Identifier.ShouldBe("b");
        nestedConcat.Right.ShouldBeOfType<LuaNameExpressionSyntax>().Name.Identifier.ShouldBe("c");

        var unary = returnStatement.Expressions[1].ShouldBeOfType<LuaUnaryExpressionSyntax>();
        unary.Operator.ShouldBe(LuaUnaryOperatorKind.Negate);

        var power = unary.Operand.ShouldBeOfType<LuaBinaryExpressionSyntax>();
        power.Operator.ShouldBe(LuaBinaryOperatorKind.Power);
        power.Left.ShouldBeOfType<LuaNameExpressionSyntax>().Name.Identifier.ShouldBe("a");
        power.Right.ShouldBeOfType<LuaNameExpressionSyntax>().Name.Identifier.ShouldBe("b");
    }

    [Fact]
    public void ParseChunk_ShouldParseFunctionDeclarationMethodAndNamedVararg()
    {
        const string source = """
function t.a:b(c, ... rest)
    return obj:call(c, "x"), f{1, 2}
end
""";

        var chunk = LuaParser.Parse(source);

        var functionStatement = chunk.Block.Statements.Single().ShouldBeOfType<LuaFunctionDeclarationStatementSyntax>();
        functionStatement.Name.Segments.Select(segment => segment.Identifier).ShouldBe(["t", "a"]);
        functionStatement.Name.MethodName.ShouldNotBeNull();
        functionStatement.Name.MethodName.Identifier.ShouldBe("b");

        functionStatement.Body.Parameters.Select(parameter => parameter.Identifier).ShouldBe(["c"]);
        functionStatement.Body.VarargParameter.ShouldNotBeNull();
        functionStatement.Body.VarargParameter.Name.ShouldNotBeNull();
        functionStatement.Body.VarargParameter.Name.Identifier.ShouldBe("rest");

        var returnStatement = functionStatement.Body.Block.Statements.Single().ShouldBeOfType<LuaReturnStatementSyntax>();
        var methodCall = returnStatement.Expressions[0].ShouldBeOfType<LuaFunctionCallExpressionSyntax>();
        methodCall.MethodName.ShouldNotBeNull();
        methodCall.MethodName.Identifier.ShouldBe("call");
        methodCall.Arguments.Style.ShouldBe(LuaCallArgumentStyle.Parenthesized);

        var tableCall = returnStatement.Expressions[1].ShouldBeOfType<LuaFunctionCallExpressionSyntax>();
        tableCall.Arguments.Style.ShouldBe(LuaCallArgumentStyle.TableConstructor);
    }

    [Fact]
    public void ParseChunk_ShouldParseDeclarationsAndGlobalWildcard()
    {
        const string source = """
global<const> *
global answer, total = 1, 2
local <close> file<const>, mode = open(), "r"
""";

        var chunk = LuaParser.Parse(source);

        var wildcard = chunk.Block.Statements[0].ShouldBeOfType<LuaGlobalWildcardStatementSyntax>();
        wildcard.Attribute.ShouldNotBeNull();
        wildcard.Attribute.Kind.ShouldBe(LuaVariableAttributeKind.Const);

        var globalDeclaration = chunk.Block.Statements[1].ShouldBeOfType<LuaGlobalDeclarationStatementSyntax>();
        globalDeclaration.Names.Names.Select(name => name.Name.Identifier).ShouldBe(["answer", "total"]);
        globalDeclaration.Initializers.Count.ShouldBe(2);

        var localDeclaration = chunk.Block.Statements[2].ShouldBeOfType<LuaLocalDeclarationStatementSyntax>();
        localDeclaration.Names.LeadingAttribute.ShouldNotBeNull();
        localDeclaration.Names.LeadingAttribute!.Kind.ShouldBe(LuaVariableAttributeKind.Close);
        localDeclaration.Names.Names[0].Attribute.ShouldNotBeNull();
        localDeclaration.Names.Names[0].Attribute!.Kind.ShouldBe(LuaVariableAttributeKind.Const);
        localDeclaration.Names.Names[1].Name.Identifier.ShouldBe("mode");
    }

    [Fact]
    public void ParseChunk_ShouldParseControlFlowStatements()
    {
        const string source = """
if ready then
    while x do break end
elseif retry then
    repeat x = x - 1 until x < 0
else
    for i = 1, 10, 2 do end
    for k, v in pairs(t) do print(k, v) end
end
""";

        var chunk = LuaParser.Parse(source);

        var ifStatement = chunk.Block.Statements.Single().ShouldBeOfType<LuaIfStatementSyntax>();
        ifStatement.ElseIfClauses.Count.ShouldBe(1);
        ifStatement.ElseClause.ShouldNotBeNull();

        ifStatement.IfClause.Block.Statements.Single().ShouldBeOfType<LuaWhileStatementSyntax>();
        ifStatement.ElseIfClauses[0].Block.Statements.Single().ShouldBeOfType<LuaRepeatStatementSyntax>();

        var elseStatements = ifStatement.ElseClause!.Block.Statements;
        elseStatements[0].ShouldBeOfType<LuaNumericForStatementSyntax>();
        elseStatements[1].ShouldBeOfType<LuaGenericForStatementSyntax>();
    }

    [Fact]
    public void ParseChunk_ShouldParseAssignmentAndTableConstructorFields()
    {
        var chunk = LuaParser.Parse("a.b[c], x = { [1] = 2, name = 'ok', y }, foo()");

        var assignment = chunk.Block.Statements.Single().ShouldBeOfType<LuaAssignmentStatementSyntax>();
        assignment.Variables.Count.ShouldBe(2);
        assignment.Variables[0].ShouldBeOfType<LuaIndexExpressionSyntax>();
        assignment.Variables[1].ShouldBeOfType<LuaNameExpressionSyntax>().Name.Identifier.ShouldBe("x");

        var table = assignment.Values[0].ShouldBeOfType<LuaTableConstructorExpressionSyntax>();
        table.Fields.Count.ShouldBe(3);
        table.Fields[0].ShouldBeOfType<LuaKeyTableFieldSyntax>();
        table.Fields[1].ShouldBeOfType<LuaNameTableFieldSyntax>().Name.Identifier.ShouldBe("name");
        table.Fields[2].ShouldBeOfType<LuaExpressionTableFieldSyntax>();

        assignment.Values[1].ShouldBeOfType<LuaFunctionCallExpressionSyntax>();
    }

    [Fact]
    public void ParseChunk_BreakOutsideLoop_ShouldThrow()
    {
        var exception = Should.Throw<LuaSyntaxException>(() => LuaParser.Parse("break"));

        exception.Message.ShouldContain("break outside loop");
    }

    [Fact]
    public void ParseChunk_VarargOutsideVarargFunction_ShouldThrow()
    {
        var exception = Should.Throw<LuaSyntaxException>(() => LuaParser.Parse("function f() return ... end"));

        exception.Message.ShouldContain("cannot use '...' outside a vararg function");
    }

    [Fact]
    public void ParseChunk_GlobalCloseAttribute_ShouldThrow()
    {
        var exception = Should.Throw<LuaSyntaxException>(() => LuaParser.Parse("global<close> value"));

        exception.Message.ShouldContain("global variables cannot be to-be-closed");
    }

    [Fact]
    public void ParseChunk_MultipleToBeClosedVariables_ShouldThrow()
    {
        var exception = Should.Throw<LuaSyntaxException>(() => LuaParser.Parse("local <close> a, b = open(), open()"));

        exception.Message.ShouldContain("multiple to-be-closed variables in declaration");
    }

    [Fact]
    public void ParseChunk_MissingEnd_ShouldReportSourceName()
    {
        var exception = Should.Throw<LuaSyntaxException>(() => LuaParser.Parse("if true then\n    return 1", "sample.lua"));

        exception.Message.ShouldContain("sample.lua:");
        exception.Message.ShouldContain("expected 'end'");
    }

    [Fact]
    public void ParseChunk_ShouldParseGotoAndLabels()
    {
        const string source = """
            goto done
            local x = 1
            ::done::
            """;

        var chunk = LuaParser.Parse(source);

        chunk.Block.Statements[0].ShouldBeOfType<LuaGotoStatementSyntax>()
            .Name.Identifier.ShouldBe("done");
        chunk.Block.Statements[2].ShouldBeOfType<LuaLabelStatementSyntax>()
            .Name.Identifier.ShouldBe("done");
    }

    [Fact]
    public void ParseChunk_ShouldParseDoBlockAndSemicolons()
    {
        var chunk = LuaParser.Parse("do end ; ;");

        chunk.Block.Statements.Count.ShouldBe(3);
        chunk.Block.Statements[0].ShouldBeOfType<LuaDoStatementSyntax>();
        chunk.Block.Statements[1].ShouldBeOfType<LuaEmptyStatementSyntax>();
        chunk.Block.Statements[2].ShouldBeOfType<LuaEmptyStatementSyntax>();
    }

    [Fact]
    public void ParseChunk_ShouldParseNestedFunctionsAndClosures()
    {
        const string source = """
            local function outer(x)
                local function inner(y)
                    return x + y
                end
                return inner
            end
            """;

        var chunk = LuaParser.Parse(source);

        var outer = chunk.Block.Statements.Single().ShouldBeOfType<LuaLocalFunctionStatementSyntax>();
        outer.Name.Identifier.ShouldBe("outer");
        outer.Body.Parameters.Count.ShouldBe(1);

        var innerStatement = outer.Body.Block.Statements[0].ShouldBeOfType<LuaLocalFunctionStatementSyntax>();
        innerStatement.Name.Identifier.ShouldBe("inner");
        innerStatement.Body.Parameters.Count.ShouldBe(1);
    }

    [Fact]
    public void ParseChunk_ShouldParseGlobalFunctionDeclaration()
    {
        var chunk = LuaParser.Parse("global function greet(name) return name end");

        var func = chunk.Block.Statements.Single().ShouldBeOfType<LuaGlobalFunctionStatementSyntax>();
        func.Name.Identifier.ShouldBe("greet");
        func.Body.Parameters.Count.ShouldBe(1);
    }

    [Fact]
    public void ParseChunk_ShouldParseFunctionCallAsStatement()
    {
        var chunk = LuaParser.Parse("print(1, 2, 3)");

        var call = chunk.Block.Statements.Single().ShouldBeOfType<LuaFunctionCallStatementSyntax>();
        call.Call.Arguments.Arguments.Count.ShouldBe(3);
    }

    [Fact]
    public void ParseChunk_ShouldParseMethodCallChain()
    {
        var chunk = LuaParser.Parse("return a:foo(1):bar(2)");

        var ret = chunk.Block.Statements.Single().ShouldBeOfType<LuaReturnStatementSyntax>();
        var outerCall = ret.Expressions.Single().ShouldBeOfType<LuaFunctionCallExpressionSyntax>();
        outerCall.MethodName!.Identifier.ShouldBe("bar");

        var innerCall = outerCall.Prefix.ShouldBeOfType<LuaFunctionCallExpressionSyntax>();
        innerCall.MethodName!.Identifier.ShouldBe("foo");
    }

    [Fact]
    public void ParseChunk_ShouldParseComplexBinaryPrecedence()
    {
        var chunk = LuaParser.Parse("return 1 + 2 * 3, a and b or c");

        var ret = chunk.Block.Statements.Single().ShouldBeOfType<LuaReturnStatementSyntax>();

        var add = ret.Expressions[0].ShouldBeOfType<LuaBinaryExpressionSyntax>();
        add.Operator.ShouldBe(LuaBinaryOperatorKind.Add);
        add.Right.ShouldBeOfType<LuaBinaryExpressionSyntax>()
            .Operator.ShouldBe(LuaBinaryOperatorKind.Multiply);

        var orExpr = ret.Expressions[1].ShouldBeOfType<LuaBinaryExpressionSyntax>();
        orExpr.Operator.ShouldBe(LuaBinaryOperatorKind.Or);
        orExpr.Left.ShouldBeOfType<LuaBinaryExpressionSyntax>()
            .Operator.ShouldBe(LuaBinaryOperatorKind.And);
    }

    [Fact]
    public void ParseChunk_ShouldParseFunctionExpressionAsValue()
    {
        var chunk = LuaParser.Parse("local f = function(a, b) return a + b end");

        var local = chunk.Block.Statements.Single().ShouldBeOfType<LuaLocalDeclarationStatementSyntax>();
        var funcExpr = local.Initializers.Single().ShouldBeOfType<LuaFunctionExpressionSyntax>();
        funcExpr.Body.Parameters.Count.ShouldBe(2);
    }

    [Fact]
    public void ParseChunk_InvalidAssignmentTarget_ShouldThrow()
    {
        Should.Throw<LuaSyntaxException>(() => LuaParser.Parse("1 + 2 = 3"))
            .Message.ShouldContain("expected");
    }
}
