using System.Text;
using Lua.Bytecode.Chunks;
using Lua.Bytecode.Instructions;
using Lua.Runtime.Values;
using Lua.Syntax.Ast;
using Lua.Syntax.Lexing;
using Lua.Syntax.Parsing;

namespace Lua.Compiler;

public static class LuaCompiler
{
    public static LuaChunk Compile(string source, string? sourceName = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var actualSourceName = string.IsNullOrWhiteSpace(sourceName) ? "<input>" : sourceName;
        var syntax = LuaParser.Parse(source, actualSourceName);
        return new ChunkCompiler(actualSourceName).Compile(syntax);
    }

    public static LuaChunk Compile(ReadOnlyMemory<byte> sourceBytes, string? sourceName = null)
    {
        var text = Encoding.Latin1.GetString(sourceBytes.Span);
        return Compile(text, sourceName);
    }

    private sealed class ChunkCompiler(string sourceName)
    {
        private readonly string _sourceName = sourceName;

        public LuaChunk Compile(LuaChunkSyntax syntax)
        {
            var mainFunction = new FunctionCompiler(_sourceName, parent: null).CompileChunk(syntax);
            return new LuaChunk
            {
                Header = new LuaChunkHeader
                {
                    Version = LuaChunkHeaderConstants.LuacVersion,
                    Format = LuaChunkHeaderConstants.LuacFormat,
                    IntSize = (byte)sizeof(int),
                    IntFormatMarker = LuaChunkHeaderConstants.LuacInt,
                    InstructionSize = (byte)sizeof(uint),
                    InstructionFormatMarker = LuaChunkHeaderConstants.LuacInstruction,
                    LuaIntegerSize = (byte)sizeof(long),
                    LuaIntegerFormatMarker = LuaChunkHeaderConstants.LuacInt,
                    LuaNumberSize = (byte)sizeof(double),
                    LuaNumberFormatMarker = LuaChunkHeaderConstants.LuacNumber
                },
                MainUpvalueCount = checked((byte)mainFunction.Upvalues.Length),
                MainFunction = mainFunction
            };
        }
    }

    private sealed class FunctionCompiler(string sourceName, FunctionCompiler? parent)
    {
        private const string EnvironmentName = "_ENV";
        private readonly string _sourceName = sourceName;
        private readonly FunctionCompiler? _parent = parent;
        private readonly List<uint> _code = [];
        private readonly List<LuaConstant> _constants = [];
        private readonly Dictionary<string, int> _constantIndices = new(StringComparer.Ordinal);
        private readonly List<LuaPrototype> _nestedPrototypes = [];
        private readonly List<UpvalueInfo> _upvalues = [];
        private readonly Dictionary<string, int> _upvalueIndices = new(StringComparer.Ordinal);
        private readonly List<LocalInfo> _visibleLocals = [];
        private readonly Stack<int> _scopeStarts = [];
        private readonly Stack<LoopContext> _loops = [];
        private int _persistentRegisterCount;
        private int _tempRegisterTop;
        private int _maxRegisterCount;
        private LuaSourcePosition _fallbackPosition = new(0, 1, 1);

        public LuaPrototype CompileChunk(LuaChunkSyntax syntax)
        {
            _fallbackPosition = syntax.Range.Start;
            EnterScope();
            CompileBlockStatements(syntax.Block.Statements);
            ExitScope();

            if (!EndsWithReturn())
            {
                EmitReturn0();
            }

            return BuildPrototype(
                lineDefined: 0,
                lastLineDefined: 0,
                parameterCount: 0,
                syntax.Range);
        }

        public LuaPrototype CompileFunction(LuaFunctionBodySyntax body, bool injectSelf)
        {
            _fallbackPosition = body.Range.Start;
            if (body.VarargParameter is not null)
            {
                throw CreateUnsupported(body.VarargParameter, "vararg functions are not supported yet");
            }

            EnterScope();

            if (injectSelf)
            {
                AddLocal("self", body.Range.Start);
            }

            foreach (var parameter in body.Parameters)
            {
                AddLocal(parameter.Identifier, parameter.Range.Start);
            }

            CompileBlockStatements(body.Block.Statements);
            ExitScope();

            if (!EndsWithReturn())
            {
                EmitReturn0();
            }

            return BuildPrototype(
                lineDefined: body.Range.Start.Line,
                lastLineDefined: body.Range.End.Line,
                parameterCount: body.Parameters.Count + (injectSelf ? 1 : 0),
                body.Range);
        }

        private LuaPrototype BuildPrototype(int lineDefined, int lastLineDefined, int parameterCount, LuaSourceRange range)
        {
            if (parameterCount > byte.MaxValue)
            {
                throw CreateError(range, "too many parameters");
            }

            if (_maxRegisterCount > byte.MaxValue)
            {
                throw CreateError(range, "function requires more than 255 registers");
            }

            return new LuaPrototype
            {
                LineDefined = lineDefined,
                LastLineDefined = lastLineDefined,
                NumberOfParameters = checked((byte)parameterCount),
                Flags = 0,
                MaxStackSize = checked((byte)_maxRegisterCount),
                Code = _code.ToArray(),
                Constants = _constants.ToArray(),
                Upvalues = BuildUpvalues(),
                NestedPrototypes = _nestedPrototypes.ToArray(),
                Source = _sourceName,
                LineInfo = Enumerable.Repeat((sbyte)0, _code.Count).ToArray(),
                AbsoluteLineInfo = [],
                LocalVariables = []
            };
        }

        private LuaUpvalueDescriptor[] BuildUpvalues()
        {
            return _upvalues
                .Select(upvalue => new LuaUpvalueDescriptor
                {
                    InStack = upvalue.InStack,
                    Index = upvalue.Index,
                    Kind = 0,
                    Name = upvalue.Name
                })
                .ToArray();
        }

        private void CompileBlockStatements(IReadOnlyList<LuaStatementSyntax> statements)
        {
            foreach (var statement in statements)
            {
                ResetTemps();
                CompileStatement(statement);
            }
        }

        private void CompileStatement(LuaStatementSyntax statement)
        {
            switch (statement)
            {
                case LuaEmptyStatementSyntax:
                    return;
                case LuaAssignmentStatementSyntax assignment:
                    CompileAssignment(assignment);
                    return;
                case LuaFunctionCallStatementSyntax callStatement:
                    CompileFunctionCall(callStatement.Call, AllocateTemp(), fixedResultCount: 0, openResults: false);
                    return;
                case LuaDoStatementSyntax doStatement:
                    CompileScopedBlock(doStatement.Block);
                    return;
                case LuaIfStatementSyntax ifStatement:
                    CompileIf(ifStatement);
                    return;
                case LuaWhileStatementSyntax whileStatement:
                    CompileWhile(whileStatement);
                    return;
                case LuaRepeatStatementSyntax repeatStatement:
                    CompileRepeat(repeatStatement);
                    return;
                case LuaBreakStatementSyntax breakStatement:
                    CompileBreak(breakStatement);
                    return;
                case LuaLocalDeclarationStatementSyntax localDeclaration:
                    CompileLocalDeclaration(localDeclaration);
                    return;
                case LuaLocalFunctionStatementSyntax localFunction:
                    CompileLocalFunction(localFunction);
                    return;
                case LuaFunctionDeclarationStatementSyntax functionDeclaration:
                    CompileNamedFunction(functionDeclaration.Name, functionDeclaration.Body, functionDeclaration.Name.MethodName is not null);
                    return;
                case LuaReturnStatementSyntax returnStatement:
                    CompileReturn(returnStatement);
                    return;
                case LuaLabelStatementSyntax label:
                    throw CreateUnsupported(label, "goto and labels are not supported yet");
                case LuaGotoStatementSyntax @goto:
                    throw CreateUnsupported(@goto, "goto and labels are not supported yet");
                case LuaNumericForStatementSyntax numericFor:
                    throw CreateUnsupported(numericFor, "for loops are not supported yet");
                case LuaGenericForStatementSyntax genericFor:
                    throw CreateUnsupported(genericFor, "for loops are not supported yet");
                case LuaGlobalDeclarationStatementSyntax globalDeclaration:
                    throw CreateUnsupported(globalDeclaration, "global declarations are not supported yet");
                case LuaGlobalWildcardStatementSyntax globalWildcard:
                    throw CreateUnsupported(globalWildcard, "global declarations are not supported yet");
                case LuaGlobalFunctionStatementSyntax globalFunction:
                    throw CreateUnsupported(globalFunction, "global functions are not supported yet");
                default:
                    throw CreateUnsupported(statement, $"unsupported statement '{statement.GetType().Name}'");
            }
        }

        private void CompileScopedBlock(LuaBlockSyntax block)
        {
            EnterScope();
            CompileBlockStatements(block.Statements);
            ExitScope();
        }

        private void CompileIf(LuaIfStatementSyntax statement)
        {
            var endJumps = new List<int>();

            CompileConditionalClause(statement.IfClause, endJumps);
            foreach (var elseIf in statement.ElseIfClauses)
            {
                CompileConditionalClause(elseIf, endJumps);
            }

            if (statement.ElseClause is not null)
            {
                CompileScopedBlock(statement.ElseClause.Block);
            }

            PatchJumps(endJumps, CurrentProgramCounter);
        }

        private void CompileConditionalClause(LuaConditionalClauseSyntax clause, List<int> endJumps)
        {
            var conditionRegister = AllocateTemp();
            CompileExpressionInto(clause.Condition, conditionRegister);
            EmitTest(conditionRegister, expectedTruthy: false);
            var skipBlockJump = EmitJumpPlaceholder();

            CompileScopedBlock(clause.Block);
            endJumps.Add(EmitJumpPlaceholder());

            PatchJump(skipBlockJump, CurrentProgramCounter);
        }

        private void CompileWhile(LuaWhileStatementSyntax statement)
        {
            var loopStart = CurrentProgramCounter;
            var conditionRegister = AllocateTemp();
            CompileExpressionInto(statement.Condition, conditionRegister);
            EmitTest(conditionRegister, expectedTruthy: false);
            var exitJump = EmitJumpPlaceholder();

            _loops.Push(new LoopContext());
            CompileScopedBlock(statement.Block);
            var loop = _loops.Pop();

            EmitJump(loopStart);
            var loopEnd = CurrentProgramCounter;
            PatchJump(exitJump, loopEnd);
            PatchJumps(loop.BreakJumps, loopEnd);
        }

        private void CompileRepeat(LuaRepeatStatementSyntax statement)
        {
            var loopStart = CurrentProgramCounter;

            _loops.Push(new LoopContext());
            CompileScopedBlock(statement.Block);

            var conditionRegister = AllocateTemp();
            CompileExpressionInto(statement.Condition, conditionRegister);
            EmitTest(conditionRegister, expectedTruthy: false);
            var continueJump = EmitJumpPlaceholder();

            var loop = _loops.Pop();
            PatchJump(continueJump, loopStart);

            var loopEnd = CurrentProgramCounter;
            PatchJumps(loop.BreakJumps, loopEnd);
        }

        private void CompileBreak(LuaBreakStatementSyntax statement)
        {
            if (_loops.Count == 0)
            {
                throw CreateError(statement.Range, "break outside loop");
            }

            _loops.Peek().BreakJumps.Add(EmitJumpPlaceholder());
        }

        private void CompileLocalDeclaration(LuaLocalDeclarationStatementSyntax statement)
        {
            EnsureDeclarationAttributesSupported(statement.Names);

            if (statement.Initializers.Count == 0)
            {
                var startRegister = _persistentRegisterCount;
                foreach (var name in statement.Names.Names)
                {
                    AddLocal(name.Name.Identifier, name.Range.Start);
                }

                EmitLoadNilRange(startRegister, statement.Names.Names.Count);
                return;
            }

            var valueRegisters = EvaluateAssignmentValues(statement.Initializers, statement.Names.Names.Count, statement.Range);

            for (var index = 0; index < statement.Names.Names.Count; index++)
            {
                var localRegister = AddLocal(statement.Names.Names[index].Name.Identifier, statement.Names.Names[index].Range.Start);
                if (index < valueRegisters.Count)
                {
                    EmitMove(localRegister, valueRegisters[index]);
                }
                else
                {
                    EmitLoadNilRange(localRegister, 1);
                }
            }
        }

        private void CompileLocalFunction(LuaLocalFunctionStatementSyntax statement)
        {
            var register = AddLocal(statement.Name.Identifier, statement.Name.Range.Start);
            CompileNestedFunction(statement.Body, injectSelf: false, register);
        }

        private void CompileNamedFunction(LuaFunctionNameSyntax name, LuaFunctionBodySyntax body, bool injectSelf)
        {
            var target = CreateFunctionAssignmentTarget(name);
            var functionRegister = AllocateTemp();
            CompileNestedFunction(body, injectSelf, functionRegister);
            StoreAssignmentTarget(target, functionRegister);
        }

        private AssignmentTarget CreateFunctionAssignmentTarget(LuaFunctionNameSyntax name)
        {
            if (name.MethodName is null && name.Segments.Count == 1)
            {
                return CreateAssignmentTarget(new LuaNameExpressionSyntax(name.Segments[0].Range, name.Segments[0]));
            }

            var tableRegister = AllocateTemp();
            CompileFunctionNamePrefix(name, tableRegister);

            var finalName = name.MethodName ?? name.Segments[^1];
            var constantIndex = GetConstantByteIndex(LuaConstant.FromString(finalName.Identifier));
            return constantIndex >= 0
                ? AssignmentTarget.TableField(tableRegister, constantIndex)
                : AssignmentTarget.TableDynamic(tableRegister, LoadConstantIntoTemp(LuaConstant.FromString(finalName.Identifier)));
        }

        private void CompileFunctionNamePrefix(LuaFunctionNameSyntax name, int targetRegister)
        {
            CompileNameRead(name.Segments[0].Identifier, name.Segments[0].Range, targetRegister);

            var lastMemberIndex = name.MethodName is null ? name.Segments.Count - 1 : name.Segments.Count;
            for (var index = 1; index < lastMemberIndex; index++)
            {
                EmitTableReadByName(targetRegister, targetRegister, name.Segments[index].Identifier, name.Segments[index].Range);
            }
        }

        private void CompileAssignment(LuaAssignmentStatementSyntax statement)
        {
            var valueRegisters = EvaluateAssignmentValues(statement.Values, statement.Variables.Count, statement.Range);
            var targets = statement.Variables.Select(CreateAssignmentTarget).ToArray();

            int? nilRegister = null;
            for (var index = 0; index < targets.Length; index++)
            {
                var valueRegister = index < valueRegisters.Count
                    ? valueRegisters[index]
                    : nilRegister ??= LoadNilIntoTemp();

                StoreAssignmentTarget(targets[index], valueRegister);
            }
        }

        private List<int> EvaluateAssignmentValues(
            IReadOnlyList<LuaExpressionSyntax> values,
            int targetCount,
            LuaSourceRange range)
        {
            var registers = new List<int>(Math.Max(values.Count, targetCount));
            for (var index = 0; index < values.Count; index++)
            {
                var register = AllocateTemp();
                var remainingTargets = Math.Max(0, targetCount - index);
                var isLast = index == values.Count - 1;
                if (isLast && remainingTargets > 1 && IsMultiResultExpression(values[index]))
                {
                    CompileExpressionWithFixedResults(values[index], register, remainingTargets);
                    for (var offset = 0; offset < remainingTargets; offset++)
                    {
                        registers.Add(register + offset);
                    }

                    continue;
                }

                CompileExpressionInto(values[index], register);
                registers.Add(register);
            }

            if (values.Count == 0 && targetCount == 0)
            {
                throw CreateError(range, "assignment must contain at least one value or target");
            }

            return registers;
        }

        private AssignmentTarget CreateAssignmentTarget(LuaVariableExpressionSyntax variable)
        {
            return variable switch
            {
                LuaNameExpressionSyntax nameExpression => CreateNameAssignmentTarget(nameExpression),
                LuaMemberAccessExpressionSyntax memberAccess => CreateMemberAssignmentTarget(memberAccess),
                LuaIndexExpressionSyntax indexExpression => CreateIndexAssignmentTarget(indexExpression),
                _ => throw CreateUnsupported(variable, $"unsupported assignment target '{variable.GetType().Name}'")
            };
        }

        private AssignmentTarget CreateNameAssignmentTarget(LuaNameExpressionSyntax nameExpression)
        {
            if (TryResolveLexicalName(nameExpression.Name.Identifier, out var reference))
            {
                return reference.Kind == ReferenceKind.Local
                    ? AssignmentTarget.Local(reference.Index)
                    : AssignmentTarget.Upvalue(reference.Index);
            }

            return CreateGlobalAssignmentTarget(nameExpression.Name.Identifier, nameExpression.Range);
        }

        private AssignmentTarget CreateGlobalAssignmentTarget(string name, LuaSourceRange range)
        {
            var keyConstant = LuaConstant.FromString(name);
            var envReference = ResolveEnvironmentReference(range);
            var keyConstantIndex = GetConstantByteIndex(keyConstant);
            if (envReference.Kind == ReferenceKind.Upvalue && keyConstantIndex >= 0)
            {
                return AssignmentTarget.UpvalueField(envReference.Index, keyConstantIndex);
            }

            var environmentRegister = AllocateTemp();
            EmitReferenceRead(envReference, environmentRegister);

            return keyConstantIndex >= 0
                ? AssignmentTarget.TableField(environmentRegister, keyConstantIndex)
                : AssignmentTarget.TableDynamic(environmentRegister, LoadConstantIntoTemp(keyConstant));
        }

        private AssignmentTarget CreateMemberAssignmentTarget(LuaMemberAccessExpressionSyntax expression)
        {
            var tableRegister = AllocateTemp();
            CompileExpressionInto(expression.Prefix, tableRegister);

            var keyConstant = LuaConstant.FromString(expression.Name.Identifier);
            var constantIndex = GetConstantByteIndex(keyConstant);
            return constantIndex >= 0
                ? AssignmentTarget.TableField(tableRegister, constantIndex)
                : AssignmentTarget.TableDynamic(tableRegister, LoadConstantIntoTemp(keyConstant));
        }

        private AssignmentTarget CreateIndexAssignmentTarget(LuaIndexExpressionSyntax expression)
        {
            var tableRegister = AllocateTemp();
            CompileExpressionInto(expression.Prefix, tableRegister);
            var keyRegister = AllocateTemp();
            CompileExpressionInto(expression.Index, keyRegister);
            return AssignmentTarget.TableDynamic(tableRegister, keyRegister);
        }

        private void StoreAssignmentTarget(AssignmentTarget target, int valueRegister)
        {
            switch (target.Kind)
            {
                case AssignmentTargetKind.Local:
                    EmitMove(target.PrimaryIndex, valueRegister);
                    break;
                case AssignmentTargetKind.Upvalue:
                    EmitSetUpValue(valueRegister, target.PrimaryIndex);
                    break;
                case AssignmentTargetKind.UpvalueField:
                    EmitSetTabUp(target.PrimaryIndex, target.SecondaryIndex, valueRegister);
                    break;
                case AssignmentTargetKind.TableField:
                    EmitSetField(target.PrimaryIndex, target.SecondaryIndex, valueRegister);
                    break;
                case AssignmentTargetKind.TableDynamic:
                    EmitSetTable(target.PrimaryIndex, target.SecondaryIndex, valueRegister);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown assignment target kind '{target.Kind}'.");
            }
        }

        private void CompileReturn(LuaReturnStatementSyntax statement)
        {
            if (statement.Expressions.Count == 0)
            {
                EmitReturn0();
                return;
            }

            var startRegister = AllocateTemp();
            for (var index = 0; index < statement.Expressions.Count - 1; index++)
            {
                CompileExpressionInto(statement.Expressions[index], startRegister + index);
            }

            var lastExpression = statement.Expressions[^1];
            if (IsMultiResultExpression(lastExpression))
            {
                CompileFunctionCall((LuaFunctionCallExpressionSyntax)lastExpression, startRegister + statement.Expressions.Count - 1, fixedResultCount: null, openResults: true);
                EmitReturnOpen(startRegister);
                return;
            }

            CompileExpressionInto(lastExpression, startRegister + statement.Expressions.Count - 1);

            if (statement.Expressions.Count == 1)
            {
                EmitReturn1(startRegister);
                return;
            }

            EmitReturn(startRegister, statement.Expressions.Count);
        }

        private void CompileExpressionWithFixedResults(LuaExpressionSyntax expression, int targetRegister, int resultCount)
        {
            ReserveRegisterRange(targetRegister, Math.Max(1, resultCount));

            if (resultCount <= 0)
            {
                if (expression is LuaFunctionCallExpressionSyntax call)
                {
                    CompileFunctionCall(call, targetRegister, fixedResultCount: 0, openResults: false);
                    return;
                }

                CompileExpressionInto(expression, targetRegister);
                return;
            }

            if (expression is LuaFunctionCallExpressionSyntax functionCall)
            {
                CompileFunctionCall(functionCall, targetRegister, resultCount, openResults: false);
                return;
            }

            CompileExpressionInto(expression, targetRegister);
            if (resultCount > 1)
            {
                EmitLoadNilRange(targetRegister + 1, resultCount - 1);
            }
        }

        private void CompileExpressionInto(LuaExpressionSyntax expression, int targetRegister)
        {
            ReserveRegisterRange(targetRegister, 1);

            switch (expression)
            {
                case LuaNilLiteralExpressionSyntax:
                    EmitLoadNilRange(targetRegister, 1);
                    return;
                case LuaBooleanLiteralExpressionSyntax booleanLiteral:
                    EmitBoolean(targetRegister, booleanLiteral.Value);
                    return;
                case LuaNumberLiteralExpressionSyntax numberLiteral:
                    EmitLoadConstant(targetRegister, ParseNumberConstant(numberLiteral));
                    return;
                case LuaStringLiteralExpressionSyntax stringLiteral:
                    EmitLoadConstant(targetRegister, LuaConstant.FromString(stringLiteral.Value));
                    return;
                case LuaNameExpressionSyntax nameExpression:
                    CompileNameRead(nameExpression.Name.Identifier, nameExpression.Range, targetRegister);
                    return;
                case LuaParenthesizedExpressionSyntax parenthesizedExpression:
                    CompileExpressionInto(parenthesizedExpression.Expression, targetRegister);
                    return;
                case LuaUnaryExpressionSyntax unaryExpression:
                    CompileUnary(unaryExpression, targetRegister);
                    return;
                case LuaBinaryExpressionSyntax binaryExpression:
                    CompileBinary(binaryExpression, targetRegister);
                    return;
                case LuaFunctionExpressionSyntax functionExpression:
                    CompileNestedFunction(functionExpression.Body, injectSelf: false, targetRegister);
                    return;
                case LuaFunctionCallExpressionSyntax functionCall:
                    CompileFunctionCall(functionCall, targetRegister, fixedResultCount: 1, openResults: false);
                    return;
                case LuaTableConstructorExpressionSyntax tableConstructor:
                    CompileTableConstructor(tableConstructor, targetRegister);
                    return;
                case LuaMemberAccessExpressionSyntax memberAccess:
                    CompileExpressionInto(memberAccess.Prefix, targetRegister);
                    EmitTableReadByName(targetRegister, targetRegister, memberAccess.Name.Identifier, memberAccess.Range);
                    return;
                case LuaIndexExpressionSyntax indexExpression:
                    CompileExpressionInto(indexExpression.Prefix, targetRegister);
                    var keyRegister = AllocateTemp();
                    CompileExpressionInto(indexExpression.Index, keyRegister);
                    EmitGetTable(targetRegister, targetRegister, keyRegister);
                    return;
                case LuaVarargExpressionSyntax varargExpression:
                    throw CreateUnsupported(varargExpression, "vararg expressions are not supported yet");
                default:
                    throw CreateUnsupported(expression, $"unsupported expression '{expression.GetType().Name}'");
            }
        }

        private void CompileUnary(LuaUnaryExpressionSyntax expression, int targetRegister)
        {
            CompileExpressionInto(expression.Operand, targetRegister);

            switch (expression.Operator)
            {
                case LuaUnaryOperatorKind.Negate:
                    EmitUnary(LuaOpcode.Unm, targetRegister, targetRegister);
                    return;
                case LuaUnaryOperatorKind.Not:
                    EmitUnary(LuaOpcode.Not, targetRegister, targetRegister);
                    return;
                case LuaUnaryOperatorKind.Length:
                    EmitUnary(LuaOpcode.Len, targetRegister, targetRegister);
                    return;
                case LuaUnaryOperatorKind.BitwiseNot:
                    EmitUnary(LuaOpcode.BNot, targetRegister, targetRegister);
                    return;
                default:
                    throw CreateUnsupported(expression, $"unsupported unary operator '{expression.Operator}'");
            }
        }

        private void CompileBinary(LuaBinaryExpressionSyntax expression, int targetRegister)
        {
            if (expression.Operator is LuaBinaryOperatorKind.And or LuaBinaryOperatorKind.Or)
            {
                CompileShortCircuitBinary(expression, targetRegister);
                return;
            }

            if (expression.Operator == LuaBinaryOperatorKind.Concat)
            {
                var concatStart = AllocateTemp();
                CompileExpressionInto(expression.Left, concatStart);
                CompileExpressionInto(expression.Right, concatStart + 1);
                EmitConcat(concatStart, 2);
                EmitMove(targetRegister, concatStart);
                return;
            }

            CompileExpressionInto(expression.Left, targetRegister);
            var rightRegister = AllocateTemp();
            CompileExpressionInto(expression.Right, rightRegister);

            switch (expression.Operator)
            {
                case LuaBinaryOperatorKind.Add:
                    EmitBinary(LuaOpcode.Add, targetRegister, targetRegister, rightRegister);
                    return;
                case LuaBinaryOperatorKind.Subtract:
                    EmitBinary(LuaOpcode.Sub, targetRegister, targetRegister, rightRegister);
                    return;
                case LuaBinaryOperatorKind.Multiply:
                    EmitBinary(LuaOpcode.Mul, targetRegister, targetRegister, rightRegister);
                    return;
                case LuaBinaryOperatorKind.Divide:
                    EmitBinary(LuaOpcode.Div, targetRegister, targetRegister, rightRegister);
                    return;
                case LuaBinaryOperatorKind.IntegerDivide:
                    EmitBinary(LuaOpcode.IDiv, targetRegister, targetRegister, rightRegister);
                    return;
                case LuaBinaryOperatorKind.Modulo:
                    EmitBinary(LuaOpcode.Mod, targetRegister, targetRegister, rightRegister);
                    return;
                case LuaBinaryOperatorKind.Power:
                    EmitBinary(LuaOpcode.Pow, targetRegister, targetRegister, rightRegister);
                    return;
                case LuaBinaryOperatorKind.BitwiseAnd:
                    EmitBinary(LuaOpcode.Band, targetRegister, targetRegister, rightRegister);
                    return;
                case LuaBinaryOperatorKind.BitwiseOr:
                    EmitBinary(LuaOpcode.Bor, targetRegister, targetRegister, rightRegister);
                    return;
                case LuaBinaryOperatorKind.BitwiseXor:
                    EmitBinary(LuaOpcode.BXor, targetRegister, targetRegister, rightRegister);
                    return;
                case LuaBinaryOperatorKind.LeftShift:
                    EmitBinary(LuaOpcode.Shl, targetRegister, targetRegister, rightRegister);
                    return;
                case LuaBinaryOperatorKind.RightShift:
                    EmitBinary(LuaOpcode.Shr, targetRegister, targetRegister, rightRegister);
                    return;
                case LuaBinaryOperatorKind.Equal:
                    EmitComparisonBoolean(LuaOpcode.Eq, targetRegister, rightRegister, expected: true);
                    return;
                case LuaBinaryOperatorKind.NotEqual:
                    EmitComparisonBoolean(LuaOpcode.Eq, targetRegister, rightRegister, expected: false);
                    return;
                case LuaBinaryOperatorKind.LessThan:
                    EmitComparisonBoolean(LuaOpcode.Lt, targetRegister, rightRegister, expected: true);
                    return;
                case LuaBinaryOperatorKind.LessEqual:
                    EmitComparisonBoolean(LuaOpcode.Le, targetRegister, rightRegister, expected: true);
                    return;
                case LuaBinaryOperatorKind.GreaterThan:
                    EmitComparisonBoolean(LuaOpcode.Lt, rightRegister, targetRegister, expected: true, targetRegister);
                    return;
                case LuaBinaryOperatorKind.GreaterEqual:
                    EmitComparisonBoolean(LuaOpcode.Le, rightRegister, targetRegister, expected: true, targetRegister);
                    return;
                default:
                    throw CreateUnsupported(expression, $"unsupported binary operator '{expression.Operator}'");
            }
        }

        private void CompileShortCircuitBinary(LuaBinaryExpressionSyntax expression, int targetRegister)
        {
            CompileExpressionInto(expression.Left, targetRegister);
            EmitTest(targetRegister, expectedTruthy: expression.Operator == LuaBinaryOperatorKind.Or);
            var skipRightJump = EmitJumpPlaceholder();
            CompileExpressionInto(expression.Right, targetRegister);
            PatchJump(skipRightJump, CurrentProgramCounter);
        }

        private void CompileTableConstructor(LuaTableConstructorExpressionSyntax expression, int targetRegister)
        {
            EmitNewTable(targetRegister);

            var arrayIndex = 1;
            foreach (var field in expression.Fields)
            {
                switch (field)
                {
                    case LuaExpressionTableFieldSyntax expressionField:
                        CompileSequentialTableField(targetRegister, arrayIndex, expressionField.Expression);
                        arrayIndex++;
                        break;
                    case LuaNameTableFieldSyntax nameField:
                        CompileNamedTableField(targetRegister, nameField.Name.Identifier, nameField.Value, nameField.Range);
                        break;
                    case LuaKeyTableFieldSyntax keyField:
                        CompileKeyedTableField(targetRegister, keyField.Key, keyField.Value);
                        break;
                    default:
                        throw CreateUnsupported(field, $"unsupported table field '{field.GetType().Name}'");
                }
            }
        }

        private void CompileSequentialTableField(int tableRegister, int arrayIndex, LuaExpressionSyntax value)
        {
            var valueRegister = AllocateTemp();
            CompileExpressionInto(value, valueRegister);

            if (arrayIndex <= LuaInstructionLayout.MaxArgB)
            {
                EmitSetIntegerKey(tableRegister, arrayIndex, valueRegister);
                return;
            }

            var keyRegister = AllocateTemp();
            EmitLoadConstant(keyRegister, LuaConstant.FromInteger(arrayIndex));
            EmitSetTable(tableRegister, keyRegister, valueRegister);
        }

        private void CompileNamedTableField(int tableRegister, string name, LuaExpressionSyntax value, LuaSourceRange range)
        {
            var valueRegister = AllocateTemp();
            CompileExpressionInto(value, valueRegister);

            var keyConstant = LuaConstant.FromString(name);
            var constantIndex = GetConstantByteIndex(keyConstant);
            if (constantIndex >= 0)
            {
                EmitSetField(tableRegister, constantIndex, valueRegister);
                return;
            }

            var keyRegister = AllocateTemp();
            EmitLoadConstant(keyRegister, keyConstant);
            EmitSetTable(tableRegister, keyRegister, valueRegister);
        }

        private void CompileKeyedTableField(int tableRegister, LuaExpressionSyntax keyExpression, LuaExpressionSyntax valueExpression)
        {
            var keyRegister = AllocateTemp();
            CompileExpressionInto(keyExpression, keyRegister);
            var valueRegister = AllocateTemp();
            CompileExpressionInto(valueExpression, valueRegister);
            EmitSetTable(tableRegister, keyRegister, valueRegister);
        }

        private void CompileNameRead(string name, LuaSourceRange range, int targetRegister)
        {
            if (TryResolveLexicalName(name, out var reference))
            {
                EmitReferenceRead(reference, targetRegister);
                return;
            }

            EmitGlobalRead(name, range, targetRegister);
        }

        private void EmitGlobalRead(string name, LuaSourceRange range, int targetRegister)
        {
            var keyConstant = LuaConstant.FromString(name);
            var environmentReference = ResolveEnvironmentReference(range);
            var keyConstantIndex = GetConstantByteIndex(keyConstant);

            if (environmentReference.Kind == ReferenceKind.Upvalue && keyConstantIndex >= 0)
            {
                EmitGetTabUp(targetRegister, environmentReference.Index, keyConstantIndex);
                return;
            }

            EmitReferenceRead(environmentReference, targetRegister);
            if (keyConstantIndex >= 0)
            {
                EmitGetField(targetRegister, targetRegister, keyConstantIndex);
                return;
            }

            var keyRegister = AllocateTemp();
            EmitLoadConstant(keyRegister, keyConstant);
            EmitGetTable(targetRegister, targetRegister, keyRegister);
        }

        private Reference ResolveEnvironmentReference(LuaSourceRange range)
        {
            if (TryResolveLexicalName(EnvironmentName, out var reference))
            {
                return reference;
            }

            throw CreateError(range, "cannot resolve _ENV");
        }

        private bool TryResolveLexicalName(string name, out Reference reference)
        {
            for (var index = _visibleLocals.Count - 1; index >= 0; index--)
            {
                if (StringComparer.Ordinal.Equals(_visibleLocals[index].Name, name))
                {
                    reference = new Reference(ReferenceKind.Local, _visibleLocals[index].Register);
                    return true;
                }
            }

            if (_upvalueIndices.TryGetValue(name, out var upvalueIndex))
            {
                reference = new Reference(ReferenceKind.Upvalue, upvalueIndex);
                return true;
            }

            if (_parent is not null && _parent.TryResolveChildCapture(name, out var captureSource))
            {
                var captureIndex = GetOrAddUpvalue(name, captureSource.InStack, captureSource.Index);
                reference = new Reference(ReferenceKind.Upvalue, captureIndex);
                return true;
            }

            if (_parent is null && StringComparer.Ordinal.Equals(name, EnvironmentName))
            {
                var environmentIndex = GetOrAddUpvalue(EnvironmentName, inStack: 0, index: 0);
                reference = new Reference(ReferenceKind.Upvalue, environmentIndex);
                return true;
            }

            reference = default;
            return false;
        }

        private bool TryResolveChildCapture(string name, out CaptureSource capture)
        {
            for (var index = _visibleLocals.Count - 1; index >= 0; index--)
            {
                if (StringComparer.Ordinal.Equals(_visibleLocals[index].Name, name))
                {
                    capture = new CaptureSource(InStack: 1, Index: checked((byte)_visibleLocals[index].Register));
                    return true;
                }
            }

            if (_upvalueIndices.TryGetValue(name, out var existingUpvalueIndex))
            {
                capture = new CaptureSource(InStack: 0, Index: checked((byte)existingUpvalueIndex));
                return true;
            }

            if (_parent is not null && _parent.TryResolveChildCapture(name, out var parentCapture))
            {
                var upvalueIndex = GetOrAddUpvalue(name, parentCapture.InStack, parentCapture.Index);
                capture = new CaptureSource(InStack: 0, Index: checked((byte)upvalueIndex));
                return true;
            }

            if (_parent is null && StringComparer.Ordinal.Equals(name, EnvironmentName))
            {
                var upvalueIndex = GetOrAddUpvalue(EnvironmentName, inStack: 0, index: 0);
                capture = new CaptureSource(InStack: 0, Index: checked((byte)upvalueIndex));
                return true;
            }

            capture = default;
            return false;
        }

        private void EmitReferenceRead(Reference reference, int targetRegister)
        {
            switch (reference.Kind)
            {
                case ReferenceKind.Local:
                    EmitMove(targetRegister, reference.Index);
                    break;
                case ReferenceKind.Upvalue:
                    EmitGetUpValue(targetRegister, reference.Index);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown reference kind '{reference.Kind}'.");
            }
        }

        private int AddLocal(string name, LuaSourcePosition position)
        {
            var register = _persistentRegisterCount;
            _persistentRegisterCount++;
            _tempRegisterTop = Math.Max(_tempRegisterTop, _persistentRegisterCount);
            TrackRegister(register);
            _visibleLocals.Add(new LocalInfo(name, register, position));
            return register;
        }

        private void EnsureDeclarationAttributesSupported(LuaDeclarationNameListSyntax declaration)
        {
            if (declaration.LeadingAttribute is not null)
            {
                throw CreateUnsupported(declaration.LeadingAttribute, "variable attributes are not supported yet");
            }

            foreach (var name in declaration.Names)
            {
                if (name.Attribute is not null)
                {
                    throw CreateUnsupported(name.Attribute, "variable attributes are not supported yet");
                }
            }
        }

        private void CompileNestedFunction(LuaFunctionBodySyntax body, bool injectSelf, int targetRegister)
        {
            var compiler = new FunctionCompiler(_sourceName, this);
            var prototype = compiler.CompileFunction(body, injectSelf);
            var prototypeIndex = _nestedPrototypes.Count;
            _nestedPrototypes.Add(prototype);
            EmitClosure(targetRegister, prototypeIndex);
        }

        private void CompileFunctionCall(
            LuaFunctionCallExpressionSyntax expression,
            int targetRegister,
            int? fixedResultCount,
            bool openResults)
        {
            if (openResults && fixedResultCount is not null)
            {
                throw new InvalidOperationException("A call cannot request fixed and open results at the same time.");
            }

            var functionRegister = targetRegister;
            var argumentStart = targetRegister + 1;

            if (expression.MethodName is not null)
            {
                CompileExpressionInto(expression.Prefix, functionRegister);
                if (TryGetByteConstantIndex(LuaConstant.FromString(expression.MethodName.Identifier), out var methodConstantIndex))
                {
                    EmitSelf(functionRegister, functionRegister, methodConstantIndex);
                }
                else
                {
                    var receiverRegister = argumentStart;
                    EmitMove(receiverRegister, functionRegister);
                    var keyRegister = AllocateTemp();
                    EmitLoadConstant(keyRegister, LuaConstant.FromString(expression.MethodName.Identifier));
                    EmitGetTable(functionRegister, receiverRegister, keyRegister);
                }

                argumentStart = targetRegister + 2;
            }
            else
            {
                CompileExpressionInto(expression.Prefix, functionRegister);
            }

            var functionAndArgumentCount = CompileCallArguments(expression.Arguments, argumentStart, allowOpenLast: true);
            if (expression.MethodName is not null && functionAndArgumentCount != 0)
            {
                functionAndArgumentCount += 1;
            }

            var resultOperand = openResults ? 0 : (fixedResultCount ?? 1) + 1;
            EmitCall(functionRegister, functionAndArgumentCount, resultOperand);
        }

        private int CompileCallArguments(LuaCallArgumentsSyntax arguments, int startRegister, bool allowOpenLast)
        {
            if (arguments.Arguments.Count == 0)
            {
                return 1;
            }

            var currentRegister = startRegister;
            for (var index = 0; index < arguments.Arguments.Count; index++)
            {
                var argument = arguments.Arguments[index];
                var isLast = index == arguments.Arguments.Count - 1;
                if (allowOpenLast && isLast && IsMultiResultExpression(argument))
                {
                    CompileFunctionCall((LuaFunctionCallExpressionSyntax)argument, currentRegister, fixedResultCount: null, openResults: true);
                    return 0;
                }

                CompileExpressionInto(argument, currentRegister);
                currentRegister += 1;
            }

            return arguments.Arguments.Count + 1;
        }

        private static bool IsMultiResultExpression(LuaExpressionSyntax expression)
        {
            return expression is LuaFunctionCallExpressionSyntax;
        }

        private LuaConstant ParseNumberConstant(LuaNumberLiteralExpressionSyntax expression)
        {
            if (!LuaValueHelper.TryParseLuaStringNumber(expression.Text, out var value))
            {
                throw CreateError(expression.Range, $"invalid numeric literal '{expression.Text}'");
            }

            return value.Kind switch
            {
                LuaValueKind.Integer => LuaConstant.FromInteger(value.AsInteger()),
                LuaValueKind.Float => LuaConstant.FromFloat(value.AsFloat()),
                _ => throw CreateError(expression.Range, $"unsupported numeric literal '{expression.Text}'")
            };
        }

        private void EnterScope()
        {
            _scopeStarts.Push(_visibleLocals.Count);
        }

        private void ExitScope()
        {
            var start = _scopeStarts.Pop();
            if (start < _visibleLocals.Count)
            {
                _visibleLocals.RemoveRange(start, _visibleLocals.Count - start);
            }
        }

        private void ResetTemps()
        {
            _tempRegisterTop = _persistentRegisterCount;
            TrackRegister(Math.Max(0, _tempRegisterTop - 1));
        }

        private int AllocateTemp()
        {
            var register = _tempRegisterTop;
            _tempRegisterTop++;
            TrackRegister(register);
            return register;
        }

        private int LoadConstantIntoTemp(LuaConstant constant)
        {
            var register = AllocateTemp();
            EmitLoadConstant(register, constant);
            return register;
        }

        private int LoadNilIntoTemp()
        {
            var register = AllocateTemp();
            EmitLoadNilRange(register, 1);
            return register;
        }

        private void TrackRegister(int register)
        {
            if (register < 0)
            {
                return;
            }

            _maxRegisterCount = Math.Max(_maxRegisterCount, register + 1);
        }

        private void ReserveRegisterRange(int startRegister, int count)
        {
            if (count <= 0)
            {
                return;
            }

            TrackRegister(startRegister + count - 1);
            _tempRegisterTop = Math.Max(_tempRegisterTop, startRegister + count);
        }

        private bool EndsWithReturn()
        {
            if (_code.Count == 0)
            {
                return false;
            }

            var opcode = DecodeOpcode(_code[^1]);
            return opcode is LuaOpcode.Return or LuaOpcode.Return0 or LuaOpcode.Return1;
        }

        private int GetOrAddUpvalue(string name, byte inStack, byte index)
        {
            if (_upvalueIndices.TryGetValue(name, out var existing))
            {
                return existing;
            }

            var upvalueIndex = _upvalues.Count;
            if (upvalueIndex > byte.MaxValue)
            {
                throw CreateError(_fallbackPosition, "too many upvalues");
            }

            _upvalueIndices[name] = upvalueIndex;
            _upvalues.Add(new UpvalueInfo(name, inStack, index));
            return upvalueIndex;
        }

        private int AddConstant(LuaConstant constant)
        {
            var key = CreateConstantKey(constant);
            if (_constantIndices.TryGetValue(key, out var index))
            {
                return index;
            }

            index = _constants.Count;
            _constants.Add(constant);
            _constantIndices[key] = index;
            return index;
        }

        private static string CreateConstantKey(LuaConstant constant)
        {
            return constant.Kind switch
            {
                LuaConstantKind.Nil => "nil",
                LuaConstantKind.Boolean => $"b:{constant.AsBoolean()}",
                LuaConstantKind.Integer => $"i:{constant.AsInteger()}",
                LuaConstantKind.Float => $"f:{BitConverter.DoubleToInt64Bits(constant.AsFloat())}",
                LuaConstantKind.String => $"s:{constant.AsString()}",
                _ => throw new InvalidOperationException($"Unsupported constant kind '{constant.Kind}'.")
            };
        }

        private bool TryGetByteConstantIndex(LuaConstant constant, out int index)
        {
            index = AddConstant(constant);
            return index <= byte.MaxValue;
        }

        private int GetConstantByteIndex(LuaConstant constant)
        {
            return TryGetByteConstantIndex(constant, out var index) ? index : -1;
        }

        private int CurrentProgramCounter => _code.Count;

        private int EmitJumpPlaceholder()
        {
            var programCounter = _code.Count;
            _code.Add(EncodeSJ(LuaOpcode.Jmp, 0));
            return programCounter;
        }

        private void PatchJumps(IEnumerable<int> jumps, int targetProgramCounter)
        {
            foreach (var jump in jumps)
            {
                PatchJump(jump, targetProgramCounter);
            }
        }

        private void PatchJump(int jumpProgramCounter, int targetProgramCounter)
        {
            var offset = targetProgramCounter - (jumpProgramCounter + 1);
            _code[jumpProgramCounter] = EncodeSJ(LuaOpcode.Jmp, offset);
        }

        private void EmitJump(int targetProgramCounter)
        {
            var offset = targetProgramCounter - (CurrentProgramCounter + 1);
            _code.Add(EncodeSJ(LuaOpcode.Jmp, offset));
        }

        private void EmitLoadConstant(int targetRegister, LuaConstant constant)
        {
            switch (constant.Kind)
            {
                case LuaConstantKind.Nil:
                    EmitLoadNilRange(targetRegister, 1);
                    return;
                case LuaConstantKind.Boolean:
                    EmitBoolean(targetRegister, constant.AsBoolean());
                    return;
            }

            var constantIndex = AddConstant(constant);
            _code.Add(EncodeAbx(LuaOpcode.LoadK, targetRegister, constantIndex));
        }

        private void EmitLoadNilRange(int startRegister, int count)
        {
            for (var remaining = count; remaining > 0; remaining -= byte.MaxValue + 1)
            {
                var chunk = Math.Min(remaining, byte.MaxValue + 1);
                _code.Add(EncodeAbc(LuaOpcode.LoadNil, startRegister, chunk - 1, 0));
                startRegister += chunk;
            }
        }

        private void EmitBoolean(int targetRegister, bool value)
        {
            _code.Add(EncodeAbc(value ? LuaOpcode.LoadTrue : LuaOpcode.LoadFalse, targetRegister, 0, 0));
        }

        private void EmitMove(int targetRegister, int sourceRegister)
        {
            if (targetRegister == sourceRegister)
            {
                return;
            }

            _code.Add(EncodeAbc(LuaOpcode.Move, targetRegister, sourceRegister, 0));
        }

        private void EmitGetUpValue(int targetRegister, int upvalueIndex)
        {
            _code.Add(EncodeAbc(LuaOpcode.GetUpVal, targetRegister, upvalueIndex, 0));
        }

        private void EmitSetUpValue(int sourceRegister, int upvalueIndex)
        {
            _code.Add(EncodeAbc(LuaOpcode.SetUpVal, sourceRegister, upvalueIndex, 0));
        }

        private void EmitGetTabUp(int targetRegister, int upvalueIndex, int constantIndex)
        {
            _code.Add(EncodeAbc(LuaOpcode.GetTabUp, targetRegister, upvalueIndex, constantIndex));
        }

        private void EmitGetField(int targetRegister, int tableRegister, int constantIndex)
        {
            _code.Add(EncodeAbc(LuaOpcode.GetField, targetRegister, tableRegister, constantIndex));
        }

        private void EmitGetTable(int targetRegister, int tableRegister, int keyRegister)
        {
            _code.Add(EncodeAbc(LuaOpcode.GetTable, targetRegister, tableRegister, keyRegister));
        }

        private void EmitSetTabUp(int upvalueIndex, int constantIndex, int valueRegister)
        {
            _code.Add(EncodeAbc(LuaOpcode.SetTabUp, upvalueIndex, constantIndex, valueRegister));
        }

        private void EmitSetField(int tableRegister, int constantIndex, int valueRegister)
        {
            _code.Add(EncodeAbc(LuaOpcode.SetField, tableRegister, constantIndex, valueRegister));
        }

        private void EmitSetTable(int tableRegister, int keyRegister, int valueRegister)
        {
            _code.Add(EncodeAbc(LuaOpcode.SetTable, tableRegister, keyRegister, valueRegister));
        }

        private void EmitSetIntegerKey(int tableRegister, int integerKey, int valueRegister)
        {
            _code.Add(EncodeAbc(LuaOpcode.SetI, tableRegister, integerKey, valueRegister));
        }

        private void EmitNewTable(int targetRegister)
        {
            _code.Add(EncodeAbc(LuaOpcode.NewTable, targetRegister, 0, 0));
            _code.Add(EncodeAx(LuaOpcode.ExtraArg, 0));
        }

        private void EmitSelf(int targetRegister, int receiverRegister, int constantIndex)
        {
            _code.Add(EncodeAbc(LuaOpcode.Self, targetRegister, receiverRegister, constantIndex));
        }

        private void EmitBinary(LuaOpcode opcode, int targetRegister, int leftRegister, int rightRegister)
        {
            _code.Add(EncodeAbc(opcode, targetRegister, leftRegister, rightRegister));
        }

        private void EmitUnary(LuaOpcode opcode, int targetRegister, int operandRegister)
        {
            _code.Add(EncodeAbc(opcode, targetRegister, operandRegister, 0));
        }

        private void EmitConcat(int startRegister, int operandCount)
        {
            _code.Add(EncodeAbc(LuaOpcode.Concat, startRegister, operandCount, 0));
        }

        private void EmitComparisonBoolean(LuaOpcode opcode, int leftRegister, int rightRegister, bool expected, int? targetRegister = null)
        {
            var resultRegister = targetRegister ?? leftRegister;
            _code.Add(EncodeAbc(opcode, leftRegister, rightRegister, 0, expected ? 1 : 0));
            var trueJump = EmitJumpPlaceholder();
            EmitBoolean(resultRegister, false);
            var endJump = EmitJumpPlaceholder();
            PatchJump(trueJump, CurrentProgramCounter);
            EmitBoolean(resultRegister, true);
            PatchJump(endJump, CurrentProgramCounter);
        }

        private void EmitTest(int registerIndex, bool expectedTruthy)
        {
            _code.Add(EncodeAbc(LuaOpcode.Test, registerIndex, 0, 0, expectedTruthy ? 1 : 0));
        }

        private void EmitCall(int functionRegister, int functionAndArgumentCount, int resultOperand)
        {
            _code.Add(EncodeAbc(LuaOpcode.Call, functionRegister, functionAndArgumentCount, resultOperand));
        }

        private void EmitReturn(int startRegister, int resultCount)
        {
            _code.Add(EncodeAbc(LuaOpcode.Return, startRegister, resultCount + 1, 0));
        }

        private void EmitReturnOpen(int startRegister)
        {
            _code.Add(EncodeAbc(LuaOpcode.Return, startRegister, 0, 0));
        }

        private void EmitReturn0()
        {
            _code.Add(EncodeAbc(LuaOpcode.Return0, 0, 0, 0));
        }

        private void EmitReturn1(int registerIndex)
        {
            _code.Add(EncodeAbc(LuaOpcode.Return1, registerIndex, 0, 0));
        }

        private void EmitClosure(int targetRegister, int prototypeIndex)
        {
            _code.Add(EncodeAbx(LuaOpcode.Closure, targetRegister, prototypeIndex));
        }

        private void EmitTableReadByName(int targetRegister, int tableRegister, string name, LuaSourceRange range)
        {
            var constant = LuaConstant.FromString(name);
            var constantIndex = GetConstantByteIndex(constant);
            if (constantIndex >= 0)
            {
                EmitGetField(targetRegister, tableRegister, constantIndex);
                return;
            }

            var keyRegister = AllocateTemp();
            EmitLoadConstant(keyRegister, constant);
            EmitGetTable(targetRegister, tableRegister, keyRegister);
        }

        private LuaCompilerException CreateUnsupported(LuaSyntaxNode node, string message)
        {
            return CreateError(node.Range, message);
        }

        private LuaCompilerException CreateError(LuaSourceRange range, string message)
        {
            return new LuaCompilerException(message, range.Start, _sourceName);
        }

        private LuaCompilerException CreateError(LuaSourcePosition position, string message)
        {
            return new LuaCompilerException(message, position, _sourceName);
        }

        private static LuaOpcode DecodeOpcode(uint raw)
        {
            return (LuaOpcode)LuaInstructionLayout.GetArg(raw, LuaInstructionLayout.PosOp, LuaInstructionLayout.SizeOp);
        }

        private static uint EncodeAbc(LuaOpcode opcode, int a, int b, int c, int k = 0)
        {
            return
                ((uint)opcode << LuaInstructionLayout.PosOp) |
                ((uint)a << LuaInstructionLayout.PosA) |
                ((uint)k << LuaInstructionLayout.PosK) |
                ((uint)b << LuaInstructionLayout.PosB) |
                ((uint)c << LuaInstructionLayout.PosC);
        }

        private static uint EncodeAbx(LuaOpcode opcode, int a, int bx)
        {
            return
                ((uint)opcode << LuaInstructionLayout.PosOp) |
                ((uint)a << LuaInstructionLayout.PosA) |
                ((uint)bx << LuaInstructionLayout.PosBx);
        }

        private static uint EncodeAx(LuaOpcode opcode, int ax)
        {
            return
                ((uint)opcode << LuaInstructionLayout.PosOp) |
                ((uint)ax << LuaInstructionLayout.PosAx);
        }

        private static uint EncodeSJ(LuaOpcode opcode, int sJ)
        {
            var encoded = (uint)(sJ + LuaInstructionLayout.OffsetSJ);
            return
                ((uint)opcode << LuaInstructionLayout.PosOp) |
                (encoded << LuaInstructionLayout.PosSJ);
        }

        private readonly record struct Reference(ReferenceKind Kind, int Index);

        private enum ReferenceKind
        {
            Local,
            Upvalue
        }

        private sealed record LocalInfo(string Name, int Register, LuaSourcePosition Position);

        private sealed record UpvalueInfo(string Name, byte InStack, byte Index);

        private readonly record struct CaptureSource(byte InStack, byte Index);

        private sealed class LoopContext
        {
            public List<int> BreakJumps { get; } = [];
        }

        private readonly record struct AssignmentTarget(
            AssignmentTargetKind Kind,
            int PrimaryIndex,
            int SecondaryIndex)
        {
            public static AssignmentTarget Local(int register) => new(AssignmentTargetKind.Local, register, 0);

            public static AssignmentTarget Upvalue(int index) => new(AssignmentTargetKind.Upvalue, index, 0);

            public static AssignmentTarget UpvalueField(int upvalueIndex, int constantIndex) =>
                new(AssignmentTargetKind.UpvalueField, upvalueIndex, constantIndex);

            public static AssignmentTarget TableField(int tableRegister, int constantIndex) =>
                new(AssignmentTargetKind.TableField, tableRegister, constantIndex);

            public static AssignmentTarget TableDynamic(int tableRegister, int keyRegister) =>
                new(AssignmentTargetKind.TableDynamic, tableRegister, keyRegister);
        }

        private enum AssignmentTargetKind
        {
            Local,
            Upvalue,
            UpvalueField,
            TableField,
            TableDynamic
        }
    }
}
