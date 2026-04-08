using Lua.Bytecode.Instructions;
using Shouldly;

namespace Lua.Bytecode.Tests;

public class LuaInstructionTests
{
    [Fact]
    public void DecodeIAbcInstruction_ShouldExposeOpcodeAndArguments()
    {
        var raw =
            ((uint)LuaOpcode.Add << LuaInstructionLayout.PosOp) |
            (12u << LuaInstructionLayout.PosA) |
            (1u << LuaInstructionLayout.PosK) |
            (34u << LuaInstructionLayout.PosB) |
            (56u << LuaInstructionLayout.PosC);

        var instruction = LuaInstruction.FromRaw(raw);

        instruction.Opcode.ShouldBe(LuaOpcode.Add);
        instruction.Name.ShouldBe("ADD");
        instruction.Format.ShouldBe(LuaInstructionFormat.IABC);
        instruction.A.ShouldBe(12);
        instruction.K.ShouldBe(1);
        instruction.B.ShouldBe(34);
        instruction.C.ShouldBe(56);
    }

    [Fact]
    public void DecodeIAbxInstruction_ShouldExposeBxAndSignedVariant()
    {
        var bx = 70_000u;
        var raw =
            ((uint)LuaOpcode.LoadK << LuaInstructionLayout.PosOp) |
            (5u << LuaInstructionLayout.PosA) |
            (bx << LuaInstructionLayout.PosBx);

        var instruction = LuaInstruction.FromRaw(raw);

        instruction.Opcode.ShouldBe(LuaOpcode.LoadK);
        instruction.Format.ShouldBe(LuaInstructionFormat.IABx);
        instruction.A.ShouldBe(5);
        instruction.Bx.ShouldBe((int)bx);
    }

    [Fact]
    public void DecodeIAsBxInstruction_ShouldExposeSignedOffset()
    {
        const int sBx = -1234;
        var encoded = (uint)(sBx + LuaInstructionLayout.OffsetSBx);
        var raw =
            ((uint)LuaOpcode.LoadI << LuaInstructionLayout.PosOp) |
            (7u << LuaInstructionLayout.PosA) |
            (encoded << LuaInstructionLayout.PosBx);

        var instruction = LuaInstruction.FromRaw(raw);

        instruction.Opcode.ShouldBe(LuaOpcode.LoadI);
        instruction.Format.ShouldBe(LuaInstructionFormat.IAsBx);
        instruction.SBx.ShouldBe(sBx);
    }

    [Fact]
    public void DecodeIvAbcInstruction_ShouldExposeVariantArguments()
    {
        var raw =
            ((uint)LuaOpcode.NewTable << LuaInstructionLayout.PosOp) |
            (9u << LuaInstructionLayout.PosA) |
            (1u << LuaInstructionLayout.PosK) |
            (17u << LuaInstructionLayout.PosVB) |
            (511u << LuaInstructionLayout.PosVC);

        var instruction = LuaInstruction.FromRaw(raw);

        instruction.Opcode.ShouldBe(LuaOpcode.NewTable);
        instruction.Format.ShouldBe(LuaInstructionFormat.IvABC);
        instruction.A.ShouldBe(9);
        instruction.K.ShouldBe(1);
        instruction.VB.ShouldBe(17);
        instruction.VC.ShouldBe(511);
    }

    [Fact]
    public void DecodeIsJInstruction_ShouldExposeSignedJump()
    {
        const int sJ = -3210;
        var encoded = (uint)(sJ + LuaInstructionLayout.OffsetSJ);
        var raw =
            ((uint)LuaOpcode.Jmp << LuaInstructionLayout.PosOp) |
            (encoded << LuaInstructionLayout.PosSJ);

        var instruction = LuaInstruction.FromRaw(raw);

        instruction.Opcode.ShouldBe(LuaOpcode.Jmp);
        instruction.Format.ShouldBe(LuaInstructionFormat.IsJ);
        instruction.SJ.ShouldBe(sJ);
    }
}
