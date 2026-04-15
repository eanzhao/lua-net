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
}
