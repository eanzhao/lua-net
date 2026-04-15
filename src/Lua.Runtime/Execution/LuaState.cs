using Lua.Runtime.Objects;
using Lua.Runtime.Values;
using System.Runtime.CompilerServices;
using System.Text;
using static Lua.Runtime.Values.LuaValueHelper;

namespace Lua.Runtime.Execution;

public sealed class LuaState
{
    private readonly List<CallFrame> _frames = [];
    private Func<LuaValue, IReadOnlyList<LuaValue>, LuaValue[]>? _callableInvoker;
    private static readonly LuaValue IPairsAuxFunction = LuaValue.FromFunction(
        new LuaClosure(
            "ipairsaux",
            body: new LuaNativeClosureBody(IPairsAux)));

    public LuaState()
    {
        Stack = new LuaStack();
        GlobalEnvironment = new LuaTable("_ENV");
        RegisterBaseFunctions();
    }

    public LuaStack Stack { get; }

    public LuaTable GlobalEnvironment { get; }

    public IReadOnlyList<CallFrame> Frames => _frames;

    public CallFrame? CurrentFrame => _frames.Count == 0 ? null : _frames[^1];

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
        RegisterBaseFunction("type", Type);
        RegisterBaseFunction("assert", Assert);
        RegisterBaseFunction("select", Select);
        RegisterBaseFunction("tonumber", ToNumber);
        RegisterBaseFunction("tostring", ToString);
        RegisterBaseFunction("pcall", ProtectedCall);
        RegisterBaseFunction("xpcall", ExtendedProtectedCall);
        RegisterBaseFunction("error", Error);
    }

    private void RegisterBaseFunction(string name, LuaNativeFunction function)
    {
        var closure = new LuaClosure(
            name,
            body: new LuaNativeClosureBody(function));
        GlobalEnvironment.SetValue(
            LuaValue.FromString(name),
            LuaValue.FromFunction(closure));
    }

    public void PushFrame(CallFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _frames.Add(frame);
    }

    public CallFrame PopFrame()
    {
        if (_frames.Count == 0)
        {
            throw new InvalidOperationException("Cannot pop from an empty frame stack.");
        }

        var lastIndex = _frames.Count - 1;
        var frame = _frames[lastIndex];
        _frames.RemoveAt(lastIndex);
        return frame;
    }

    public void SetCallableInvoker(Func<LuaValue, IReadOnlyList<LuaValue>, LuaValue[]> callableInvoker)
    {
        ArgumentNullException.ThrowIfNull(callableInvoker);
        _callableInvoker = callableInvoker;
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
        if (!TryGetRawMetatable(value, out var metatable) || metatable is null)
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
            LuaValueKind.String => [LuaValue.FromInteger(Encoding.UTF8.GetByteCount(value.AsString()))],
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
        if (TryGetMetamethod(value, "__pairs", out var metamethod))
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
        if (TryGetMetamethod(value, "__tostring", out var metamethod))
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

    private static bool TryGetRawMetatable(LuaValue value, out LuaTable? metatable)
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
                metatable = null;
                return false;
        }
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
                return TryParseLuaStringNumber(value.AsString(), out result);
            default:
                result = LuaValue.Nil;
                return false;
        }
    }

    private static bool TryParseLuaStringNumber(string text, out LuaValue result)
    {
        var span = text.AsSpan().Trim();
        if (span.IsEmpty)
        {
            result = LuaValue.Nil;
            return false;
        }

        if (TryParseLuaHexNumber(span, out result))
        {
            return true;
        }

        if (TryParseLuaDecimalNumber(span, out result))
        {
            return true;
        }

        result = LuaValue.Nil;
        return false;
    }

    private static bool TryParseLuaDecimalNumber(ReadOnlySpan<char> text, out LuaValue result)
    {
        var treatsAsFloat = text.IndexOfAny('.', 'e', 'E') >= 0;
        if (!treatsAsFloat &&
            long.TryParse(text, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var integer))
        {
            result = LuaValue.FromInteger(integer);
            return true;
        }

        if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number))
        {
            result = LuaValue.FromFloat(number);
            return true;
        }

        result = LuaValue.Nil;
        return false;
    }

    private static bool TryParseLuaHexNumber(ReadOnlySpan<char> text, out LuaValue result)
    {
        var index = 0;
        var negative = false;
        if (text[index] is '+' or '-')
        {
            negative = text[index] == '-';
            index++;
        }

        if (index + 2 > text.Length ||
            text[index] != '0' ||
            (text[index + 1] != 'x' && text[index + 1] != 'X'))
        {
            result = LuaValue.Nil;
            return false;
        }

        index += 2;
        if (index >= text.Length)
        {
            result = LuaValue.Nil;
            return false;
        }

        var digitsStart = index;
        var integerPart = 0d;
        while (index < text.Length && TryGetHexDigit(text[index], out var integerDigit))
        {
            integerPart = (integerPart * 16d) + integerDigit;
            index++;
        }

        var fractionPart = 0d;
        var fractionDivisor = 16d;
        if (index < text.Length && text[index] == '.')
        {
            index++;
            while (index < text.Length && TryGetHexDigit(text[index], out var fractionDigit))
            {
                fractionPart += fractionDigit / fractionDivisor;
                fractionDivisor *= 16d;
                index++;
            }
        }

        var hasDigits = index > digitsStart;
        if (!hasDigits)
        {
            result = LuaValue.Nil;
            return false;
        }

        var exponent = 0;
        var hasExponent = false;
        if (index < text.Length && (text[index] == 'p' || text[index] == 'P'))
        {
            hasExponent = true;
            index++;
            if (index >= text.Length)
            {
                result = LuaValue.Nil;
                return false;
            }

            var exponentNegative = false;
            if (text[index] is '+' or '-')
            {
                exponentNegative = text[index] == '-';
                index++;
            }

            if (index >= text.Length || !char.IsAsciiDigit(text[index]))
            {
                result = LuaValue.Nil;
                return false;
            }

            while (index < text.Length && char.IsAsciiDigit(text[index]))
            {
                exponent = (exponent * 10) + (text[index] - '0');
                index++;
            }

            if (exponentNegative)
            {
                exponent = -exponent;
            }
        }

        if (index != text.Length)
        {
            result = LuaValue.Nil;
            return false;
        }

        if (!hasExponent && fractionPart == 0d)
        {
            return TryParseHexInteger(text, negative, out result);
        }

        var number = (integerPart + fractionPart) * Math.Pow(2d, exponent);
        if (negative)
        {
            number = -number;
        }

        result = LuaValue.FromFloat(number);
        return true;
    }

    private static bool TryParseHexInteger(ReadOnlySpan<char> text, bool negative, out LuaValue result)
    {
        var prefixStart = text[0] is '+' or '-' ? 3 : 2;
        var digits = text[prefixStart..];
        if (!ulong.TryParse(digits, System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out var number))
        {
            result = LuaValue.Nil;
            return false;
        }

        result = negative
            ? LuaValue.FromInteger(unchecked((long)(0UL - number)))
            : LuaValue.FromInteger(unchecked((long)number));
        return true;
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

    private static LuaValue[] NormalizeResults(IReadOnlyList<LuaValue> results, int count)
    {
        var normalized = new LuaValue[count];
        for (var index = 0; index < count; index++)
        {
            normalized[index] = index < results.Count ? results[index] : LuaValue.Nil;
        }

        return normalized;
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
        if (TryGetRawMetatable(value, out var metatable) &&
            metatable is not null &&
            metatable.TryGetValue(LuaValue.FromString("__name"), out var nameValue) &&
            nameValue.Kind == LuaValueKind.String)
        {
            return nameValue.AsString();
        }

        return GetTypeName(value);
    }

    private static bool TryGetHexDigit(char c, out int digit)
    {
        if (c is >= '0' and <= '9')
        {
            digit = c - '0';
            return true;
        }

        if (c is >= 'a' and <= 'f')
        {
            digit = (c - 'a') + 10;
            return true;
        }

        if (c is >= 'A' and <= 'F')
        {
            digit = (c - 'A') + 10;
            return true;
        }

        digit = default;
        return false;
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
