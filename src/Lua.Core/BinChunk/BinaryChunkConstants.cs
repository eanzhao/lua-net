namespace Lua.Core.BinChunk;

public class BinaryChunkConstants
{
    public static readonly byte[] LuaSignature = { 0x1B, 0x4C, 0x75, 0x61 };
    public const byte LuacVersion = 0x53;
    public const byte LuacFormat = 0;
    public static readonly byte[] LuacData = { 0x19, 0x93, 0x0D, 0x0A, 0x1A, 0x0A };
    public const uint InstructionSize = 4;
    public const uint LuaIntegerSize = 8;
    public const uint LuaNumberSize = 8;
    public const ushort LuacInt = 0x5678;
    public const double LuacNum = 370.5;

    public const byte TagNil = 0x00;
    public const byte TagFalse = 0x01;
    public const byte TagTrue = 0x11;
    public const byte TagNumber = 0x03;
    public const byte TagInteger = 0x13;
    public const byte TagShortStr = 0x04;
    public const byte TagLongStr = 0x14;
}