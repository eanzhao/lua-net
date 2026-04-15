using Lua.Runtime.Objects;
using Lua.Runtime.Values;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using static Lua.Runtime.Values.LuaValueHelper;

namespace Lua.Runtime.Execution;

public sealed partial class LuaState
{
    private static LuaValue[] StringFormat(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var formatText = RequireStringArgument(arguments, 0, "string.format");
        var formatBytes = GetLuaStringBytes(formatText);
        var output = new List<byte>(formatBytes.Length * 2);
        var argumentIndex = 1;

        for (var index = 0; index < formatBytes.Length; index++)
        {
            if (formatBytes[index] != (byte)'%')
            {
                output.Add(formatBytes[index]);
                continue;
            }

            if (index + 1 < formatBytes.Length && formatBytes[index + 1] == (byte)'%')
            {
                output.Add((byte)'%');
                index++;
                continue;
            }

            var specifier = ParseFormatSpecifier(formatBytes, ref index);
            var value = RequireArgument(arguments, argumentIndex, "string.format");
            argumentIndex++;
            output.AddRange(FormatSpecifierValue(state, specifier, value));
        }

        return [LuaValue.FromString(CreateLuaString(CollectionsMarshal.AsSpan(output)))];
    }

    private static byte[] FormatSpecifierValue(LuaState state, StringFormatSpecifier specifier, LuaValue value)
    {
        return specifier.Conversion switch
        {
            (byte)'c' => ApplyByteWidth(FormatCharSpecifier(value), specifier),
            (byte)'d' or (byte)'i' => FormatSignedIntegerSpecifier(RequireIntegerForFormat(value, specifier.Conversion), specifier, numericBase: 10, uppercase: false),
            (byte)'o' => FormatUnsignedIntegerSpecifier(RequireIntegerForFormat(value, specifier.Conversion), specifier, numericBase: 8, uppercase: false),
            (byte)'u' => FormatUnsignedIntegerSpecifier(RequireIntegerForFormat(value, specifier.Conversion), specifier, numericBase: 10, uppercase: false),
            (byte)'x' => FormatUnsignedIntegerSpecifier(RequireIntegerForFormat(value, specifier.Conversion), specifier, numericBase: 16, uppercase: false),
            (byte)'X' => FormatUnsignedIntegerSpecifier(RequireIntegerForFormat(value, specifier.Conversion), specifier, numericBase: 16, uppercase: true),
            (byte)'e' or (byte)'E' or (byte)'f' or (byte)'g' or (byte)'G' or (byte)'a' or (byte)'A' =>
                FormatFloatSpecifier(RequireNumberForFormat(value, specifier.Conversion), specifier),
            (byte)'s' => ApplyByteWidth(FormatStringSpecifier(state, value, specifier), specifier),
            (byte)'q' => FormatQuotedSpecifier(value, specifier),
            (byte)'p' => ApplyByteWidth(Encoding.UTF8.GetBytes(FormatPointerSpecifier(value)), specifier),
            _ => throw CreateRuntimeError($"invalid option '%{(char)specifier.Conversion}' to 'string.format'")
        };
    }

    private static byte[] FormatCharSpecifier(LuaValue value)
    {
        var integer = RequireIntegerForFormat(value, (byte)'c');
        if (integer is < byte.MinValue or > byte.MaxValue)
        {
            throw CreateRuntimeError("value out of range");
        }

        return [(byte)integer];
    }

    private static byte[] FormatStringSpecifier(LuaState state, LuaValue value, StringFormatSpecifier specifier)
    {
        var text = ConvertToPrintedString(state, value);
        var bytes = GetLuaStringBytes(text);
        if (specifier.Precision is int precision && precision < bytes.Length)
        {
            bytes = bytes[..precision];
        }

        return bytes;
    }

    private static byte[] FormatQuotedSpecifier(LuaValue value, StringFormatSpecifier specifier)
    {
        if (specifier.HasModifiers)
        {
            throw CreateRuntimeError("specifier '%q' does not support modifiers");
        }

        return value.Kind switch
        {
            LuaValueKind.Nil => Encoding.UTF8.GetBytes("nil"),
            LuaValueKind.Boolean => Encoding.UTF8.GetBytes(value.AsBoolean() ? "true" : "false"),
            LuaValueKind.Integer => Encoding.UTF8.GetBytes(value.AsInteger().ToString(CultureInfo.InvariantCulture)),
            LuaValueKind.Float => Encoding.UTF8.GetBytes(FormatHexFloat(value.AsFloat(), precision: 13, uppercase: false)),
            LuaValueKind.String => QuoteLuaString(GetLuaStringBytes(value.AsString())),
            _ => throw CreateRuntimeError($"value has no literal form ({GetTypeName(value)})")
        };
    }

    private static string FormatPointerSpecifier(LuaValue value)
    {
        return value.Kind switch
        {
            LuaValueKind.String => $"0x{RuntimeHelpers.GetHashCode(value.AsString()):x}",
            LuaValueKind.Table => $"0x{RuntimeHelpers.GetHashCode(value.AsTable()):x}",
            LuaValueKind.Function => $"0x{RuntimeHelpers.GetHashCode(value.AsFunction()):x}",
            LuaValueKind.Thread => $"0x{RuntimeHelpers.GetHashCode(value.AsThread()):x}",
            LuaValueKind.UserData => $"0x{RuntimeHelpers.GetHashCode(value.AsUserData()):x}",
            _ => "0x0"
        };
    }

    private static byte[] FormatSignedIntegerSpecifier(long value, StringFormatSpecifier specifier, int numericBase, bool uppercase)
    {
        var negative = value < 0;
        var magnitude = negative
            ? value == long.MinValue
                ? 0x8000_0000_0000_0000UL
                : unchecked((ulong)(-value))
            : unchecked((ulong)value);
        var digits = ApplyIntegerPrecision(FormatUnsignedDigits(magnitude, numericBase, uppercase), specifier.Precision);
        var prefix = negative
            ? "-"
            : specifier.ForceSign
                ? "+"
                : specifier.SpaceSign
                    ? " "
                    : string.Empty;
        return ApplyNumericWidth(prefix, digits, specifier, prefixLengthForZeroPad: prefix.Length);
    }

    private static byte[] FormatUnsignedIntegerSpecifier(long value, StringFormatSpecifier specifier, int numericBase, bool uppercase)
    {
        var digits = ApplyIntegerPrecision(FormatUnsignedDigits(unchecked((ulong)value), numericBase, uppercase), specifier.Precision);
        var prefix = specifier.Alternative ? numericBase switch
        {
            8 => digits.Length == 0 || digits[0] != (byte)'0' ? "0" : string.Empty,
            16 when digits.Length > 0 => uppercase ? "0X" : "0x",
            _ => string.Empty
        } : string.Empty;
        return ApplyNumericWidth(prefix, digits, specifier, prefixLengthForZeroPad: prefix.Length);
    }

    private static byte[] FormatFloatSpecifier(double value, StringFormatSpecifier specifier)
    {
        var negative = double.IsNegative(value);
        var absolute = Math.Abs(value);
        var precision = specifier.Precision ?? (specifier.Conversion is (byte)'a' or (byte)'A' ? 13 : 6);
        if (specifier.Conversion is (byte)'g' or (byte)'G' && precision == 0)
        {
            precision = 1;
        }

        string body;
        switch (specifier.Conversion)
        {
            case (byte)'f':
                body = absolute.ToString($"F{precision}", CultureInfo.InvariantCulture);
                break;
            case (byte)'e':
            case (byte)'E':
                body = absolute.ToString($"E{precision}", CultureInfo.InvariantCulture);
                body = specifier.Conversion == (byte)'e' ? body.ToLowerInvariant() : body.ToUpperInvariant();
                break;
            case (byte)'g':
            case (byte)'G':
                body = absolute.ToString($"G{precision}", CultureInfo.InvariantCulture);
                body = specifier.Conversion == (byte)'g' ? body.ToLowerInvariant() : body.ToUpperInvariant();
                break;
            case (byte)'a':
            case (byte)'A':
                body = FormatHexFloat(absolute, precision, uppercase: specifier.Conversion == (byte)'A');
                if (body.StartsWith('-'))
                {
                    body = body[1..];
                }

                break;
            default:
                throw CreateRuntimeError($"invalid option '%{(char)specifier.Conversion}' to 'string.format'");
        }

        if (specifier.Alternative && !body.Contains('.') && !body.Contains('p') && !body.Contains('P') && !body.Contains('e') && !body.Contains('E'))
        {
            body += ".";
        }

        var prefix = negative
            ? "-"
            : specifier.ForceSign
                ? "+"
                : specifier.SpaceSign
                    ? " "
                    : string.Empty;
        return ApplyNumericWidth(prefix, body, specifier, prefixLengthForZeroPad: prefix.Length);
    }

    private static string FormatHexFloat(double value, int precision, bool uppercase)
    {
        if (double.IsNaN(value))
        {
            return uppercase ? "NAN" : "nan";
        }

        if (double.IsPositiveInfinity(value))
        {
            return uppercase ? "INF" : "inf";
        }

        if (double.IsNegativeInfinity(value))
        {
            return uppercase ? "-INF" : "-inf";
        }

        if (value == 0)
        {
            return uppercase ? "0X0P+0" : "0x0p+0";
        }

        var sign = value < 0 ? "-" : string.Empty;
        var absolute = Math.Abs(value);
        var exponent = Math.ILogB(absolute);
        var mantissa = Math.ScaleB(absolute, -exponent);
        var fraction = mantissa - 1.0;
        var digits = new StringBuilder();
        const string lowerDigits = "0123456789abcdef";
        const string upperDigits = "0123456789ABCDEF";
        var alphabet = uppercase ? upperDigits : lowerDigits;

        for (var index = 0; index < precision; index++)
        {
            fraction *= 16.0;
            var digit = (int)Math.Floor(fraction);
            if (digit < 0)
            {
                digit = 0;
            }
            else if (digit > 15)
            {
                digit = 15;
            }

            digits.Append(alphabet[digit]);
            fraction -= digit;
        }

        while (digits.Length > 0 && digits[^1] == '0')
        {
            digits.Length -= 1;
        }

        var prefix = uppercase ? "0X1" : "0x1";
        var exponentPrefix = uppercase ? "P" : "p";
        return digits.Length == 0
            ? $"{sign}{prefix}{exponentPrefix}{FormatSignedExponent(exponent)}"
            : $"{sign}{prefix}.{digits}{exponentPrefix}{FormatSignedExponent(exponent)}";
    }

    private static string FormatSignedExponent(int exponent)
    {
        return exponent >= 0
            ? $"+{exponent.ToString(CultureInfo.InvariantCulture)}"
            : exponent.ToString(CultureInfo.InvariantCulture);
    }

    private static byte[] ApplyIntegerPrecision(string digits, int? precision)
    {
        if (precision == 0 && digits == "0")
        {
            return [];
        }

        if (precision is int value && value > digits.Length)
        {
            digits = digits.PadLeft(value, '0');
        }

        return Encoding.UTF8.GetBytes(digits);
    }

    private static byte[] ApplyNumericWidth(string prefix, string body, StringFormatSpecifier specifier, int prefixLengthForZeroPad)
    {
        return ApplyNumericWidth(prefix, Encoding.UTF8.GetBytes(body), specifier, prefixLengthForZeroPad);
    }

    private static byte[] ApplyNumericWidth(string prefix, byte[] body, StringFormatSpecifier specifier, int prefixLengthForZeroPad)
    {
        var prefixBytes = Encoding.UTF8.GetBytes(prefix);
        if (specifier.Width is not int width || width <= prefixBytes.Length + body.Length)
        {
            return prefixBytes.Concat(body).ToArray();
        }

        var padCount = width - prefixBytes.Length - body.Length;
        if (specifier.LeftAlign)
        {
            return prefixBytes
                .Concat(body)
                .Concat(Enumerable.Repeat((byte)' ', padCount))
                .ToArray();
        }

        if (specifier.ZeroPad && specifier.Precision is null)
        {
            return prefixBytes
                .Concat(Enumerable.Repeat((byte)'0', padCount))
                .Concat(body)
                .ToArray();
        }

        return Enumerable.Repeat((byte)' ', padCount)
            .Concat(prefixBytes)
            .Concat(body)
            .ToArray();
    }

    private static byte[] ApplyByteWidth(byte[] value, StringFormatSpecifier specifier)
    {
        if (specifier.Width is not int width || width <= value.Length)
        {
            return value;
        }

        var padCount = width - value.Length;
        return specifier.LeftAlign
            ? value.Concat(Enumerable.Repeat((byte)' ', padCount)).ToArray()
            : Enumerable.Repeat((byte)' ', padCount).Concat(value).ToArray();
    }

    private static byte[] QuoteLuaString(byte[] bytes)
    {
        var output = new List<byte>(bytes.Length + 2) { (byte)'"' };
        foreach (var value in bytes)
        {
            switch (value)
            {
                case (byte)'\\':
                    output.AddRange("\\\\"u8.ToArray());
                    break;
                case (byte)'"':
                    output.AddRange("\\\""u8.ToArray());
                    break;
                case (byte)'\a':
                    output.AddRange("\\a"u8.ToArray());
                    break;
                case (byte)'\b':
                    output.AddRange("\\b"u8.ToArray());
                    break;
                case (byte)'\f':
                    output.AddRange("\\f"u8.ToArray());
                    break;
                case (byte)'\n':
                    output.AddRange("\\n"u8.ToArray());
                    break;
                case (byte)'\r':
                    output.AddRange("\\r"u8.ToArray());
                    break;
                case (byte)'\t':
                    output.AddRange("\\t"u8.ToArray());
                    break;
                case (byte)'\v':
                    output.AddRange("\\v"u8.ToArray());
                    break;
                default:
                    if (value is >= 0x20 and <= 0x7E)
                    {
                        output.Add(value);
                    }
                    else
                    {
                        output.Add((byte)'\\');
                        output.AddRange(Encoding.UTF8.GetBytes(value.ToString("D3", CultureInfo.InvariantCulture)));
                    }

                    break;
            }
        }

        output.Add((byte)'"');
        return CollectionsMarshal.AsSpan(output).ToArray();
    }

    private static string FormatUnsignedDigits(ulong value, int numericBase, bool uppercase)
    {
        if (value == 0)
        {
            return "0";
        }

        const string lowerDigits = "0123456789abcdef";
        const string upperDigits = "0123456789ABCDEF";
        var alphabet = uppercase ? upperDigits : lowerDigits;
        var builder = new StringBuilder();
        while (value > 0)
        {
            var digit = (int)(value % (ulong)numericBase);
            builder.Insert(0, alphabet[digit]);
            value /= (ulong)numericBase;
        }

        return builder.ToString();
    }

    private static long RequireIntegerForFormat(LuaValue value, byte conversion)
    {
        if (TryConvertToNumber(value, out var number) && TryGetInteger(number, out var integer))
        {
            return integer;
        }

        throw CreateRuntimeError($"bad argument to 'format' (number has no integer representation for '%{(char)conversion}')");
    }

    private static double RequireNumberForFormat(LuaValue value, byte conversion)
    {
        if (TryConvertToNumber(value, out var number))
        {
            return ToDouble(number);
        }

        throw CreateRuntimeError($"bad argument to 'format' (number expected for '%{(char)conversion}')");
    }

    private static StringFormatSpecifier ParseFormatSpecifier(byte[] format, ref int index)
    {
        var position = index + 1;
        var specifier = new StringFormatSpecifier();

        while (position < format.Length)
        {
            switch (format[position])
            {
                case (byte)'-':
                    specifier.LeftAlign = true;
                    position++;
                    continue;
                case (byte)'+':
                    specifier.ForceSign = true;
                    position++;
                    continue;
                case (byte)' ':
                    specifier.SpaceSign = true;
                    position++;
                    continue;
                case (byte)'#':
                    specifier.Alternative = true;
                    position++;
                    continue;
                case (byte)'0':
                    specifier.ZeroPad = true;
                    position++;
                    continue;
            }

            break;
        }

        if (position < format.Length && char.IsDigit((char)format[position]))
        {
            specifier.Width = ParseFormatNumber(format, ref position);
        }

        if (position < format.Length && format[position] == (byte)'.')
        {
            position++;
            specifier.Precision = ParseFormatNumber(format, ref position);
        }

        if (position >= format.Length)
        {
            throw CreateRuntimeError("invalid format (ends with '%')");
        }

        specifier.Conversion = format[position];
        index = position;
        return specifier;
    }

    private static int ParseFormatNumber(byte[] format, ref int position)
    {
        var start = position;
        while (position < format.Length && char.IsDigit((char)format[position]))
        {
            position++;
        }

        return int.Parse(Encoding.UTF8.GetString(format, start, position - start), CultureInfo.InvariantCulture);
    }

    private sealed class StringFormatSpecifier
    {
        public bool LeftAlign { get; set; }

        public bool ForceSign { get; set; }

        public bool SpaceSign { get; set; }

        public bool Alternative { get; set; }

        public bool ZeroPad { get; set; }

        public int? Width { get; set; }

        public int? Precision { get; set; }

        public byte Conversion { get; set; }

        public bool HasModifiers =>
            LeftAlign || ForceSign || SpaceSign || Alternative || ZeroPad || Width is not null || Precision is not null;
    }
}
