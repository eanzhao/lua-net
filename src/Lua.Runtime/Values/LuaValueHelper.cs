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

    private static bool NoMetamethod(out LuaValue metamethod)
    {
        metamethod = LuaValue.Nil;
        return false;
    }
}
