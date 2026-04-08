using System.Globalization;

namespace Lua.Bytecode.Chunks;

public readonly struct LuaConstant : IEquatable<LuaConstant>
{
    private readonly string? _string;
    private readonly long _integer;
    private readonly double _float;
    private readonly bool _boolean;

    private LuaConstant(
        LuaConstantKind kind,
        string? @string = null,
        long integer = 0,
        double @float = 0,
        bool boolean = false)
    {
        Kind = kind;
        _string = @string;
        _integer = integer;
        _float = @float;
        _boolean = boolean;
    }

    public static LuaConstant Nil { get; } = new(LuaConstantKind.Nil);

    public LuaConstantKind Kind { get; }

    public static LuaConstant FromBoolean(bool value) => new(LuaConstantKind.Boolean, boolean: value);

    public static LuaConstant FromInteger(long value) => new(LuaConstantKind.Integer, integer: value);

    public static LuaConstant FromFloat(double value) => new(LuaConstantKind.Float, @float: value);

    public static LuaConstant FromString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new LuaConstant(LuaConstantKind.String, @string: value);
    }

    public bool AsBoolean() =>
        Kind == LuaConstantKind.Boolean ? _boolean : ThrowInvalidCast<bool>("boolean");

    public long AsInteger() =>
        Kind == LuaConstantKind.Integer ? _integer : ThrowInvalidCast<long>("integer");

    public double AsFloat() =>
        Kind == LuaConstantKind.Float ? _float : ThrowInvalidCast<double>("float");

    public string AsString() =>
        Kind == LuaConstantKind.String ? _string! : ThrowInvalidCast<string>("string");

    public override string ToString()
    {
        return Kind switch
        {
            LuaConstantKind.Nil => "nil",
            LuaConstantKind.Boolean => _boolean ? "true" : "false",
            LuaConstantKind.Integer => _integer.ToString(CultureInfo.InvariantCulture),
            LuaConstantKind.Float => _float.ToString("G17", CultureInfo.InvariantCulture),
            LuaConstantKind.String => _string!,
            _ => Kind.ToString()
        };
    }

    public bool Equals(LuaConstant other)
    {
        if (Kind != other.Kind)
        {
            return false;
        }

        return Kind switch
        {
            LuaConstantKind.Nil => true,
            LuaConstantKind.Boolean => _boolean == other._boolean,
            LuaConstantKind.Integer => _integer == other._integer,
            LuaConstantKind.Float => _float.Equals(other._float),
            LuaConstantKind.String => StringComparer.Ordinal.Equals(_string, other._string),
            _ => false
        };
    }

    public override bool Equals(object? obj) => obj is LuaConstant other && Equals(other);

    public override int GetHashCode()
    {
        return Kind switch
        {
            LuaConstantKind.Nil => HashCode.Combine(Kind),
            LuaConstantKind.Boolean => HashCode.Combine(Kind, _boolean),
            LuaConstantKind.Integer => HashCode.Combine(Kind, _integer),
            LuaConstantKind.Float => HashCode.Combine(Kind, _float),
            LuaConstantKind.String => HashCode.Combine(Kind, StringComparer.Ordinal.GetHashCode(_string!)),
            _ => HashCode.Combine(Kind)
        };
    }

    public static bool operator ==(LuaConstant left, LuaConstant right) => left.Equals(right);

    public static bool operator !=(LuaConstant left, LuaConstant right) => !left.Equals(right);

    private T ThrowInvalidCast<T>(string expected) =>
        throw new InvalidOperationException($"Cannot read a {expected} from a {Kind} constant.");
}
