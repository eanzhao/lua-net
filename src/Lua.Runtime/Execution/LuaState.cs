using Lua.Runtime.Objects;
using Lua.Runtime.Values;
using System.Runtime.CompilerServices;
using System.Text;
using static Lua.Runtime.Values.LuaValueHelper;

namespace Lua.Runtime.Execution;

public sealed partial class LuaState
{
    public delegate LuaClosure BinaryChunkLoader(
        ReadOnlyMemory<byte> chunkBytes,
        string? chunkName,
        bool hasEnvironment,
        LuaValue environment);

    public delegate LuaClosure TextChunkLoader(
        ReadOnlyMemory<byte> chunkBytes,
        string? chunkName,
        bool hasEnvironment,
        LuaValue environment);

    private enum WarningMode
    {
        Off,
        Ready,
        Continue
    }

    private readonly Dictionary<LuaValueKind, LuaTable> _typeMetatables = [];
    private readonly StringBuilder _warningBuffer = new();
    private BinaryChunkLoader? _binaryChunkLoader;
    private TextChunkLoader? _textChunkLoader;
    private Func<LuaValue, IReadOnlyList<LuaValue>, LuaValue[]>? _callableInvoker;
    private Func<LuaThread, IReadOnlyList<LuaValue>, LuaValue[]>? _coroutineResumer;
    private Func<LuaThread, LuaValue[]>? _coroutineCloser;
    private bool _gcRunning = true;
    private WarningMode _warningMode = WarningMode.Off;
    private static readonly LuaValue IPairsAuxFunction = LuaValue.FromFunction(
        new LuaClosure(
            "ipairsaux",
            body: new LuaNativeClosureBody(IPairsAux)));
    private static readonly LuaValue Utf8CodesStrictIteratorFunction = LuaValue.FromFunction(
        new LuaClosure(
            "utf8.codes.iter",
            body: new LuaNativeClosureBody(Utf8CodesIteratorStrict)));
    private static readonly LuaValue Utf8CodesLaxIteratorFunction = LuaValue.FromFunction(
        new LuaClosure(
            "utf8.codes.iterlax",
            body: new LuaNativeClosureBody(Utf8CodesIteratorLax)));
    private static readonly byte[] BinaryChunkSignature = [0x1B, (byte)'L', (byte)'u', (byte)'a'];
    private static readonly UTF8Encoding StrictUtf8Encoding = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private const int MaxUnicode = 0x10FFFF;
    private const string InvalidUtf8CodeMessage = "invalid UTF-8 code";
    private const string Utf8CharPattern = "[\0-\x7F\xC2-\xFD][\x80-\xBF]*";

    public LuaState()
    {
        MainThread = new LuaThread("main", isMainThread: true);
        CurrentThread = MainThread;
        GlobalEnvironment = new LuaTable("_ENV");
        StringLibrary = new LuaTable("string");
        TableLibrary = new LuaTable("table");
        MathLibrary = new LuaTable("math");
        Utf8Library = new LuaTable("utf8");
        CoroutineLibrary = new LuaTable("coroutine");
        PackageLibrary = new LuaTable("package");
        PackageLoaded = new LuaTable("package.loaded");
        PackagePreload = new LuaTable("package.preload");
        PackageSearchers = new LuaTable("package.searchers");
        RegisterBaseFunctions();
        RegisterStringSupport();
        RegisterTableSupport();
        RegisterMathSupport();
        RegisterUtf8Support();
        RegisterCoroutineSupport();
        RegisterPackageSupport();
    }

    public LuaThread MainThread { get; }

    public LuaThread CurrentThread { get; private set; }

    public LuaStack Stack => CurrentThread.Stack;

    public LuaTable GlobalEnvironment { get; }

    public LuaTable StringLibrary { get; }

    public LuaTable TableLibrary { get; }

    public LuaTable MathLibrary { get; }

    public LuaTable Utf8Library { get; }

    public LuaTable CoroutineLibrary { get; }

    public LuaTable PackageLibrary { get; }

    public LuaTable PackageLoaded { get; }

    public LuaTable PackagePreload { get; }

    public LuaTable PackageSearchers { get; }

    public Action<string> PrintOutput { get; set; } = static _ => { };

    public Action<string> WarningOutput { get; set; } = static _ => { };

    public Func<string, byte[]> FileReader { get; set; } = static path => File.ReadAllBytes(path);

    public IReadOnlyList<CallFrame> Frames => CurrentThread.Frames;

    public CallFrame? CurrentFrame => CurrentThread.CurrentFrame;

    private void RegisterBaseFunctions()
    {
        RegisterBaseFunction("setmetatable", SetMetatable);
        RegisterBaseFunction("getmetatable", GetMetatable);
        RegisterBaseFunction("rawequal", RawEqual);
        RegisterBaseFunction("rawlen", RawLen);
        RegisterBaseFunction("rawget", RawGet);
        RegisterBaseFunction("rawset", RawSet);
        RegisterBaseFunction("next", Next);
        RegisterBaseFunction("pairs", Pairs);
        RegisterBaseFunction("ipairs", IPairs);
        RegisterBaseFunction("collectgarbage", CollectGarbage);
        RegisterBaseFunction("load", Load);
        RegisterBaseFunction("loadfile", LoadFile);
        RegisterBaseFunction("dofile", DoFile);
        RegisterBaseFunction("print", Print);
        RegisterBaseFunction("warn", Warn);
        RegisterBaseFunction("type", Type);
        RegisterBaseFunction("assert", Assert);
        RegisterBaseFunction("select", Select);
        RegisterBaseFunction("tonumber", ToNumber);
        RegisterBaseFunction("tostring", ToString);
        RegisterBaseFunction("pcall", ProtectedCall);
        RegisterBaseFunction("xpcall", ExtendedProtectedCall);
        RegisterBaseFunction("error", Error);
    }

    private void RegisterCoroutineSupport()
    {
        RegisterLibraryFunction(CoroutineLibrary, "create", CoroutineCreate, "coroutine.create");
        RegisterLibraryFunction(CoroutineLibrary, "resume", CoroutineResume, "coroutine.resume");
        RegisterLibraryFunction(CoroutineLibrary, "yield", CoroutineYield, "coroutine.yield");
        RegisterLibraryFunction(CoroutineLibrary, "wrap", CoroutineWrap, "coroutine.wrap");
        RegisterLibraryFunction(CoroutineLibrary, "status", CoroutineStatus, "coroutine.status");
        RegisterLibraryFunction(CoroutineLibrary, "isyieldable", CoroutineIsYieldable, "coroutine.isyieldable");
        RegisterLibraryFunction(CoroutineLibrary, "close", CoroutineClose, "coroutine.close");
        RegisterLibraryFunction(CoroutineLibrary, "running", CoroutineRunning, "coroutine.running");

        GlobalEnvironment.SetValue(LuaValue.FromString("coroutine"), LuaValue.FromTable(CoroutineLibrary));
    }

    private void RegisterPackageSupport()
    {
        PackageLibrary.SetValue(LuaValue.FromString("path"), LuaValue.FromString(GetDefaultPackagePath()));
        PackageLibrary.SetValue(LuaValue.FromString("cpath"), LuaValue.FromString(GetDefaultNativePackagePath()));
        PackageLibrary.SetValue(LuaValue.FromString("config"), LuaValue.FromString(CreatePackageConfigString()));
        PackageLibrary.SetValue(LuaValue.FromString("loaded"), LuaValue.FromTable(PackageLoaded));
        PackageLibrary.SetValue(LuaValue.FromString("preload"), LuaValue.FromTable(PackagePreload));
        PackageLibrary.SetValue(LuaValue.FromString("searchers"), LuaValue.FromTable(PackageSearchers));
        RegisterLibraryFunction(PackageLibrary, "loadlib", PackageLoadLib, "package.loadlib");
        RegisterLibraryFunction(PackageLibrary, "searchpath", PackageSearchPath, "package.searchpath");

        PackageLoaded.SetValue(LuaValue.FromString("package"), LuaValue.FromTable(PackageLibrary));
        PackageLoaded.SetValue(LuaValue.FromString("_G"), LuaValue.FromTable(GlobalEnvironment));

        PackageSearchers.SetValue(
            LuaValue.FromInteger(1),
            LuaValue.FromFunction(new LuaClosure(
                "package.searcher.preload",
                body: new LuaNativeClosureBody(PackageSearcherPreload))));
        PackageSearchers.SetValue(
            LuaValue.FromInteger(2),
            LuaValue.FromFunction(new LuaClosure(
                "package.searcher.lua",
                body: new LuaNativeClosureBody(PackageSearcherLua))));
        PackageSearchers.SetValue(
            LuaValue.FromInteger(3),
            LuaValue.FromFunction(new LuaClosure(
                "package.searcher.c",
                body: new LuaNativeClosureBody(PackageSearcherC))));
        PackageSearchers.SetValue(
            LuaValue.FromInteger(4),
            LuaValue.FromFunction(new LuaClosure(
                "package.searcher.croot",
                body: new LuaNativeClosureBody(PackageSearcherCRoot))));

        GlobalEnvironment.SetValue(LuaValue.FromString("package"), LuaValue.FromTable(PackageLibrary));
        RegisterBaseFunction("require", Require);
    }

    private void RegisterStringSupport()
    {
        RegisterLibraryFunction(StringLibrary, "byte", StringByte, "string.byte");
        RegisterLibraryFunction(StringLibrary, "char", StringChar, "string.char");
        RegisterLibraryFunction(StringLibrary, "find", StringFind, "string.find");
        RegisterLibraryFunction(StringLibrary, "format", StringFormat, "string.format");
        RegisterLibraryFunction(StringLibrary, "gmatch", StringGMatch, "string.gmatch");
        RegisterLibraryFunction(StringLibrary, "gsub", StringGSub, "string.gsub");
        RegisterLibraryFunction(StringLibrary, "upper", StringUpper, "string.upper");
        RegisterLibraryFunction(StringLibrary, "lower", StringLower, "string.lower");
        RegisterLibraryFunction(StringLibrary, "len", StringLen, "string.len");
        RegisterLibraryFunction(StringLibrary, "match", StringMatch, "string.match");
        RegisterLibraryFunction(StringLibrary, "pack", StringPack, "string.pack");
        RegisterLibraryFunction(StringLibrary, "packsize", StringPackSize, "string.packsize");
        RegisterLibraryFunction(StringLibrary, "rep", StringRep, "string.rep");
        RegisterLibraryFunction(StringLibrary, "reverse", StringReverse, "string.reverse");
        RegisterLibraryFunction(StringLibrary, "sub", StringSub, "string.sub");
        RegisterLibraryFunction(StringLibrary, "unpack", StringUnpack, "string.unpack");

        GlobalEnvironment.SetValue(LuaValue.FromString("string"), LuaValue.FromTable(StringLibrary));

        var metatable = new LuaTable("string-metatable");
        RegisterLibraryFunction(metatable, "__add", StringAdd, "__add");
        RegisterLibraryFunction(metatable, "__sub", StringSubtract, "__sub");
        RegisterLibraryFunction(metatable, "__mul", StringMultiply, "__mul");
        RegisterLibraryFunction(metatable, "__mod", StringModulo, "__mod");
        RegisterLibraryFunction(metatable, "__pow", StringPower, "__pow");
        RegisterLibraryFunction(metatable, "__div", StringDivide, "__div");
        RegisterLibraryFunction(metatable, "__idiv", StringIntegerDivide, "__idiv");
        RegisterLibraryFunction(metatable, "__unm", StringUnaryMinus, "__unm");
        metatable.SetValue(LuaValue.FromString("__index"), LuaValue.FromTable(StringLibrary));
        SetTypeMetatable(LuaValueKind.String, metatable);
    }

    private void RegisterTableSupport()
    {
        RegisterLibraryFunction(TableLibrary, "concat", TableConcat, "table.concat");
        RegisterLibraryFunction(TableLibrary, "insert", TableInsert, "table.insert");
        RegisterLibraryFunction(TableLibrary, "remove", TableRemove, "table.remove");
        RegisterLibraryFunction(TableLibrary, "move", TableMove, "table.move");
        RegisterLibraryFunction(TableLibrary, "sort", TableSort, "table.sort");
        RegisterLibraryFunction(TableLibrary, "pack", TablePack, "table.pack");
        RegisterLibraryFunction(TableLibrary, "unpack", TableUnpack, "table.unpack");

        GlobalEnvironment.SetValue(LuaValue.FromString("table"), LuaValue.FromTable(TableLibrary));
    }

    private void RegisterMathSupport()
    {
        RegisterLibraryFunction(MathLibrary, "abs", MathAbs, "math.abs");
        RegisterLibraryFunction(MathLibrary, "ceil", MathCeil, "math.ceil");
        RegisterLibraryFunction(MathLibrary, "floor", MathFloor, "math.floor");
        RegisterLibraryFunction(MathLibrary, "max", MathMax, "math.max");
        RegisterLibraryFunction(MathLibrary, "min", MathMin, "math.min");
        RegisterLibraryFunction(MathLibrary, "sqrt", MathSqrt, "math.sqrt");
        RegisterLibraryFunction(MathLibrary, "log", MathLog, "math.log");
        RegisterLibraryFunction(MathLibrary, "exp", MathExp, "math.exp");
        RegisterLibraryFunction(MathLibrary, "sin", MathSin, "math.sin");
        RegisterLibraryFunction(MathLibrary, "cos", MathCos, "math.cos");
        RegisterLibraryFunction(MathLibrary, "tan", MathTan, "math.tan");
        RegisterLibraryFunction(MathLibrary, "asin", MathAsin, "math.asin");
        RegisterLibraryFunction(MathLibrary, "acos", MathAcos, "math.acos");
        RegisterLibraryFunction(MathLibrary, "atan", MathAtan, "math.atan");
        RegisterLibraryFunction(MathLibrary, "deg", MathDeg, "math.deg");
        RegisterLibraryFunction(MathLibrary, "rad", MathRad, "math.rad");
        RegisterLibraryFunction(MathLibrary, "fmod", MathFMod, "math.fmod");
        RegisterLibraryFunction(MathLibrary, "modf", MathModF, "math.modf");
        RegisterLibraryFunction(MathLibrary, "tointeger", MathToInteger, "math.tointeger");
        RegisterLibraryFunction(MathLibrary, "type", MathType, "math.type");
        RegisterLibraryFunction(MathLibrary, "ult", MathUnsignedLessThan, "math.ult");

        MathLibrary.SetValue(LuaValue.FromString("pi"), LuaValue.FromFloat(Math.PI));
        MathLibrary.SetValue(LuaValue.FromString("huge"), LuaValue.FromFloat(double.PositiveInfinity));
        MathLibrary.SetValue(LuaValue.FromString("maxinteger"), LuaValue.FromInteger(long.MaxValue));
        MathLibrary.SetValue(LuaValue.FromString("mininteger"), LuaValue.FromInteger(long.MinValue));

        GlobalEnvironment.SetValue(LuaValue.FromString("math"), LuaValue.FromTable(MathLibrary));
    }

    private void RegisterUtf8Support()
    {
        RegisterLibraryFunction(Utf8Library, "offset", Utf8Offset, "utf8.offset");
        RegisterLibraryFunction(Utf8Library, "codepoint", Utf8CodePoint, "utf8.codepoint");
        RegisterLibraryFunction(Utf8Library, "char", Utf8Char, "utf8.char");
        RegisterLibraryFunction(Utf8Library, "len", Utf8Len, "utf8.len");
        RegisterLibraryFunction(Utf8Library, "codes", Utf8Codes, "utf8.codes");
        Utf8Library.SetValue(LuaValue.FromString("charpattern"), LuaValue.FromString(Utf8CharPattern));

        GlobalEnvironment.SetValue(LuaValue.FromString("utf8"), LuaValue.FromTable(Utf8Library));
    }

    private void RegisterBaseFunction(string name, LuaNativeFunction function)
    {
        RegisterLibraryFunction(GlobalEnvironment, name, function, name);
    }

    private static void RegisterLibraryFunction(
        LuaTable table,
        string name,
        LuaNativeFunction function,
        string debugName)
    {
        var closure = new LuaClosure(
            debugName,
            body: new LuaNativeClosureBody(function));
        table.SetValue(
            LuaValue.FromString(name),
            LuaValue.FromFunction(closure));
    }

    public void PushFrame(CallFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        CurrentThread.PushFrame(frame);
    }

    public CallFrame PopFrame()
    {
        return CurrentThread.PopFrame();
    }

    public void SetCallableInvoker(Func<LuaValue, IReadOnlyList<LuaValue>, LuaValue[]> callableInvoker)
    {
        ArgumentNullException.ThrowIfNull(callableInvoker);
        _callableInvoker = callableInvoker;
    }

    public void SetBinaryChunkLoader(BinaryChunkLoader binaryChunkLoader)
    {
        ArgumentNullException.ThrowIfNull(binaryChunkLoader);
        _binaryChunkLoader = binaryChunkLoader;
    }

    public void SetTextChunkLoader(TextChunkLoader textChunkLoader)
    {
        ArgumentNullException.ThrowIfNull(textChunkLoader);
        _textChunkLoader = textChunkLoader;
    }

    public void SetCoroutineResumer(Func<LuaThread, IReadOnlyList<LuaValue>, LuaValue[]> coroutineResumer)
    {
        ArgumentNullException.ThrowIfNull(coroutineResumer);
        _coroutineResumer = coroutineResumer;
    }

    public void SetCoroutineCloser(Func<LuaThread, LuaValue[]> coroutineCloser)
    {
        ArgumentNullException.ThrowIfNull(coroutineCloser);
        _coroutineCloser = coroutineCloser;
    }

    public LuaThread SwitchCurrentThread(LuaThread thread)
    {
        ArgumentNullException.ThrowIfNull(thread);

        var previous = CurrentThread;
        CurrentThread = thread;
        return previous;
    }

    public LuaValue[] InvokeCallable(LuaValue callable, IReadOnlyList<LuaValue> arguments)
    {
        if (_callableInvoker is not null)
        {
            return _callableInvoker(callable, arguments);
        }

        if (callable.Kind == LuaValueKind.Function &&
            callable.AsFunction().Body is LuaNativeClosureBody body)
        {
            return body.Function(this, callable.AsFunction(), arguments);
        }

        throw new InvalidOperationException("Callable invoker is not configured.");
    }

    public bool TryGetRawMetatable(LuaValue value, out LuaTable? metatable)
    {
        switch (value.Kind)
        {
            case LuaValueKind.Table:
                metatable = value.AsTable().Metatable;
                return metatable is not null;
            case LuaValueKind.UserData:
                metatable = value.AsUserData().Metatable;
                return metatable is not null;
            default:
                return _typeMetatables.TryGetValue(value.Kind, out metatable);
        }
    }

    public bool TryGetMetamethod(LuaValue value, string metamethodName, out LuaValue metamethod)
    {
        if (TryGetRawMetatable(value, out var metatable) && metatable is not null)
        {
            return metatable.TryGetValue(LuaValue.FromString(metamethodName), out metamethod) && !metamethod.IsNil;
        }

        metamethod = LuaValue.Nil;
        return false;
    }

    private void SetTypeMetatable(LuaValueKind kind, LuaTable metatable)
    {
        _typeMetatables[kind] = metatable;
    }

    private static LuaValue[] SetMetatable(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {

        var tableValue = RequireArgument(arguments, 0, "setmetatable");
        if (tableValue.Kind != LuaValueKind.Table)
        {
            throw CreateArgumentTypeError("setmetatable", 1, "table", tableValue);
        }

        var table = tableValue.AsTable();
        if (TryGetProtectedMetatableValue(table.Metatable, out _))
        {
            throw CreateRuntimeError("cannot change a protected metatable");
        }

        var metatableValue = RequireArgument(arguments, 1, "setmetatable");

        if (metatableValue.IsNil)
        {
            table.SetMetatable(null);
            return [tableValue];
        }

        if (metatableValue.Kind != LuaValueKind.Table)
        {
            throw CreateArgumentTypeError("setmetatable", 2, "nil or table", metatableValue);
        }

        table.SetMetatable(metatableValue.AsTable());
        return [tableValue];
    }

    private static LuaValue[] GetMetatable(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {

        var value = RequireArgument(arguments, 0, "getmetatable");
        if (!state.TryGetRawMetatable(value, out var metatable) || metatable is null)
        {
            return [LuaValue.Nil];
        }

        if (TryGetProtectedMetatableValue(metatable, out var protectedValue))
        {
            return [protectedValue];
        }

        return [LuaValue.FromTable(metatable)];
    }

    private static LuaValue[] RawEqual(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {

        var left = RequireArgument(arguments, 0, "rawequal");
        var right = RequireArgument(arguments, 1, "rawequal");
        return [LuaValue.FromBoolean(AreRawEqual(left, right))];
    }

    private static LuaValue[] RawLen(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {

        var value = RequireArgument(arguments, 0, "rawlen");
        return value.Kind switch
        {
            LuaValueKind.Table => [LuaValue.FromInteger(value.AsTable().GetSequenceLength())],
            LuaValueKind.String => [LuaValue.FromInteger(GetLuaStringBytes(value.AsString()).Length)],
            _ => throw CreateArgumentTypeError("rawlen", 1, "table or string", value)
        };
    }

    private static LuaValue[] RawGet(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {

        var tableValue = RequireArgument(arguments, 0, "rawget");
        if (tableValue.Kind != LuaValueKind.Table)
        {
            throw CreateArgumentTypeError("rawget", 1, "table", tableValue);
        }

        var key = RequireArgument(arguments, 1, "rawget");
        if (key.IsNil || IsNaNKey(key))
        {
            return [LuaValue.Nil];
        }

        return [tableValue.AsTable().GetValue(key)];
    }

    private static LuaValue[] RawSet(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {

        var tableValue = RequireArgument(arguments, 0, "rawset");
        if (tableValue.Kind != LuaValueKind.Table)
        {
            throw CreateArgumentTypeError("rawset", 1, "table", tableValue);
        }

        var key = RequireArgument(arguments, 1, "rawset");
        ValidateTableAssignmentKey(key);

        tableValue.AsTable().SetValue(key, RequireArgument(arguments, 2, "rawset"));
        return [tableValue];
    }

    private static LuaValue[] Next(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {

        var tableValue = RequireArgument(arguments, 0, "next");
        if (tableValue.Kind != LuaValueKind.Table)
        {
            throw CreateArgumentTypeError("next", 1, "table", tableValue);
        }

        var currentKey = arguments.Count >= 2 ? arguments[1] : LuaValue.Nil;
        return tableValue.AsTable().TryGetNextEntry(currentKey, out var nextKey, out var nextValue)
            ? [nextKey, nextValue]
            : [LuaValue.Nil];
    }

    private static LuaValue[] Pairs(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {

        var value = RequireArgument(arguments, 0, "pairs");
        if (state.TryGetMetamethod(value, "__pairs", out var metamethod))
        {
            return NormalizeResults(state.InvokeCallable(metamethod, [value]), 4);
        }

        return
        [
            GetBaseFunctionValue(state, "next"),
            value,
            LuaValue.Nil,
            LuaValue.Nil
        ];
    }

    private static LuaValue[] IPairs(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {

        var value = RequireArgument(arguments, 0, "ipairs");
        return [IPairsAuxFunction, value, LuaValue.FromInteger(0)];
    }

    private static LuaValue[] IPairsAux(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {

        var tableValue = RequireArgument(arguments, 0, "ipairsaux");
        if (tableValue.Kind != LuaValueKind.Table)
        {
            throw CreateArgumentTypeError("ipairsaux", 1, "table", tableValue);
        }

        var indexValue = RequireArgument(arguments, 1, "ipairsaux");
        if (!TryGetInteger(indexValue, out var index))
        {
            throw CreateArgumentTypeError("ipairsaux", 2, "integer", indexValue);
        }

        var nextIndex = unchecked(index + 1);
        var nextValue = tableValue.AsTable().GetValue(LuaValue.FromInteger(nextIndex));
        return nextValue.IsNil
            ? [LuaValue.Nil]
            : [LuaValue.FromInteger(nextIndex), nextValue];
    }

    private static LuaValue[] Type(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {

        var value = RequireArgument(arguments, 0, "type");
        return [LuaValue.FromString(GetTypeName(value))];
    }

    private static LuaValue[] CollectGarbage(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var option = GetOptionalStringArgument(arguments, 0, "collect", "collectgarbage");

        switch (option)
        {
            case "stop":
                state._gcRunning = false;
                return [LuaValue.FromInteger(0)];
            case "restart":
                state._gcRunning = true;
                return [LuaValue.FromInteger(0)];
            case "collect":
                GC.Collect();
                GC.WaitForPendingFinalizers();
                return [LuaValue.FromInteger(0)];
            case "count":
                return [LuaValue.FromFloat(GC.GetTotalMemory(forceFullCollection: false) / 1024d)];
            case "step":
            {
                if (arguments.Count > 1 && !TryGetInteger(arguments[1], out _))
                {
                    throw CreateArgumentTypeError("collectgarbage", 2, "integer", arguments[1]);
                }

                return [LuaValue.FromBoolean(false)];
            }
            case "isrunning":
                return [LuaValue.FromBoolean(state._gcRunning)];
            default:
                throw CreateArgumentError("collectgarbage", 1, $"invalid option '{option}'");
        }
    }

    private static LuaValue[] Print(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {

        var values = new string[arguments.Count];
        for (var index = 0; index < arguments.Count; index++)
        {
            values[index] = ConvertToPrintedString(state, arguments[index]);
        }

        state.PrintOutput(string.Join('\t', values));
        return [];
    }

    private static LuaValue[] Require(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var moduleName = RequireStringArgument(arguments, 0, "require");
        var moduleNameValue = LuaValue.FromString(moduleName);
        var loadedTable = state.GetPackageTableField("loaded");
        var loadedValue = loadedTable.GetValue(moduleNameValue);
        if (IsTruthy(loadedValue))
        {
            return [loadedValue];
        }

        var (loader, loaderData) = state.FindPackageLoader(moduleName);
        var loaderResults = state.InvokeCallable(loader, [moduleNameValue, loaderData]);
        var loaderResult = loaderResults.Length == 0 ? LuaValue.Nil : loaderResults[0];
        if (!loaderResult.IsNil)
        {
            loadedTable.SetValue(moduleNameValue, loaderResult);
        }

        var moduleValue = loadedTable.GetValue(moduleNameValue);
        if (moduleValue.IsNil)
        {
            moduleValue = LuaValue.FromBoolean(true);
            loadedTable.SetValue(moduleNameValue, moduleValue);
        }

        return [moduleValue, loaderData];
    }

    private static LuaValue[] Load(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var source = RequireArgument(arguments, 0, "load");
        var chunkName = GetOptionalStringArgument(
            arguments,
            1,
            source.Kind == LuaValueKind.String ? source.AsString() : "=(load)",
            "load");
        var mode = GetLoadMode(arguments, 2, "load");
        var hasEnvironment = arguments.Count > 3;
        var environment = hasEnvironment ? arguments[3] : LuaValue.Nil;

        if (source.Kind == LuaValueKind.String)
        {
            return state.LoadChunk(
                EncodeLuaString(source.AsString()),
                chunkName,
                mode,
                hasEnvironment,
                environment);
        }

        if (source.Kind != LuaValueKind.Function)
        {
            throw CreateArgumentTypeError("load", 1, "string or function", source);
        }

        var readerOutput = new StringBuilder();
        while (true)
        {
            var results = state.InvokeCallable(source, []);
            var piece = results.Length == 0 ? LuaValue.Nil : results[0];
            if (piece.IsNil)
            {
                break;
            }

            if (!TryConvertToStringArgument(piece, out var text))
            {
                throw CreateRuntimeError("reader function must return a string");
            }

            readerOutput.Append(text);
        }

        return state.LoadChunk(
            EncodeLuaString(readerOutput.ToString()),
            chunkName,
            mode,
            hasEnvironment,
            environment);
    }

    private static LuaValue[] LoadFile(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var fileName = GetOptionalFileName(arguments, 0, "loadfile");
        if (fileName is null)
        {
            return [LuaValue.Nil, LuaValue.FromString("stdin loading is not supported yet")];
        }

        var mode = GetLoadMode(arguments, 1, "loadfile");
        var hasEnvironment = arguments.Count > 2;
        var environment = hasEnvironment ? arguments[2] : LuaValue.Nil;

        if (!state.TryReadChunkFile(fileName, out var bytes, out var errorMessage))
        {
            return [LuaValue.Nil, LuaValue.FromString(errorMessage)];
        }

        return state.LoadChunk(bytes, fileName, mode, hasEnvironment, environment);
    }

    private static LuaValue[] DoFile(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var fileName = GetOptionalFileName(arguments, 0, "dofile");
        if (fileName is null)
        {
            throw CreateRuntimeError("stdin loading is not supported yet");
        }

        if (!state.TryReadChunkFile(fileName, out var bytes, out var errorMessage))
        {
            throw new LuaRuntimeException(LuaValue.FromString(errorMessage));
        }

        var loadResults = state.LoadChunk(
            bytes,
            fileName,
            mode: "bt",
            hasEnvironment: false,
            environment: LuaValue.Nil);
        if (loadResults[0].IsNil)
        {
            throw new LuaRuntimeException(loadResults[1]);
        }

        return state.InvokeCallable(loadResults[0], []);
    }

    private static LuaValue[] PackageSearcherPreload(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var moduleName = RequireStringArgument(arguments, 0, "package.searcher.preload");
        var loader = state.GetPackageTableField("preload").GetValue(LuaValue.FromString(moduleName));
        return loader.IsNil
            ? [LuaValue.FromString($"no field package.preload['{moduleName}']")]
            : [loader, LuaValue.FromString(":preload:")];
    }

    private static LuaValue[] PackageSearcherLua(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var moduleName = RequireStringArgument(arguments, 0, "package.searcher.lua");
        if (!state.TrySearchPackageFile(moduleName, "path", Path.DirectorySeparatorChar.ToString(), out var filename, out var errorMessage))
        {
            return [LuaValue.FromString(errorMessage)];
        }

        if (!state.TryReadChunkFile(filename, out var bytes, out errorMessage))
        {
            throw CreateRuntimeError(
                $"error loading module '{moduleName}' from file '{filename}':\n\t{errorMessage}");
        }

        var loadResults = state.LoadChunk(
            bytes,
            filename,
            mode: "bt",
            hasEnvironment: false,
            environment: LuaValue.Nil);
        if (loadResults[0].IsNil)
        {
            throw CreateRuntimeError(
                $"error loading module '{moduleName}' from file '{filename}':\n\t{FormatLuaValue(loadResults[1])}");
        }

        return [loadResults[0], LuaValue.FromString(filename)];
    }

    private static LuaValue[] Warn(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {

        var message = RequireArgument(arguments, 0, "warn");
        state.EmitWarning(ConvertToWarningString(message, 1), toContinue: arguments.Count > 1);

        for (var index = 1; index < arguments.Count; index++)
        {
            state.EmitWarning(
                ConvertToWarningString(arguments[index], index + 1),
                toContinue: index + 1 < arguments.Count);
        }

        return [];
    }

    private static LuaValue[] StringUpper(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {

        var value = RequireStringArgument(arguments, 0, "string.upper");
        return [LuaValue.FromString(value.ToUpperInvariant())];
    }

    private static LuaValue[] StringLower(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {

        var value = RequireStringArgument(arguments, 0, "string.lower");
        return [LuaValue.FromString(value.ToLowerInvariant())];
    }

    private static LuaValue[] StringLen(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {

        var value = RequireStringArgument(arguments, 0, "string.len");
        return [LuaValue.FromInteger(GetLuaStringBytes(value).Length)];
    }

    private static LuaValue[] StringAdd(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        return ExecuteStringBinaryArithmetic(state, closure, arguments, "__add", "add", TryAdd);
    }

    private static LuaValue[] StringSubtract(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        return ExecuteStringBinaryArithmetic(state, closure, arguments, "__sub", "subtract", TrySubtract);
    }

    private static LuaValue[] StringMultiply(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        return ExecuteStringBinaryArithmetic(state, closure, arguments, "__mul", "multiply", TryMultiply);
    }

    private static LuaValue[] StringModulo(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        return ExecuteStringBinaryArithmetic(state, closure, arguments, "__mod", "modulo", TryModulo);
    }

    private static LuaValue[] StringPower(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        return ExecuteStringBinaryArithmetic(state, closure, arguments, "__pow", "power", TryPower);
    }

    private static LuaValue[] StringDivide(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        return ExecuteStringBinaryArithmetic(state, closure, arguments, "__div", "divide", TryDivide);
    }

    private static LuaValue[] StringIntegerDivide(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        return ExecuteStringBinaryArithmetic(state, closure, arguments, "__idiv", "divide", TryIntegerDivide);
    }

    private static LuaValue[] StringUnaryMinus(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {

        var value = RequireArgument(arguments, 0, "__unm");
        if (TryConvertToNumber(value, out var number))
        {
            var (success, result) = TryUnaryMinus(number);
            if (success)
            {
                return [result];
            }
        }

        throw CreateRuntimeError($"attempt to perform arithmetic on a '{GetTypeName(value)}'");
    }

    private static LuaValue[] TableConcat(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var table = RequireTableArgument(arguments, 0, "table.concat");
        var separator = GetOptionalStringArgument(arguments, 1, string.Empty, "table.concat");
        var start = GetOptionalIntegerArgument(arguments, 2, 1, "table.concat");
        var end = GetOptionalIntegerArgument(arguments, 3, table.GetSequenceLength(), "table.concat");
        if (start > end)
        {
            return [LuaValue.FromString(string.Empty)];
        }

        var buffer = new List<byte>();
        var separatorBytes = GetLuaStringBytes(separator);
        for (var index = start; index <= end; index++)
        {
            var field = table.GetValue(LuaValue.FromInteger(index));
            if (!TryConvertToStringArgument(field, out var text))
            {
                throw CreateRuntimeError(
                    $"invalid value ({GetTypeName(field)}) at index {index.ToString(System.Globalization.CultureInfo.InvariantCulture)} in table for 'concat'");
            }

            if (index > start)
            {
                buffer.AddRange(separatorBytes);
            }

            buffer.AddRange(GetLuaStringBytes(text));
        }

        return [LuaValue.FromString(CreateLuaString(buffer.ToArray()))];
    }

    private static LuaValue[] TableInsert(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var table = RequireTableArgument(arguments, 0, "table.insert");
        var firstEmptyIndex = checked(table.GetSequenceLength() + 1);

        long position;
        LuaValue value;
        switch (arguments.Count)
        {
            case 2:
                position = firstEmptyIndex;
                value = arguments[1];
                break;
            case 3:
                position = RequireIntegerArgument(arguments, 1, "table.insert");
                if (position < 1 || position > firstEmptyIndex)
                {
                    throw CreateArgumentError("table.insert", 2, "position out of bounds");
                }

                value = arguments[2];
                for (var index = firstEmptyIndex; index > position; index--)
                {
                    table.SetValue(
                        LuaValue.FromInteger(index),
                        table.GetValue(LuaValue.FromInteger(index - 1)));
                }

                break;
            default:
                throw CreateRuntimeError("wrong number of arguments to 'insert'");
        }

        table.SetValue(LuaValue.FromInteger(position), value);
        return [];
    }

    private static LuaValue[] TableRemove(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var table = RequireTableArgument(arguments, 0, "table.remove");
        var size = table.GetSequenceLength();
        var position = arguments.Count > 1 && !arguments[1].IsNil
            ? RequireIntegerArgument(arguments, 1, "table.remove")
            : size;

        if (position != size && (position < 1 || position > size + 1))
        {
            throw CreateArgumentError("table.remove", 2, "position out of bounds");
        }

        var result = table.GetValue(LuaValue.FromInteger(position));
        var index = position;
        for (; index < size; index++)
        {
            table.SetValue(
                LuaValue.FromInteger(index),
                table.GetValue(LuaValue.FromInteger(index + 1)));
        }

        table.SetValue(LuaValue.FromInteger(index), LuaValue.Nil);
        return [result];
    }

    private static LuaValue[] TableMove(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var source = RequireTableArgument(arguments, 0, "table.move");
        var from = RequireIntegerArgument(arguments, 1, "table.move");
        var to = RequireIntegerArgument(arguments, 2, "table.move");
        var target = RequireIntegerArgument(arguments, 3, "table.move");
        var destination = arguments.Count > 4 && !arguments[4].IsNil
            ? RequireTableArgument(arguments, 4, "table.move")
            : source;

        if (to >= from)
        {
            long count;
            try
            {
                count = checked(to - from + 1);
            }
            catch (OverflowException)
            {
                throw CreateArgumentError("table.move", 3, "too many elements to move");
            }

            try
            {
                _ = checked(target + count - 1);
            }
            catch (OverflowException)
            {
                throw CreateArgumentError("table.move", 4, "destination wrap around");
            }

            if (!ReferenceEquals(source, destination) || target <= from || target > to)
            {
                for (var offset = 0L; offset < count; offset++)
                {
                    destination.SetValue(
                        LuaValue.FromInteger(target + offset),
                        source.GetValue(LuaValue.FromInteger(from + offset)));
                }
            }
            else
            {
                for (var offset = count - 1; offset >= 0; offset--)
                {
                    destination.SetValue(
                        LuaValue.FromInteger(target + offset),
                        source.GetValue(LuaValue.FromInteger(from + offset)));
                }
            }
        }

        return [LuaValue.FromTable(destination)];
    }

    private static LuaValue[] TableSort(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var table = RequireTableArgument(arguments, 0, "table.sort");
        LuaValue? comparator = null;
        if (arguments.Count > 1 && !arguments[1].IsNil)
        {
            if (arguments[1].Kind != LuaValueKind.Function)
            {
                throw CreateArgumentTypeError("table.sort", 2, "function", arguments[1]);
            }

            comparator = arguments[1];
        }

        var length = table.GetSequenceLength();
        if (length <= 1)
        {
            return [];
        }

        if (length > int.MaxValue)
        {
            throw CreateArgumentError("table.sort", 1, "array too big");
        }

        var values = new LuaValue[(int)length];
        for (var index = 0; index < length; index++)
        {
            values[index] = table.GetValue(LuaValue.FromInteger(index + 1));
        }

        Array.Sort(values, (left, right) => CompareTableSortValues(state, left, right, comparator));

        for (var index = 1; index < values.Length; index++)
        {
            if (CompareTableSortValues(state, values[index], values[index - 1], comparator) < 0)
            {
                throw CreateRuntimeError("invalid order function for sorting");
            }
        }

        for (var index = 0; index < values.Length; index++)
        {
            table.SetValue(LuaValue.FromInteger(index + 1), values[index]);
        }

        return [];
    }

    private static LuaValue[] TablePack(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var table = new LuaTable("table.pack", arrayCapacity: arguments.Count, hashCapacity: 1);
        for (var index = 0; index < arguments.Count; index++)
        {
            table.SetValue(LuaValue.FromInteger(index + 1), arguments[index]);
        }

        table.SetValue(LuaValue.FromString("n"), LuaValue.FromInteger(arguments.Count));
        return [LuaValue.FromTable(table)];
    }

    private static LuaValue[] TableUnpack(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var table = RequireTableArgument(arguments, 0, "table.unpack");
        var start = GetOptionalIntegerArgument(arguments, 1, 1, "table.unpack");
        var end = GetOptionalIntegerArgument(arguments, 2, table.GetSequenceLength(), "table.unpack");
        if (start > end)
        {
            return [];
        }

        long resultCount;
        try
        {
            resultCount = checked(end - start + 1);
        }
        catch (OverflowException)
        {
            throw CreateRuntimeError("too many results to unpack");
        }

        if (resultCount > int.MaxValue)
        {
            throw CreateRuntimeError("too many results to unpack");
        }

        var results = new LuaValue[(int)resultCount];
        for (var index = 0; index < results.Length; index++)
        {
            results[index] = table.GetValue(LuaValue.FromInteger(start + index));
        }

        return results;
    }

    private static LuaValue[] MathAbs(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var value = RequireNumberArgument(arguments, 0, "math.abs");
        if (value.Kind == LuaValueKind.Integer)
        {
            var integer = value.AsInteger();
            if (integer < 0)
            {
                integer = unchecked((long)(0UL - (ulong)integer));
            }

            return [LuaValue.FromInteger(integer)];
        }

        return [LuaValue.FromFloat(Math.Abs(ToDouble(value)))];
    }

    private static LuaValue[] MathCeil(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var value = RequireNumberArgument(arguments, 0, "math.ceil");
        return value.Kind == LuaValueKind.Integer
            ? [value]
            : [CreateNumericResult(Math.Ceiling(ToDouble(value)))];
    }

    private static LuaValue[] MathFloor(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var value = RequireNumberArgument(arguments, 0, "math.floor");
        return value.Kind == LuaValueKind.Integer
            ? [value]
            : [CreateNumericResult(Math.Floor(ToDouble(value)))];
    }

    private static LuaValue[] MathMax(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var best = RequireNumberArgument(arguments, 0, "math.max");
        var bestNumber = ToDouble(best);

        for (var index = 1; index < arguments.Count; index++)
        {
            var candidate = RequireNumberArgument(arguments, index, "math.max");
            var candidateNumber = ToDouble(candidate);
            if (candidateNumber > bestNumber)
            {
                best = candidate;
                bestNumber = candidateNumber;
            }
        }

        return [best];
    }

    private static LuaValue[] MathMin(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var best = RequireNumberArgument(arguments, 0, "math.min");
        var bestNumber = ToDouble(best);

        for (var index = 1; index < arguments.Count; index++)
        {
            var candidate = RequireNumberArgument(arguments, index, "math.min");
            var candidateNumber = ToDouble(candidate);
            if (candidateNumber < bestNumber)
            {
                best = candidate;
                bestNumber = candidateNumber;
            }
        }

        return [best];
    }

    private static LuaValue[] MathSqrt(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        return [LuaValue.FromFloat(Math.Sqrt(RequireDoubleArgument(arguments, 0, "math.sqrt")))];
    }

    private static LuaValue[] MathLog(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var value = RequireDoubleArgument(arguments, 0, "math.log");
        double result;
        if (arguments.Count < 2 || arguments[1].IsNil)
        {
            result = Math.Log(value);
        }
        else
        {
            var numberBase = RequireDoubleArgument(arguments, 1, "math.log");
            result = numberBase switch
            {
                2d => Math.Log2(value),
                10d => Math.Log10(value),
                _ => Math.Log(value) / Math.Log(numberBase)
            };
        }

        return [LuaValue.FromFloat(result)];
    }

    private static LuaValue[] MathExp(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        return [LuaValue.FromFloat(Math.Exp(RequireDoubleArgument(arguments, 0, "math.exp")))];
    }

    private static LuaValue[] MathSin(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        return [LuaValue.FromFloat(Math.Sin(RequireDoubleArgument(arguments, 0, "math.sin")))];
    }

    private static LuaValue[] MathCos(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        return [LuaValue.FromFloat(Math.Cos(RequireDoubleArgument(arguments, 0, "math.cos")))];
    }

    private static LuaValue[] MathTan(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        return [LuaValue.FromFloat(Math.Tan(RequireDoubleArgument(arguments, 0, "math.tan")))];
    }

    private static LuaValue[] MathAsin(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        return [LuaValue.FromFloat(Math.Asin(RequireDoubleArgument(arguments, 0, "math.asin")))];
    }

    private static LuaValue[] MathAcos(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        return [LuaValue.FromFloat(Math.Acos(RequireDoubleArgument(arguments, 0, "math.acos")))];
    }

    private static LuaValue[] MathAtan(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var y = RequireDoubleArgument(arguments, 0, "math.atan");
        var x = arguments.Count > 1 && !arguments[1].IsNil
            ? RequireDoubleArgument(arguments, 1, "math.atan")
            : 1d;
        return [LuaValue.FromFloat(Math.Atan2(y, x))];
    }

    private static LuaValue[] MathDeg(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        return [LuaValue.FromFloat(RequireDoubleArgument(arguments, 0, "math.deg") * (180d / Math.PI))];
    }

    private static LuaValue[] MathRad(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        return [LuaValue.FromFloat(RequireDoubleArgument(arguments, 0, "math.rad") * (Math.PI / 180d))];
    }

    private static LuaValue[] MathFMod(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var left = RequireNumberArgument(arguments, 0, "math.fmod");
        var right = RequireNumberArgument(arguments, 1, "math.fmod");
        if (left.Kind == LuaValueKind.Integer && right.Kind == LuaValueKind.Integer)
        {
            var divisor = right.AsInteger();
            if (divisor == 0)
            {
                throw CreateArgumentError("math.fmod", 2, "zero");
            }

            if (divisor == -1)
            {
                return [LuaValue.FromInteger(0)];
            }

            return [LuaValue.FromInteger(left.AsInteger() % divisor)];
        }

        var leftNumber = ToDouble(left);
        var rightNumber = ToDouble(right);
        var quotient = Math.Truncate(leftNumber / rightNumber);
        return [LuaValue.FromFloat(leftNumber - (quotient * rightNumber))];
    }

    private static LuaValue[] MathModF(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var value = RequireNumberArgument(arguments, 0, "math.modf");
        if (value.Kind == LuaValueKind.Integer)
        {
            return [value, LuaValue.FromFloat(0d)];
        }

        var number = ToDouble(value);
        var integerPart = number < 0d ? Math.Ceiling(number) : Math.Floor(number);
        return
        [
            CreateNumericResult(integerPart),
            LuaValue.FromFloat(number == integerPart ? 0d : number - integerPart)
        ];
    }

    private static LuaValue[] MathToInteger(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var value = RequireArgument(arguments, 0, "math.tointeger");
        if (TryConvertToNumber(value, out var number) && TryGetInteger(number, out var integer))
        {
            return [LuaValue.FromInteger(integer)];
        }

        return [LuaValue.Nil];
    }

    private static LuaValue[] MathType(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var value = RequireArgument(arguments, 0, "math.type");
        return value.Kind switch
        {
            LuaValueKind.Integer => [LuaValue.FromString("integer")],
            LuaValueKind.Float => [LuaValue.FromString("float")],
            _ => [LuaValue.Nil]
        };
    }

    private static LuaValue[] MathUnsignedLessThan(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var left = RequireIntegerArgument(arguments, 0, "math.ult");
        var right = RequireIntegerArgument(arguments, 1, "math.ult");
        return [LuaValue.FromBoolean(unchecked((ulong)left) < unchecked((ulong)right))];
    }

    private static LuaValue[] Utf8Offset(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var text = RequireStringArgument(arguments, 0, "utf8.offset");
        var bytes = GetUtf8Bytes(text);
        var count = RequireIntegerArgument(arguments, 1, "utf8.offset");
        var defaultPosition = count >= 0 ? 1L : bytes.Length + 1L;
        var position = ResolveRelativePosition(
            arguments.Count > 2 && !arguments[2].IsNil
                ? RequireIntegerArgument(arguments, 2, "utf8.offset")
                : defaultPosition,
            bytes.Length);

        if (position < 1 || position > bytes.Length + 1L)
        {
            throw CreateArgumentError("utf8.offset", 3, "position out of bounds");
        }

        var index = (int)(position - 1);
        if (count == 0)
        {
            while (index > 0 && index < bytes.Length && IsUtf8ContinuationByte(bytes[index]))
            {
                index--;
            }
        }
        else
        {
            if (index < bytes.Length && IsUtf8ContinuationByte(bytes[index]))
            {
                throw CreateRuntimeError("initial position is a continuation byte");
            }

            if (count < 0)
            {
                while (count < 0 && index > 0)
                {
                    do
                    {
                        index--;
                    }
                    while (index > 0 && IsUtf8ContinuationByte(bytes[index]));

                    count++;
                }
            }
            else
            {
                count--;
                while (count > 0 && index < bytes.Length)
                {
                    do
                    {
                        index++;
                    }
                    while (index < bytes.Length && IsUtf8ContinuationByte(bytes[index]));

                    count--;
                }
            }
        }

        if (count != 0)
        {
            return [LuaValue.Nil];
        }

        var start = index + 1;
        var end = start;
        if (index < bytes.Length && (bytes[index] & 0x80) != 0)
        {
            if (IsUtf8ContinuationByte(bytes[index]))
            {
                throw CreateRuntimeError("initial position is a continuation byte");
            }

            while (index + 1 < bytes.Length && IsUtf8ContinuationByte(bytes[index + 1]))
            {
                index++;
            }

            end = index + 1;
        }

        return [LuaValue.FromInteger(start), LuaValue.FromInteger(end)];
    }

    private static LuaValue[] Utf8CodePoint(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var text = RequireStringArgument(arguments, 0, "utf8.codepoint");
        var bytes = GetUtf8Bytes(text);
        var start = ResolveRelativePosition(
            arguments.Count > 1 && !arguments[1].IsNil
                ? RequireIntegerArgument(arguments, 1, "utf8.codepoint")
                : 1L,
            bytes.Length);
        var end = ResolveRelativePosition(
            arguments.Count > 2 && !arguments[2].IsNil
                ? RequireIntegerArgument(arguments, 2, "utf8.codepoint")
                : start,
            bytes.Length);
        var lax = arguments.Count > 3 && IsTruthy(arguments[3]);

        if (start < 1)
        {
            throw CreateArgumentError("utf8.codepoint", 2, "out of bounds");
        }

        if (end > bytes.Length)
        {
            throw CreateArgumentError("utf8.codepoint", 3, "out of bounds");
        }

        if (start > end)
        {
            return [];
        }

        var results = new List<LuaValue>();
        for (var index = (int)(start - 1); index < end;)
        {
            if (!TryDecodeUtf8(bytes, index, strict: !lax, out var nextIndex, out var codePoint))
            {
                throw CreateRuntimeError(InvalidUtf8CodeMessage);
            }

            results.Add(LuaValue.FromInteger(codePoint));
            index = nextIndex;
        }

        return results.ToArray();
    }

    private static LuaValue[] Utf8Char(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < arguments.Count; index++)
        {
            var codePoint = RequireIntegerArgument(arguments, index, "utf8.char");
            if (codePoint < 0 ||
                codePoint > MaxUnicode ||
                codePoint is >= 0xD800 and <= 0xDFFF)
            {
                throw CreateArgumentError("utf8.char", index + 1, "value out of range");
            }

            builder.Append(new Rune((int)codePoint));
        }

        return [LuaValue.FromString(builder.ToString())];
    }

    private static LuaValue[] Utf8Len(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var text = RequireStringArgument(arguments, 0, "utf8.len");
        var bytes = GetUtf8Bytes(text);
        var start = ResolveRelativePosition(
            arguments.Count > 1 && !arguments[1].IsNil
                ? RequireIntegerArgument(arguments, 1, "utf8.len")
                : 1L,
            bytes.Length);
        var end = ResolveRelativePosition(
            arguments.Count > 2 && !arguments[2].IsNil
                ? RequireIntegerArgument(arguments, 2, "utf8.len")
                : -1L,
            bytes.Length);
        var lax = arguments.Count > 3 && IsTruthy(arguments[3]);

        if (start < 1 || start > bytes.Length + 1L)
        {
            throw CreateArgumentError("utf8.len", 2, "initial position out of bounds");
        }

        if (end > bytes.Length)
        {
            throw CreateArgumentError("utf8.len", 3, "final position out of bounds");
        }

        long count = 0;
        for (var index = (int)(start - 1); index <= end - 1;)
        {
            if (!TryDecodeUtf8(bytes, index, strict: !lax, out var nextIndex, out _))
            {
                return [LuaValue.Nil, LuaValue.FromInteger(index + 1)];
            }

            count++;
            index = nextIndex;
        }

        return [LuaValue.FromInteger(count)];
    }

    private static LuaValue[] Utf8Codes(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var text = RequireStringArgument(arguments, 0, "utf8.codes");
        var bytes = GetUtf8Bytes(text);
        if (bytes.Length > 0 && IsUtf8ContinuationByte(bytes[0]))
        {
            throw CreateArgumentError("utf8.codes", 1, InvalidUtf8CodeMessage);
        }

        var lax = arguments.Count > 1 && IsTruthy(arguments[1]);
        return
        [
            lax ? Utf8CodesLaxIteratorFunction : Utf8CodesStrictIteratorFunction,
            LuaValue.FromString(text),
            LuaValue.FromInteger(0)
        ];
    }

    private static LuaValue[] Utf8CodesIteratorStrict(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        return Utf8CodesIterator(arguments, strict: true);
    }

    private static LuaValue[] Utf8CodesIteratorLax(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        return Utf8CodesIterator(arguments, strict: false);
    }

    private static LuaValue[] Utf8CodesIterator(IReadOnlyList<LuaValue> arguments, bool strict)
    {
        var text = RequireStringArgument(arguments, 0, "utf8.codes.iter");
        var bytes = GetUtf8Bytes(text);
        var lastIndex = RequireIntegerArgument(arguments, 1, "utf8.codes.iter");
        var index = unchecked((ulong)lastIndex);

        if (index < (ulong)bytes.Length)
        {
            while (index < (ulong)bytes.Length && IsUtf8ContinuationByte(bytes[(int)index]))
            {
                index++;
            }
        }

        if (index >= (ulong)bytes.Length)
        {
            return [];
        }

        var currentIndex = (int)index;
        if (!TryDecodeUtf8(bytes, currentIndex, strict, out var nextIndex, out var codePoint) ||
            (nextIndex < bytes.Length && IsUtf8ContinuationByte(bytes[nextIndex])))
        {
            throw CreateRuntimeError(InvalidUtf8CodeMessage);
        }

        return [LuaValue.FromInteger(currentIndex + 1), LuaValue.FromInteger(codePoint)];
    }

    private static LuaValue[] Assert(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {

        var condition = RequireArgument(arguments, 0, "assert");
        if (IsTruthy(condition))
        {
            return arguments.ToArray();
        }

        var errorObject = arguments.Count >= 2
            ? arguments[1]
            : LuaValue.FromString("assertion failed!");
        throw new LuaRuntimeException(errorObject);
    }

    private static LuaValue[] Select(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {

        var selector = RequireArgument(arguments, 0, "select");
        var count = arguments.Count;

        if (selector.Kind == LuaValueKind.String &&
            selector.AsString().Length > 0 &&
            selector.AsString()[0] == '#')
        {
            return [LuaValue.FromInteger(count - 1)];
        }

        if (!TryGetInteger(selector, out var index))
        {
            throw CreateArgumentTypeError("select", 1, "number", selector);
        }

        if (index < 0)
        {
            index = count + index;
        }
        else if (index > count)
        {
            index = count;
        }

        if (index < 1)
        {
            throw CreateArgumentError("select", 1, "index out of range");
        }

        var resultStart = (int)index;
        if (resultStart >= arguments.Count)
        {
            return [];
        }

        return arguments.Skip(resultStart).ToArray();
    }

    private static LuaValue[] ProtectedCall(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {

        var callable = RequireArgument(arguments, 0, "pcall");
        var callArguments = arguments.Count > 1 ? arguments.Skip(1).ToArray() : Array.Empty<LuaValue>();
        return ExecuteProtectedCall(state, callable, callArguments, messageHandler: null);
    }

    private static LuaValue[] ToNumber(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {

        var value = RequireArgument(arguments, 0, "tonumber");
        if (arguments.Count < 2 || arguments[1].IsNil)
        {
            return TryConvertToNumber(value, out var number)
                ? [number]
                : [LuaValue.Nil];
        }

        if (value.Kind != LuaValueKind.String)
        {
            throw CreateArgumentTypeError("tonumber", 1, "string", value);
        }

        var baseValue = arguments[1];
        if (!TryGetInteger(baseValue, out var numberBase))
        {
            throw CreateArgumentTypeError("tonumber", 2, "integer", baseValue);
        }

        if (numberBase is < 2 or > 36)
        {
            throw CreateArgumentError("tonumber", 2, "base out of range");
        }

        return TryParseIntegerWithBase(value.AsString(), (int)numberBase, out var integer)
            ? [LuaValue.FromInteger(integer)]
            : [LuaValue.Nil];
    }

    private static LuaValue[] ToString(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {

        var value = RequireArgument(arguments, 0, "tostring");
        if (state.TryGetMetamethod(value, "__tostring", out var metamethod))
        {
            var results = state.InvokeCallable(metamethod, [value]);
            if (results.Length == 0 || results[0].Kind != LuaValueKind.String)
            {
                throw CreateRuntimeError("'__tostring' must return a string");
            }

            return [results[0]];
        }

        return [LuaValue.FromString(FormatLuaValue(value))];
    }

    private static LuaValue[] ExtendedProtectedCall(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {

        var callable = RequireArgument(arguments, 0, "xpcall");
        var messageHandler = RequireArgument(arguments, 1, "xpcall");
        if (messageHandler.Kind != LuaValueKind.Function)
        {
            throw CreateArgumentTypeError("xpcall", 2, "function", messageHandler);
        }

        var callArguments = arguments.Count > 2 ? arguments.Skip(2).ToArray() : Array.Empty<LuaValue>();
        return ExecuteProtectedCall(state, callable, callArguments, messageHandler);
    }

    private static LuaValue[] Error(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {

        var errorObject = arguments.Count == 0 ? LuaValue.Nil : arguments[0];
        throw new LuaRuntimeException(errorObject);
    }

    private static LuaValue RequireArgument(IReadOnlyList<LuaValue> arguments, int index, string functionName)
    {
        if (index < arguments.Count)
        {
            return arguments[index];
        }

        throw CreateRuntimeError($"bad argument #{index + 1} to '{functionName}' (value expected)");
    }

    private static string GetOptionalStringArgument(
        IReadOnlyList<LuaValue> arguments,
        int index,
        string defaultValue,
        string functionName)
    {
        if (index >= arguments.Count || arguments[index].IsNil)
        {
            return defaultValue;
        }

        if (TryConvertToStringArgument(arguments[index], out var text))
        {
            return text;
        }

        throw CreateArgumentTypeError(functionName, index + 1, "string", arguments[index]);
    }

    private static string GetLoadMode(IReadOnlyList<LuaValue> arguments, int index, string functionName)
    {
        var mode = GetOptionalStringArgument(arguments, index, "bt", functionName);
        if (mode.Length == 0 ||
            mode.Contains('B') ||
            mode.Any(static ch => ch is not ('b' or 't')))
        {
            throw CreateArgumentError(functionName, index + 1, "invalid mode");
        }

        return mode;
    }

    private static string? GetOptionalFileName(IReadOnlyList<LuaValue> arguments, int index, string functionName)
    {
        if (index >= arguments.Count || arguments[index].IsNil)
        {
            return null;
        }

        return RequireStringArgument(arguments, index, functionName);
    }

    private static string RequireStringArgument(IReadOnlyList<LuaValue> arguments, int index, string functionName)
    {
        var value = RequireArgument(arguments, index, functionName);
        if (TryConvertToStringArgument(value, out var text))
        {
            return text;
        }

        throw CreateArgumentTypeError(functionName, index + 1, "string", value);
    }

    private static LuaTable RequireTableArgument(IReadOnlyList<LuaValue> arguments, int index, string functionName)
    {
        var value = RequireArgument(arguments, index, functionName);
        if (value.Kind == LuaValueKind.Table)
        {
            return value.AsTable();
        }

        throw CreateArgumentTypeError(functionName, index + 1, "table", value);
    }

    private static LuaValue RequireNumberArgument(IReadOnlyList<LuaValue> arguments, int index, string functionName)
    {
        var value = RequireArgument(arguments, index, functionName);
        if (TryConvertToNumber(value, out var number))
        {
            return number;
        }

        throw CreateArgumentTypeError(functionName, index + 1, "number", value);
    }

    private static long RequireIntegerArgument(IReadOnlyList<LuaValue> arguments, int index, string functionName)
    {
        var value = RequireArgument(arguments, index, functionName);
        if (TryConvertToNumber(value, out var number) && TryGetInteger(number, out var integer))
        {
            return integer;
        }

        throw CreateArgumentTypeError(functionName, index + 1, "integer", value);
    }

    private static long GetOptionalIntegerArgument(
        IReadOnlyList<LuaValue> arguments,
        int index,
        long defaultValue,
        string functionName)
    {
        if (index >= arguments.Count || arguments[index].IsNil)
        {
            return defaultValue;
        }

        return RequireIntegerArgument(arguments, index, functionName);
    }

    private static double RequireDoubleArgument(IReadOnlyList<LuaValue> arguments, int index, string functionName)
    {
        return ToDouble(RequireNumberArgument(arguments, index, functionName));
    }

    private static bool TryConvertToStringArgument(LuaValue value, out string text)
    {
        switch (value.Kind)
        {
            case LuaValueKind.String:
                text = value.AsString();
                return true;
            case LuaValueKind.Integer:
            case LuaValueKind.Float:
                text = FormatLuaValue(value);
                return true;
            default:
                text = string.Empty;
                return false;
        }
    }

    private static int CompareTableSortValues(
        LuaState state,
        LuaValue left,
        LuaValue right,
        LuaValue? comparator)
    {
        if (comparator is not null)
        {
            if (IsTruthy(InvokeSortComparator(state, comparator.Value, left, right)))
            {
                return -1;
            }

            if (IsTruthy(InvokeSortComparator(state, comparator.Value, right, left)))
            {
                return 1;
            }

            return 0;
        }

        return CompareSortableValues(left, right);
    }

    private static LuaValue InvokeSortComparator(LuaState state, LuaValue comparator, LuaValue left, LuaValue right)
    {
        var results = state.InvokeCallable(comparator, [left, right]);
        return results.Length == 0 ? LuaValue.Nil : results[0];
    }

    private static int CompareSortableValues(LuaValue left, LuaValue right)
    {
        if (TryGetNumber(left, out var leftNumber) && TryGetNumber(right, out var rightNumber))
        {
            return leftNumber.CompareTo(rightNumber);
        }

        if (left.Kind == LuaValueKind.String && right.Kind == LuaValueKind.String)
        {
            return string.CompareOrdinal(left.AsString(), right.AsString());
        }

        throw CreateRuntimeError($"attempt to compare {GetTypeName(left)} with {GetTypeName(right)}");
    }

    private static double ToDouble(LuaValue value)
    {
        return value.Kind == LuaValueKind.Integer
            ? value.AsInteger()
            : value.AsFloat();
    }

    private static LuaValue CreateNumericResult(double value)
    {
        return double.IsFinite(value) &&
               value >= long.MinValue &&
               value <= long.MaxValue &&
               Math.Truncate(value) == value
            ? LuaValue.FromInteger((long)value)
            : LuaValue.FromFloat(value);
    }

    private static long ResolveRelativePosition(long position, int length)
    {
        if (position >= 0)
        {
            return position;
        }

        return 0UL - unchecked((ulong)position) > (ulong)length
            ? 0
            : length + position + 1;
    }

    private static byte[] GetUtf8Bytes(string text)
    {
        try
        {
            return StrictUtf8Encoding.GetBytes(text);
        }
        catch (EncoderFallbackException)
        {
            throw CreateRuntimeError(InvalidUtf8CodeMessage);
        }
    }

    private static bool IsUtf8ContinuationByte(byte value)
    {
        return (value & 0xC0) == 0x80;
    }

    private static bool TryDecodeUtf8(
        ReadOnlySpan<byte> bytes,
        int index,
        bool strict,
        out int nextIndex,
        out int codePoint)
    {
        ReadOnlySpan<int> limits = [int.MaxValue, 0x80, 0x800, 0x10000, 0x200000, 0x4000000];
        if ((uint)index >= (uint)bytes.Length)
        {
            nextIndex = default;
            codePoint = default;
            return false;
        }

        var first = bytes[index];
        var result = 0;
        var lastIndex = index;
        if (first < 0x80)
        {
            result = first;
        }
        else
        {
            var leading = first;
            var count = 0;
            while ((leading & 0x40) != 0)
            {
                count++;
                var continuationIndex = index + count;
                if ((uint)continuationIndex >= (uint)bytes.Length || !IsUtf8ContinuationByte(bytes[continuationIndex]))
                {
                    nextIndex = default;
                    codePoint = default;
                    return false;
                }

                result = (result << 6) | (bytes[continuationIndex] & 0x3F);
                leading <<= 1;
            }

            result |= (leading & 0x7F) << (count * 5);
            if (count > 5 || result > 0x7FFFFFFF || result < limits[count])
            {
                nextIndex = default;
                codePoint = default;
                return false;
            }

            lastIndex += count;
        }

        if (strict &&
            (result > MaxUnicode || result is >= 0xD800 and <= 0xDFFF))
        {
            nextIndex = default;
            codePoint = default;
            return false;
        }

        nextIndex = lastIndex + 1;
        codePoint = result;
        return true;
    }

    private static LuaValue[] ExecuteStringBinaryArithmetic(
        LuaState state,
        LuaClosure closure,
        IReadOnlyList<LuaValue> arguments,
        string metamethodName,
        string operationName,
        Func<LuaValue, LuaValue, (bool Success, LuaValue Result)> operation)
    {
        var left = RequireArgument(arguments, 0, metamethodName);
        var right = RequireArgument(arguments, 1, metamethodName);

        if (TryConvertToNumber(left, out var numericLeft) &&
            TryConvertToNumber(right, out var numericRight))
        {
            var (success, result) = operation(numericLeft, numericRight);
            if (success)
            {
                return [result];
            }
        }

        if (right.Kind != LuaValueKind.String &&
            state.TryGetMetamethod(right, metamethodName, out var rightMetamethod) &&
            rightMetamethod.Kind == LuaValueKind.Function &&
            !ReferenceEquals(rightMetamethod.AsFunction(), closure))
        {
            return state.InvokeCallable(rightMetamethod, [left, right]);
        }

        throw CreateRuntimeError(
            $"attempt to {operationName} a '{GetTypeName(left)}' with a '{GetTypeName(right)}'");
    }

    private LuaValue[] LoadChunk(
        ReadOnlyMemory<byte> chunkBytes,
        string? chunkName,
        string mode,
        bool hasEnvironment,
        LuaValue environment)
    {
        if (IsBinaryChunk(chunkBytes.Span))
        {
            if (!mode.Contains('b'))
            {
                return [LuaValue.Nil, LuaValue.FromString($"attempt to load a binary chunk (mode is '{mode}')")];
            }

            if (_binaryChunkLoader is null)
            {
                return [LuaValue.Nil, LuaValue.FromString("binary chunk loading is not configured")];
            }

            try
            {
                var loadedClosure = _binaryChunkLoader(chunkBytes, chunkName, hasEnvironment, environment);
                return [LuaValue.FromFunction(loadedClosure)];
            }
            catch (LuaRuntimeException ex)
            {
                return [LuaValue.Nil, ex.ErrorObject];
            }
            catch (Exception ex)
            {
                return [LuaValue.Nil, LuaValue.FromString(ex.Message)];
            }
        }

        if (!mode.Contains('t'))
        {
            return [LuaValue.Nil, LuaValue.FromString($"attempt to load a text chunk (mode is '{mode}')")];
        }

        if (_textChunkLoader is null)
        {
            return [LuaValue.Nil, LuaValue.FromString("text chunk loading is not configured")];
        }

        try
        {
            var loadedClosure = _textChunkLoader(chunkBytes, chunkName, hasEnvironment, environment);
            return [LuaValue.FromFunction(loadedClosure)];
        }
        catch (LuaRuntimeException ex)
        {
            return [LuaValue.Nil, ex.ErrorObject];
        }
        catch (Exception ex)
        {
            return [LuaValue.Nil, LuaValue.FromString(ex.Message)];
        }
    }

    private (LuaValue Loader, LuaValue LoaderData) FindPackageLoader(string moduleName)
    {
        var searchers = GetPackageTableField("searchers");
        var errorMessage = new StringBuilder();

        for (var index = 1L; ; index++)
        {
            var searcher = searchers.GetValue(LuaValue.FromInteger(index));
            if (searcher.IsNil)
            {
                break;
            }

            var results = InvokeCallable(searcher, [LuaValue.FromString(moduleName)]);
            var loader = results.Length == 0 ? LuaValue.Nil : results[0];
            var loaderData = results.Length > 1 ? results[1] : LuaValue.Nil;

            if (loader.Kind == LuaValueKind.Function)
            {
                return (loader, loaderData);
            }

            if (loader.Kind == LuaValueKind.String)
            {
                if (errorMessage.Length == 0)
                {
                    errorMessage.Append("\n\t");
                }
                else
                {
                    errorMessage.Append("\n\t");
                }

                errorMessage.Append(loader.AsString());
            }
        }

        throw CreateRuntimeError($"module '{moduleName}' not found:{errorMessage}");
    }

    private LuaTable GetPackageTableField(string fieldName)
    {
        var value = PackageLibrary.GetValue(LuaValue.FromString(fieldName));
        if (value.Kind != LuaValueKind.Table)
        {
            throw CreateRuntimeError($"'package.{fieldName}' must be a table");
        }

        return value.AsTable();
    }

    private string GetPackageStringField(string fieldName)
    {
        var value = PackageLibrary.GetValue(LuaValue.FromString(fieldName));
        if (value.Kind != LuaValueKind.String)
        {
            throw CreateRuntimeError($"'package.{fieldName}' must be a string");
        }

        return value.AsString();
    }

    private bool TryReadChunkFile(string fileName, out ReadOnlyMemory<byte> bytes, out string errorMessage)
    {
        try
        {
            bytes = FileReader(fileName);
            errorMessage = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            bytes = ReadOnlyMemory<byte>.Empty;
            errorMessage = $"cannot open {fileName}: {ex.Message}";
            return false;
        }
    }

    private static byte[] EncodeLuaString(string text)
    {
        return Encoding.Latin1.GetBytes(text);
    }

    private static bool IsBinaryChunk(ReadOnlySpan<byte> bytes)
    {
        return bytes.Length >= BinaryChunkSignature.Length &&
               bytes[..BinaryChunkSignature.Length].SequenceEqual(BinaryChunkSignature);
    }


    private static bool TryGetProtectedMetatableValue(LuaTable? metatable, out LuaValue value)
    {
        if (metatable is null)
        {
            value = LuaValue.Nil;
            return false;
        }

        return metatable.TryGetValue(LuaValue.FromString("__metatable"), out value);
    }

    private static bool AreRawEqual(LuaValue left, LuaValue right)
    {
        if (left.Kind == right.Kind)
        {
            return left == right;
        }

        return TryGetNumber(left, out var leftNumber) &&
               TryGetNumber(right, out var rightNumber) &&
               leftNumber.Equals(rightNumber);
    }


    private static bool TryConvertToNumber(LuaValue value, out LuaValue result)
    {
        switch (value.Kind)
        {
            case LuaValueKind.Integer:
            case LuaValueKind.Float:
                result = value;
                return true;
            case LuaValueKind.String:
                return LuaValueHelper.TryParseLuaStringNumber(value.AsString(), out result);
            default:
                result = LuaValue.Nil;
                return false;
        }
    }

    private static bool TryParseIntegerWithBase(string text, int numberBase, out long result)
    {
        var span = text.AsSpan().Trim();
        if (span.IsEmpty)
        {
            result = default;
            return false;
        }

        var index = 0;
        var negative = false;
        if (span[index] is '+' or '-')
        {
            negative = span[index] == '-';
            index++;
        }

        if (index >= span.Length)
        {
            result = default;
            return false;
        }

        ulong value = 0;
        var sawDigit = false;
        while (index < span.Length)
        {
            if (!TryGetBaseDigit(span[index], numberBase, out var digit))
            {
                result = default;
                return false;
            }

            sawDigit = true;
            value = unchecked((value * (uint)numberBase) + (uint)digit);
            index++;
        }

        if (!sawDigit)
        {
            result = default;
            return false;
        }

        result = negative
            ? unchecked((long)(0UL - value))
            : unchecked((long)value);
        return true;
    }

    private static void ValidateTableAssignmentKey(LuaValue key)
    {
        if (key.IsNil)
        {
            throw CreateRuntimeError("table index is nil");
        }

        if (IsNaNKey(key))
        {
            throw CreateRuntimeError("table index is NaN");
        }
    }

    private static LuaRuntimeException CreateArgumentTypeError(
        string functionName,
        int argumentIndex,
        string expected,
        LuaValue actual)
    {
        return CreateRuntimeError(
            $"bad argument #{argumentIndex} to '{functionName}' ({expected} expected, got {GetTypeName(actual)})");
    }

    private static LuaRuntimeException CreateArgumentError(string functionName, int argumentIndex, string message)
    {
        return CreateRuntimeError($"bad argument #{argumentIndex} to '{functionName}' ({message})");
    }

    private static LuaRuntimeException CreateRuntimeError(string message)
    {
        return new LuaRuntimeException(LuaValue.FromString(message));
    }

    private static LuaValue GetBaseFunctionValue(LuaState state, string name)
    {
        return state.GlobalEnvironment.GetValue(LuaValue.FromString(name));
    }

    private void EmitWarning(string message, bool toContinue)
    {
        switch (_warningMode)
        {
            case WarningMode.Off:
                TryHandleWarningControl(message, toContinue);
                return;
            case WarningMode.Ready:
                if (TryHandleWarningControl(message, toContinue))
                {
                    return;
                }

                _warningBuffer.Clear();
                _warningBuffer.Append("Lua warning: ");
                _warningBuffer.Append(message);
                if (toContinue)
                {
                    _warningMode = WarningMode.Continue;
                }
                else
                {
                    WarningOutput(_warningBuffer.ToString());
                    _warningBuffer.Clear();
                }

                return;
            case WarningMode.Continue:
                _warningBuffer.Append(message);
                if (toContinue)
                {
                    return;
                }

                WarningOutput(_warningBuffer.ToString());
                _warningBuffer.Clear();
                _warningMode = WarningMode.Ready;
                return;
            default:
                throw new InvalidOperationException($"Unknown warning mode '{_warningMode}'.");
        }
    }

    private bool TryHandleWarningControl(string message, bool toContinue)
    {
        if (toContinue || !message.StartsWith('@'))
        {
            return false;
        }

        _warningBuffer.Clear();
        _warningMode = message switch
        {
            "@off" => WarningMode.Off,
            "@on" => WarningMode.Ready,
            _ => _warningMode
        };

        return true;
    }

    private static LuaValue[] NormalizeResults(IReadOnlyList<LuaValue> results, int count)
    {
        var normalized = new LuaValue[count];
        for (var index = 0; index < count; index++)
        {
            normalized[index] = index < results.Count ? results[index] : LuaValue.Nil;
        }

        return normalized;
    }

    private static string ConvertToPrintedString(LuaState state, LuaValue value)
    {
        var results = state.InvokeCallable(GetBaseFunctionValue(state, "tostring"), [value]);
        if (results.Length == 0 || results[0].Kind != LuaValueKind.String)
        {
            throw new InvalidOperationException("The 'tostring' base function must return a string.");
        }

        return results[0].AsString();
    }

    private static string ConvertToWarningString(LuaValue value, int argumentIndex)
    {
        return value.Kind switch
        {
            LuaValueKind.String => value.AsString(),
            LuaValueKind.Integer or LuaValueKind.Float => FormatLuaValue(value),
            _ => throw CreateArgumentTypeError("warn", argumentIndex, "string", value)
        };
    }

    private static string FormatLuaValue(LuaValue value)
    {
        return value.Kind switch
        {
            LuaValueKind.Nil => "nil",
            LuaValueKind.Boolean => value.AsBoolean() ? "true" : "false",
            LuaValueKind.Integer => value.AsInteger().ToString(System.Globalization.CultureInfo.InvariantCulture),
            LuaValueKind.Float => FormatLuaFloat(value.AsFloat()),
            LuaValueKind.String => value.AsString(),
            LuaValueKind.Table => FormatObjectValue(GetDisplayTypeName(value), value.AsTable()),
            LuaValueKind.Function => FormatObjectValue("function", value.AsFunction()),
            LuaValueKind.Thread => FormatObjectValue("thread", value.AsThread()),
            LuaValueKind.UserData => FormatObjectValue(GetDisplayTypeName(value), value.AsUserData()),
            _ => value.Kind.ToString().ToLowerInvariant()
        };
    }

    private static string FormatLuaFloat(double value)
    {
        var text = value.ToString("G17", System.Globalization.CultureInfo.InvariantCulture);
        if (!double.IsFinite(value) ||
            text.Contains('.') ||
            text.Contains('E') ||
            text.Contains('e'))
        {
            return text;
        }

        return text + ".0";
    }

    private static string FormatObjectValue(string typeName, object reference)
    {
        return $"{typeName}: 0x{RuntimeHelpers.GetHashCode(reference):x}";
    }

    private static string GetDisplayTypeName(LuaValue value)
    {
        LuaTable? metatable = value.Kind switch
        {
            LuaValueKind.Table => value.AsTable().Metatable,
            LuaValueKind.UserData => value.AsUserData().Metatable,
            _ => null
        };

        if (metatable is not null &&
            metatable.TryGetValue(LuaValue.FromString("__name"), out var nameValue) &&
            nameValue.Kind == LuaValueKind.String)
        {
            return nameValue.AsString();
        }

        return GetTypeName(value);
    }

    private static bool TryGetBaseDigit(char c, int numberBase, out int digit)
    {
        if (c is >= '0' and <= '9')
        {
            digit = c - '0';
            return digit < numberBase;
        }

        if (c is >= 'a' and <= 'z')
        {
            digit = (c - 'a') + 10;
            return digit < numberBase;
        }

        if (c is >= 'A' and <= 'Z')
        {
            digit = (c - 'A') + 10;
            return digit < numberBase;
        }

        digit = default;
        return false;
    }

    private static LuaValue[] ExecuteProtectedCall(
        LuaState state,
        LuaValue callable,
        IReadOnlyList<LuaValue> callArguments,
        LuaValue? messageHandler)
    {
        try
        {
            var results = state.InvokeCallable(callable, callArguments);
            return PrependSuccessResult(results);
        }
        catch (Exception ex)
        {
            var errorObject = GetErrorObject(ex);
            if (messageHandler is not null)
            {
                errorObject = InvokeMessageHandler(state, messageHandler.Value, errorObject);
            }

            return [LuaValue.FromBoolean(false), errorObject];
        }
    }

    private static LuaValue[] PrependSuccessResult(IReadOnlyList<LuaValue> results)
    {
        var protectedResults = new LuaValue[results.Count + 1];
        protectedResults[0] = LuaValue.FromBoolean(true);
        for (var index = 0; index < results.Count; index++)
        {
            protectedResults[index + 1] = results[index];
        }

        return protectedResults;
    }

    private static LuaValue InvokeMessageHandler(LuaState state, LuaValue messageHandler, LuaValue errorObject)
    {
        try
        {
            var handledResults = state.InvokeCallable(messageHandler, [errorObject]);
            return handledResults.Length == 0 ? LuaValue.Nil : handledResults[0];
        }
        catch
        {
            return LuaValue.FromString("error in error handling");
        }
    }

    private static LuaValue GetErrorObject(Exception exception)
    {
        return exception switch
        {
            LuaRuntimeException runtimeException => runtimeException.ErrorObject,
            _ => LuaValue.FromString(exception.Message)
        };
    }

}
