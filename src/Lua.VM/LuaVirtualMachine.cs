using System.Runtime.ExceptionServices;
using Lua.Bytecode.Chunks;
using Lua.Bytecode.Instructions;
using Lua.Runtime.Execution;
using Lua.Runtime.Objects;
using Lua.Runtime.Values;
using Lua.VM.Closures;
using static Lua.Runtime.Values.LuaValueHelper;

namespace Lua.VM;

public sealed partial class LuaVirtualMachine
{
    private const byte VarArgFlagMask = 0b00000011;
    private const byte VarArgTableFlag = 0b00000010;
    private const int IndexMetamethodEvent = 0;
    private const int NewIndexMetamethodEvent = 1;
    private const int LengthMetamethodEvent = 4;
    private const int EqualityMetamethodEvent = 5;
    private const int UnaryMinusMetamethodEvent = 18;
    private const int BitwiseNotMetamethodEvent = 19;
    private const int LessThanMetamethodEvent = 20;
    private const int LessEqualMetamethodEvent = 21;
    private const int ConcatMetamethodEvent = 22;
    private const int CallMetamethodEvent = 23;
    private const int MaxCallMetamethodDepth = 32;
    private const int MaxTableAccessMetamethodDepth = 2000;
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
        State.SetCallableInvoker(CallValue);
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
            LuaNativeClosureBody body => ExecuteNativeClosure(closure, body, actualArguments),
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

    private LuaValue[] CallValue(LuaValue callable, IReadOnlyList<LuaValue> arguments)
    {
        var resolved = ResolveCallable(callable, arguments);
        return Call(resolved.Closure, resolved.Arguments);
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

    private LuaValue[] ExecuteNativeClosure(LuaClosure closure, LuaNativeClosureBody body, IReadOnlyList<LuaValue> arguments)
    {
        try
        {
            return body.Function(State, closure, arguments);
        }
        catch (LuaRuntimeException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new LuaRuntimeException(LuaValue.FromString(ex.Message), ex);
        }
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
                    throw new NotImplementedException($"Opcode '{instruction.Name}' is not implemented yet.");
            }
        }

        return [];
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

    private void ExecuteToBeClosed(CallFrame frame, LuaInstruction instruction)
    {
        RegisterToBeClosed(frame, instruction.A);
    }

    private void ExecuteClosureInstruction(CallFrame frame, LuaPrototype prototype, LuaInstruction instruction)
    {
        var nestedPrototype = prototype.NestedPrototypes[instruction.Bx];
        var nestedClosure = CreateClosure(nestedPrototype, frame);

        SetRegister(frame, instruction.A, LuaValue.FromFunction(nestedClosure));
    }
}
