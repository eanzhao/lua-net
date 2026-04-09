using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text;
using Lua.Bytecode.Chunks;
using Lua.Bytecode.Instructions;
using Lua.Runtime.Execution;
using Lua.Runtime.Objects;
using Lua.Runtime.Values;
using Lua.VM.Closures;

namespace Lua.VM;

public sealed class LuaVirtualMachine
{
    private const byte VarArgFlagMask = 0b00000011;
    private const byte VarArgTableFlag = 0b00000010;
    private const int LengthMetamethodEvent = 4;
    private const int EqualityMetamethodEvent = 5;
    private const int UnaryMinusMetamethodEvent = 18;
    private const int BitwiseNotMetamethodEvent = 19;
    private const int LessThanMetamethodEvent = 20;
    private const int LessEqualMetamethodEvent = 21;
    private const int ConcatMetamethodEvent = 22;
    private const int CallMetamethodEvent = 23;
    private const int MaxCallMetamethodDepth = 32;
    private static readonly string[] MetamethodNames =
    [
        "__index", "__newindex",
        "__gc", "__mode", "__len", "__eq",
        "__add", "__sub", "__mul", "__mod", "__pow",
        "__div", "__idiv",
        "__band", "__bor", "__bxor", "__shl", "__shr",
        "__unm", "__bnot", "__lt", "__le",
        "__concat", "__call", "__close"
    ];

    public LuaVirtualMachine()
    {
        State = new LuaState();
    }

    public LuaState State { get; }

    public LuaValue[] Execute(LuaChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        var closure = CreateClosure(chunk.MainFunction);
        return Call(closure);
    }

    public LuaValue[] Call(LuaClosure closure, IReadOnlyList<LuaValue>? arguments = null)
    {
        ArgumentNullException.ThrowIfNull(closure);

        var actualArguments = arguments ?? Array.Empty<LuaValue>();
        return closure.Body switch
        {
            LuaBytecodeClosureBody body => ExecuteClosure(closure, body.Prototype, actualArguments),
            LuaNativeClosureBody body => body.Function(State, closure, actualArguments),
            null => throw new InvalidOperationException("The closure does not contain an executable body."),
            _ => throw new InvalidOperationException($"Unsupported closure body type '{closure.Body.GetType().Name}'.")
        };
    }

    public LuaClosure CreateClosure(LuaPrototype prototype, string? debugName = null)
    {
        ArgumentNullException.ThrowIfNull(prototype);

        return new LuaClosure(
            debugName ?? GetDebugName(prototype),
            prototype.Upvalues.Length,
            new LuaBytecodeClosureBody(prototype),
            BuildUpvalues(prototype, parentFrame: null));
    }

    private LuaValue[] ExecuteClosure(LuaClosure closure, LuaPrototype prototype, IReadOnlyList<LuaValue> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var baseIndex = State.Stack.Count;
        var fixedArgumentCount = Math.Min(arguments.Count, prototype.NumberOfParameters);
        var frameSize = Math.Max(prototype.MaxStackSize, prototype.NumberOfParameters);

        State.Stack.SetTop(baseIndex + frameSize);
        InitializeRegisters(baseIndex, arguments, fixedArgumentCount);

        var frame = new CallFrame(
            closure,
            baseIndex,
            expectedResults: 0,
            registerTop: fixedArgumentCount,
            varargs: GetVarargs(prototype, arguments, fixedArgumentCount));
        State.PushFrame(frame);
        LuaValue[] results = [];
        Exception? pendingException = null;

        try
        {
            results = RunClosure(frame, prototype);
        }
        catch (Exception ex)
        {
            pendingException = ex;
        }
        finally
        {
            try
            {
                pendingException = CloseResourcesFrom(frame, 0, pendingException);
            }
            finally
            {
                State.PopFrame();
                State.Stack.SetTop(baseIndex);
            }
        }

        if (pendingException is not null)
        {
            ExceptionDispatchInfo.Capture(pendingException).Throw();
        }

        return results;
    }

    private void ExecuteAddImmediate(CallFrame frame, LuaPrototype prototype, LuaInstruction instruction)
    {
        var left = GetRegister(frame, instruction.B);
        var right = LuaValue.FromInteger(ToSignedC(instruction.C));

        ExecuteBinaryArithmetic(frame, prototype, instruction, left, right, TryAdd);
    }

    private void ExecuteBinaryArithmetic(
        CallFrame frame,
        LuaPrototype prototype,
        LuaInstruction instruction,
        LuaValue left,
        LuaValue right,
        Func<LuaValue, LuaValue, (bool Success, LuaValue Result)> operation)
    {
        var (success, result) = operation(left, right);
        if (!success)
        {
            if (HasFollowingMetamethodInstruction(frame, prototype))
            {
                return;
            }

            throw new NotSupportedException("Arithmetic metamethod dispatch is not implemented yet.");
        }

        SetRegister(frame, instruction.A, result);
        SkipMetamethodInstructionIfPresent(frame, prototype);
    }

    private void ExecuteUnaryArithmetic(
        CallFrame frame,
        LuaInstruction instruction,
        Func<LuaValue, (bool Success, LuaValue Result)> operation,
        int metamethodEvent)
    {
        var operand = GetRegister(frame, instruction.B);
        var (success, result) = operation(operand);
        if (!success)
        {
            result = CallBinaryMetamethodResult(operand, operand, metamethodEvent);
        }

        SetRegister(frame, instruction.A, result);
    }

    private void ExecuteLoadNil(CallFrame frame, LuaInstruction instruction)
    {
        for (var index = 0; index <= instruction.B; index++)
        {
            SetRegister(frame, instruction.A + index, LuaValue.Nil);
        }
    }

    private void ExecuteLoadKx(CallFrame frame, LuaPrototype prototype, LuaInstruction instruction)
    {
        var constantIndex = ReadFollowingExtraArgument(frame, prototype, instruction.Opcode);
        SetRegister(frame, instruction.A, ConvertConstant(prototype.Constants[constantIndex]));
    }

    private void ExecuteTableGet(CallFrame frame, int targetRegister, LuaValue tableValue, LuaValue key)
    {
        if (tableValue.Kind != LuaValueKind.Table)
        {
            throw new NotSupportedException("Table access metamethod dispatch is not implemented yet.");
        }

        SetRegister(frame, targetRegister, tableValue.AsTable().GetValue(key));
    }

    private void ExecuteTableSet(CallFrame frame, LuaValue tableValue, LuaValue key, LuaValue value)
    {
        if (tableValue.Kind != LuaValueKind.Table)
        {
            throw new NotSupportedException("Table access metamethod dispatch is not implemented yet.");
        }

        tableValue.AsTable().SetValue(key, value);
    }

    private void ExecuteSetList(CallFrame frame, LuaPrototype prototype, LuaInstruction instruction)
    {
        var tableValue = GetRegister(frame, instruction.A);
        if (tableValue.Kind != LuaValueKind.Table)
        {
            throw new NotSupportedException("SETLIST currently supports tables only.");
        }

        var elementCount = instruction.VB;
        if (elementCount == 0)
        {
            elementCount = GetOpenValueCount(frame, instruction.A + 1);
        }

        var startIndex = instruction.VC;
        if (instruction.K != 0)
        {
            startIndex += ReadFollowingExtraArgument(frame, prototype, instruction.Opcode) * (LuaInstructionLayout.MaxArgVC + 1);
        }

        var currentIndex = startIndex + elementCount;
        var table = tableValue.AsTable();

        for (var offset = elementCount; offset > 0; offset--)
        {
            table.SetValue(LuaValue.FromInteger(currentIndex), GetRegister(frame, instruction.A + offset));
            currentIndex -= 1;
        }
    }

    private void ExecuteNewTable(CallFrame frame, LuaPrototype prototype, LuaInstruction instruction)
    {
        var extraArgument = ReadFollowingExtraArgument(frame, prototype, instruction.Opcode);
        var hashCapacity = instruction.VB > 0 ? 1L << (instruction.VB - 1) : 0L;
        var arrayCapacity = (long)instruction.VC;

        if (instruction.K != 0)
        {
            arrayCapacity += (long)extraArgument * (LuaInstructionLayout.MaxArgVC + 1L);
        }

        var table = new LuaTable(
            arrayCapacity: SaturateCapacity(arrayCapacity),
            hashCapacity: SaturateCapacity(hashCapacity));

        SetRegister(frame, instruction.A, LuaValue.FromTable(table));
    }

    private void ExecuteSelf(CallFrame frame, LuaPrototype prototype, LuaInstruction instruction)
    {
        var receiver = GetRegister(frame, instruction.B);
        var key = ConvertConstant(prototype.Constants[instruction.C]);

        SetRegister(frame, instruction.A + 1, receiver);
        ExecuteTableGet(frame, instruction.A, receiver, key);
    }

    private void ExecuteLength(CallFrame frame, LuaInstruction instruction)
    {
        var value = GetRegister(frame, instruction.B);

        if (value.Kind == LuaValueKind.String)
        {
            SetRegister(frame, instruction.A, LuaValue.FromInteger(Encoding.UTF8.GetByteCount(value.AsString())));
            return;
        }

        if (value.Kind == LuaValueKind.Table)
        {
            if (TryGetMetamethod(value, GetMetamethodName(LengthMetamethodEvent), out var metamethod))
            {
                SetRegister(frame, instruction.A, CallMetamethodResult(metamethod.AsFunction(), value, value));
                return;
            }

            SetRegister(frame, instruction.A, LuaValue.FromInteger(value.AsTable().GetSequenceLength()));
            return;
        }

        if (TryGetMetamethod(value, GetMetamethodName(LengthMetamethodEvent), out var dynamicMetamethod))
        {
            SetRegister(frame, instruction.A, CallMetamethodResult(dynamicMetamethod.AsFunction(), value, value));
            return;
        }

        throw new NotSupportedException("Length semantics beyond strings, tables, and '__len' metamethods are not implemented yet.");
    }

    private void ExecuteToBeClosed(CallFrame frame, LuaInstruction instruction)
    {
        RegisterToBeClosed(frame, instruction.A);
    }

    private void ExecuteConcat(CallFrame frame, LuaInstruction instruction)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(instruction.B);

        var values = new LuaValue[instruction.B];
        for (var index = 0; index < instruction.B; index++)
        {
            values[index] = GetRegister(frame, instruction.A + index);
        }

        var total = values.Length;
        while (total > 1)
        {
            var left = values[total - 2];
            var right = values[total - 1];

            if (!TryConcatenateValues(left, right, out var result))
            {
                result = CallBinaryMetamethodResult(left, right, ConcatMetamethodEvent);
            }

            values[total - 2] = result;
            total -= 1;
        }

        SetRegister(frame, instruction.A, values[0]);
    }

    private void ExecuteCall(CallFrame frame, LuaInstruction instruction)
    {
        var callable = GetRegister(frame, instruction.A);
        var arguments = ReadArguments(frame, instruction.A, instruction.B);
        var results = CallValue(callable, arguments);

        if (instruction.C == 0)
        {
            WriteOpenResults(frame, instruction.A, results);
            return;
        }

        WriteResults(frame, instruction.A, instruction.C - 1, results);
    }

    private LuaValue[] ExecuteTailCall(CallFrame frame, LuaInstruction instruction)
    {
        var callable = GetRegister(frame, instruction.A);
        var arguments = ReadArguments(frame, instruction.A, instruction.B);
        return CallValue(callable, arguments);
    }

    private LuaValue[] ExecuteReturn(CallFrame frame, LuaInstruction instruction)
    {
        if (instruction.B == 0)
        {
            return ReadOpenResults(frame, instruction.A);
        }

        var resultCount = instruction.B - 1;
        var results = new LuaValue[resultCount];

        for (var index = 0; index < resultCount; index++)
        {
            results[index] = GetRegister(frame, instruction.A + index);
        }

        return results;
    }

    private void ExecuteVarArg(CallFrame frame, LuaInstruction instruction)
    {
        if (instruction.K != 0)
        {
            ExecuteVarArgFromTable(frame, instruction);
            return;
        }

        var requestedResultCount = instruction.C - 1;
        if (requestedResultCount < 0)
        {
            WriteOpenResults(frame, instruction.A, frame.Varargs);
            return;
        }

        WriteResults(frame, instruction.A, requestedResultCount, frame.Varargs);
    }

    private void ExecuteGetVarArg(CallFrame frame, LuaInstruction instruction)
    {
        SetRegister(frame, instruction.A, GetVarArgValue(frame, GetRegister(frame, instruction.C)));
    }

    private void ExecuteVarArgFromTable(CallFrame frame, LuaInstruction instruction)
    {
        var varargTable = GetRegister(frame, instruction.B).AsTable();
        var requestedResultCount = instruction.C - 1;
        var availableCount = GetVarArgCount(varargTable);

        if (requestedResultCount < 0)
        {
            var results = new LuaValue[availableCount];
            for (var index = 0; index < availableCount; index++)
            {
                results[index] = varargTable.GetValue(LuaValue.FromInteger(index + 1));
            }

            WriteOpenResults(frame, instruction.A, results);
            return;
        }

        var values = new LuaValue[requestedResultCount];
        for (var index = 0; index < requestedResultCount; index++)
        {
            values[index] = index < availableCount
                ? varargTable.GetValue(LuaValue.FromInteger(index + 1))
                : LuaValue.Nil;
        }

        WriteResults(frame, instruction.A, requestedResultCount, values);
    }

    private void ExecuteErrNNil(LuaPrototype prototype, CallFrame frame, LuaInstruction instruction)
    {
        if (GetRegister(frame, instruction.A).IsNil)
        {
            return;
        }

        var globalName = instruction.Bx == 0
            ? "?"
            : GetConstantString(prototype.Constants[instruction.Bx - 1]) ?? "?";

        throw new LuaRuntimeException(LuaValue.FromString($"global '{globalName}' already defined"));
    }

    private void ExecuteIntegerForPrep(
        CallFrame frame,
        LuaInstruction instruction,
        long initialValue,
        LuaValue limitValue,
        long stepValue)
    {
        if (stepValue == 0)
        {
            throw new InvalidOperationException("'for' step is zero");
        }

        var limit = GetIntegerForLimit(limitValue, initialValue, stepValue);
        if (ShouldSkipIntegerForLoop(initialValue, limit, stepValue))
        {
            frame.Advance(instruction.Bx + 1);
            return;
        }

        SetRegister(frame, instruction.A, LuaValue.FromInteger(ComputeIntegerForLoopCount(initialValue, limit, stepValue)));
        SetRegister(frame, instruction.A + 1, LuaValue.FromInteger(stepValue));
        SetRegister(frame, instruction.A + 2, LuaValue.FromInteger(initialValue));
    }

    private void ExecuteFloatForPrep(
        CallFrame frame,
        LuaInstruction instruction,
        LuaValue initialValue,
        LuaValue limitValue,
        LuaValue stepValue)
    {
        if (!TryGetNumber(limitValue, out var numericLimit))
        {
            throw new InvalidOperationException("'for' limit must be a number");
        }

        if (!TryGetNumber(stepValue, out var numericStep))
        {
            throw new InvalidOperationException("'for' step must be a number");
        }

        if (!TryGetNumber(initialValue, out var numericInitial))
        {
            throw new InvalidOperationException("'for' initial value must be a number");
        }

        if (numericStep == 0d)
        {
            throw new InvalidOperationException("'for' step is zero");
        }

        if (ShouldSkipFloatForLoop(numericInitial, numericLimit, numericStep))
        {
            frame.Advance(instruction.Bx + 1);
            return;
        }

        SetRegister(frame, instruction.A, LuaValue.FromFloat(numericLimit));
        SetRegister(frame, instruction.A + 1, LuaValue.FromFloat(numericStep));
        SetRegister(frame, instruction.A + 2, LuaValue.FromFloat(numericInitial));
    }

    private void ExecuteIntegerForLoop(CallFrame frame, LuaInstruction instruction)
    {
        var remaining = unchecked((ulong)GetRegister(frame, instruction.A).AsInteger());
        if (remaining == 0)
        {
            return;
        }

        var step = GetRegister(frame, instruction.A + 1).AsInteger();
        var index = GetRegister(frame, instruction.A + 2).AsInteger();

        SetRegister(frame, instruction.A, LuaValue.FromInteger(unchecked((long)(remaining - 1))));
        SetRegister(frame, instruction.A + 2, LuaValue.FromInteger(index + step));
        JumpRelative(frame, -instruction.Bx);
    }

    private void ExecuteFloatForLoop(CallFrame frame, LuaInstruction instruction)
    {
        var step = GetRegister(frame, instruction.A + 1).AsFloat();
        var limit = GetRegister(frame, instruction.A).AsFloat();
        var index = GetRegister(frame, instruction.A + 2).AsFloat() + step;

        if (ShouldContinueFloatForLoop(index, limit, step))
        {
            SetRegister(frame, instruction.A + 2, LuaValue.FromFloat(index));
            JumpRelative(frame, -instruction.Bx);
        }
    }

    private void ExecuteForPrep(CallFrame frame, LuaInstruction instruction)
    {
        var initialValue = GetRegister(frame, instruction.A);
        var limitValue = GetRegister(frame, instruction.A + 1);
        var stepValue = GetRegister(frame, instruction.A + 2);

        if (initialValue.Kind == LuaValueKind.Integer && stepValue.Kind == LuaValueKind.Integer)
        {
            ExecuteIntegerForPrep(frame, instruction, initialValue.AsInteger(), limitValue, stepValue.AsInteger());
            return;
        }

        ExecuteFloatForPrep(frame, instruction, initialValue, limitValue, stepValue);
    }

    private void ExecuteForLoop(CallFrame frame, LuaInstruction instruction)
    {
        if (GetRegister(frame, instruction.A + 1).Kind == LuaValueKind.Integer)
        {
            ExecuteIntegerForLoop(frame, instruction);
            return;
        }

        ExecuteFloatForLoop(frame, instruction);
    }

    private void ExecuteTForPrep(CallFrame frame, LuaInstruction instruction)
    {
        var controlValue = GetRegister(frame, instruction.A + 2);
        var closeValue = GetRegister(frame, instruction.A + 3);

        SetRegister(frame, instruction.A + 2, closeValue);
        SetRegister(frame, instruction.A + 3, controlValue);
        RegisterToBeClosed(frame, instruction.A + 2);
        frame.Advance(instruction.Bx);
    }

    private void ExecuteTForCall(CallFrame frame, LuaInstruction instruction)
    {
        SetRegister(frame, instruction.A + 5, GetRegister(frame, instruction.A + 3));
        SetRegister(frame, instruction.A + 4, GetRegister(frame, instruction.A + 1));
        SetRegister(frame, instruction.A + 3, GetRegister(frame, instruction.A));

        var iterator = GetRegister(frame, instruction.A + 3);
        var results = CallValue(
            iterator,
            [
                GetRegister(frame, instruction.A + 4),
                GetRegister(frame, instruction.A + 5)
            ]);

        WriteResults(frame, instruction.A + 3, instruction.C, results);
    }

    private void ExecuteTForLoop(CallFrame frame, LuaInstruction instruction)
    {
        if (!GetRegister(frame, instruction.A + 3).IsNil)
        {
            JumpRelative(frame, -instruction.Bx);
        }
    }

    private void ExecuteVarArgPrep(CallFrame frame, LuaPrototype prototype)
    {
        if (!UsesVarArgTable(prototype))
        {
            return;
        }

        SetRegister(frame, prototype.NumberOfParameters, LuaValue.FromTable(CreateVarArgTable(frame.Varargs)));
    }

    private void RegisterToBeClosed(CallFrame frame, int registerIndex)
    {
        var value = GetRegister(frame, registerIndex);
        if (value.IsNil || (value.Kind == LuaValueKind.Boolean && !value.AsBoolean()))
        {
            return;
        }

        EnsureCloseMethodExists(value);
        frame.RegisterToBeClosed(registerIndex);
    }

    private void ExecuteClosureInstruction(CallFrame frame, LuaPrototype prototype, LuaInstruction instruction)
    {
        var nestedPrototype = prototype.NestedPrototypes[instruction.Bx];
        var nestedClosure = CreateClosure(nestedPrototype, frame);

        SetRegister(frame, instruction.A, LuaValue.FromFunction(nestedClosure));
    }

    private void ExecuteJump(CallFrame frame, LuaInstruction instruction)
    {
        JumpRelative(frame, instruction.SJ);
    }

    private void ExecuteMetamethodBinary(CallFrame frame, LuaPrototype prototype, LuaInstruction instruction)
    {
        var resultRegister = ReadPreviousInstruction(frame, prototype, instruction.Opcode).A;
        ExecuteBinaryMetamethod(
            frame,
            resultRegister,
            GetRegister(frame, instruction.A),
            GetRegister(frame, instruction.B),
            instruction.C);
    }

    private void ExecuteMetamethodBinaryImmediate(CallFrame frame, LuaPrototype prototype, LuaInstruction instruction)
    {
        var resultRegister = ReadPreviousInstruction(frame, prototype, instruction.Opcode).A;
        var registerOperand = GetRegister(frame, instruction.A);
        var immediateOperand = LuaValue.FromInteger(ToSignedB(instruction.B));

        if (instruction.K != 0)
        {
            ExecuteBinaryMetamethod(frame, resultRegister, immediateOperand, registerOperand, instruction.C);
            return;
        }

        ExecuteBinaryMetamethod(frame, resultRegister, registerOperand, immediateOperand, instruction.C);
    }

    private void ExecuteMetamethodBinaryConstant(CallFrame frame, LuaPrototype prototype, LuaInstruction instruction)
    {
        var resultRegister = ReadPreviousInstruction(frame, prototype, instruction.Opcode).A;
        var registerOperand = GetRegister(frame, instruction.A);
        var constantOperand = ConvertConstant(prototype.Constants[instruction.B]);

        if (instruction.K != 0)
        {
            ExecuteBinaryMetamethod(frame, resultRegister, constantOperand, registerOperand, instruction.C);
            return;
        }

        ExecuteBinaryMetamethod(frame, resultRegister, registerOperand, constantOperand, instruction.C);
    }

    private void ExecuteBinaryMetamethod(
        CallFrame frame,
        int resultRegister,
        LuaValue left,
        LuaValue right,
        int eventIndex)
    {
        var metamethod = ResolveBinaryMetamethod(left, right, eventIndex).AsFunction();
        var results = Call(metamethod, [left, right]);
        SetRegister(frame, resultRegister, results.Length == 0 ? LuaValue.Nil : results[0]);
    }

    private LuaValue[] CallValue(LuaValue callable, IReadOnlyList<LuaValue> arguments)
    {
        var resolved = ResolveCallable(callable, arguments);
        return Call(resolved.Closure, resolved.Arguments);
    }

    private void ExecuteEqualityComparison(CallFrame frame, LuaValue left, LuaValue right, int expected)
    {
        ExecuteConditionalJump(frame, AreEqualWithMetamethod(left, right), expected);
    }

    private void ExecuteRegisterComparison(
        CallFrame frame,
        LuaValue left,
        LuaValue right,
        int expected,
        Func<int, bool> accept,
        int metamethodEvent)
    {
        if (!TryCompareOrdered(left, right, out var comparison))
        {
            ExecuteConditionalJump(frame, CallBinaryMetamethodBoolean(left, right, metamethodEvent), expected);
            return;
        }

        ExecuteConditionalJump(frame, accept(comparison), expected);
    }

    private void ExecuteImmediateComparison(
        CallFrame frame,
        LuaInstruction instruction,
        Func<double, double, bool> comparison,
        int metamethodEvent = -1,
        bool flipOperands = false,
        bool allowNonNumericAsFalse = false)
    {
        var value = GetRegister(frame, instruction.A);
        var immediateValue = GetImmediateComparisonValue(instruction);

        if (TryGetNumber(value, out var numericValue) && TryGetNumber(immediateValue, out var numericImmediate))
        {
            ExecuteConditionalJump(frame, comparison(numericValue, numericImmediate), instruction.K);
            return;
        }

        if (allowNonNumericAsFalse)
        {
            ExecuteConditionalJump(frame, false, instruction.K);
            return;
        }

        if (metamethodEvent >= 0)
        {
            var left = flipOperands ? immediateValue : value;
            var right = flipOperands ? value : immediateValue;
            ExecuteConditionalJump(frame, CallBinaryMetamethodBoolean(left, right, metamethodEvent), instruction.K);
            return;
        }

        throw new NotSupportedException("Comparison semantics beyond numeric immediates are not implemented yet.");
    }

    private void ExecuteTestSet(CallFrame frame, LuaInstruction instruction)
    {
        var value = GetRegister(frame, instruction.B);

        if (IsTruthy(value) != (instruction.K != 0))
        {
            frame.Advance();
            return;
        }

        SetRegister(frame, instruction.A, value);
        ExecuteNextJump(frame);
    }

    private void ExecuteConditionalJump(CallFrame frame, bool condition, int expected)
    {
        if (condition != (expected != 0))
        {
            frame.Advance();
            return;
        }

        ExecuteNextJump(frame);
    }

    private void ExecuteNextJump(CallFrame frame)
    {
        if (frame.ProgramCounter >= GetCurrentPrototype(frame).Code.Length)
        {
            throw new InvalidOperationException("Conditional instruction is missing the following jump.");
        }

        var jumpInstruction = LuaInstruction.FromRaw(GetCurrentPrototype(frame).Code[frame.ProgramCounter]);
        if (jumpInstruction.Opcode != LuaOpcode.Jmp)
        {
            throw new InvalidOperationException("Conditional instruction must be followed by JMP.");
        }

        frame.Jump(frame.ProgramCounter + jumpInstruction.SJ + 1);
    }

    private static LuaBytecodeClosureBody GetBytecodeBody(LuaClosure closure)
    {
        if (closure.Body is not LuaBytecodeClosureBody body)
        {
            throw new InvalidOperationException("The closure does not contain a bytecode body.");
        }

        return body;
    }

    private LuaValue ResolveBinaryMetamethod(LuaValue left, LuaValue right, int eventIndex)
    {
        var metamethodName = GetMetamethodName(eventIndex);

        if (TryGetMetamethod(left, metamethodName, out var metamethod) ||
            TryGetMetamethod(right, metamethodName, out metamethod))
        {
            return metamethod;
        }

        throw new LuaRuntimeException(
            LuaValue.FromString($"no metamethod '{metamethodName}' for {left.Kind} and {right.Kind}"));
    }

    private LuaValue CallBinaryMetamethodResult(LuaValue left, LuaValue right, int eventIndex)
    {
        return CallMetamethodResult(ResolveBinaryMetamethod(left, right, eventIndex).AsFunction(), left, right);
    }

    private bool CallBinaryMetamethodBoolean(LuaValue left, LuaValue right, int eventIndex)
    {
        return IsTruthy(CallBinaryMetamethodResult(left, right, eventIndex));
    }

    private LuaValue CallMetamethodResult(LuaClosure metamethod, LuaValue left, LuaValue right)
    {
        var results = Call(metamethod, [left, right]);
        return results.Length == 0 ? LuaValue.Nil : results[0];
    }

    private (LuaClosure Closure, IReadOnlyList<LuaValue> Arguments) ResolveCallable(
        LuaValue callable,
        IReadOnlyList<LuaValue> arguments)
    {
        var currentCallable = callable;
        var currentArguments = arguments;

        for (var depth = 0; depth < MaxCallMetamethodDepth; depth++)
        {
            if (currentCallable.Kind == LuaValueKind.Function)
            {
                return (currentCallable.AsFunction(), currentArguments);
            }

            if (!TryGetMetamethod(currentCallable, GetMetamethodName(CallMetamethodEvent), out var metamethod))
            {
                throw new LuaRuntimeException(LuaValue.FromString($"attempt to call a {currentCallable.Kind} value"));
            }

            currentArguments = PrependArgument(currentCallable, currentArguments);
            currentCallable = metamethod;
        }

        throw new LuaRuntimeException(LuaValue.FromString("'__call' chain too long"));
    }

    private void InitializeRegisters(int baseIndex, IReadOnlyList<LuaValue> arguments, int fixedArgumentCount)
    {
        for (var index = 0; index < fixedArgumentCount; index++)
        {
            State.Stack[baseIndex + index] = arguments[index];
        }
    }

    private LuaValue[] ReadArguments(CallFrame frame, int functionRegister, int functionAndArgumentCount)
    {
        var argumentCount = functionAndArgumentCount == 0
            ? GetOpenValueCount(frame, functionRegister + 1)
            : functionAndArgumentCount - 1;
        var arguments = new LuaValue[argumentCount];

        for (var index = 0; index < argumentCount; index++)
        {
            arguments[index] = GetRegister(frame, functionRegister + index + 1);
        }

        return arguments;
    }

    private void WriteResults(CallFrame frame, int registerIndex, int resultCount, IReadOnlyList<LuaValue> results)
    {
        for (var index = 0; index < resultCount; index++)
        {
            var value = index < results.Count ? results[index] : LuaValue.Nil;
            SetRegister(frame, registerIndex + index, value);
        }
    }

    private void WriteOpenResults(CallFrame frame, int registerIndex, IReadOnlyList<LuaValue> results)
    {
        for (var index = 0; index < results.Count; index++)
        {
            SetRegister(frame, registerIndex + index, results[index]);
        }

        frame.SetRegisterTop(registerIndex + results.Count);
    }

    private LuaValue[] ReadOpenResults(CallFrame frame, int registerIndex)
    {
        var resultCount = GetOpenValueCount(frame, registerIndex);
        var results = new LuaValue[resultCount];

        for (var index = 0; index < resultCount; index++)
        {
            results[index] = GetRegister(frame, registerIndex + index);
        }

        return results;
    }

    private static int GetOpenValueCount(CallFrame frame, int registerIndex)
    {
        return Math.Max(0, frame.RegisterTop - registerIndex);
    }

    private static void JumpRelative(CallFrame frame, int offset)
    {
        frame.Jump(frame.ProgramCounter + offset);
    }

    private LuaValue[] RunClosure(CallFrame frame, LuaPrototype prototype)
    {
        while (frame.ProgramCounter < prototype.Code.Length)
        {
            var instruction = LuaInstruction.FromRaw(prototype.Code[frame.ProgramCounter]);
            frame.Advance();

            switch (instruction.Opcode)
            {
                case LuaOpcode.Move:
                    SetRegister(frame, instruction.A, GetRegister(frame, instruction.B));
                    break;
                case LuaOpcode.LoadFalse:
                    SetRegister(frame, instruction.A, LuaValue.FromBoolean(false));
                    break;
                case LuaOpcode.LFalseSkip:
                    SetRegister(frame, instruction.A, LuaValue.FromBoolean(false));
                    frame.Advance();
                    break;
                case LuaOpcode.LoadTrue:
                    SetRegister(frame, instruction.A, LuaValue.FromBoolean(true));
                    break;
                case LuaOpcode.LoadNil:
                    ExecuteLoadNil(frame, instruction);
                    break;
                case LuaOpcode.LoadI:
                    SetRegister(frame, instruction.A, LuaValue.FromInteger(instruction.SBx));
                    break;
                case LuaOpcode.LoadF:
                    SetRegister(frame, instruction.A, LuaValue.FromFloat(instruction.SBx));
                    break;
                case LuaOpcode.LoadK:
                    SetRegister(frame, instruction.A, ConvertConstant(prototype.Constants[instruction.Bx]));
                    break;
                case LuaOpcode.LoadKx:
                    ExecuteLoadKx(frame, prototype, instruction);
                    break;
                case LuaOpcode.GetUpVal:
                    SetRegister(frame, instruction.A, GetUpvalue(frame, instruction.B));
                    break;
                case LuaOpcode.GetTabUp:
                    ExecuteTableGet(frame, instruction.A, GetUpvalue(frame, instruction.B), ConvertConstant(prototype.Constants[instruction.C]));
                    break;
                case LuaOpcode.GetTable:
                    ExecuteTableGet(frame, instruction.A, GetRegister(frame, instruction.B), GetRegister(frame, instruction.C));
                    break;
                case LuaOpcode.GetI:
                    ExecuteTableGet(frame, instruction.A, GetRegister(frame, instruction.B), LuaValue.FromInteger(instruction.C));
                    break;
                case LuaOpcode.GetField:
                    ExecuteTableGet(frame, instruction.A, GetRegister(frame, instruction.B), ConvertConstant(prototype.Constants[instruction.C]));
                    break;
                case LuaOpcode.SetUpVal:
                    SetUpvalue(frame, instruction.B, GetRegister(frame, instruction.A));
                    break;
                case LuaOpcode.SetTabUp:
                    ExecuteTableSet(frame, GetUpvalue(frame, instruction.A), ConvertConstant(prototype.Constants[instruction.B]), GetRkValue(frame, prototype, instruction.C, instruction.K));
                    break;
                case LuaOpcode.SetTable:
                    ExecuteTableSet(frame, GetRegister(frame, instruction.A), GetRegister(frame, instruction.B), GetRkValue(frame, prototype, instruction.C, instruction.K));
                    break;
                case LuaOpcode.SetI:
                    ExecuteTableSet(frame, GetRegister(frame, instruction.A), LuaValue.FromInteger(instruction.B), GetRkValue(frame, prototype, instruction.C, instruction.K));
                    break;
                case LuaOpcode.SetField:
                    ExecuteTableSet(frame, GetRegister(frame, instruction.A), ConvertConstant(prototype.Constants[instruction.B]), GetRkValue(frame, prototype, instruction.C, instruction.K));
                    break;
                case LuaOpcode.NewTable:
                    ExecuteNewTable(frame, prototype, instruction);
                    break;
                case LuaOpcode.Self:
                    ExecuteSelf(frame, prototype, instruction);
                    break;
                case LuaOpcode.AddI:
                    ExecuteAddImmediate(frame, prototype, instruction);
                    break;
                case LuaOpcode.AddK:
                    ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), ConvertConstant(prototype.Constants[instruction.C]), TryAdd);
                    break;
                case LuaOpcode.SubK:
                    ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), ConvertConstant(prototype.Constants[instruction.C]), TrySubtract);
                    break;
                case LuaOpcode.MulK:
                    ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), ConvertConstant(prototype.Constants[instruction.C]), TryMultiply);
                    break;
                case LuaOpcode.ModK:
                    ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), ConvertConstant(prototype.Constants[instruction.C]), TryModulo);
                    break;
                case LuaOpcode.PowK:
                    ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), ConvertConstant(prototype.Constants[instruction.C]), TryPower);
                    break;
                case LuaOpcode.DivK:
                    ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), ConvertConstant(prototype.Constants[instruction.C]), TryDivide);
                    break;
                case LuaOpcode.IDivK:
                    ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), ConvertConstant(prototype.Constants[instruction.C]), TryIntegerDivide);
                    break;
                case LuaOpcode.BandK:
                    ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), ConvertConstant(prototype.Constants[instruction.C]), TryBitwiseAnd);
                    break;
                case LuaOpcode.BorK:
                    ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), ConvertConstant(prototype.Constants[instruction.C]), TryBitwiseOr);
                    break;
                case LuaOpcode.BXorK:
                    ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), ConvertConstant(prototype.Constants[instruction.C]), TryBitwiseXor);
                    break;
                case LuaOpcode.ShlI:
                    ExecuteBinaryArithmetic(frame, prototype, instruction, LuaValue.FromInteger(ToSignedC(instruction.C)), GetRegister(frame, instruction.B), TryShiftLeft);
                    break;
                case LuaOpcode.ShrI:
                    ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), LuaValue.FromInteger(ToSignedC(instruction.C)), TryShiftRight);
                    break;
                case LuaOpcode.Add:
                    ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), GetRegister(frame, instruction.C), TryAdd);
                    break;
                case LuaOpcode.Sub:
                    ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), GetRegister(frame, instruction.C), TrySubtract);
                    break;
                case LuaOpcode.Mul:
                    ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), GetRegister(frame, instruction.C), TryMultiply);
                    break;
                case LuaOpcode.Mod:
                    ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), GetRegister(frame, instruction.C), TryModulo);
                    break;
                case LuaOpcode.Pow:
                    ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), GetRegister(frame, instruction.C), TryPower);
                    break;
                case LuaOpcode.Div:
                    ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), GetRegister(frame, instruction.C), TryDivide);
                    break;
                case LuaOpcode.IDiv:
                    ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), GetRegister(frame, instruction.C), TryIntegerDivide);
                    break;
                case LuaOpcode.Band:
                    ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), GetRegister(frame, instruction.C), TryBitwiseAnd);
                    break;
                case LuaOpcode.Bor:
                    ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), GetRegister(frame, instruction.C), TryBitwiseOr);
                    break;
                case LuaOpcode.BXor:
                    ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), GetRegister(frame, instruction.C), TryBitwiseXor);
                    break;
                case LuaOpcode.Shl:
                    ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), GetRegister(frame, instruction.C), TryShiftLeft);
                    break;
                case LuaOpcode.Shr:
                    ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), GetRegister(frame, instruction.C), TryShiftRight);
                    break;
                case LuaOpcode.Unm:
                    ExecuteUnaryArithmetic(frame, instruction, TryUnaryMinus, UnaryMinusMetamethodEvent);
                    break;
                case LuaOpcode.BNot:
                    ExecuteUnaryArithmetic(frame, instruction, TryBitwiseNot, BitwiseNotMetamethodEvent);
                    break;
                case LuaOpcode.Not:
                    SetRegister(frame, instruction.A, LuaValue.FromBoolean(!IsTruthy(GetRegister(frame, instruction.B))));
                    break;
                case LuaOpcode.Len:
                    ExecuteLength(frame, instruction);
                    break;
                case LuaOpcode.Concat:
                    ExecuteConcat(frame, instruction);
                    break;
                case LuaOpcode.Close:
                    RethrowIfNeeded(CloseResourcesFrom(frame, instruction.A));
                    break;
                case LuaOpcode.Tbc:
                    ExecuteToBeClosed(frame, instruction);
                    break;
                case LuaOpcode.Call:
                    ExecuteCall(frame, instruction);
                    break;
                case LuaOpcode.TailCall:
                    return ExecuteTailCall(frame, instruction);
                case LuaOpcode.Return:
                    return ExecuteReturn(frame, instruction);
                case LuaOpcode.Return0:
                    return [];
                case LuaOpcode.Return1:
                    return [GetRegister(frame, instruction.A)];
                case LuaOpcode.ForLoop:
                    ExecuteForLoop(frame, instruction);
                    break;
                case LuaOpcode.ForPrep:
                    ExecuteForPrep(frame, instruction);
                    break;
                case LuaOpcode.TForPrep:
                    ExecuteTForPrep(frame, instruction);
                    break;
                case LuaOpcode.TForCall:
                    ExecuteTForCall(frame, instruction);
                    break;
                case LuaOpcode.TForLoop:
                    ExecuteTForLoop(frame, instruction);
                    break;
                case LuaOpcode.SetList:
                    ExecuteSetList(frame, prototype, instruction);
                    break;
                case LuaOpcode.Closure:
                    ExecuteClosureInstruction(frame, prototype, instruction);
                    break;
                case LuaOpcode.VarArg:
                    ExecuteVarArg(frame, instruction);
                    break;
                case LuaOpcode.GetVArg:
                    ExecuteGetVarArg(frame, instruction);
                    break;
                case LuaOpcode.VarArgPrep:
                    ExecuteVarArgPrep(frame, prototype);
                    break;
                case LuaOpcode.ErrNNil:
                    ExecuteErrNNil(prototype, frame, instruction);
                    break;
                case LuaOpcode.Jmp:
                    ExecuteJump(frame, instruction);
                    break;
                case LuaOpcode.Eq:
                    ExecuteEqualityComparison(frame, GetRegister(frame, instruction.A), GetRegister(frame, instruction.B), instruction.K);
                    break;
                case LuaOpcode.Lt:
                    ExecuteRegisterComparison(frame, GetRegister(frame, instruction.A), GetRegister(frame, instruction.B), instruction.K, static comparison => comparison < 0, LessThanMetamethodEvent);
                    break;
                case LuaOpcode.Le:
                    ExecuteRegisterComparison(frame, GetRegister(frame, instruction.A), GetRegister(frame, instruction.B), instruction.K, static comparison => comparison <= 0, LessEqualMetamethodEvent);
                    break;
                case LuaOpcode.EqK:
                    ExecuteEqualityComparison(frame, GetRegister(frame, instruction.A), ConvertConstant(prototype.Constants[instruction.B]), instruction.K);
                    break;
                case LuaOpcode.EqI:
                    ExecuteImmediateComparison(frame, instruction, static (left, right) => left == right, allowNonNumericAsFalse: true);
                    break;
                case LuaOpcode.LtI:
                    ExecuteImmediateComparison(frame, instruction, static (left, right) => left < right, metamethodEvent: LessThanMetamethodEvent);
                    break;
                case LuaOpcode.LeI:
                    ExecuteImmediateComparison(frame, instruction, static (left, right) => left <= right, metamethodEvent: LessEqualMetamethodEvent);
                    break;
                case LuaOpcode.GtI:
                    ExecuteImmediateComparison(frame, instruction, static (left, right) => left > right, metamethodEvent: LessThanMetamethodEvent, flipOperands: true);
                    break;
                case LuaOpcode.GeI:
                    ExecuteImmediateComparison(frame, instruction, static (left, right) => left >= right, metamethodEvent: LessEqualMetamethodEvent, flipOperands: true);
                    break;
                case LuaOpcode.Test:
                    ExecuteConditionalJump(frame, IsTruthy(GetRegister(frame, instruction.A)), instruction.K);
                    break;
                case LuaOpcode.TestSet:
                    ExecuteTestSet(frame, instruction);
                    break;
                case LuaOpcode.MmBin:
                    ExecuteMetamethodBinary(frame, prototype, instruction);
                    break;
                case LuaOpcode.MmBinI:
                    ExecuteMetamethodBinaryImmediate(frame, prototype, instruction);
                    break;
                case LuaOpcode.MmBinK:
                    ExecuteMetamethodBinaryConstant(frame, prototype, instruction);
                    break;
                default:
                    throw new NotSupportedException($"Opcode '{instruction.Name}' is not implemented yet.");
            }
        }

        return [];
    }

    private Exception? CloseResourcesFrom(CallFrame frame, int registerIndex, Exception? pendingException = null)
    {
        try
        {
            var currentException = pendingException;
            foreach (var trackedRegister in frame.ConsumeToBeClosedRegistersFrom(registerIndex))
            {
                try
                {
                    CloseToBeClosedValue(frame, trackedRegister, GetErrorObject(currentException));
                }
                catch (Exception ex)
                {
                    currentException = ex;
                }
            }

            return currentException;
        }
        finally
        {
            frame.CloseOpenUpvaluesFrom(State, registerIndex);
        }
    }

    private void CloseToBeClosedValue(CallFrame frame, int registerIndex, LuaValue errorObject)
    {
        var value = GetRegister(frame, registerIndex);
        if (value.IsNil || (value.Kind == LuaValueKind.Boolean && !value.AsBoolean()))
        {
            return;
        }

        var closeMethod = GetCloseMethod(value);
        if (closeMethod.Kind != LuaValueKind.Function)
        {
            throw new InvalidOperationException("To-be-closed value must expose a '__close' function.");
        }

        Call(closeMethod.AsFunction(), [value, errorObject]);
    }

    private static LuaValue GetErrorObject(Exception? exception)
    {
        return exception switch
        {
            null => LuaValue.Nil,
            LuaRuntimeException runtimeException => runtimeException.ErrorObject,
            _ => LuaValue.FromString(exception.Message)
        };
    }

    private static void RethrowIfNeeded(Exception? exception)
    {
        if (exception is null)
        {
            return;
        }

        ExceptionDispatchInfo.Capture(exception).Throw();
    }

    private void EnsureCloseMethodExists(LuaValue value)
    {
        var closeMethod = GetCloseMethod(value);
        if (closeMethod.Kind != LuaValueKind.Function)
        {
            throw new InvalidOperationException("To-be-closed value must expose a '__close' function.");
        }
    }

    private LuaValue GetRegister(CallFrame frame, int registerIndex)
    {
        return State.Stack[frame.BaseIndex + registerIndex];
    }

    private void SetRegister(CallFrame frame, int registerIndex, LuaValue value)
    {
        EnsureRegisterExists(frame, registerIndex);
        State.Stack[frame.BaseIndex + registerIndex] = value;
    }

    private LuaValue GetUpvalue(CallFrame frame, int upvalueIndex)
    {
        return frame.Closure.Upvalues[upvalueIndex].GetValue(State);
    }

    private void SetUpvalue(CallFrame frame, int upvalueIndex, LuaValue value)
    {
        frame.Closure.Upvalues[upvalueIndex].SetValue(State, value);
    }

    private static LuaPrototype GetCurrentPrototype(CallFrame frame)
    {
        return GetBytecodeBody(frame.Closure).Prototype;
    }

    private static LuaValue GetCloseMethod(LuaValue value)
    {
        return value.Kind switch
        {
            LuaValueKind.Table => value.AsTable().TryGetMetamethod("__close", out var metamethod)
                ? metamethod
                : LuaValue.Nil,
            _ => throw new NotSupportedException("To-be-closed values currently support tables only.")
        };
    }

    private static LuaValue ConvertConstant(LuaConstant constant)
    {
        return constant.Kind switch
        {
            LuaConstantKind.Nil => LuaValue.Nil,
            LuaConstantKind.Boolean => LuaValue.FromBoolean(constant.AsBoolean()),
            LuaConstantKind.Integer => LuaValue.FromInteger(constant.AsInteger()),
            LuaConstantKind.Float => LuaValue.FromFloat(constant.AsFloat()),
            LuaConstantKind.String => LuaValue.FromString(constant.AsString()),
            _ => throw new NotSupportedException($"Constant kind '{constant.Kind}' is not implemented yet.")
        };
    }

    private static string? GetConstantString(LuaConstant constant)
    {
        return constant.Kind == LuaConstantKind.String ? constant.AsString() : null;
    }

    private static IReadOnlyList<LuaValue> GetVarargs(
        LuaPrototype prototype,
        IReadOnlyList<LuaValue> arguments,
        int fixedArgumentCount)
    {
        if (!IsVarArgFunction(prototype) || arguments.Count <= fixedArgumentCount)
        {
            return Array.Empty<LuaValue>();
        }

        var varargs = new LuaValue[arguments.Count - fixedArgumentCount];
        for (var index = 0; index < varargs.Length; index++)
        {
            varargs[index] = arguments[fixedArgumentCount + index];
        }

        return varargs;
    }

    private static LuaTable CreateVarArgTable(IReadOnlyList<LuaValue> varargs)
    {
        var table = new LuaTable("vararg", arrayCapacity: varargs.Count);
        for (var index = 0; index < varargs.Count; index++)
        {
            table.SetValue(LuaValue.FromInteger(index + 1), varargs[index]);
        }

        table.SetValue(LuaValue.FromString("n"), LuaValue.FromInteger(varargs.Count));
        return table;
    }

    private LuaClosure CreateClosure(LuaPrototype prototype, CallFrame parentFrame, string? debugName = null)
    {
        ArgumentNullException.ThrowIfNull(prototype);
        ArgumentNullException.ThrowIfNull(parentFrame);

        return new LuaClosure(
            debugName ?? GetDebugName(prototype),
            prototype.Upvalues.Length,
            new LuaBytecodeClosureBody(prototype),
            BuildUpvalues(prototype, parentFrame));
    }

    private LuaValue GetRkValue(CallFrame frame, LuaPrototype prototype, int operand, int isConstant)
    {
        return isConstant != 0
            ? ConvertConstant(prototype.Constants[operand])
            : GetRegister(frame, operand);
    }

    private static LuaValue GetVarArgValue(CallFrame frame, LuaValue key)
    {
        if (TryGetInteger(key, out var integerKey))
        {
            if (integerKey >= 1 && integerKey <= frame.Varargs.Count)
            {
                return frame.Varargs[(int)integerKey - 1];
            }

            return LuaValue.Nil;
        }

        if (key.Kind == LuaValueKind.String && string.Equals(key.AsString(), "n", StringComparison.Ordinal))
        {
            return LuaValue.FromInteger(frame.Varargs.Count);
        }

        return LuaValue.Nil;
    }

    private static int GetVarArgCount(LuaTable table)
    {
        var countValue = table.GetValue(LuaValue.FromString("n"));
        if (!TryGetInteger(countValue, out var count) || count < 0 || count > int.MaxValue)
        {
            throw new InvalidOperationException("vararg table has no proper 'n'");
        }

        return (int)count;
    }

    private void EnsureRegisterExists(CallFrame frame, int registerIndex)
    {
        var absoluteIndex = frame.BaseIndex + registerIndex;
        if (absoluteIndex >= State.Stack.Count)
        {
            State.Stack.SetTop(absoluteIndex + 1);
        }
    }

    private LuaUpvalue[] BuildUpvalues(LuaPrototype prototype, CallFrame? parentFrame)
    {
        var upvalues = new LuaUpvalue[prototype.Upvalues.Length];

        for (var index = 0; index < prototype.Upvalues.Length; index++)
        {
            var descriptor = prototype.Upvalues[index];

            if (parentFrame is null)
            {
                upvalues[index] = new LuaUpvalue(
                    string.Equals(descriptor.Name, "_ENV", StringComparison.Ordinal)
                        ? LuaValue.FromTable(State.GlobalEnvironment)
                        : LuaValue.Nil);

                continue;
            }

            upvalues[index] = descriptor.InStack != 0
                ? parentFrame.GetOrCreateOpenUpvalue(descriptor.Index)
                : parentFrame.Closure.Upvalues[descriptor.Index];
        }

        return upvalues;
    }

    private static string GetDebugName(LuaPrototype prototype)
    {
        if (prototype.LineDefined == 0)
        {
            return "main";
        }

        return $"function@{prototype.LineDefined}";
    }

    private static (bool Success, LuaValue Result) TryAdd(LuaValue left, LuaValue right)
    {
        if (left.Kind == LuaValueKind.Integer && right.Kind == LuaValueKind.Integer)
        {
            return (true, LuaValue.FromInteger(left.AsInteger() + right.AsInteger()));
        }

        if (TryGetNumber(left, out var leftNumber) && TryGetNumber(right, out var rightNumber))
        {
            return (true, LuaValue.FromFloat(leftNumber + rightNumber));
        }

        return (false, LuaValue.Nil);
    }

    private static (bool Success, LuaValue Result) TrySubtract(LuaValue left, LuaValue right)
    {
        if (left.Kind == LuaValueKind.Integer && right.Kind == LuaValueKind.Integer)
        {
            return (true, LuaValue.FromInteger(left.AsInteger() - right.AsInteger()));
        }

        if (TryGetNumber(left, out var leftNumber) && TryGetNumber(right, out var rightNumber))
        {
            return (true, LuaValue.FromFloat(leftNumber - rightNumber));
        }

        return (false, LuaValue.Nil);
    }

    private static (bool Success, LuaValue Result) TryMultiply(LuaValue left, LuaValue right)
    {
        if (left.Kind == LuaValueKind.Integer && right.Kind == LuaValueKind.Integer)
        {
            return (true, LuaValue.FromInteger(left.AsInteger() * right.AsInteger()));
        }

        if (TryGetNumber(left, out var leftNumber) && TryGetNumber(right, out var rightNumber))
        {
            return (true, LuaValue.FromFloat(leftNumber * rightNumber));
        }

        return (false, LuaValue.Nil);
    }

    private static (bool Success, LuaValue Result) TryPower(LuaValue left, LuaValue right)
    {
        if (TryGetNumber(left, out var leftNumber) && TryGetNumber(right, out var rightNumber))
        {
            return (true, LuaValue.FromFloat(Math.Pow(leftNumber, rightNumber)));
        }

        return (false, LuaValue.Nil);
    }

    private static (bool Success, LuaValue Result) TryDivide(LuaValue left, LuaValue right)
    {
        if (TryGetNumber(left, out var leftNumber) && TryGetNumber(right, out var rightNumber))
        {
            return (true, LuaValue.FromFloat(leftNumber / rightNumber));
        }

        return (false, LuaValue.Nil);
    }

    private static (bool Success, LuaValue Result) TryIntegerDivide(LuaValue left, LuaValue right)
    {
        if (left.Kind == LuaValueKind.Integer && right.Kind == LuaValueKind.Integer)
        {
            return (true, LuaValue.FromInteger(LuaIntegerFloorDivide(left.AsInteger(), right.AsInteger())));
        }

        if (TryGetNumber(left, out var leftNumber) && TryGetNumber(right, out var rightNumber))
        {
            return (true, LuaValue.FromFloat(Math.Floor(leftNumber / rightNumber)));
        }

        return (false, LuaValue.Nil);
    }

    private static (bool Success, LuaValue Result) TryModulo(LuaValue left, LuaValue right)
    {
        if (left.Kind == LuaValueKind.Integer && right.Kind == LuaValueKind.Integer)
        {
            return (true, LuaValue.FromInteger(LuaIntegerModulo(left.AsInteger(), right.AsInteger())));
        }

        if (TryGetNumber(left, out var leftNumber) && TryGetNumber(right, out var rightNumber))
        {
            var quotient = Math.Floor(leftNumber / rightNumber);
            return (true, LuaValue.FromFloat(leftNumber - quotient * rightNumber));
        }

        return (false, LuaValue.Nil);
    }

    private static (bool Success, LuaValue Result) TryUnaryMinus(LuaValue value)
    {
        return value.Kind switch
        {
            LuaValueKind.Integer => (true, LuaValue.FromInteger(-value.AsInteger())),
            LuaValueKind.Float => (true, LuaValue.FromFloat(-value.AsFloat())),
            _ => (false, LuaValue.Nil)
        };
    }

    private static (bool Success, LuaValue Result) TryBitwiseAnd(LuaValue left, LuaValue right)
    {
        return TryBinaryIntegerOperation(left, right, static (x, y) => x & y);
    }

    private static (bool Success, LuaValue Result) TryBitwiseOr(LuaValue left, LuaValue right)
    {
        return TryBinaryIntegerOperation(left, right, static (x, y) => x | y);
    }

    private static (bool Success, LuaValue Result) TryBitwiseXor(LuaValue left, LuaValue right)
    {
        return TryBinaryIntegerOperation(left, right, static (x, y) => x ^ y);
    }

    private static (bool Success, LuaValue Result) TryShiftLeft(LuaValue left, LuaValue right)
    {
        return TryBinaryIntegerOperation(left, right, static (x, y) => LuaShiftLeft(x, y));
    }

    private static (bool Success, LuaValue Result) TryShiftRight(LuaValue left, LuaValue right)
    {
        return TryBinaryIntegerOperation(left, right, static (x, y) => LuaShiftLeft(x, -y));
    }

    private static (bool Success, LuaValue Result) TryBitwiseNot(LuaValue value)
    {
        if (!TryGetInteger(value, out var integer))
        {
            return (false, LuaValue.Nil);
        }

        return (true, LuaValue.FromInteger(~integer));
    }

    private static bool AreEqual(LuaValue left, LuaValue right)
    {
        if (left.Kind == right.Kind)
        {
            if (left == right)
            {
                return true;
            }

            if (left.Kind == LuaValueKind.Table &&
                TryGetMetamethod(left, GetMetamethodName(EqualityMetamethodEvent), out _))
            {
                return false;
            }

            return left == right;
        }

        if (TryGetNumber(left, out var leftNumber) && TryGetNumber(right, out var rightNumber))
        {
            return leftNumber.Equals(rightNumber);
        }

        if (left.Kind == right.Kind &&
            (left.Kind == LuaValueKind.Table || left.Kind == LuaValueKind.UserData))
        {
            return false;
        }

        return false;
    }

    private bool AreEqualWithMetamethod(LuaValue left, LuaValue right)
    {
        if (left.Kind != right.Kind)
        {
            return TryGetNumber(left, out var leftNumber) &&
                   TryGetNumber(right, out var rightNumber) &&
                   leftNumber.Equals(rightNumber);
        }

        if (left == right)
        {
            return true;
        }

        if (left.Kind is LuaValueKind.Table or LuaValueKind.UserData)
        {
            if (TryGetMetamethod(left, GetMetamethodName(EqualityMetamethodEvent), out var metamethod) ||
                TryGetMetamethod(right, GetMetamethodName(EqualityMetamethodEvent), out metamethod))
            {
                return IsTruthy(CallMetamethodResult(metamethod.AsFunction(), left, right));
            }
        }

        return AreEqual(left, right);
    }

    private static bool TryCompareOrdered(LuaValue left, LuaValue right, out int comparison)
    {
        if (left.Kind == LuaValueKind.String && right.Kind == LuaValueKind.String)
        {
            comparison = StringComparer.Ordinal.Compare(left.AsString(), right.AsString());
            return true;
        }

        if (TryGetNumber(left, out var leftNumber) && TryGetNumber(right, out var rightNumber))
        {
            comparison = leftNumber.CompareTo(rightNumber);
            return true;
        }

        comparison = default;
        return false;
    }

    private static bool TryGetMetamethod(LuaValue value, string metamethodName, out LuaValue metamethod)
    {
        return value.Kind switch
        {
            LuaValueKind.Table => value.AsTable().TryGetMetamethod(metamethodName, out metamethod),
            _ => ReturnMissingMetamethod(out metamethod)
        };
    }

    private static bool ReturnMissingMetamethod(out LuaValue metamethod)
    {
        metamethod = LuaValue.Nil;
        return false;
    }

    private static LuaValue GetImmediateComparisonValue(LuaInstruction instruction)
    {
        var immediate = ToSignedB(instruction.B);
        return instruction.C != 0
            ? LuaValue.FromFloat(immediate)
            : LuaValue.FromInteger(immediate);
    }

    private static bool TryGetNumber(LuaValue value, out double result)
    {
        switch (value.Kind)
        {
            case LuaValueKind.Integer:
                result = value.AsInteger();
                return true;
            case LuaValueKind.Float:
                result = value.AsFloat();
                return true;
            default:
                result = default;
                return false;
        }
    }

    private static bool TryGetInteger(LuaValue value, out long result)
    {
        switch (value.Kind)
        {
            case LuaValueKind.Integer:
                result = value.AsInteger();
                return true;
            case LuaValueKind.Float:
            {
                var number = value.AsFloat();
                if (double.IsFinite(number) &&
                    number >= long.MinValue &&
                    number <= long.MaxValue &&
                    Math.Truncate(number) == number)
                {
                    result = (long)number;
                    return true;
                }

                break;
            }
        }

        result = default;
        return false;
    }

    private static long GetIntegerForLimit(LuaValue value, long initialValue, long stepValue)
    {
        if (value.Kind == LuaValueKind.Integer)
        {
            return value.AsInteger();
        }

        if (!TryGetNumber(value, out var numericLimit))
        {
            throw new InvalidOperationException("'for' limit must be a number");
        }

        if (!double.IsFinite(numericLimit))
        {
            return stepValue > 0 ? long.MaxValue : long.MinValue;
        }

        if (stepValue > 0)
        {
            if (numericLimit < initialValue)
            {
                return long.MinValue;
            }

            if (numericLimit >= long.MaxValue)
            {
                return long.MaxValue;
            }

            return (long)Math.Floor(numericLimit);
        }

        if (numericLimit > initialValue)
        {
            return long.MaxValue;
        }

        if (numericLimit <= long.MinValue)
        {
            return long.MinValue;
        }

        return (long)Math.Ceiling(numericLimit);
    }

    private static bool ShouldSkipIntegerForLoop(long initialValue, long limit, long stepValue)
    {
        return stepValue > 0 ? limit < initialValue : initialValue < limit;
    }

    private static long ComputeIntegerForLoopCount(long initialValue, long limit, long stepValue)
    {
        ulong count;
        if (stepValue > 0)
        {
            count = unchecked((ulong)limit) - unchecked((ulong)initialValue);
            if (stepValue != 1)
            {
                count /= (ulong)stepValue;
            }
        }
        else
        {
            count = unchecked((ulong)initialValue) - unchecked((ulong)limit);
            count /= GetUnsignedAbs(stepValue);
        }

        return unchecked((long)count);
    }

    private static ulong GetUnsignedAbs(long value)
    {
        return value >= 0
            ? (ulong)value
            : unchecked((ulong)(-(value + 1))) + 1UL;
    }

    private static bool ShouldSkipFloatForLoop(double initialValue, double limit, double stepValue)
    {
        return stepValue > 0d ? limit < initialValue : initialValue < limit;
    }

    private static bool ShouldContinueFloatForLoop(double value, double limit, double stepValue)
    {
        return stepValue > 0d ? value <= limit : limit <= value;
    }

    private static bool IsVarArgFunction(LuaPrototype prototype)
    {
        return (prototype.Flags & VarArgFlagMask) != 0;
    }

    private static bool UsesVarArgTable(LuaPrototype prototype)
    {
        return (prototype.Flags & VarArgTableFlag) != 0;
    }

    private static bool TryGetConcatenationString(LuaValue value, out string result)
    {
        switch (value.Kind)
        {
            case LuaValueKind.String:
                result = value.AsString();
                return true;
            case LuaValueKind.Integer:
                result = value.AsInteger().ToString(CultureInfo.InvariantCulture);
                return true;
            case LuaValueKind.Float:
                result = value.AsFloat().ToString("G17", CultureInfo.InvariantCulture);
                return true;
            default:
                result = string.Empty;
                return false;
        }
    }

    private static bool TryConcatenateValues(LuaValue left, LuaValue right, out LuaValue result)
    {
        if (TryGetConcatenationString(left, out var leftText) &&
            TryGetConcatenationString(right, out var rightText))
        {
            result = LuaValue.FromString(leftText + rightText);
            return true;
        }

        result = LuaValue.Nil;
        return false;
    }

    private static LuaValue[] PrependArgument(LuaValue head, IReadOnlyList<LuaValue> tail)
    {
        var arguments = new LuaValue[tail.Count + 1];
        arguments[0] = head;
        for (var index = 0; index < tail.Count; index++)
        {
            arguments[index + 1] = tail[index];
        }

        return arguments;
    }

    private static bool IsTruthy(LuaValue value)
    {
        return value.Kind switch
        {
            LuaValueKind.Nil => false,
            LuaValueKind.Boolean => value.AsBoolean(),
            _ => true
        };
    }

    private static int ToSignedB(int value)
    {
        return value - LuaInstructionLayout.OffsetSC;
    }

    private static int ToSignedC(int value)
    {
        return value - LuaInstructionLayout.OffsetSC;
    }

    private static long LuaIntegerFloorDivide(long left, long right)
    {
        if (right == 0)
        {
            throw new DivideByZeroException("attempt to divide by zero");
        }

        if (right == -1 && left == long.MinValue)
        {
            return -left;
        }

        var quotient = left / right;
        if ((left ^ right) < 0 && left % right != 0)
        {
            quotient -= 1;
        }

        return quotient;
    }

    private static long LuaIntegerModulo(long left, long right)
    {
        if (right == 0)
        {
            throw new DivideByZeroException("attempt to perform 'n%0'");
        }

        if (right == -1)
        {
            return 0;
        }

        var remainder = left % right;
        if (remainder != 0 && (remainder ^ right) < 0)
        {
            remainder += right;
        }

        return remainder;
    }

    private static (bool Success, LuaValue Result) TryBinaryIntegerOperation(
        LuaValue left,
        LuaValue right,
        Func<long, long, long> operation)
    {
        if (!TryGetInteger(left, out var leftInteger) || !TryGetInteger(right, out var rightInteger))
        {
            return (false, LuaValue.Nil);
        }

        return (true, LuaValue.FromInteger(operation(leftInteger, rightInteger)));
    }

    private static long LuaShiftLeft(long value, long shift)
    {
        if (shift < 0)
        {
            if (shift <= -64)
            {
                return 0;
            }

            return value >> (int)(-shift);
        }

        if (shift >= 64)
        {
            return 0;
        }

        return value << (int)shift;
    }

    private static int SaturateCapacity(long value)
    {
        if (value <= 0)
        {
            return 0;
        }

        return value >= int.MaxValue ? int.MaxValue : (int)value;
    }

    private static string GetMetamethodName(int eventIndex)
    {
        if (eventIndex < 0 || eventIndex >= MetamethodNames.Length)
        {
            throw new InvalidOperationException($"Unknown metamethod event index '{eventIndex}'.");
        }

        return MetamethodNames[eventIndex];
    }

    private static int ReadFollowingExtraArgument(CallFrame frame, LuaPrototype prototype, LuaOpcode owner)
    {
        if (frame.ProgramCounter >= prototype.Code.Length)
        {
            throw new InvalidOperationException($"Opcode '{owner}' is missing the following EXTRAARG.");
        }

        var extraInstruction = LuaInstruction.FromRaw(prototype.Code[frame.ProgramCounter]);
        if (extraInstruction.Opcode != LuaOpcode.ExtraArg)
        {
            throw new InvalidOperationException($"Opcode '{owner}' must be followed by EXTRAARG.");
        }

        frame.Advance();
        return extraInstruction.Ax;
    }

    private static LuaInstruction ReadPreviousInstruction(CallFrame frame, LuaPrototype prototype, LuaOpcode owner)
    {
        if (frame.ProgramCounter < 2)
        {
            throw new InvalidOperationException($"Opcode '{owner}' is missing the preceding arithmetic instruction.");
        }

        return LuaInstruction.FromRaw(prototype.Code[frame.ProgramCounter - 2]);
    }

    private static bool HasFollowingMetamethodInstruction(CallFrame frame, LuaPrototype prototype)
    {
        if (frame.ProgramCounter >= prototype.Code.Length)
        {
            return false;
        }

        var next = LuaInstruction.FromRaw(prototype.Code[frame.ProgramCounter]).Opcode;
        return next is LuaOpcode.MmBin or LuaOpcode.MmBinI or LuaOpcode.MmBinK;
    }

    private static void SkipMetamethodInstructionIfPresent(CallFrame frame, LuaPrototype prototype)
    {
        if (frame.ProgramCounter >= prototype.Code.Length)
        {
            return;
        }

        var next = LuaInstruction.FromRaw(prototype.Code[frame.ProgramCounter]).Opcode;
        if (next is LuaOpcode.MmBin or LuaOpcode.MmBinI or LuaOpcode.MmBinK)
        {
            frame.Advance();
        }
    }
}
