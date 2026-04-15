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

    private static bool NoMetamethod(out LuaValue metamethod)
    {
        metamethod = LuaValue.Nil;
        return false;
    }
}
