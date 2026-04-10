using Lua.Runtime.Objects;
using Lua.Runtime.Values;
using System.Text;

namespace Lua.Runtime.Execution;

public sealed class LuaState
{
    private readonly List<CallFrame> _frames = [];

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

    private static LuaValue[] SetMetatable(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        _ = state;
        _ = closure;

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
        _ = state;
        _ = closure;

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
        _ = state;
        _ = closure;

        var left = RequireArgument(arguments, 0, "rawequal");
        var right = RequireArgument(arguments, 1, "rawequal");
        return [LuaValue.FromBoolean(AreRawEqual(left, right))];
    }

    private static LuaValue[] RawLen(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        _ = state;
        _ = closure;

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
        _ = state;
        _ = closure;

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
        _ = state;
        _ = closure;

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

    private static LuaValue[] Error(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        _ = state;
        _ = closure;

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

    private static bool IsNaNKey(LuaValue value)
    {
        return value.Kind == LuaValueKind.Float && double.IsNaN(value.AsFloat());
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

    private static LuaRuntimeException CreateRuntimeError(string message)
    {
        return new LuaRuntimeException(LuaValue.FromString(message));
    }

    private static string GetTypeName(LuaValue value)
    {
        return value.Kind switch
        {
            LuaValueKind.Nil => "nil",
            LuaValueKind.Boolean => "boolean",
            LuaValueKind.Integer or LuaValueKind.Float => "number",
            LuaValueKind.String => "string",
            LuaValueKind.Table => "table",
            LuaValueKind.Function => "function",
            LuaValueKind.Thread => "thread",
            LuaValueKind.UserData => "userdata",
            _ => value.Kind.ToString().ToLowerInvariant()
        };
    }
}
