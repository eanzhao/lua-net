using Lua.Bytecode.Instructions;
using Shouldly;

namespace Lua.Bytecode.Tests;

public class LuaOpcodeTablesTests
{
    [Fact]
    public void OpcodeTables_ShouldMatchEnumSize()
    {
        LuaOpcodeTables.Count.ShouldBe(Enum.GetValues<LuaOpcode>().Length);
    }

    [Fact]
    public void OpcodeNames_ShouldMatchOfficialOrder()
    {
        LuaOpcodeTables.GetName(LuaOpcode.Move).ShouldBe("MOVE");
        LuaOpcodeTables.GetName(LuaOpcode.LoadI).ShouldBe("LOADI");
        LuaOpcodeTables.GetName(LuaOpcode.Jmp).ShouldBe("JMP");
        LuaOpcodeTables.GetName(LuaOpcode.ExtraArg).ShouldBe("EXTRAARG");
    }

    [Fact]
    public void OpcodeFormats_ShouldMatchOfficialModes()
    {
        LuaOpcodeTables.GetFormat(LuaOpcode.LoadK).ShouldBe(LuaInstructionFormat.IABx);
        LuaOpcodeTables.GetFormat(LuaOpcode.NewTable).ShouldBe(LuaInstructionFormat.IvABC);
        LuaOpcodeTables.GetFormat(LuaOpcode.Jmp).ShouldBe(LuaInstructionFormat.IsJ);
        LuaOpcodeTables.GetFormat(LuaOpcode.ExtraArg).ShouldBe(LuaInstructionFormat.IAx);
    }
}
