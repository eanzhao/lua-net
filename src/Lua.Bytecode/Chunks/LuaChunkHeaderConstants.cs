namespace Lua.Bytecode.Chunks;

public static class LuaChunkHeaderConstants
{
    public static ReadOnlySpan<byte> LuaSignature => [0x1B, 0x4C, 0x75, 0x61];

    public const byte LuacVersion = 0x55;

    public const byte LuacFormat = 0;

    public static ReadOnlySpan<byte> LuacData => [0x19, 0x93, 0x0D, 0x0A, 0x1A, 0x0A];

    public const int LuacInt = -0x5678;

    public const uint LuacInstruction = 0x12345678;

    public const double LuacNumber = -370.5;
}
