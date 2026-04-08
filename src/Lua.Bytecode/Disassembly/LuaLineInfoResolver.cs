using Lua.Bytecode.Chunks;

namespace Lua.Bytecode.Disassembly;

public static class LuaLineInfoResolver
{
    private const sbyte AbsoluteLineInfoMarker = -128;
    private const int MaxInstructionsWithoutAbsoluteInfo = 128;

    public static int GetLine(LuaPrototype prototype, int programCounter)
    {
        ArgumentNullException.ThrowIfNull(prototype);

        if (prototype.LineInfo.Length == 0 || programCounter < 0 || programCounter >= prototype.LineInfo.Length)
        {
            return -1;
        }

        var (baseLine, baseProgramCounter) = GetBaseLine(prototype, programCounter);

        while (baseProgramCounter++ < programCounter)
        {
            var delta = prototype.LineInfo[baseProgramCounter];
            if (delta == AbsoluteLineInfoMarker)
            {
                continue;
            }

            baseLine += delta;
        }

        return baseLine;
    }

    private static (int Line, int ProgramCounter) GetBaseLine(LuaPrototype prototype, int programCounter)
    {
        if (prototype.AbsoluteLineInfo.Length == 0 || programCounter < prototype.AbsoluteLineInfo[0].ProgramCounter)
        {
            return (prototype.LineDefined, -1);
        }

        var index = programCounter / MaxInstructionsWithoutAbsoluteInfo - 1;
        while (index + 1 < prototype.AbsoluteLineInfo.Length &&
               programCounter >= prototype.AbsoluteLineInfo[index + 1].ProgramCounter)
        {
            index++;
        }

        var info = prototype.AbsoluteLineInfo[index];
        return (info.Line, info.ProgramCounter);
    }
}
