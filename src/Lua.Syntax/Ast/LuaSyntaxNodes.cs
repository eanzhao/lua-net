using Lua.Syntax.Lexing;

namespace Lua.Syntax.Ast;

public abstract record class LuaSyntaxNode(LuaSourceRange Range);

public abstract record class LuaStatementSyntax(LuaSourceRange Range) : LuaSyntaxNode(Range);

public abstract record class LuaExpressionSyntax(LuaSourceRange Range) : LuaSyntaxNode(Range);

public abstract record class LuaVariableExpressionSyntax(LuaSourceRange Range) : LuaExpressionSyntax(Range);

public abstract record class LuaTableFieldSyntax(LuaSourceRange Range) : LuaSyntaxNode(Range);

public enum LuaVariableAttributeKind
{
    Const,
    Close
}

public enum LuaUnaryOperatorKind
{
    Negate,
    Not,
    Length,
    BitwiseNot
}

public enum LuaBinaryOperatorKind
{
    Add,
    Subtract,
    Multiply,
    Divide,
    IntegerDivide,
    Modulo,
    Power,
    BitwiseAnd,
    BitwiseOr,
    BitwiseXor,
    LeftShift,
    RightShift,
    Concat,
    LessThan,
    LessEqual,
    GreaterThan,
    GreaterEqual,
    Equal,
    NotEqual,
    And,
    Or
}

public enum LuaCallArgumentStyle
{
    Parenthesized,
    TableConstructor,
    LiteralString
}

public sealed record class LuaChunkSyntax(LuaSourceRange Range, LuaBlockSyntax Block) : LuaSyntaxNode(Range);

public sealed record class LuaBlockSyntax(LuaSourceRange Range, IReadOnlyList<LuaStatementSyntax> Statements) : LuaSyntaxNode(Range);

public sealed record class LuaNameSyntax(LuaSourceRange Range, string Identifier) : LuaSyntaxNode(Range);

public sealed record class LuaVariableAttributeSyntax(LuaSourceRange Range, LuaVariableAttributeKind Kind) : LuaSyntaxNode(Range);

public sealed record class LuaDeclaredNameSyntax(
    LuaSourceRange Range,
    LuaNameSyntax Name,
    LuaVariableAttributeSyntax? Attribute) : LuaSyntaxNode(Range);

public sealed record class LuaDeclarationNameListSyntax(
    LuaSourceRange Range,
    LuaVariableAttributeSyntax? LeadingAttribute,
    IReadOnlyList<LuaDeclaredNameSyntax> Names) : LuaSyntaxNode(Range);

public sealed record class LuaVarargParameterSyntax(
    LuaSourceRange Range,
    LuaNameSyntax? Name) : LuaSyntaxNode(Range);

public sealed record class LuaFunctionBodySyntax(
    LuaSourceRange Range,
    IReadOnlyList<LuaNameSyntax> Parameters,
    LuaVarargParameterSyntax? VarargParameter,
    LuaBlockSyntax Block) : LuaSyntaxNode(Range);

public sealed record class LuaFunctionNameSyntax(
    LuaSourceRange Range,
    IReadOnlyList<LuaNameSyntax> Segments,
    LuaNameSyntax? MethodName) : LuaSyntaxNode(Range);

public sealed record class LuaConditionalClauseSyntax(
    LuaSourceRange Range,
    LuaExpressionSyntax Condition,
    LuaBlockSyntax Block) : LuaSyntaxNode(Range);

public sealed record class LuaElseClauseSyntax(
    LuaSourceRange Range,
    LuaBlockSyntax Block) : LuaSyntaxNode(Range);

public sealed record class LuaCallArgumentsSyntax(
    LuaSourceRange Range,
    LuaCallArgumentStyle Style,
    IReadOnlyList<LuaExpressionSyntax> Arguments) : LuaSyntaxNode(Range);

public sealed record class LuaEmptyStatementSyntax(LuaSourceRange Range) : LuaStatementSyntax(Range);

public sealed record class LuaAssignmentStatementSyntax(
    LuaSourceRange Range,
    IReadOnlyList<LuaVariableExpressionSyntax> Variables,
    IReadOnlyList<LuaExpressionSyntax> Values) : LuaStatementSyntax(Range);

public sealed record class LuaFunctionCallStatementSyntax(
    LuaSourceRange Range,
    LuaFunctionCallExpressionSyntax Call) : LuaStatementSyntax(Range);

public sealed record class LuaLabelStatementSyntax(
    LuaSourceRange Range,
    LuaNameSyntax Name) : LuaStatementSyntax(Range);

public sealed record class LuaBreakStatementSyntax(LuaSourceRange Range) : LuaStatementSyntax(Range);

public sealed record class LuaGotoStatementSyntax(
    LuaSourceRange Range,
    LuaNameSyntax Name) : LuaStatementSyntax(Range);

public sealed record class LuaDoStatementSyntax(
    LuaSourceRange Range,
    LuaBlockSyntax Block) : LuaStatementSyntax(Range);

public sealed record class LuaWhileStatementSyntax(
    LuaSourceRange Range,
    LuaExpressionSyntax Condition,
    LuaBlockSyntax Block) : LuaStatementSyntax(Range);

public sealed record class LuaRepeatStatementSyntax(
    LuaSourceRange Range,
    LuaBlockSyntax Block,
    LuaExpressionSyntax Condition) : LuaStatementSyntax(Range);

public sealed record class LuaIfStatementSyntax(
    LuaSourceRange Range,
    LuaConditionalClauseSyntax IfClause,
    IReadOnlyList<LuaConditionalClauseSyntax> ElseIfClauses,
    LuaElseClauseSyntax? ElseClause) : LuaStatementSyntax(Range);

public sealed record class LuaNumericForStatementSyntax(
    LuaSourceRange Range,
    LuaNameSyntax Name,
    LuaExpressionSyntax InitialValue,
    LuaExpressionSyntax Limit,
    LuaExpressionSyntax? Step,
    LuaBlockSyntax Block) : LuaStatementSyntax(Range);

public sealed record class LuaGenericForStatementSyntax(
    LuaSourceRange Range,
    IReadOnlyList<LuaNameSyntax> Names,
    IReadOnlyList<LuaExpressionSyntax> Expressions,
    LuaBlockSyntax Block) : LuaStatementSyntax(Range);

public sealed record class LuaFunctionDeclarationStatementSyntax(
    LuaSourceRange Range,
    LuaFunctionNameSyntax Name,
    LuaFunctionBodySyntax Body) : LuaStatementSyntax(Range);

public sealed record class LuaLocalFunctionStatementSyntax(
    LuaSourceRange Range,
    LuaNameSyntax Name,
    LuaFunctionBodySyntax Body) : LuaStatementSyntax(Range);

public sealed record class LuaGlobalFunctionStatementSyntax(
    LuaSourceRange Range,
    LuaNameSyntax Name,
    LuaFunctionBodySyntax Body) : LuaStatementSyntax(Range);

public sealed record class LuaLocalDeclarationStatementSyntax(
    LuaSourceRange Range,
    LuaDeclarationNameListSyntax Names,
    IReadOnlyList<LuaExpressionSyntax> Initializers) : LuaStatementSyntax(Range);

public sealed record class LuaGlobalDeclarationStatementSyntax(
    LuaSourceRange Range,
    LuaDeclarationNameListSyntax Names,
    IReadOnlyList<LuaExpressionSyntax> Initializers) : LuaStatementSyntax(Range);

public sealed record class LuaGlobalWildcardStatementSyntax(
    LuaSourceRange Range,
    LuaVariableAttributeSyntax? Attribute) : LuaStatementSyntax(Range);

public sealed record class LuaReturnStatementSyntax(
    LuaSourceRange Range,
    IReadOnlyList<LuaExpressionSyntax> Expressions) : LuaStatementSyntax(Range);

public sealed record class LuaNilLiteralExpressionSyntax(LuaSourceRange Range) : LuaExpressionSyntax(Range);

public sealed record class LuaBooleanLiteralExpressionSyntax(
    LuaSourceRange Range,
    bool Value) : LuaExpressionSyntax(Range);

public sealed record class LuaNumberLiteralExpressionSyntax(
    LuaSourceRange Range,
    string Text) : LuaExpressionSyntax(Range);

public sealed record class LuaStringLiteralExpressionSyntax(
    LuaSourceRange Range,
    string Text,
    string Value) : LuaExpressionSyntax(Range);

public sealed record class LuaVarargExpressionSyntax(LuaSourceRange Range) : LuaExpressionSyntax(Range);

public sealed record class LuaNameExpressionSyntax(
    LuaSourceRange Range,
    LuaNameSyntax Name) : LuaVariableExpressionSyntax(Range);

public sealed record class LuaParenthesizedExpressionSyntax(
    LuaSourceRange Range,
    LuaExpressionSyntax Expression) : LuaExpressionSyntax(Range);

public sealed record class LuaUnaryExpressionSyntax(
    LuaSourceRange Range,
    LuaUnaryOperatorKind Operator,
    LuaExpressionSyntax Operand) : LuaExpressionSyntax(Range);

public sealed record class LuaBinaryExpressionSyntax(
    LuaSourceRange Range,
    LuaExpressionSyntax Left,
    LuaBinaryOperatorKind Operator,
    LuaExpressionSyntax Right) : LuaExpressionSyntax(Range);

public sealed record class LuaFunctionExpressionSyntax(
    LuaSourceRange Range,
    LuaFunctionBodySyntax Body) : LuaExpressionSyntax(Range);

public sealed record class LuaTableConstructorExpressionSyntax(
    LuaSourceRange Range,
    IReadOnlyList<LuaTableFieldSyntax> Fields) : LuaExpressionSyntax(Range);

public sealed record class LuaExpressionTableFieldSyntax(
    LuaSourceRange Range,
    LuaExpressionSyntax Expression) : LuaTableFieldSyntax(Range);

public sealed record class LuaNameTableFieldSyntax(
    LuaSourceRange Range,
    LuaNameSyntax Name,
    LuaExpressionSyntax Value) : LuaTableFieldSyntax(Range);

public sealed record class LuaKeyTableFieldSyntax(
    LuaSourceRange Range,
    LuaExpressionSyntax Key,
    LuaExpressionSyntax Value) : LuaTableFieldSyntax(Range);

public sealed record class LuaIndexExpressionSyntax(
    LuaSourceRange Range,
    LuaExpressionSyntax Prefix,
    LuaExpressionSyntax Index) : LuaVariableExpressionSyntax(Range);

public sealed record class LuaMemberAccessExpressionSyntax(
    LuaSourceRange Range,
    LuaExpressionSyntax Prefix,
    LuaNameSyntax Name) : LuaVariableExpressionSyntax(Range);

public sealed record class LuaFunctionCallExpressionSyntax(
    LuaSourceRange Range,
    LuaExpressionSyntax Prefix,
    LuaNameSyntax? MethodName,
    LuaCallArgumentsSyntax Arguments) : LuaExpressionSyntax(Range);
