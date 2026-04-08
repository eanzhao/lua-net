using Lua.Bytecode.Chunks;
using Shouldly;

namespace Lua.Bytecode.Tests;

public class LuaChunkHeaderConstantsTests
{
    [Fact]
    public void HeaderConstants_ShouldMatchLua55OfficialValues()
    {
        LuaChunkHeaderConstants.LuaSignature.ToArray().ShouldBe([0x1B, 0x4C, 0x75, 0x61]);
        LuaChunkHeaderConstants.LuacData.ToArray().ShouldBe([0x19, 0x93, 0x0D, 0x0A, 0x1A, 0x0A]);
        LuaChunkHeaderConstants.LuacVersion.ShouldBe((byte)0x55);
        LuaChunkHeaderConstants.LuacFormat.ShouldBe((byte)0);
        LuaChunkHeaderConstants.LuacInt.ShouldBe(-0x5678);
        LuaChunkHeaderConstants.LuacInstruction.ShouldBe(0x12345678u);
        LuaChunkHeaderConstants.LuacNumber.ShouldBe(-370.5);
    }
}
