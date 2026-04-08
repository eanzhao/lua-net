namespace Lua.Bytecode.Instructions;

public readonly record struct LuaInstruction(uint Raw)
{
    public LuaOpcode Opcode => (LuaOpcode)LuaInstructionLayout.GetArg(Raw, LuaInstructionLayout.PosOp, LuaInstructionLayout.SizeOp);

    public LuaInstructionFormat Format => LuaOpcodeTables.GetFormat(Opcode);

    public int A => LuaInstructionLayout.GetArg(Raw, LuaInstructionLayout.PosA, LuaInstructionLayout.SizeA);

    public int B => LuaInstructionLayout.GetArg(Raw, LuaInstructionLayout.PosB, LuaInstructionLayout.SizeB);

    public int C => LuaInstructionLayout.GetArg(Raw, LuaInstructionLayout.PosC, LuaInstructionLayout.SizeC);

    public int VB => LuaInstructionLayout.GetArg(Raw, LuaInstructionLayout.PosVB, LuaInstructionLayout.SizeVB);

    public int VC => LuaInstructionLayout.GetArg(Raw, LuaInstructionLayout.PosVC, LuaInstructionLayout.SizeVC);

    public int K => LuaInstructionLayout.GetArg(Raw, LuaInstructionLayout.PosK, 1);

    public int Bx => LuaInstructionLayout.GetArg(Raw, LuaInstructionLayout.PosBx, LuaInstructionLayout.SizeBx);

    public int Ax => LuaInstructionLayout.GetArg(Raw, LuaInstructionLayout.PosAx, LuaInstructionLayout.SizeAx);

    public int SBx => Bx - LuaInstructionLayout.OffsetSBx;

    public int SJ => LuaInstructionLayout.GetArg(Raw, LuaInstructionLayout.PosSJ, LuaInstructionLayout.SizeSJ) - LuaInstructionLayout.OffsetSJ;

    public string Name => LuaOpcodeTables.GetName(Opcode);

    public static LuaInstruction FromRaw(uint raw) => new(raw);
}
