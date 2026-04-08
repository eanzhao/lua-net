namespace Lua.Bytecode.Instructions;

public static class LuaInstructionLayout
{
    public const int SizeC = 8;
    public const int SizeVC = 10;
    public const int SizeB = 8;
    public const int SizeVB = 6;
    public const int SizeBx = SizeC + SizeB + 1;
    public const int SizeA = 8;
    public const int SizeAx = SizeBx + SizeA;
    public const int SizeSJ = SizeBx + SizeA;
    public const int SizeOp = 7;

    public const int PosOp = 0;
    public const int PosA = PosOp + SizeOp;
    public const int PosK = PosA + SizeA;
    public const int PosB = PosK + 1;
    public const int PosVB = PosK + 1;
    public const int PosC = PosB + SizeB;
    public const int PosVC = PosVB + SizeVB;
    public const int PosBx = PosK;
    public const int PosAx = PosA;
    public const int PosSJ = PosA;

    public const int MaxArgA = (1 << SizeA) - 1;
    public const int MaxArgB = (1 << SizeB) - 1;
    public const int MaxArgVB = (1 << SizeVB) - 1;
    public const int MaxArgC = (1 << SizeC) - 1;
    public const int MaxArgVC = (1 << SizeVC) - 1;
    public const int MaxArgBx = (1 << SizeBx) - 1;
    public const int MaxArgAx = (1 << SizeAx) - 1;
    public const int MaxArgSJ = (1 << SizeSJ) - 1;
    public const int OffsetSBx = MaxArgBx >> 1;
    public const int OffsetSC = MaxArgC >> 1;
    public const int OffsetSJ = MaxArgSJ >> 1;

    public static int GetArg(uint instruction, int position, int size)
    {
        var mask = (1u << size) - 1u;
        return (int)((instruction >> position) & mask);
    }
}
