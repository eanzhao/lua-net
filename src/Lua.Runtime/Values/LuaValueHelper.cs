using System.Globalization;
using Lua.Runtime.Objects;

namespace Lua.Runtime.Values;

public static class LuaValueHelper
{
    public static bool TryGetNumber(LuaValue value, out double result)
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

    public static bool TryGetInteger(LuaValue value, out long result)
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

    public static bool TryParseLuaStringNumber(string text, out LuaValue result)
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

    public static (bool Success, LuaValue Result) TryAdd(LuaValue left, LuaValue right)
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

    public static (bool Success, LuaValue Result) TrySubtract(LuaValue left, LuaValue right)
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

    public static (bool Success, LuaValue Result) TryMultiply(LuaValue left, LuaValue right)
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

    public static (bool Success, LuaValue Result) TryPower(LuaValue left, LuaValue right)
    {
        if (TryGetNumber(left, out var leftNumber) && TryGetNumber(right, out var rightNumber))
        {
            return (true, LuaValue.FromFloat(Math.Pow(leftNumber, rightNumber)));
        }

        return (false, LuaValue.Nil);
    }

    public static (bool Success, LuaValue Result) TryDivide(LuaValue left, LuaValue right)
    {
        if (TryGetNumber(left, out var leftNumber) && TryGetNumber(right, out var rightNumber))
        {
            return (true, LuaValue.FromFloat(leftNumber / rightNumber));
        }

        return (false, LuaValue.Nil);
    }

    public static (bool Success, LuaValue Result) TryIntegerDivide(LuaValue left, LuaValue right)
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

    public static (bool Success, LuaValue Result) TryModulo(LuaValue left, LuaValue right)
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

    public static (bool Success, LuaValue Result) TryUnaryMinus(LuaValue value)
    {
        return value.Kind switch
        {
            LuaValueKind.Integer => (true, LuaValue.FromInteger(-value.AsInteger())),
            LuaValueKind.Float => (true, LuaValue.FromFloat(-value.AsFloat())),
            _ => (false, LuaValue.Nil)
        };
    }

    public static bool TryGetMetamethod(LuaValue value, string metamethodName, out LuaValue metamethod)
    {
        IMetatableOwner? owner = value.Kind switch
        {
            LuaValueKind.Table => value.AsTable(),
            LuaValueKind.UserData => value.AsUserData(),
            _ => null
        };

        if (owner is not null)
        {
            return owner.TryGetMetamethod(metamethodName, out metamethod);
        }

        metamethod = LuaValue.Nil;
        return false;
    }

    public static string GetTypeName(LuaValue value)
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

    public static bool IsTruthy(LuaValue value)
    {
        return value.Kind switch
        {
            LuaValueKind.Nil => false,
            LuaValueKind.Boolean => value.AsBoolean(),
            _ => true
        };
    }

    public static bool IsNaNKey(LuaValue value)
    {
        return value.Kind == LuaValueKind.Float && double.IsNaN(value.AsFloat());
    }

    public static long LuaIntegerFloorDivide(long left, long right)
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

    public static long LuaIntegerModulo(long left, long right)
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

    private static bool TryParseLuaDecimalNumber(ReadOnlySpan<char> text, out LuaValue result)
    {
        var treatsAsFloat = text.IndexOfAny('.', 'e', 'E') >= 0;
        if (!treatsAsFloat &&
            long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
        {
            result = LuaValue.FromInteger(integer);
            return true;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
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
        var dotIndex = -1;
        var sawFractionDigits = false;
        var fractionDigitsAreZero = true;
        if (index < text.Length && text[index] == '.')
        {
            dotIndex = index;
            index++;
            while (index < text.Length && TryGetHexDigit(text[index], out var fractionDigit))
            {
                sawFractionDigits = true;
                fractionDigitsAreZero &= fractionDigit == 0;
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

        if (!hasExponent)
        {
            if (dotIndex < 0)
            {
                return TryParseLuaHexInteger(text, negative, out result);
            }

            if (sawFractionDigits && fractionDigitsAreZero)
            {
                return TryParseLuaHexInteger(text[..dotIndex], negative, out result);
            }

            result = LuaValue.Nil;
            return false;
        }

        var number = (integerPart + fractionPart) * Math.Pow(2d, exponent);
        if (negative)
        {
            number = -number;
        }

        result = LuaValue.FromFloat(number);
        return true;
    }

    private static bool TryParseLuaHexInteger(ReadOnlySpan<char> text, bool negative, out LuaValue result)
    {
        var prefixStart = text[0] is '+' or '-' ? 3 : 2;
        var digits = text[prefixStart..];
        if (!ulong.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var number))
        {
            result = LuaValue.Nil;
            return false;
        }

        result = negative
            ? LuaValue.FromInteger(unchecked((long)(0UL - number)))
            : LuaValue.FromInteger(unchecked((long)number));
        return true;
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

    private static bool NoMetamethod(out LuaValue metamethod)
    {
        metamethod = LuaValue.Nil;
        return false;
    }
}
