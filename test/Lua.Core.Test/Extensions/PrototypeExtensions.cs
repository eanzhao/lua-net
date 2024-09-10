using Lua.Core.BinChunk;

namespace Lua.Core.Test.Extensions;

public static class PrototypeExtensions
{
    public static void List(this Prototype f)
    {
        f.PrintHeader();
        f.PrintCode();
        f.PrintDetail();
        foreach (var p in f.Protos)
        {
            p.List();
        }
    }

    public static void PrintHeader(this Prototype f)
    {
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

        Console.WriteLine(
            $"\n{funcType} <{f.Source}:{f.LineDefined},{f.LastLineDefined}> ({f.Code.Length} instruction)");
        Console.Write($"{f.NumParams}{varargFlag} params, {f.MaxStackSize} slots, {f.Upvalues.Length} upvalues, ");
        Console.WriteLine($"{f.LocVars.Length} locals, {f.Constants.Length} constants, {f.Protos.Length} functions");
    }

    public static void PrintCode(this Prototype f)
    {
        for (var pc = 0; pc < f.Code.Length; pc++)
        {
            var c = f.Code[pc];
            var line = "-";
            if (f.LineInfo.Length > 0)
            {
                line = f.LineInfo[pc].ToString();
            }

            Console.Write("\t{0}\t[{1}]\t0x{2:x8}\n", pc + 1, line, c);
        }
    }

    private static void PrintDetail(this Prototype f)
    {
        Console.Write("constants ({0}):\n", f.Constants.Length);
        for (var i = 0; i < f.Constants.Length; i++)
        {
            var k = f.Constants[i];
            Console.Write("\t{0}\t{1}\n", i + 1, ConstantToString(k));
        }

        Console.Write("locals ({0}):\n", f.LocVars.Length);
        for (var i = 0; i < f.LocVars.Length; i++)
        {
            var locVar = f.LocVars[i];
            Console.Write("\t{0}\t{1}\t{2}\t{3}\n", i, locVar.VarName, locVar.StartPC + 1, locVar.EndPC + 1);
        }

        Console.Write("upvalues ({0}):\n", f.Upvalues.Length);
        for (var i = 0; i < f.Upvalues.Length; i++)
        {
            var upval = f.Upvalues[i];
            Console.Write("\t{0}\t{1}\t{2}\t{3}\n", i, f.UpvalName(i), upval.Instack, upval.Idx);
        }
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