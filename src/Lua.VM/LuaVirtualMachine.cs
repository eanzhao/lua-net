using System.Runtime.ExceptionServices;
using Lua.Bytecode.Chunks;
using Lua.Bytecode.Instructions;
using Lua.Compiler;
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
    private const int AddMetamethodEvent = 6;
    private const int SubtractMetamethodEvent = 7;
    private const int MultiplyMetamethodEvent = 8;
    private const int ModuloMetamethodEvent = 9;
    private const int PowerMetamethodEvent = 10;
    private const int DivideMetamethodEvent = 11;
    private const int IntegerDivideMetamethodEvent = 12;
    private const int BitwiseAndMetamethodEvent = 13;
    private const int BitwiseOrMetamethodEvent = 14;
    private const int BitwiseXorMetamethodEvent = 15;
    private const int ShiftLeftMetamethodEvent = 16;
    private const int ShiftRightMetamethodEvent = 17;
    private const int UnaryMinusMetamethodEvent = 18;
    private const int BitwiseNotMetamethodEvent = 19;
    private const int LessThanMetamethodEvent = 20;
    private const int LessEqualMetamethodEvent = 21;
    private const int ConcatMetamethodEvent = 22;
    private const int CallMetamethodEvent = 23;
    private const int MaxCallMetamethodDepth = 32;
    private const int MaxTableAccessMetamethodDepth = 2000;
    private const int MaxCallFrameDepth = 1024;
    private int _nextHostCallId = 1;
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
        State.SetBinaryChunkLoader(LoadBinaryChunk);
        State.SetTextChunkLoader(LoadTextChunk);
        State.SetBytecodeChunkDumper(DumpBytecodeClosure);
        State.SetCoroutineResumer(ResumeCoroutine);
        State.SetCoroutineCloser(CloseCoroutine);
    }

    public LuaState State { get; }

    public LuaValue[] Execute(LuaChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        var closure = CreateRootClosure(chunk.MainFunction, debugName: null, environment: null);
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

        return CreateRootClosure(prototype, debugName, environment: null);
    }

    private LuaClosure CreateRootClosure(LuaPrototype prototype, string? debugName, LuaValue? environment)
    {
        return new LuaClosure(
            debugName ?? GetDebugName(prototype),
            prototype.Upvalues.Length,
            new LuaBytecodeClosureBody(prototype),
            BuildUpvalues(prototype, parentFrame: null, environment),
            prototype.Upvalues.Select(static upvalue => upvalue.Name).ToArray(),
            prototype.Source,
            prototype.LineDefined);
    }

    private LuaValue[] ExecuteClosure(LuaClosure closure, LuaPrototype prototype, IReadOnlyList<LuaValue> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var hostCallId = CreateHostCallId();
        var frameDepth = State.Frames.Count;
        PushBytecodeFrame(closure, prototype, arguments, LuaCallReturnTarget.ForHostCall(hostCallId));

        try
        {
            return RunInterpreter(hostCallId);
        }
        catch (LuaYieldException ex) when (ReferenceEquals(ex.Thread, State.CurrentThread))
        {
            throw;
        }
        catch (Exception ex)
        {
            AbortFramesToDepth(frameDepth, ex);
            throw;
        }
    }

    private LuaValue[] CallValue(LuaValue callable, IReadOnlyList<LuaValue> arguments)
    {
        var resolved = ResolveCallable(callable, arguments);
        return resolved.Closure.Body switch
        {
            LuaBytecodeClosureBody body => ExecuteClosure(resolved.Closure, body.Prototype, resolved.Arguments),
            LuaNativeClosureBody body => ExecuteNativeClosure(resolved.Closure, body, resolved.Arguments),
            null => throw new InvalidOperationException("The closure does not contain an executable body."),
            _ => throw new InvalidOperationException($"Unsupported closure body type '{resolved.Closure.Body.GetType().Name}'.")
        };
    }

    private LuaClosure CreateClosure(LuaPrototype prototype, CallFrame parentFrame, string? debugName = null)
    {
        ArgumentNullException.ThrowIfNull(prototype);
        ArgumentNullException.ThrowIfNull(parentFrame);

        return new LuaClosure(
            debugName ?? GetDebugName(prototype),
            prototype.Upvalues.Length,
            new LuaBytecodeClosureBody(prototype),
            BuildUpvalues(prototype, parentFrame, rootEnvironment: null),
            prototype.Upvalues.Select(static upvalue => upvalue.Name).ToArray(),
            prototype.Source,
            prototype.LineDefined);
    }

    private LuaClosure LoadBinaryChunk(ReadOnlyMemory<byte> chunkBytes, string? chunkName, bool hasEnvironment, LuaValue environment)
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(chunkBytes.ToArray(), chunkName);
        return CreateRootClosure(chunk.MainFunction, debugName: null, environment: hasEnvironment ? environment : null);
    }

    private LuaClosure LoadTextChunk(ReadOnlyMemory<byte> chunkBytes, string? chunkName, bool hasEnvironment, LuaValue environment)
    {
        var chunk = LuaCompiler.Compile(chunkBytes, chunkName);
        return CreateRootClosure(chunk.MainFunction, debugName: null, environment: hasEnvironment ? environment : null);
    }

    private static bool DumpBytecodeClosure(
        LuaClosure closure,
        bool stripDebugInformation,
        out ReadOnlyMemory<byte> dumpedChunk)
    {
        if (closure.Body is not LuaBytecodeClosureBody body)
        {
            dumpedChunk = ReadOnlyMemory<byte>.Empty;
            return false;
        }

        var writer = new LuaChunkWriter();
        dumpedChunk = writer.Write(new LuaChunk
        {
            Header = CreateDefaultChunkHeader(),
            MainUpvalueCount = checked((byte)body.Prototype.Upvalues.Length),
            MainFunction = body.Prototype
        }, stripDebugInformation);
        return true;
    }

    private LuaValue[] ExecuteNativeClosure(LuaClosure closure, LuaNativeClosureBody body, IReadOnlyList<LuaValue> arguments)
    {
        var shouldTrackBoundary =
            !string.Equals(closure.DebugName, "coroutine.yield", StringComparison.Ordinal) &&
            !string.Equals(closure.DebugName, "coroutine.isyieldable", StringComparison.Ordinal) &&
            !string.Equals(closure.DebugName, "pcall", StringComparison.Ordinal) &&
            !string.Equals(closure.DebugName, "xpcall", StringComparison.Ordinal);
        EnsureCallFrameCapacity();
        var nativeFrame = new CallFrame(
            closure,
            baseIndex: State.Stack.Count,
            expectedResults: 0);
        State.PushFrame(nativeFrame);

        try
        {
            if (shouldTrackBoundary)
            {
                State.CurrentThread.EnterNonYieldableCall();
            }

            var results = body.Function(State, closure, arguments);
            ExecuteReturnHook(nativeFrame);
            return results;
        }
        catch (LuaYieldException)
        {
            throw;
        }
        catch (LuaThreadCloseException)
        {
            throw;
        }
        catch (LuaRuntimeException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new LuaRuntimeException(LuaValue.FromString(ex.Message), ex);
        }
        finally
        {
            if (shouldTrackBoundary && State.CurrentThread.NonYieldableCallDepth > 0)
            {
                State.CurrentThread.ExitNonYieldableCall();
            }

            if (ReferenceEquals(State.CurrentFrame, nativeFrame))
            {
                State.PopFrame();
            }
        }
    }

    private static LuaChunkHeader CreateDefaultChunkHeader()
    {
        return new LuaChunkHeader
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
        };
    }

    private LuaValue[] RunInterpreter(int hostCallId)
    {
        while (true)
        {
            try
            {
                var frame = State.CurrentFrame
                    ?? throw new InvalidOperationException("The interpreter has no active call frame.");

                if (TryResumePendingCall(frame, hostCallId, out var resumedCompleted, out var resumedResults))
                {
                    if (resumedCompleted)
                    {
                        return resumedResults;
                    }

                    continue;
                }

                if (TryResumePendingClose(frame, hostCallId, out var closeCompleted, out var closeResults))
                {
                    if (closeCompleted)
                    {
                        return closeResults;
                    }

                    continue;
                }

                var prototype = GetCurrentPrototype(frame);

                if (frame.ProgramCounter >= prototype.Code.Length)
                {
                    if (TryCompleteFrame(frame, [], hostCallId, out var finishedResults))
                    {
                        return finishedResults;
                    }

                    continue;
                }

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
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), ConvertConstant(prototype.Constants[instruction.C]), TryAdd, AddMetamethodEvent);
                        break;
                    case LuaOpcode.SubK:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), ConvertConstant(prototype.Constants[instruction.C]), TrySubtract, SubtractMetamethodEvent);
                        break;
                    case LuaOpcode.MulK:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), ConvertConstant(prototype.Constants[instruction.C]), TryMultiply, MultiplyMetamethodEvent);
                        break;
                    case LuaOpcode.ModK:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), ConvertConstant(prototype.Constants[instruction.C]), TryModulo, ModuloMetamethodEvent);
                        break;
                    case LuaOpcode.PowK:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), ConvertConstant(prototype.Constants[instruction.C]), TryPower, PowerMetamethodEvent);
                        break;
                    case LuaOpcode.DivK:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), ConvertConstant(prototype.Constants[instruction.C]), TryDivide, DivideMetamethodEvent);
                        break;
                    case LuaOpcode.IDivK:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), ConvertConstant(prototype.Constants[instruction.C]), TryIntegerDivide, IntegerDivideMetamethodEvent);
                        break;
                    case LuaOpcode.BandK:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), ConvertConstant(prototype.Constants[instruction.C]), TryBitwiseAnd, BitwiseAndMetamethodEvent);
                        break;
                    case LuaOpcode.BorK:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), ConvertConstant(prototype.Constants[instruction.C]), TryBitwiseOr, BitwiseOrMetamethodEvent);
                        break;
                    case LuaOpcode.BXorK:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), ConvertConstant(prototype.Constants[instruction.C]), TryBitwiseXor, BitwiseXorMetamethodEvent);
                        break;
                    case LuaOpcode.ShlI:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, LuaValue.FromInteger(ToSignedC(instruction.C)), GetRegister(frame, instruction.B), TryShiftLeft, ShiftLeftMetamethodEvent);
                        break;
                    case LuaOpcode.ShrI:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), LuaValue.FromInteger(ToSignedC(instruction.C)), TryShiftRight, ShiftRightMetamethodEvent);
                        break;
                    case LuaOpcode.Add:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), GetRegister(frame, instruction.C), TryAdd, AddMetamethodEvent);
                        break;
                    case LuaOpcode.Sub:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), GetRegister(frame, instruction.C), TrySubtract, SubtractMetamethodEvent);
                        break;
                    case LuaOpcode.Mul:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), GetRegister(frame, instruction.C), TryMultiply, MultiplyMetamethodEvent);
                        break;
                    case LuaOpcode.Mod:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), GetRegister(frame, instruction.C), TryModulo, ModuloMetamethodEvent);
                        break;
                    case LuaOpcode.Pow:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), GetRegister(frame, instruction.C), TryPower, PowerMetamethodEvent);
                        break;
                    case LuaOpcode.Div:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), GetRegister(frame, instruction.C), TryDivide, DivideMetamethodEvent);
                        break;
                    case LuaOpcode.IDiv:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), GetRegister(frame, instruction.C), TryIntegerDivide, IntegerDivideMetamethodEvent);
                        break;
                    case LuaOpcode.Band:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), GetRegister(frame, instruction.C), TryBitwiseAnd, BitwiseAndMetamethodEvent);
                        break;
                    case LuaOpcode.Bor:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), GetRegister(frame, instruction.C), TryBitwiseOr, BitwiseOrMetamethodEvent);
                        break;
                    case LuaOpcode.BXor:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), GetRegister(frame, instruction.C), TryBitwiseXor, BitwiseXorMetamethodEvent);
                        break;
                    case LuaOpcode.Shl:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), GetRegister(frame, instruction.C), TryShiftLeft, ShiftLeftMetamethodEvent);
                        break;
                    case LuaOpcode.Shr:
                        ExecuteBinaryArithmetic(frame, prototype, instruction, GetRegister(frame, instruction.B), GetRegister(frame, instruction.C), TryShiftRight, ShiftRightMetamethodEvent);
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
                        if (StartCloseContinuation(
                                frame,
                                instruction.A,
                                LuaPendingCloseContinuationKind.ContinueExecution,
                                returnResults: null))
                        {
                            continue;
                        }

                        RethrowIfNeeded(CloseResourcesFrom(frame, instruction.A));
                        break;
                    case LuaOpcode.Tbc:
                        ExecuteToBeClosed(prototype, frame, instruction);
                        break;
                    case LuaOpcode.Call:
                        ExecuteCall(frame, instruction);
                        break;
                    case LuaOpcode.TailCall:
                        if (ExecuteTailCall(frame, instruction, hostCallId, out var tailCallResults))
                        {
                            return tailCallResults;
                        }

                        break;
                    case LuaOpcode.Return:
                        if (TryCompleteFrame(frame, ExecuteReturn(frame, instruction), hostCallId, out var returnResults))
                        {
                            return returnResults;
                        }

                        break;
                    case LuaOpcode.Return0:
                        if (TryCompleteFrame(frame, [], hostCallId, out var return0Results))
                        {
                            return return0Results;
                        }

                        break;
                    case LuaOpcode.Return1:
                        if (TryCompleteFrame(frame, [GetRegister(frame, instruction.A)], hostCallId, out var return1Results))
                        {
                            return return1Results;
                        }

                        break;
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
            catch (Exception ex) when (ex is not LuaYieldException && ex is not LuaThreadCloseException && TryHandlePendingCloseException(ex))
            {
                continue;
            }
        }
    }

    private int CreateHostCallId()
    {
        return _nextHostCallId++;
    }

    private CallFrame PushBytecodeFrame(
        LuaClosure closure,
        LuaPrototype prototype,
        IReadOnlyList<LuaValue> arguments,
        LuaCallReturnTarget returnTarget)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        EnsureCallFrameCapacity();

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
            varargs: GetVarargs(prototype, arguments, fixedArgumentCount),
            returnTarget: returnTarget);
        State.PushFrame(frame);
        return frame;
    }

    private bool TryResumePendingCall(
        CallFrame frame,
        int hostCallId,
        out bool completedFrame,
        out LuaValue[] completedResults)
    {
        completedFrame = false;
        completedResults = [];

        if (frame.PendingCall is null || !State.CurrentThread.HasResumeValues())
        {
            return false;
        }

        var resumeValues = State.CurrentThread.ConsumeResumeValues();
        if (frame.PendingCall.Kind == LuaPendingCallKind.TailReturn)
        {
            frame.ClearPendingCall();
            completedFrame = TryCompleteFrame(frame, resumeValues, hostCallId, out completedResults);
            return true;
        }

        WriteCallResults(frame, frame.PendingCall.RegisterIndex, frame.PendingCall.ResultCount, resumeValues);
        frame.ClearPendingCall();
        return true;
    }

    private void EnsureCallFrameCapacity()
    {
        if (State.Frames.Count >= MaxCallFrameDepth)
        {
            throw new LuaRuntimeException(LuaValue.FromString("stack overflow"));
        }
    }

    private bool TryCompleteFrame(
        CallFrame frame,
        IReadOnlyList<LuaValue> results,
        int hostCallId,
        out LuaValue[] completedResults)
    {
        completedResults = [];

        if (StartCloseContinuation(
                frame,
                registerIndex: 0,
                LuaPendingCloseContinuationKind.Return,
                results))
        {
            return false;
        }

        var pendingException = CloseResourcesFrom(frame, 0);
        if (pendingException is not null)
        {
            ExceptionDispatchInfo.Capture(pendingException).Throw();
        }

        return CompleteFrameAfterClose(frame, results, hostCallId, out completedResults);
    }

    private bool CompleteFrameAfterClose(
        CallFrame frame,
        IReadOnlyList<LuaValue> results,
        int hostCallId,
        out LuaValue[] completedResults)
    {
        completedResults = [];
        ExecuteReturnHook(frame);
        State.PopFrame();
        State.Stack.SetTop(frame.BaseIndex);

        switch (frame.ReturnTarget.Kind)
        {
            case LuaCallReturnTargetKind.HostCall:
                if (frame.ReturnTarget.HostCallId == hostCallId)
                {
                    completedResults = results.ToArray();
                    return true;
                }

                throw new InvalidOperationException($"Unexpected host call id '{frame.ReturnTarget.HostCallId}'.");
            case LuaCallReturnTargetKind.ThreadRoot:
                State.CurrentThread.MarkCompleted();
                completedResults = results.ToArray();
                return true;
            case LuaCallReturnTargetKind.Registers:
                var callerFrame = frame.ReturnTarget.CallerFrame
                    ?? throw new InvalidOperationException("Register return target is missing the caller frame.");
                WriteCallResults(callerFrame, frame.ReturnTarget.RegisterIndex, frame.ReturnTarget.ResultCount, results);
                return false;
            case LuaCallReturnTargetKind.CloseContinuation:
                return false;
            case LuaCallReturnTargetKind.None:
                return false;
            default:
                throw new InvalidOperationException($"Unknown return target kind '{frame.ReturnTarget.Kind}'.");
        }
    }

    private void WriteCallResults(CallFrame frame, int registerIndex, int resultCount, IReadOnlyList<LuaValue> results)
    {
        if (resultCount < 0)
        {
            WriteOpenResults(frame, registerIndex, results);
            return;
        }

        WriteResults(frame, registerIndex, resultCount, results);
    }

    private void AbortFramesToDepth(int frameDepth, Exception pendingException)
    {
        pendingException = CleanupFramesToDepth(frameDepth, pendingException) ?? pendingException;
        ExceptionDispatchInfo.Capture(pendingException).Throw();
    }

    private Exception? CleanupFramesToDepth(int frameDepth, Exception? pendingException = null)
    {
        while (State.Frames.Count > frameDepth)
        {
            var frame = State.CurrentFrame
                ?? throw new InvalidOperationException("The interpreter has no active call frame.");
            State.PopFrame();
            pendingException = CloseResourcesFrom(frame, 0, pendingException);
            State.Stack.SetTop(frame.BaseIndex);
        }

        return pendingException;
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

    private void ExecuteToBeClosed(LuaPrototype prototype, CallFrame frame, LuaInstruction instruction)
    {
        var instructionIndex = frame.ProgramCounter - 1;
        var variableName =
            prototype.ToBeClosedNames is not null &&
            instructionIndex >= 0 &&
            instructionIndex < prototype.ToBeClosedNames.Length
                ? prototype.ToBeClosedNames[instructionIndex]
                : null;

        RegisterToBeClosed(frame, instruction.A, variableName);
    }

    private void ExecuteClosureInstruction(CallFrame frame, LuaPrototype prototype, LuaInstruction instruction)
    {
        var nestedPrototype = prototype.NestedPrototypes[instruction.Bx];
        var nestedClosure = CreateClosure(nestedPrototype, frame);

        SetRegister(frame, instruction.A, LuaValue.FromFunction(nestedClosure));
    }

    private bool TryHandlePendingCloseException(Exception exception)
    {
        var frames = State.CurrentThread.Frames;
        for (var index = frames.Count - 1; index >= 0; index--)
        {
            if (frames[index].PendingClose is null)
            {
                continue;
            }

            var frameDepth = index + 1;
            var cleanedException = CleanupFramesToDepth(frameDepth, exception) ?? exception;
            frames[index].PendingClose!.PendingException = AnnotateCloseError(cleanedException);
            return true;
        }

        return false;
    }
}
