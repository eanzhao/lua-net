using System.Text;
using Lua.Core.BinChunk;

namespace Lua.Core.Extensions;

public static class PrototypeExtensions
{
    public static string List(this Prototype f)
    {
        var sb = new StringBuilder();

        sb.AppendLine(f.PrintHeader());
        sb.AppendLine(f.PrintCode());
        sb.AppendLine(f.PrintDetail());
        foreach (var p in f.Protos)
        {
            sb.AppendLine(p.List());
        }

        return sb.ToString();
    }

    public static string PrintHeader(this Prototype f)
    {
        var sb = new StringBuilder();
        var funcType = "main";
        if (f.LineDefined > 0)
        {
            funcType = "function";
        }

        var varargFlag = string.Empty;
        if (f.IsVararg > 0)
        {
            varargFlag = "+";
        }

        var header = $"\n{funcType} <{f.Source}:{f.LineDefined},{f.LastLineDefined}> ({f.Code.Length} instruction at )";
        sb.PrintAndCollect(header);

        var details =
            $"{f.NumParams}{varargFlag} params, {f.MaxStackSize} slots, {f.Upvalues.Length} upvalues, {f.LocVars.Length} locals, {f.Constants.Length} constants, {f.Protos.Length} functions";
        sb.PrintAndCollect(details);

        return sb.ToString();
    }

    public static string PrintCode(this Prototype f)
    {
        var sb = new StringBuilder();

        for (var pc = 0; pc < f.Code.Length; pc++)
        {
            var c = f.Code[pc];
            var line = "-";
            if (f.LineInfo.Length > 0)
            {
                line = f.LineInfo[pc].ToString();
            }

            sb.PrintAndCollect($"\t{pc + 1}\t[{line}]\t0x{c:x8}");
        }

        return sb.ToString();
    }

    private static string PrintDetail(this Prototype f)
    {
        var sb = new StringBuilder();

        sb.PrintAndCollect($"constants ({f.Constants.Length}):");
        for (var i = 0; i < f.Constants.Length; i++)
        {
            var k = f.Constants[i];
            sb.PrintAndCollect($"\t{i + 1}\t{ConstantToString(k)}\n");
        }

        sb.PrintAndCollect($"locals ({f.LocVars.Length}):\n");
        for (var i = 0; i < f.LocVars.Length; i++)
        {
            var locVar = f.LocVars[i];
            sb.PrintAndCollect($"\t{i}\t{locVar.VarName}\t{locVar.StartPC + 1}\t{locVar.EndPC + 1}");
        }

        sb.PrintAndCollect($"upvalues ({f.Upvalues.Length}):");
        for (var i = 0; i < f.Upvalues.Length; i++)
        {
            var upval = f.Upvalues[i];
            sb.PrintAndCollect($"\t{i}\t{f.UpvalName(i)}\t{upval.Instack}\t{upval.Idx}");
        }

        return sb.ToString();
    }

    private static string UpvalName(this Prototype f, int idx)
    {
        return f.UpvalueNames.Length > 0 ? f.UpvalueNames[idx] : "-";
    }

    private static object ConstantToString(object? k)
    {
        if (k == null)
        {
            return "nil";
        }

        return k.GetType().Name switch
        {
            "Boolean" => (bool)k,
            "Double" => (double)k,
            "Long" => (long)k,
            "String" => k,
            _ => "?"
        };
    }
}