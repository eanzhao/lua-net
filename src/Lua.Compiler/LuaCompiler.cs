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

    private sealed class FunctionCompiler(string sourceName, FunctionCompiler? parent, string? debugName = null)
    {
        private const string EnvironmentName = "_ENV";
        private const byte VarArgFunctionFlag = 0b00000001;
        private const byte VarArgTableFlag = 0b00000010;
        private readonly string _sourceName = sourceName;
        private readonly FunctionCompiler? _parent = parent;
        private readonly string? _debugName = debugName;
        private readonly List<uint> _code = [];
        private readonly List<byte> _registerTopHints = [];
        private readonly List<LuaConstant> _constants = [];
        private readonly Dictionary<string, int> _constantIndices = new(StringComparer.Ordinal);
        private readonly List<LuaPrototype> _nestedPrototypes = [];
        private readonly List<UpvalueInfo> _upvalues = [];
        private readonly Dictionary<string, int> _upvalueIndices = new(StringComparer.Ordinal);
        private readonly Dictionary<int, string> _toBeClosedNames = [];
        private readonly List<LocalInfo> _visibleLocals = [];
        private readonly List<GlobalDeclarationInfo> _visibleGlobalDeclarations = [];
        private readonly List<ScopeInfo> _scopes = [];
        private readonly List<LoopContext> _loops = [];
        private readonly List<LabelInfo> _visibleLabels = [];
        private readonly List<PendingGotoInfo> _pendingGotos = [];
        private int _persistentRegisterCount;
        private int _tempRegisterTop;
        private int _maxRegisterCount;
        private LuaSourcePosition _fallbackPosition = new(0, 1, 1);

        public LuaPrototype CompileChunk(LuaChunkSyntax syntax)
        {
            _fallbackPosition = syntax.Range.Start;
            EnterScope();
            CompileBlockStatements(syntax.Block.Statements);
            EnsureAllGotosResolved();
            ExitScope();

            if (!EndsWithReturn())
            {
                EmitReturn0();
            }

            return BuildPrototype(
                lineDefined: 0,
                lastLineDefined: 0,
                parameterCount: 0,
                syntax.Range,
                flags: VarArgFunctionFlag);
        }

        public LuaPrototype CompileFunction(LuaFunctionBodySyntax body, bool injectSelf)
        {
            _fallbackPosition = body.Range.Start;
            var parameterCount = body.Parameters.Count + (injectSelf ? 1 : 0);
            var flags = GetFunctionFlags(body);

            EnterScope();

            if (injectSelf)
            {
                AddLocal("self", body.Range.Start);
            }

            foreach (var parameter in body.Parameters)
            {
                AddLocal(parameter.Identifier, parameter.Range.Start);
            }

            if (body.VarargParameter?.Name is not null)
            {
                AddLocal(body.VarargParameter.Name.Identifier, body.VarargParameter.Name.Range.Start, isReadOnly: true);
                EmitVarArgPrep();
            }

            CompileBlockStatements(body.Block.Statements);
            EnsureAllGotosResolved();
            ExitScope();

            if (!EndsWithReturn())
            {
                EmitReturn0();
            }

            PatchVarArgInstructionsForTable(flags, parameterCount);

            return BuildPrototype(
                lineDefined: body.Range.Start.Line,
                lastLineDefined: body.Range.End.Line,
                parameterCount,
                body.Range,
                flags);
        }

        private static byte GetFunctionFlags(LuaFunctionBodySyntax body)
        {
            var flags = body.VarargParameter is not null ? VarArgFunctionFlag : (byte)0;
            if (body.VarargParameter?.Name is not null)
            {
                flags |= VarArgTableFlag;
            }

            return flags;
        }

        private LuaPrototype BuildPrototype(
            int lineDefined,
            int lastLineDefined,
            int parameterCount,
            LuaSourceRange range,
            byte flags)
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
                DebugName = _debugName,
                LineDefined = lineDefined,
                LastLineDefined = lastLineDefined,
                NumberOfParameters = checked((byte)parameterCount),
                Flags = flags,
                MaxStackSize = checked((byte)_maxRegisterCount),
                Code = _code.ToArray(),
                Constants = _constants.ToArray(),
                Upvalues = BuildUpvalues(),
                NestedPrototypes = _nestedPrototypes.ToArray(),
                Source = _sourceName,
                LineInfo = Enumerable.Repeat((sbyte)0, _code.Count).ToArray(),
                AbsoluteLineInfo = [],
                LocalVariables = [],
                ToBeClosedNames = BuildToBeClosedNames(),
                RegisterTopHints = _registerTopHints.ToArray()
            };
        }

        private string?[]? BuildToBeClosedNames()
        {
            if (_toBeClosedNames.Count == 0)
            {
                return null;
            }

            var names = new string?[_code.Count];
            foreach (var entry in _toBeClosedNames)
            {
                names[entry.Key] = entry.Value;
            }

            return names;
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
                case LuaNumericForStatementSyntax numericFor:
                    CompileNumericFor(numericFor);
                    return;
                case LuaGenericForStatementSyntax genericFor:
                    CompileGenericFor(genericFor);
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
                    CompileLabel(label);
                    return;
                case LuaGotoStatementSyntax @goto:
                    CompileGoto(@goto);
                    return;
                case LuaGlobalDeclarationStatementSyntax globalDeclaration:
                    CompileGlobalDeclaration(globalDeclaration);
                    return;
                case LuaGlobalWildcardStatementSyntax globalWildcard:
                    CompileGlobalWildcard(globalWildcard);
                    return;
                case LuaGlobalFunctionStatementSyntax globalFunction:
                    CompileGlobalFunction(globalFunction);
                    return;
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

            EnterScope();
            _loops.Add(new LoopContext(CurrentScope));
            CompileBlockStatements(statement.Block.Statements);
            var loop = _loops[^1];
            _loops.RemoveAt(_loops.Count - 1);
            ExitScope();

            EmitJump(loopStart);
            var loopEnd = CurrentProgramCounter;
            PatchJump(exitJump, loopEnd);
            PatchJumps(loop.BreakJumps, loopEnd);
        }

        private void CompileRepeat(LuaRepeatStatementSyntax statement)
        {
            var loopStart = CurrentProgramCounter;

            EnterScope();
            _loops.Add(new LoopContext(CurrentScope));
            CompileBlockStatements(statement.Block.Statements);

            var conditionRegister = AllocateTemp();
            CompileExpressionInto(statement.Condition, conditionRegister);
            if (GetCurrentScopeCloseRegister() is int closeRegister)
            {
                EmitClose(closeRegister);
            }

            EmitTest(conditionRegister, expectedTruthy: false);
            var continueJump = EmitJumpPlaceholder();

            var loop = _loops[^1];
            _loops.RemoveAt(_loops.Count - 1);
            ExitScope(emitClose: false);
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

            EmitBreakScopeClose(_loops[^1]);
            _loops[^1].BreakJumps.Add(EmitJumpPlaceholder());
        }

        private void CompileNumericFor(LuaNumericForStatementSyntax statement)
        {
            var registerMark = _persistentRegisterCount;
            var stateRegister = AllocatePersistentRegister();
            var limitRegister = AllocatePersistentRegister();
            var variableRegister = AllocatePersistentRegister();

            CompileExpressionInto(statement.InitialValue, stateRegister);
            CompileExpressionInto(statement.Limit, limitRegister);

            if (statement.Step is null)
            {
                EmitLoadConstant(variableRegister, LuaConstant.FromInteger(1));
            }
            else
            {
                CompileExpressionInto(statement.Step, variableRegister);
            }

            var prepProgramCounter = EmitForPrepPlaceholder(stateRegister);
            var bodyProgramCounter = CurrentProgramCounter;

            EnterScope();
            AddExistingLocal(
                statement.Name.Identifier,
                variableRegister,
                statement.Name.Range.Start,
                isReadOnly: true);

            _loops.Add(new LoopContext(CurrentScope));
            CompileBlockStatements(statement.Block.Statements);
            var loop = _loops[^1];
            _loops.RemoveAt(_loops.Count - 1);

            ExitScope();

            var forLoopProgramCounter = EmitForLoop(stateRegister, bodyProgramCounter);
            PatchForPrep(prepProgramCounter, stateRegister, forLoopProgramCounter);

            var loopEnd = CurrentProgramCounter;
            PatchJumps(loop.BreakJumps, loopEnd);
            _persistentRegisterCount = registerMark;
        }

        private void CompileGenericFor(LuaGenericForStatementSyntax statement)
        {
            if (statement.Names.Count > byte.MaxValue)
            {
                throw CreateError(statement.Range, "generic for loop has too many variables");
            }

            var registerMark = _persistentRegisterCount;
            var iteratorRegister = AllocatePersistentRegister();
            var stateRegister = AllocatePersistentRegister();
            var controlRegister = AllocatePersistentRegister();
            var firstLoopVariableRegister = AllocatePersistentRegister();

            for (var index = 1; index < statement.Names.Count; index++)
            {
                AllocatePersistentRegister();
            }

            var expressionRegisters = EvaluateAssignmentValues(statement.Expressions, 4, statement.Range);
            int? nilRegister = null;
            for (var index = 0; index < 4; index++)
            {
                var sourceRegister = index < expressionRegisters.Count
                    ? expressionRegisters[index]
                    : nilRegister ??= LoadNilIntoTemp();

                EmitMove(iteratorRegister + index, sourceRegister);
            }

            var prepProgramCounter = EmitTForPrepPlaceholder(iteratorRegister);
            var bodyProgramCounter = CurrentProgramCounter;

            EnterScope();
            for (var index = 0; index < statement.Names.Count; index++)
            {
                AddExistingLocal(
                    statement.Names[index].Identifier,
                    firstLoopVariableRegister + index,
                    statement.Names[index].Range.Start,
                    isReadOnly: true);
            }

            _loops.Add(new LoopContext(CurrentScope, iteratorRegister));
            CompileBlockStatements(statement.Block.Statements);
            var loop = _loops[^1];
            _loops.RemoveAt(_loops.Count - 1);

            ExitScope();

            var callProgramCounter = EmitTForCall(iteratorRegister, statement.Names.Count);
            EmitTForLoop(iteratorRegister, bodyProgramCounter);
            var closeProgramCounter = CurrentProgramCounter;
            EmitClose(iteratorRegister);

            PatchTForPrep(prepProgramCounter, iteratorRegister, callProgramCounter);
            PatchJumps(loop.BreakJumps, closeProgramCounter);
            _persistentRegisterCount = registerMark;
        }

        private void CompileGoto(LuaGotoStatementSyntax statement)
        {
            var pendingGoto = new PendingGotoInfo(
                statement.Name.Identifier,
                statement.Range.Start,
                EmitJumpPlaceholder(),
                EmitJumpPlaceholder(),
                CaptureScopeStates(),
                CaptureLoopStates());

            if (TryGetVisibleLabel(statement.Name.Identifier, out var label))
            {
                ResolveGoto(pendingGoto, label);
                return;
            }

            _pendingGotos.Add(pendingGoto);
        }

        private void CompileLabel(LuaLabelStatementSyntax statement)
        {
            for (var index = _visibleLabels.Count - 1; index >= 0; index--)
            {
                if (StringComparer.Ordinal.Equals(_visibleLabels[index].Name, statement.Name.Identifier))
                {
                    throw CreateError(statement.Range, $"label '{statement.Name.Identifier}' already defined");
                }
            }

            var label = new LabelInfo(
                statement.Name.Identifier,
                statement.Range.Start,
                CurrentProgramCounter,
                CaptureScopeStates(),
                CaptureLoopStates(),
                CurrentScope);
            _visibleLabels.Add(label);
            ResolvePendingGotos(label);
        }

        private void CompileLocalDeclaration(LuaLocalDeclarationStatementSyntax statement)
        {
            if (statement.Initializers.Count == 0)
            {
                var startRegister = _persistentRegisterCount;
                var toBeClosedRegisters = new List<(int Register, string Name)>();
                foreach (var name in statement.Names.Names)
                {
                    var attributes = GetLocalAttributes(statement.Names.LeadingAttribute, name.Attribute);
                    var register = AddLocal(
                        name.Name.Identifier,
                        name.Range.Start,
                        attributes.IsReadOnly,
                        attributes.IsToBeClosed);
                    if (attributes.IsToBeClosed)
                    {
                        toBeClosedRegisters.Add((register, name.Name.Identifier));
                    }
                }

                EmitLoadNilRange(startRegister, statement.Names.Names.Count);
                for (var index = 0; index < toBeClosedRegisters.Count; index++)
                {
                    EmitToBeClosed(toBeClosedRegisters[index].Register, toBeClosedRegisters[index].Name);
                }

                return;
            }

            var valueRegisters = EvaluateAssignmentValues(
                statement.Initializers,
                statement.Names.Names.Count,
                statement.Range,
                index => index < statement.Names.Names.Count
                    ? statement.Names.Names[index].Name.Identifier
                    : null);

            for (var index = 0; index < statement.Names.Names.Count; index++)
            {
                var attributes = GetLocalAttributes(statement.Names.LeadingAttribute, statement.Names.Names[index].Attribute);
                var localRegister = AddLocal(
                    statement.Names.Names[index].Name.Identifier,
                    statement.Names.Names[index].Range.Start,
                    attributes.IsReadOnly,
                    attributes.IsToBeClosed);
                if (index < valueRegisters.Count)
                {
                    EmitMove(localRegister, valueRegisters[index]);
                }
                else
                {
                    EmitLoadNilRange(localRegister, 1);
                }

                if (attributes.IsToBeClosed)
                {
                    EmitToBeClosed(localRegister, statement.Names.Names[index].Name.Identifier);
                }
            }
        }

        private void CompileGlobalDeclaration(LuaGlobalDeclarationStatementSyntax statement)
        {
            var declarations = CreateGlobalDeclarations(statement.Names);

            if (statement.Initializers.Count > 0)
            {
                var valueRegisters = EvaluateAssignmentValues(
                    statement.Initializers,
                    declarations.Count,
                    statement.Range,
                    index => index < declarations.Count
                        ? declarations[index].Name
                        : null);
                int? nilRegister = null;

                for (var index = declarations.Count - 1; index >= 0; index--)
                {
                    var valueRegister = index < valueRegisters.Count
                        ? valueRegisters[index]
                        : nilRegister ??= LoadNilIntoTemp();

                    EmitGlobalInitializationCheck(declarations[index].Name!, statement.Range);
                    StoreAssignmentTarget(CreateGlobalAssignmentTarget(declarations[index].Name!, statement.Range), valueRegister);
                }
            }

            AddVisibleGlobalDeclarations(declarations);
        }

        private void CompileGlobalWildcard(LuaGlobalWildcardStatementSyntax statement)
        {
            AddVisibleGlobalDeclaration(new GlobalDeclarationInfo(
                Name: null,
                IsReadOnly: GetGlobalAttributeIsReadOnly(statement.Attribute),
                Position: statement.Range.Start));
        }

        private void CompileGlobalFunction(LuaGlobalFunctionStatementSyntax statement)
        {
            AddVisibleGlobalDeclaration(new GlobalDeclarationInfo(
                statement.Name.Identifier,
                IsReadOnly: false,
                Position: statement.Name.Range.Start));

            var functionRegister = AllocateTemp();
            CompileNestedFunction(statement.Body, injectSelf: false, functionRegister, statement.Name.Identifier);
            EmitGlobalInitializationCheck(statement.Name.Identifier, statement.Range);
            StoreAssignmentTarget(CreateGlobalAssignmentTarget(statement.Name.Identifier, statement.Range), functionRegister);
        }

        private void CompileLocalFunction(LuaLocalFunctionStatementSyntax statement)
        {
            var register = AddLocal(statement.Name.Identifier, statement.Name.Range.Start);
            CompileNestedFunction(statement.Body, injectSelf: false, register, statement.Name.Identifier);
        }

        private void CompileNamedFunction(LuaFunctionNameSyntax name, LuaFunctionBodySyntax body, bool injectSelf)
        {
            var target = CreateFunctionAssignmentTarget(name);
            var functionRegister = AllocateTemp();
            CompileNestedFunction(body, injectSelf, functionRegister, GetFunctionDebugName(name));
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
            var valueRegisters = EvaluateAssignmentValues(
                statement.Values,
                statement.Variables.Count,
                statement.Range,
                index => index < statement.Variables.Count
                    ? GetFunctionDebugName(statement.Variables[index])
                    : null);
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
            LuaSourceRange range,
            Func<int, string?>? getFunctionDebugName = null)
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

                CompileExpressionInto(values[index], register, getFunctionDebugName?.Invoke(index));
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
                if (reference.IsReadOnly)
                {
                    throw CreateError(
                        nameExpression.Range,
                        $"attempt to assign to const variable '{nameExpression.Name.Identifier}'");
                }

                return reference.Kind == ReferenceKind.Local
                    ? AssignmentTarget.Local(reference.Index)
                    : AssignmentTarget.Upvalue(reference.Index);
            }

            if (TryResolveExplicitGlobal(nameExpression.Name.Identifier, out var globalDeclaration))
            {
                if (globalDeclaration.IsReadOnly)
                {
                    throw CreateError(
                        nameExpression.Range,
                        $"attempt to assign to const variable '{nameExpression.Name.Identifier}'");
                }

                return CreateGlobalAssignmentTarget(nameExpression.Name.Identifier, nameExpression.Range);
            }

            if (HasActiveExplicitGlobalDeclarations)
            {
                throw CreateError(nameExpression.Range, $"variable '{nameExpression.Name.Identifier}' not declared");
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
                CompileExpressionWithOpenResults(lastExpression, startRegister + statement.Expressions.Count - 1);
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

            if (expression is LuaVarargExpressionSyntax)
            {
                EmitVarArg(targetRegister, resultCount);
                return;
            }

            CompileExpressionInto(expression, targetRegister);
            if (resultCount > 1)
            {
                EmitLoadNilRange(targetRegister + 1, resultCount - 1);
            }
        }

        private void CompileExpressionWithOpenResults(LuaExpressionSyntax expression, int targetRegister)
        {
            ReserveRegisterRange(targetRegister, 1);

            switch (expression)
            {
                case LuaFunctionCallExpressionSyntax functionCall:
                    CompileFunctionCall(functionCall, targetRegister, fixedResultCount: null, openResults: true);
                    return;
                case LuaVarargExpressionSyntax:
                    EmitVarArg(targetRegister, resultCount: null);
                    return;
                default:
                    throw new InvalidOperationException($"Expression '{expression.GetType().Name}' does not support open results.");
            }
        }

        private void CompileExpressionInto(LuaExpressionSyntax expression, int targetRegister, string? functionDebugName = null)
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
                    CompileExpressionInto(parenthesizedExpression.Expression, targetRegister, functionDebugName);
                    return;
                case LuaUnaryExpressionSyntax unaryExpression:
                    CompileUnary(unaryExpression, targetRegister);
                    return;
                case LuaBinaryExpressionSyntax binaryExpression:
                    CompileBinary(binaryExpression, targetRegister);
                    return;
                case LuaFunctionExpressionSyntax functionExpression:
                    CompileNestedFunction(functionExpression.Body, injectSelf: false, targetRegister, functionDebugName);
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
                case LuaVarargExpressionSyntax:
                    EmitVarArg(targetRegister, resultCount: 1);
                    return;
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
            for (var fieldIndex = 0; fieldIndex < expression.Fields.Count; fieldIndex++)
            {
                var field = expression.Fields[fieldIndex];
                var isLastField = fieldIndex == expression.Fields.Count - 1;

                switch (field)
                {
                    case LuaExpressionTableFieldSyntax expressionField when isLastField && IsMultiResultExpression(expressionField.Expression):
                        CompileOpenSequentialTableField(targetRegister, arrayIndex, expressionField.Expression);
                        break;
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

        private void CompileOpenSequentialTableField(int tableRegister, int arrayIndex, LuaExpressionSyntax value)
        {
            CompileExpressionWithOpenResults(value, tableRegister + 1);
            EmitSetList(tableRegister, elementCount: 0, startIndex: arrayIndex - 1);
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
            CompileExpressionInto(value, valueRegister, name);

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
            CompileExpressionInto(valueExpression, valueRegister, GetFunctionDebugName(keyExpression));
            EmitSetTable(tableRegister, keyRegister, valueRegister);
        }

        private void CompileNameRead(string name, LuaSourceRange range, int targetRegister)
        {
            if (TryResolveLexicalName(name, out var reference))
            {
                EmitReferenceRead(reference, targetRegister);
                return;
            }

            if (TryResolveExplicitGlobal(name, out _))
            {
                EmitGlobalRead(name, range, targetRegister);
                return;
            }

            if (HasActiveExplicitGlobalDeclarations)
            {
                throw CreateError(range, $"variable '{name}' not declared");
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
                    reference = new Reference(
                        ReferenceKind.Local,
                        _visibleLocals[index].Register,
                        _visibleLocals[index].IsReadOnly);
                    return true;
                }
            }

            if (_upvalueIndices.TryGetValue(name, out var upvalueIndex))
            {
                reference = new Reference(
                    ReferenceKind.Upvalue,
                    upvalueIndex,
                    _upvalues[upvalueIndex].IsReadOnly);
                return true;
            }

            if (_parent is not null && _parent.TryResolveChildCapture(name, out var captureSource))
            {
                var captureIndex = GetOrAddUpvalue(
                    name,
                    captureSource.InStack,
                    captureSource.Index,
                    captureSource.IsReadOnly);
                reference = new Reference(ReferenceKind.Upvalue, captureIndex, captureSource.IsReadOnly);
                return true;
            }

            if (_parent is null && StringComparer.Ordinal.Equals(name, EnvironmentName))
            {
                var environmentIndex = GetOrAddUpvalue(EnvironmentName, inStack: 0, index: 0, isReadOnly: false);
                reference = new Reference(ReferenceKind.Upvalue, environmentIndex, IsReadOnly: false);
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
                    _visibleLocals[index].MarkCaptured();
                    TrackScopeClose(_visibleLocals[index].Scope, _visibleLocals[index].Register);
                    capture = new CaptureSource(
                        InStack: 1,
                        Index: checked((byte)_visibleLocals[index].Register),
                        IsReadOnly: _visibleLocals[index].IsReadOnly);
                    return true;
                }
            }

            if (_upvalueIndices.TryGetValue(name, out var existingUpvalueIndex))
            {
                capture = new CaptureSource(
                    InStack: 0,
                    Index: checked((byte)existingUpvalueIndex),
                    IsReadOnly: _upvalues[existingUpvalueIndex].IsReadOnly);
                return true;
            }

            if (_parent is not null && _parent.TryResolveChildCapture(name, out var parentCapture))
            {
                var upvalueIndex = GetOrAddUpvalue(
                    name,
                    parentCapture.InStack,
                    parentCapture.Index,
                    parentCapture.IsReadOnly);
                capture = new CaptureSource(
                    InStack: 0,
                    Index: checked((byte)upvalueIndex),
                    IsReadOnly: parentCapture.IsReadOnly);
                return true;
            }

            if (_parent is null && StringComparer.Ordinal.Equals(name, EnvironmentName))
            {
                var upvalueIndex = GetOrAddUpvalue(EnvironmentName, inStack: 0, index: 0, isReadOnly: false);
                capture = new CaptureSource(InStack: 0, Index: checked((byte)upvalueIndex), IsReadOnly: false);
                return true;
            }

            capture = default;
            return false;
        }

        private bool TryResolveExplicitGlobal(string name, out GlobalDeclarationInfo declaration)
        {
            for (var index = _visibleGlobalDeclarations.Count - 1; index >= 0; index--)
            {
                var candidate = _visibleGlobalDeclarations[index];
                if (candidate.Name is not null && StringComparer.Ordinal.Equals(candidate.Name, name))
                {
                    declaration = candidate;
                    return true;
                }
            }

            for (var index = _visibleGlobalDeclarations.Count - 1; index >= 0; index--)
            {
                var candidate = _visibleGlobalDeclarations[index];
                if (candidate.Name is null)
                {
                    declaration = candidate;
                    return true;
                }
            }

            if (_parent is not null && _parent.TryResolveExplicitGlobal(name, out declaration))
            {
                return true;
            }

            declaration = default!;
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

        private int AddLocal(
            string name,
            LuaSourcePosition position,
            bool isReadOnly = false,
            bool isToBeClosed = false)
        {
            var register = AllocatePersistentRegister();
            var local = new LocalInfo(name, register, position, isReadOnly, isToBeClosed, CurrentScope);
            _visibleLocals.Add(local);
            CurrentScope.Declarations.Add(ScopeDeclarationInfo.ForLocal(local));
            if (isToBeClosed)
            {
                TrackScopeClose(CurrentScope, register);
            }

            return register;
        }

        private void AddExistingLocal(
            string name,
            int register,
            LuaSourcePosition position,
            bool isReadOnly = false,
            bool isToBeClosed = false)
        {
            var local = new LocalInfo(name, register, position, isReadOnly, isToBeClosed, CurrentScope);
            _visibleLocals.Add(local);
            CurrentScope.Declarations.Add(ScopeDeclarationInfo.ForLocal(local));
            if (isToBeClosed)
            {
                TrackScopeClose(CurrentScope, register);
            }
        }

        private void AddVisibleGlobalDeclarations(IReadOnlyList<GlobalDeclarationInfo> declarations)
        {
            _visibleGlobalDeclarations.AddRange(declarations);
            foreach (var declaration in declarations)
            {
                CurrentScope.Declarations.Add(ScopeDeclarationInfo.ForGlobal(declaration.Name ?? "*"));
            }
        }

        private void AddVisibleGlobalDeclaration(GlobalDeclarationInfo declaration)
        {
            _visibleGlobalDeclarations.Add(declaration);
            CurrentScope.Declarations.Add(ScopeDeclarationInfo.ForGlobal(declaration.Name ?? "*"));
        }

        private List<GlobalDeclarationInfo> CreateGlobalDeclarations(LuaDeclarationNameListSyntax declaration)
        {
            var declarations = new List<GlobalDeclarationInfo>(declaration.Names.Count);
            foreach (var name in declaration.Names)
            {
                declarations.Add(new GlobalDeclarationInfo(
                    name.Name.Identifier,
                    GetGlobalAttributeIsReadOnly(declaration.LeadingAttribute) ||
                        GetGlobalAttributeIsReadOnly(name.Attribute),
                    name.Range.Start));
            }

            return declarations;
        }

        private bool GetGlobalAttributeIsReadOnly(LuaVariableAttributeSyntax? attribute)
        {
            if (attribute is null)
            {
                return false;
            }

            return attribute.Kind switch
            {
                LuaVariableAttributeKind.Const => true,
                LuaVariableAttributeKind.Close => throw CreateError(attribute.Range, "global variables cannot be to-be-closed"),
                _ => throw CreateUnsupported(attribute, "unsupported global variable attribute")
            };
        }

        private static LocalAttributes GetLocalAttributes(
            LuaVariableAttributeSyntax? leadingAttribute,
            LuaVariableAttributeSyntax? attribute)
        {
            var effectiveAttribute = attribute?.Kind ?? leadingAttribute?.Kind;
            return effectiveAttribute switch
            {
                LuaVariableAttributeKind.Const => new LocalAttributes(IsReadOnly: true, IsToBeClosed: false),
                LuaVariableAttributeKind.Close => new LocalAttributes(IsReadOnly: false, IsToBeClosed: true),
                _ => default
            };
        }

        private void EmitGlobalInitializationCheck(string name, LuaSourceRange range)
        {
            var currentValueRegister = AllocateTemp();
            EmitGlobalRead(name, range, currentValueRegister);
            EmitErrNNil(currentValueRegister, name);
        }

        private void CompileNestedFunction(
            LuaFunctionBodySyntax body,
            bool injectSelf,
            int targetRegister,
            string? debugName = null)
        {
            var compiler = new FunctionCompiler(_sourceName, this, debugName);
            var prototype = compiler.CompileFunction(body, injectSelf);
            var prototypeIndex = _nestedPrototypes.Count;
            _nestedPrototypes.Add(prototype);
            EmitClosure(targetRegister, prototypeIndex);
        }

        private static string GetFunctionDebugName(LuaFunctionNameSyntax name)
        {
            return name.MethodName?.Identifier ?? name.Segments[^1].Identifier;
        }

        private static string? GetFunctionDebugName(LuaVariableExpressionSyntax variable)
        {
            return variable switch
            {
                LuaNameExpressionSyntax nameExpression => nameExpression.Name.Identifier,
                LuaMemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier,
                LuaIndexExpressionSyntax { Index: LuaStringLiteralExpressionSyntax stringLiteral } => stringLiteral.Value,
                _ => null
            };
        }

        private static string? GetFunctionDebugName(LuaExpressionSyntax expression)
        {
            return expression switch
            {
                LuaStringLiteralExpressionSyntax stringLiteral => stringLiteral.Value,
                _ => null
            };
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
                    CompileExpressionWithOpenResults(argument, currentRegister);
                    return 0;
                }

                CompileExpressionInto(argument, currentRegister);
                currentRegister += 1;
            }

            return arguments.Arguments.Count + 1;
        }

        private static bool IsMultiResultExpression(LuaExpressionSyntax expression)
        {
            return expression is LuaFunctionCallExpressionSyntax or LuaVarargExpressionSyntax;
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

        private bool HasActiveExplicitGlobalDeclarations =>
            _visibleGlobalDeclarations.Count > 0 ||
            (_parent?.HasActiveExplicitGlobalDeclarations ?? false);

        private ScopeInfo CurrentScope => _scopes[^1];

        private void EnterScope()
        {
            _scopes.Add(new ScopeInfo(_visibleLocals.Count, _visibleGlobalDeclarations.Count, _persistentRegisterCount));
        }

        private void ExitScope(bool emitClose = true)
        {
            var scope = _scopes[^1];
            _scopes.RemoveAt(_scopes.Count - 1);

            if (emitClose && scope.FirstCloseRegister is int closeRegister)
            {
                EmitClose(closeRegister);
            }

            var start = scope.LocalStart;
            if (start < _visibleLocals.Count)
            {
                _visibleLocals.RemoveRange(start, _visibleLocals.Count - start);
            }

            var globalStart = scope.GlobalStart;
            if (globalStart < _visibleGlobalDeclarations.Count)
            {
                _visibleGlobalDeclarations.RemoveRange(globalStart, _visibleGlobalDeclarations.Count - globalStart);
            }

            _persistentRegisterCount = scope.PersistentRegisterStart;
            _visibleLabels.RemoveAll(label => ReferenceEquals(label.Scope, scope));
        }

        private int? GetCurrentScopeCloseRegister()
        {
            return _scopes.Count == 0 ? null : _scopes[^1].FirstCloseRegister;
        }

        private void ResetTemps()
        {
            if (_tempRegisterTop > _persistentRegisterCount)
            {
                EmitLoadNilRange(_persistentRegisterCount, _tempRegisterTop - _persistentRegisterCount);
            }

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

        private int AllocatePersistentRegister()
        {
            var register = _persistentRegisterCount;
            _persistentRegisterCount++;
            _tempRegisterTop = Math.Max(_tempRegisterTop, _persistentRegisterCount);
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

        private int GetOrAddUpvalue(string name, byte inStack, byte index, bool isReadOnly)
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
            _upvalues.Add(new UpvalueInfo(name, inStack, index, isReadOnly));
            return upvalueIndex;
        }

        private void TrackScopeClose(ScopeInfo scope, int register)
        {
            scope.FirstCloseRegister = scope.FirstCloseRegister is null
                ? register
                : Math.Min(scope.FirstCloseRegister.Value, register);
        }

        private void EmitBreakScopeClose(LoopContext loop)
        {
            int? closeRegister = null;
            var loopScopeIndex = _scopes.FindIndex(scope => ReferenceEquals(scope, loop.Scope));
            if (loopScopeIndex < 0)
            {
                throw new InvalidOperationException("Active loop scope was not found in the current scope stack.");
            }

            for (var index = loopScopeIndex; index < _scopes.Count; index++)
            {
                if (_scopes[index].FirstCloseRegister is not int candidate)
                {
                    continue;
                }

                closeRegister = closeRegister is null
                    ? candidate
                    : Math.Min(closeRegister.Value, candidate);
            }

            if (closeRegister is int register)
            {
                EmitClose(register);
            }
        }

        private ScopeSnapshotEntry[] CaptureScopeStates()
        {
            var states = new ScopeSnapshotEntry[_scopes.Count];
            for (var index = 0; index < _scopes.Count; index++)
            {
                states[index] = new ScopeSnapshotEntry(_scopes[index], _scopes[index].Declarations.Count);
            }

            return states;
        }

        private LoopContext[] CaptureLoopStates()
        {
            return [.. _loops];
        }

        private bool TryGetVisibleLabel(string name, out LabelInfo label)
        {
            for (var index = _visibleLabels.Count - 1; index >= 0; index--)
            {
                if (StringComparer.Ordinal.Equals(_visibleLabels[index].Name, name))
                {
                    label = _visibleLabels[index];
                    return true;
                }
            }

            label = null!;
            return false;
        }

        private void ResolvePendingGotos(LabelInfo label)
        {
            for (var index = _pendingGotos.Count - 1; index >= 0; index--)
            {
                var pendingGoto = _pendingGotos[index];
                if (!StringComparer.Ordinal.Equals(pendingGoto.Name, label.Name) ||
                    !CanTargetLabel(pendingGoto.ScopeStates, label.ScopeStates))
                {
                    continue;
                }

                ResolveGoto(pendingGoto, label);
                _pendingGotos.RemoveAt(index);
            }
        }

        private void ResolveGoto(PendingGotoInfo pendingGoto, LabelInfo label)
        {
            EnsureGotoDoesNotEnterScope(pendingGoto, label);
            PatchGotoClose(pendingGoto.OptionalCloseProgramCounter, GetGotoCloseRegister(pendingGoto, label));
            PatchJump(pendingGoto.JumpProgramCounter, label.ProgramCounter);
        }

        private void PatchGotoClose(int programCounter, int? closeRegister)
        {
            if (closeRegister is null)
            {
                return;
            }

            _code[programCounter] = EncodeAbc(LuaOpcode.Close, closeRegister.Value, 0, 0);
        }

        private static bool CanTargetLabel(
            IReadOnlyList<ScopeSnapshotEntry> sourceScopes,
            IReadOnlyList<ScopeSnapshotEntry> targetScopes)
        {
            if (targetScopes.Count > sourceScopes.Count)
            {
                return false;
            }

            for (var index = 0; index < targetScopes.Count; index++)
            {
                if (!ReferenceEquals(sourceScopes[index].Scope, targetScopes[index].Scope))
                {
                    return false;
                }
            }

            return true;
        }

        private void EnsureGotoDoesNotEnterScope(PendingGotoInfo pendingGoto, LabelInfo label)
        {
            for (var index = 0; index < label.ScopeStates.Count; index++)
            {
                var sourceState = pendingGoto.ScopeStates[index];
                var targetState = label.ScopeStates[index];
                if (targetState.DeclarationCount <= sourceState.DeclarationCount)
                {
                    continue;
                }

                var declaration = targetState.Scope.Declarations[sourceState.DeclarationCount];
                throw CreateError(
                    pendingGoto.Position,
                    $"goto '{pendingGoto.Name}' jumps into the scope of '{declaration.Name}'");
            }
        }

        private int? GetGotoCloseRegister(PendingGotoInfo pendingGoto, LabelInfo label)
        {
            int? closeRegister = null;
            for (var index = 0; index < label.ScopeStates.Count; index++)
            {
                var sourceCount = pendingGoto.ScopeStates[index].DeclarationCount;
                var targetCount = label.ScopeStates[index].DeclarationCount;
                closeRegister = MinRegister(
                    closeRegister,
                    GetDeclarationCloseRegister(
                        pendingGoto.ScopeStates[index].Scope,
                        targetCount,
                        sourceCount));
            }

            for (var index = label.ScopeStates.Count; index < pendingGoto.ScopeStates.Count; index++)
            {
                var sourceState = pendingGoto.ScopeStates[index];
                closeRegister = MinRegister(
                    closeRegister,
                    GetDeclarationCloseRegister(
                        sourceState.Scope,
                        0,
                        sourceState.DeclarationCount));
            }

            var commonLoopCount = GetCommonPrefixCount(pendingGoto.Loops, label.Loops);
            for (var index = commonLoopCount; index < pendingGoto.Loops.Count; index++)
            {
                closeRegister = MinRegister(closeRegister, pendingGoto.Loops[index].ExitCloseRegister);
            }

            return closeRegister;
        }

        private static int GetCommonPrefixCount(
            IReadOnlyList<LoopContext> sourceLoops,
            IReadOnlyList<LoopContext> targetLoops)
        {
            var commonCount = Math.Min(sourceLoops.Count, targetLoops.Count);
            var index = 0;
            while (index < commonCount && ReferenceEquals(sourceLoops[index], targetLoops[index]))
            {
                index++;
            }

            return index;
        }

        private static int? GetDeclarationCloseRegister(ScopeInfo scope, int startIndex, int endExclusive)
        {
            int? closeRegister = null;
            for (var index = startIndex; index < endExclusive; index++)
            {
                closeRegister = MinRegister(closeRegister, scope.Declarations[index].GetCloseRegister());
            }

            return closeRegister;
        }

        private static int? MinRegister(int? current, int? candidate)
        {
            if (candidate is null)
            {
                return current;
            }

            return current is null ? candidate : Math.Min(current.Value, candidate.Value);
        }

        private void EnsureAllGotosResolved()
        {
            if (_pendingGotos.Count == 0)
            {
                return;
            }

            var pendingGoto = _pendingGotos[0];
            throw CreateError(pendingGoto.Position, $"no visible label '{pendingGoto.Name}'");
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

        private void AddInstruction(uint instruction)
        {
            _code.Add(instruction);
            _registerTopHints.Add(checked((byte)_tempRegisterTop));
        }

        private int EmitJumpPlaceholder()
        {
            var programCounter = _code.Count;
            AddInstruction(EncodeSJ(LuaOpcode.Jmp, 0));
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
            AddInstruction(EncodeSJ(LuaOpcode.Jmp, offset));
        }

        private int EmitForPrepPlaceholder(int registerIndex)
        {
            var programCounter = CurrentProgramCounter;
            AddInstruction(EncodeAbx(LuaOpcode.ForPrep, registerIndex, 0));
            return programCounter;
        }

        private void PatchForPrep(int prepProgramCounter, int registerIndex, int forLoopProgramCounter)
        {
            var offset = forLoopProgramCounter - (prepProgramCounter + 1);
            _code[prepProgramCounter] = EncodeAbx(LuaOpcode.ForPrep, registerIndex, offset);
        }

        private int EmitForLoop(int registerIndex, int targetProgramCounter)
        {
            var programCounter = CurrentProgramCounter;
            var offset = (programCounter + 1) - targetProgramCounter;
            AddInstruction(EncodeAbx(LuaOpcode.ForLoop, registerIndex, offset));
            return programCounter;
        }

        private int EmitTForPrepPlaceholder(int registerIndex)
        {
            var programCounter = CurrentProgramCounter;
            AddInstruction(EncodeAbx(LuaOpcode.TForPrep, registerIndex, 0));
            return programCounter;
        }

        private void PatchTForPrep(int prepProgramCounter, int registerIndex, int callProgramCounter)
        {
            var offset = callProgramCounter - (prepProgramCounter + 1);
            _code[prepProgramCounter] = EncodeAbx(LuaOpcode.TForPrep, registerIndex, offset);
        }

        private int EmitTForCall(int registerIndex, int resultCount)
        {
            var programCounter = CurrentProgramCounter;
            AddInstruction(EncodeAbc(LuaOpcode.TForCall, registerIndex, 0, resultCount));
            return programCounter;
        }

        private int EmitTForLoop(int registerIndex, int targetProgramCounter)
        {
            var programCounter = CurrentProgramCounter;
            var offset = (programCounter + 1) - targetProgramCounter;
            AddInstruction(EncodeAbx(LuaOpcode.TForLoop, registerIndex, offset));
            return programCounter;
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
            AddInstruction(EncodeAbx(LuaOpcode.LoadK, targetRegister, constantIndex));
        }

        private void EmitLoadNilRange(int startRegister, int count)
        {
            for (var remaining = count; remaining > 0; remaining -= byte.MaxValue + 1)
            {
                var chunk = Math.Min(remaining, byte.MaxValue + 1);
                AddInstruction(EncodeAbc(LuaOpcode.LoadNil, startRegister, chunk - 1, 0));
                startRegister += chunk;
            }
        }

        private void EmitBoolean(int targetRegister, bool value)
        {
            AddInstruction(EncodeAbc(value ? LuaOpcode.LoadTrue : LuaOpcode.LoadFalse, targetRegister, 0, 0));
        }

        private void EmitMove(int targetRegister, int sourceRegister)
        {
            if (targetRegister == sourceRegister)
            {
                return;
            }

            AddInstruction(EncodeAbc(LuaOpcode.Move, targetRegister, sourceRegister, 0));
        }

        private void EmitGetUpValue(int targetRegister, int upvalueIndex)
        {
            AddInstruction(EncodeAbc(LuaOpcode.GetUpVal, targetRegister, upvalueIndex, 0));
        }

        private void EmitSetUpValue(int sourceRegister, int upvalueIndex)
        {
            AddInstruction(EncodeAbc(LuaOpcode.SetUpVal, sourceRegister, upvalueIndex, 0));
        }

        private void EmitGetTabUp(int targetRegister, int upvalueIndex, int constantIndex)
        {
            AddInstruction(EncodeAbc(LuaOpcode.GetTabUp, targetRegister, upvalueIndex, constantIndex));
        }

        private void EmitGetField(int targetRegister, int tableRegister, int constantIndex)
        {
            AddInstruction(EncodeAbc(LuaOpcode.GetField, targetRegister, tableRegister, constantIndex));
        }

        private void EmitGetTable(int targetRegister, int tableRegister, int keyRegister)
        {
            AddInstruction(EncodeAbc(LuaOpcode.GetTable, targetRegister, tableRegister, keyRegister));
        }

        private void EmitSetTabUp(int upvalueIndex, int constantIndex, int valueRegister)
        {
            AddInstruction(EncodeAbc(LuaOpcode.SetTabUp, upvalueIndex, constantIndex, valueRegister));
        }

        private void EmitSetField(int tableRegister, int constantIndex, int valueRegister)
        {
            AddInstruction(EncodeAbc(LuaOpcode.SetField, tableRegister, constantIndex, valueRegister));
        }

        private void EmitSetTable(int tableRegister, int keyRegister, int valueRegister)
        {
            AddInstruction(EncodeAbc(LuaOpcode.SetTable, tableRegister, keyRegister, valueRegister));
        }

        private void EmitErrNNil(int registerIndex, string globalName)
        {
            var constantIndex = AddConstant(LuaConstant.FromString(globalName));
            AddInstruction(EncodeAbx(LuaOpcode.ErrNNil, registerIndex, constantIndex + 1));
        }

        private void EmitSetIntegerKey(int tableRegister, int integerKey, int valueRegister)
        {
            AddInstruction(EncodeAbc(LuaOpcode.SetI, tableRegister, integerKey, valueRegister));
        }

        private void EmitNewTable(int targetRegister)
        {
            AddInstruction(EncodeAVbc(LuaOpcode.NewTable, targetRegister, 0, 0));
            AddInstruction(EncodeAx(LuaOpcode.ExtraArg, 0));
        }

        private void EmitSetList(int tableRegister, int elementCount, int startIndex)
        {
            if (elementCount < 0)
            {
                throw new InvalidOperationException("SETLIST element count cannot be negative.");
            }

            if (startIndex < 0)
            {
                throw new InvalidOperationException("SETLIST start index cannot be negative.");
            }

            if (elementCount > LuaInstructionLayout.MaxArgVB)
            {
                throw CreateError(_fallbackPosition, "table constructor has too many pending list elements");
            }

            var extraArg = startIndex / (LuaInstructionLayout.MaxArgVC + 1);
            var encodedStartIndex = startIndex % (LuaInstructionLayout.MaxArgVC + 1);
            var usesExtraArg = extraArg != 0;

            AddInstruction(EncodeAVbc(LuaOpcode.SetList, tableRegister, elementCount, encodedStartIndex, usesExtraArg ? 1 : 0));
            if (usesExtraArg)
            {
                AddInstruction(EncodeAx(LuaOpcode.ExtraArg, extraArg));
            }
        }

        private void EmitSelf(int targetRegister, int receiverRegister, int constantIndex)
        {
            AddInstruction(EncodeAbc(LuaOpcode.Self, targetRegister, receiverRegister, constantIndex));
        }

        private void EmitBinary(LuaOpcode opcode, int targetRegister, int leftRegister, int rightRegister)
        {
            AddInstruction(EncodeAbc(opcode, targetRegister, leftRegister, rightRegister));
        }

        private void EmitUnary(LuaOpcode opcode, int targetRegister, int operandRegister)
        {
            AddInstruction(EncodeAbc(opcode, targetRegister, operandRegister, 0));
        }

        private void EmitConcat(int startRegister, int operandCount)
        {
            AddInstruction(EncodeAbc(LuaOpcode.Concat, startRegister, operandCount, 0));
        }

        private void EmitComparisonBoolean(LuaOpcode opcode, int leftRegister, int rightRegister, bool expected, int? targetRegister = null)
        {
            var resultRegister = targetRegister ?? leftRegister;
            AddInstruction(EncodeAbc(opcode, leftRegister, rightRegister, 0, expected ? 1 : 0));
            var trueJump = EmitJumpPlaceholder();
            EmitBoolean(resultRegister, false);
            var endJump = EmitJumpPlaceholder();
            PatchJump(trueJump, CurrentProgramCounter);
            EmitBoolean(resultRegister, true);
            PatchJump(endJump, CurrentProgramCounter);
        }

        private void EmitTest(int registerIndex, bool expectedTruthy)
        {
            AddInstruction(EncodeAbc(LuaOpcode.Test, registerIndex, 0, 0, expectedTruthy ? 1 : 0));
        }

        private void EmitCall(int functionRegister, int functionAndArgumentCount, int resultOperand)
        {
            AddInstruction(EncodeAbc(LuaOpcode.Call, functionRegister, functionAndArgumentCount, resultOperand));
        }

        private void EmitClose(int registerIndex)
        {
            AddInstruction(EncodeAbc(LuaOpcode.Close, registerIndex, 0, 0));
        }

        private void EmitToBeClosed(int registerIndex, string? variableName = null)
        {
            if (!string.IsNullOrEmpty(variableName))
            {
                _toBeClosedNames[_code.Count] = variableName;
            }

            AddInstruction(EncodeAbc(LuaOpcode.Tbc, registerIndex, 0, 0));
        }

        private void EmitVarArg(int targetRegister, int? resultCount)
        {
            var resultOperand = resultCount is null ? 0 : resultCount.Value + 1;
            AddInstruction(EncodeAbc(LuaOpcode.VarArg, targetRegister, 0, resultOperand));
        }

        private void PatchVarArgInstructionsForTable(byte flags, int varargTableRegister)
        {
            if ((flags & VarArgTableFlag) == 0)
            {
                return;
            }

            for (var i = 0; i < _code.Count; i++)
            {
                var instruction = LuaInstruction.FromRaw(_code[i]);
                if (instruction.Opcode == LuaOpcode.VarArg)
                {
                    _code[i] = EncodeAbcWithK(LuaOpcode.VarArg, instruction.A, varargTableRegister, instruction.C, k: 1);
                }
            }
        }

        private static uint EncodeAbcWithK(LuaOpcode opcode, int a, int b, int c, int k)
        {
            return (uint)((int)opcode | (a << 7) | (k << 15) | (b << 16) | (c << 24));
        }

        private void EmitVarArgPrep()
        {
            AddInstruction(EncodeAbc(LuaOpcode.VarArgPrep, 0, 0, 0));
        }

        private void EmitReturn(int startRegister, int resultCount)
        {
            AddInstruction(EncodeAbc(LuaOpcode.Return, startRegister, resultCount + 1, 0));
        }

        private void EmitReturnOpen(int startRegister)
        {
            AddInstruction(EncodeAbc(LuaOpcode.Return, startRegister, 0, 0));
        }

        private void EmitReturn0()
        {
            AddInstruction(EncodeAbc(LuaOpcode.Return0, 0, 0, 0));
        }

        private void EmitReturn1(int registerIndex)
        {
            AddInstruction(EncodeAbc(LuaOpcode.Return1, registerIndex, 0, 0));
        }

        private void EmitClosure(int targetRegister, int prototypeIndex)
        {
            AddInstruction(EncodeAbx(LuaOpcode.Closure, targetRegister, prototypeIndex));
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

        private static uint EncodeAVbc(LuaOpcode opcode, int a, int vb, int vc, int k = 0)
        {
            return
                ((uint)opcode << LuaInstructionLayout.PosOp) |
                ((uint)a << LuaInstructionLayout.PosA) |
                ((uint)k << LuaInstructionLayout.PosK) |
                ((uint)vb << LuaInstructionLayout.PosVB) |
                ((uint)vc << LuaInstructionLayout.PosVC);
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

        private readonly record struct Reference(ReferenceKind Kind, int Index, bool IsReadOnly);

        private enum ReferenceKind
        {
            Local,
            Upvalue
        }

        private sealed class LocalInfo(
            string name,
            int register,
            LuaSourcePosition position,
            bool isReadOnly,
            bool isToBeClosed,
            ScopeInfo scope)
        {
            public string Name { get; } = name;

            public int Register { get; } = register;

            public LuaSourcePosition Position { get; } = position;

            public bool IsReadOnly { get; } = isReadOnly;

            public bool IsToBeClosed { get; } = isToBeClosed;

            public ScopeInfo Scope { get; } = scope;

            public bool IsCaptured { get; private set; }

            public bool NeedsClose => IsToBeClosed || IsCaptured;

            public void MarkCaptured()
            {
                IsCaptured = true;
            }
        }

        private sealed record GlobalDeclarationInfo(string? Name, bool IsReadOnly, LuaSourcePosition Position);

        private sealed record UpvalueInfo(string Name, byte InStack, byte Index, bool IsReadOnly);

        private readonly record struct CaptureSource(byte InStack, byte Index, bool IsReadOnly);

        private readonly record struct LocalAttributes(bool IsReadOnly, bool IsToBeClosed);

        private sealed class ScopeInfo(int localStart, int globalStart, int persistentRegisterStart)
        {
            public int LocalStart { get; } = localStart;

            public int GlobalStart { get; } = globalStart;

            public int PersistentRegisterStart { get; } = persistentRegisterStart;

            public List<ScopeDeclarationInfo> Declarations { get; } = [];

            public int? FirstCloseRegister { get; set; }
        }

        private sealed class LoopContext(ScopeInfo scope, int? exitCloseRegister = null)
        {
            public ScopeInfo Scope { get; } = scope;

            public int? ExitCloseRegister { get; } = exitCloseRegister;

            public List<int> BreakJumps { get; } = [];
        }

        private sealed class ScopeDeclarationInfo(string name, LocalInfo? local)
        {
            public string Name { get; } = name;

            public LocalInfo? Local { get; } = local;

            public static ScopeDeclarationInfo ForLocal(LocalInfo local)
            {
                return new ScopeDeclarationInfo(local.Name, local);
            }

            public static ScopeDeclarationInfo ForGlobal(string name)
            {
                return new ScopeDeclarationInfo(name, local: null);
            }

            public int? GetCloseRegister()
            {
                return Local?.NeedsClose == true ? Local.Register : null;
            }
        }

        private readonly record struct ScopeSnapshotEntry(ScopeInfo Scope, int DeclarationCount);

        private sealed class LabelInfo(
            string name,
            LuaSourcePosition position,
            int programCounter,
            IReadOnlyList<ScopeSnapshotEntry> scopeStates,
            IReadOnlyList<LoopContext> loops,
            ScopeInfo scope)
        {
            public string Name { get; } = name;

            public LuaSourcePosition Position { get; } = position;

            public int ProgramCounter { get; } = programCounter;

            public IReadOnlyList<ScopeSnapshotEntry> ScopeStates { get; } = scopeStates;

            public IReadOnlyList<LoopContext> Loops { get; } = loops;

            public ScopeInfo Scope { get; } = scope;
        }

        private sealed class PendingGotoInfo(
            string name,
            LuaSourcePosition position,
            int optionalCloseProgramCounter,
            int jumpProgramCounter,
            IReadOnlyList<ScopeSnapshotEntry> scopeStates,
            IReadOnlyList<LoopContext> loops)
        {
            public string Name { get; } = name;

            public LuaSourcePosition Position { get; } = position;

            public int OptionalCloseProgramCounter { get; } = optionalCloseProgramCounter;

            public int JumpProgramCounter { get; } = jumpProgramCounter;

            public IReadOnlyList<ScopeSnapshotEntry> ScopeStates { get; } = scopeStates;

            public IReadOnlyList<LoopContext> Loops { get; } = loops;
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
