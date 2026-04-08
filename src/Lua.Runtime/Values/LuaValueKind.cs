namespace Lua.Runtime.Values;

public enum LuaValueKind
{
    Nil = 0,
    Boolean = 1,
    Integer = 2,
    Float = 3,
    String = 4,
    Table = 5,
    Function = 6,
    Thread = 7,
    UserData = 8
}
