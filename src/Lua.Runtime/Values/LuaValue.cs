using System.Globalization;
using Lua.Runtime.Objects;

namespace Lua.Runtime.Values;

public readonly struct LuaValue : IEquatable<LuaValue>
{
    private readonly object? _reference;
    private readonly long _integer;
    private readonly double _float;
    private readonly bool _boolean;

    private LuaValue(
        LuaValueKind kind,
        object? reference = null,
        long integer = 0,
        double @float = 0,
        bool boolean = false)
    {
        Kind = kind;
        _reference = reference;
        _integer = integer;
        _float = @float;
        _boolean = boolean;
    }

    public static LuaValue Nil { get; } = new(LuaValueKind.Nil);

    public LuaValueKind Kind { get; }

    public bool IsNil => Kind == LuaValueKind.Nil;

    public bool IsNumber => Kind is LuaValueKind.Integer or LuaValueKind.Float;

    public static LuaValue FromBoolean(bool value) => new(LuaValueKind.Boolean, boolean: value);

    public static LuaValue FromInteger(long value) => new(LuaValueKind.Integer, integer: value);

    public static LuaValue FromFloat(double value) => new(LuaValueKind.Float, @float: value);

    public static LuaValue FromString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new LuaValue(LuaValueKind.String, reference: value);
    }

    public static LuaValue FromTable(LuaTable value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new LuaValue(LuaValueKind.Table, reference: value);
    }

    public static LuaValue FromFunction(LuaClosure value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new LuaValue(LuaValueKind.Function, reference: value);
    }

    public static LuaValue FromThread(LuaThread value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new LuaValue(LuaValueKind.Thread, reference: value);
    }

    public static LuaValue FromUserData(LuaUserData value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new LuaValue(LuaValueKind.UserData, reference: value);
    }

    public bool AsBoolean() =>
        Kind == LuaValueKind.Boolean ? _boolean : ThrowInvalidCast<bool>("boolean");

    public long AsInteger() =>
        Kind == LuaValueKind.Integer ? _integer : ThrowInvalidCast<long>("integer");

    public double AsFloat() =>
        Kind == LuaValueKind.Float ? _float : ThrowInvalidCast<double>("float");

    public string AsString() =>
        Kind == LuaValueKind.String ? (string)_reference! : ThrowInvalidCast<string>("string");

    public LuaTable AsTable() =>
        Kind == LuaValueKind.Table ? (LuaTable)_reference! : ThrowInvalidCast<LuaTable>("table");

    public LuaClosure AsFunction() =>
        Kind == LuaValueKind.Function ? (LuaClosure)_reference! : ThrowInvalidCast<LuaClosure>("function");

    public LuaThread AsThread() =>
        Kind == LuaValueKind.Thread ? (LuaThread)_reference! : ThrowInvalidCast<LuaThread>("thread");

    public LuaUserData AsUserData() =>
        Kind == LuaValueKind.UserData ? (LuaUserData)_reference! : ThrowInvalidCast<LuaUserData>("userdata");

    public override string ToString()
    {
        return Kind switch
        {
            LuaValueKind.Nil => "nil",
            LuaValueKind.Boolean => _boolean ? "true" : "false",
            LuaValueKind.Integer => _integer.ToString(CultureInfo.InvariantCulture),
            LuaValueKind.Float => _float.ToString("G17", CultureInfo.InvariantCulture),
            LuaValueKind.String => (string)_reference!,
            LuaValueKind.Table => "table",
            LuaValueKind.Function => "function",
            LuaValueKind.Thread => "thread",
            LuaValueKind.UserData => "userdata",
            _ => Kind.ToString()
        };
    }

    public bool Equals(LuaValue other)
    {
        if (Kind != other.Kind)
        {
            return false;
        }

        return Kind switch
        {
            LuaValueKind.Nil => true,
            LuaValueKind.Boolean => _boolean == other._boolean,
            LuaValueKind.Integer => _integer == other._integer,
            LuaValueKind.Float => _float.Equals(other._float),
            LuaValueKind.String => StringComparer.Ordinal.Equals((string?)_reference, (string?)other._reference),
            LuaValueKind.Table or
                LuaValueKind.Function or
                LuaValueKind.Thread or
                LuaValueKind.UserData => ReferenceEquals(_reference, other._reference),
            _ => false
        };
    }

    public override bool Equals(object? obj) => obj is LuaValue other && Equals(other);

    public override int GetHashCode()
    {
        return Kind switch
        {
            LuaValueKind.Nil => HashCode.Combine(Kind),
            LuaValueKind.Boolean => HashCode.Combine(Kind, _boolean),
            LuaValueKind.Integer => HashCode.Combine(Kind, _integer),
            LuaValueKind.Float => HashCode.Combine(Kind, _float),
            LuaValueKind.String => HashCode.Combine(Kind, StringComparer.Ordinal.GetHashCode((string)_reference!)),
            LuaValueKind.Table or
                LuaValueKind.Function or
                LuaValueKind.Thread or
                LuaValueKind.UserData => HashCode.Combine(Kind, _reference),
            _ => HashCode.Combine(Kind)
        };
    }

    public static bool operator ==(LuaValue left, LuaValue right) => left.Equals(right);

    public static bool operator !=(LuaValue left, LuaValue right) => !left.Equals(right);

    private T ThrowInvalidCast<T>(string expected) =>
        throw new InvalidOperationException($"Cannot read a {expected} from a {Kind} value.");
}
