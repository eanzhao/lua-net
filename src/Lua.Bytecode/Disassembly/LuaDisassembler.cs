using System.Globalization;
using System.Text;
using Lua.Bytecode.Chunks;
using Lua.Bytecode.Instructions;

namespace Lua.Bytecode.Disassembly;

public sealed class LuaDisassembler
{
    private static readonly string[] MetamethodNames =
    [
        "__index", "__newindex",
        "__gc", "__mode", "__len", "__eq",
        "__add", "__sub", "__mul", "__mod", "__pow",
        "__div", "__idiv",
        "__band", "__bor", "__bxor", "__shl", "__shr",
        "__unm", "__bnot", "__lt", "__le",
        "__concat", "__call", "__close"
    ];

    public string Disassemble(LuaChunk chunk, bool includeDebug = true)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        var builder = new StringBuilder();
        WriteFunction(builder, chunk.MainFunction, includeDebug);
        return builder.ToString();
    }

    private void WriteFunction(StringBuilder builder, LuaPrototype prototype, bool includeDebug)
    {
        WriteHeader(builder, prototype);
        WriteCode(builder, prototype);

        if (includeDebug)
        {
            WriteDebug(builder, prototype);
        }

        foreach (var nested in prototype.NestedPrototypes)
        {
            WriteFunction(builder, nested, includeDebug);
        }
    }

    private void WriteHeader(StringBuilder builder, LuaPrototype prototype)
    {
        builder.AppendLine();

        var functionType = prototype.LineDefined == 0 ? "main" : "function";
        var source = GetDisplaySource(prototype.Source);
        builder.Append(functionType)
            .Append(" <")
            .Append(source)
            .Append(':')
            .Append(prototype.LineDefined)
            .Append(',')
            .Append(prototype.LastLineDefined)
            .Append("> (")
            .Append(prototype.Code.Length)
            .Append(" instructions)")
            .AppendLine();

        builder.Append(prototype.NumberOfParameters);
        if ((prototype.Flags & 0x03) != 0)
        {
            builder.Append('+');
        }

        builder.Append(" params, ")
            .Append(prototype.MaxStackSize)
            .Append(" slots, ")
            .Append(prototype.Upvalues.Length)
            .Append(" upvalues, ")
            .Append(prototype.LocalVariables.Length)
            .Append(" locals, ")
            .Append(prototype.Constants.Length)
            .Append(" constants, ")
            .Append(prototype.NestedPrototypes.Length)
            .Append(" functions")
            .AppendLine();
    }

    private void WriteCode(StringBuilder builder, LuaPrototype prototype)
    {
        for (var pc = 0; pc < prototype.Code.Length; pc++)
        {
            var instruction = LuaInstruction.FromRaw(prototype.Code[pc]);
            var line = LuaLineInfoResolver.GetLine(prototype, pc);

            builder.Append('\t')
                .Append(pc + 1)
                .Append('\t')
                .Append(line > 0 ? $"[{line}]" : "[-]")
                .Append('\t')
                .Append(instruction.Name.PadRight(9))
                .Append('\t')
                .Append(FormatInstruction(prototype, pc, instruction))
                .AppendLine();
        }
    }

    private void WriteDebug(StringBuilder builder, LuaPrototype prototype)
    {
        builder.Append("constants (")
            .Append(prototype.Constants.Length)
            .AppendLine("):");

        for (var index = 0; index < prototype.Constants.Length; index++)
        {
            builder.Append('\t')
                .Append(index)
                .Append('\t')
                .Append(GetConstantType(prototype.Constants[index]))
                .Append('\t')
                .Append(FormatConstant(prototype.Constants[index]))
                .AppendLine();
        }

        builder.Append("locals (")
            .Append(prototype.LocalVariables.Length)
            .AppendLine("):");

        for (var index = 0; index < prototype.LocalVariables.Length; index++)
        {
            var local = prototype.LocalVariables[index];
            builder.Append('\t')
                .Append(index)
                .Append('\t')
                .Append(local.Name ?? "?")
                .Append('\t')
                .Append(local.StartProgramCounter + 1)
                .Append('\t')
                .Append(local.EndProgramCounter + 1)
                .AppendLine();
        }

        builder.Append("upvalues (")
            .Append(prototype.Upvalues.Length)
            .AppendLine("):");

        for (var index = 0; index < prototype.Upvalues.Length; index++)
        {
            var upvalue = prototype.Upvalues[index];
            builder.Append('\t')
                .Append(index)
                .Append('\t')
                .Append(upvalue.Name ?? "-")
                .Append('\t')
                .Append(upvalue.InStack)
                .Append('\t')
                .Append(upvalue.Index)
                .AppendLine();
        }
    }

    private string FormatInstruction(LuaPrototype prototype, int pc, LuaInstruction instruction)
    {
        return instruction.Opcode switch
        {
            LuaOpcode.Move => $"{instruction.A} {instruction.B}",
            LuaOpcode.LoadI => $"{instruction.A} {instruction.SBx}",
            LuaOpcode.LoadF => $"{instruction.A} {instruction.SBx}",
            LuaOpcode.LoadK => $"{instruction.A} {instruction.Bx}{Comment(FormatConstant(prototype, instruction.Bx))}",
            LuaOpcode.LoadKx => $"{instruction.A}{Comment(FormatConstant(prototype, GetExtraArg(prototype, pc).Ax))}",
            LuaOpcode.LoadFalse => $"{instruction.A}",
            LuaOpcode.LFalseSkip => $"{instruction.A}",
            LuaOpcode.LoadTrue => $"{instruction.A}",
            LuaOpcode.LoadNil => $"{instruction.A} {instruction.B}{Comment($"{instruction.B + 1} out")}",
            LuaOpcode.GetUpVal => $"{instruction.A} {instruction.B}{Comment(GetUpvalueName(prototype, instruction.B))}",
            LuaOpcode.SetUpVal => $"{instruction.A} {instruction.B}{Comment(GetUpvalueName(prototype, instruction.B))}",
            LuaOpcode.GetTabUp => $"{instruction.A} {instruction.B} {instruction.C}{Comment($"{GetUpvalueName(prototype, instruction.B)} {FormatConstant(prototype, instruction.C)}")}",
            LuaOpcode.GetTable => $"{instruction.A} {instruction.B} {instruction.C}",
            LuaOpcode.GetI => $"{instruction.A} {instruction.B} {instruction.C}",
            LuaOpcode.GetField => $"{instruction.A} {instruction.B} {instruction.C}{Comment(FormatConstant(prototype, instruction.C))}",
            LuaOpcode.SetTabUp => $"{instruction.A} {instruction.B} {instruction.C}{KSuffix(instruction)}{Comment(FormatSetTabUpComment(prototype, instruction))}",
            LuaOpcode.SetTable => $"{instruction.A} {instruction.B} {instruction.C}{KSuffix(instruction)}{OptionalConstantComment(prototype, instruction)}",
            LuaOpcode.SetI => $"{instruction.A} {instruction.B} {instruction.C}{KSuffix(instruction)}{OptionalConstantComment(prototype, instruction)}",
            LuaOpcode.SetField => $"{instruction.A} {instruction.B} {instruction.C}{KSuffix(instruction)}{Comment(FormatSetFieldComment(prototype, instruction))}",
            LuaOpcode.NewTable => $"{instruction.A} {instruction.VB} {instruction.VC}{KSuffix(instruction)}{Comment(GetNewTableComment(prototype, pc, instruction))}",
            LuaOpcode.Self => $"{instruction.A} {instruction.B} {instruction.C}{KSuffix(instruction)}{OptionalConstantComment(prototype, instruction)}",
            LuaOpcode.AddI => $"{instruction.A} {instruction.B} {ToSignedC(instruction.C)}",
            LuaOpcode.AddK => $"{instruction.A} {instruction.B} {instruction.C}{Comment(FormatConstant(prototype, instruction.C))}",
            LuaOpcode.SubK => $"{instruction.A} {instruction.B} {instruction.C}{Comment(FormatConstant(prototype, instruction.C))}",
            LuaOpcode.MulK => $"{instruction.A} {instruction.B} {instruction.C}{Comment(FormatConstant(prototype, instruction.C))}",
            LuaOpcode.ModK => $"{instruction.A} {instruction.B} {instruction.C}{Comment(FormatConstant(prototype, instruction.C))}",
            LuaOpcode.PowK => $"{instruction.A} {instruction.B} {instruction.C}{Comment(FormatConstant(prototype, instruction.C))}",
            LuaOpcode.DivK => $"{instruction.A} {instruction.B} {instruction.C}{Comment(FormatConstant(prototype, instruction.C))}",
            LuaOpcode.IDivK => $"{instruction.A} {instruction.B} {instruction.C}{Comment(FormatConstant(prototype, instruction.C))}",
            LuaOpcode.BandK => $"{instruction.A} {instruction.B} {instruction.C}{Comment(FormatConstant(prototype, instruction.C))}",
            LuaOpcode.BorK => $"{instruction.A} {instruction.B} {instruction.C}{Comment(FormatConstant(prototype, instruction.C))}",
            LuaOpcode.BXorK => $"{instruction.A} {instruction.B} {instruction.C}{Comment(FormatConstant(prototype, instruction.C))}",
            LuaOpcode.ShlI => $"{instruction.A} {instruction.B} {ToSignedC(instruction.C)}",
            LuaOpcode.ShrI => $"{instruction.A} {instruction.B} {ToSignedC(instruction.C)}",
            LuaOpcode.Add => $"{instruction.A} {instruction.B} {instruction.C}",
            LuaOpcode.Sub => $"{instruction.A} {instruction.B} {instruction.C}",
            LuaOpcode.Mul => $"{instruction.A} {instruction.B} {instruction.C}",
            LuaOpcode.Mod => $"{instruction.A} {instruction.B} {instruction.C}",
            LuaOpcode.Pow => $"{instruction.A} {instruction.B} {instruction.C}",
            LuaOpcode.Div => $"{instruction.A} {instruction.B} {instruction.C}",
            LuaOpcode.IDiv => $"{instruction.A} {instruction.B} {instruction.C}",
            LuaOpcode.Band => $"{instruction.A} {instruction.B} {instruction.C}",
            LuaOpcode.Bor => $"{instruction.A} {instruction.B} {instruction.C}",
            LuaOpcode.BXor => $"{instruction.A} {instruction.B} {instruction.C}",
            LuaOpcode.Shl => $"{instruction.A} {instruction.B} {instruction.C}",
            LuaOpcode.Shr => $"{instruction.A} {instruction.B} {instruction.C}",
            LuaOpcode.MmBin => $"{instruction.A} {instruction.B} {instruction.C}{Comment(GetMetamethodName(instruction.C))}",
            LuaOpcode.MmBinI => $"{instruction.A} {ToSignedB(instruction.B)} {instruction.C} {instruction.K}{Comment(FormatMmBinIComment(instruction))}",
            LuaOpcode.MmBinK => $"{instruction.A} {instruction.B} {instruction.C} {instruction.K}{Comment($"{GetMetamethodName(instruction.C)} {FormatConstant(prototype, instruction.B)}{FlipSuffix(instruction)}")}",
            LuaOpcode.Unm => $"{instruction.A} {instruction.B}",
            LuaOpcode.BNot => $"{instruction.A} {instruction.B}",
            LuaOpcode.Not => $"{instruction.A} {instruction.B}",
            LuaOpcode.Len => $"{instruction.A} {instruction.B}",
            LuaOpcode.Concat => $"{instruction.A} {instruction.B}",
            LuaOpcode.Close => $"{instruction.A}",
            LuaOpcode.Tbc => $"{instruction.A}",
            LuaOpcode.Jmp => $"{instruction.SJ}{Comment($"to {pc + instruction.SJ + 2}")}",
            LuaOpcode.Eq => $"{instruction.A} {instruction.B} {instruction.K}",
            LuaOpcode.Lt => $"{instruction.A} {instruction.B} {instruction.K}",
            LuaOpcode.Le => $"{instruction.A} {instruction.B} {instruction.K}",
            LuaOpcode.EqK => $"{instruction.A} {instruction.B} {instruction.K}{Comment(FormatConstant(prototype, instruction.B))}",
            LuaOpcode.EqI => $"{instruction.A} {ToSignedB(instruction.B)} {instruction.K}",
            LuaOpcode.LtI => $"{instruction.A} {ToSignedB(instruction.B)} {instruction.K}",
            LuaOpcode.LeI => $"{instruction.A} {ToSignedB(instruction.B)} {instruction.K}",
            LuaOpcode.GtI => $"{instruction.A} {ToSignedB(instruction.B)} {instruction.K}",
            LuaOpcode.GeI => $"{instruction.A} {ToSignedB(instruction.B)} {instruction.K}",
            LuaOpcode.Test => $"{instruction.A} {instruction.K}",
            LuaOpcode.TestSet => $"{instruction.A} {instruction.B} {instruction.K}",
            LuaOpcode.Call => $"{instruction.A} {instruction.B} {instruction.C}{Comment(FormatCallComment(instruction))}",
            LuaOpcode.TailCall => $"{instruction.A} {instruction.B} {instruction.C}{KSuffix(instruction)}{Comment($"{instruction.B - 1} in")}",
            LuaOpcode.Return => $"{instruction.A} {instruction.B} {instruction.C}{KSuffix(instruction)}{Comment(FormatReturnComment(instruction))}",
            LuaOpcode.Return0 => string.Empty,
            LuaOpcode.Return1 => $"{instruction.A}",
            LuaOpcode.ForLoop => $"{instruction.A} {instruction.Bx}{Comment($"to {pc - instruction.Bx + 2}")}",
            LuaOpcode.ForPrep => $"{instruction.A} {instruction.Bx}{Comment($"exit to {pc + instruction.Bx + 3}")}",
            LuaOpcode.TForPrep => $"{instruction.A} {instruction.Bx}{Comment($"to {pc + instruction.Bx + 2}")}",
            LuaOpcode.TForCall => $"{instruction.A} {instruction.C}",
            LuaOpcode.TForLoop => $"{instruction.A} {instruction.Bx}{Comment($"to {pc - instruction.Bx + 2}")}",
            LuaOpcode.SetList => $"{instruction.A} {instruction.VB} {instruction.VC}{KSuffix(instruction)}{FormatSetListComment(prototype, pc, instruction)}",
            LuaOpcode.Closure => $"{instruction.A} {instruction.Bx}{Comment($"proto {instruction.Bx}")}",
            LuaOpcode.VarArg => $"{instruction.A} {instruction.B} {instruction.C}{KSuffix(instruction)}{Comment(FormatVarArgComment(instruction))}",
            LuaOpcode.GetVArg => $"{instruction.A} {instruction.B} {instruction.C}",
            LuaOpcode.ErrNNil => $"{instruction.A} {instruction.Bx}{Comment(FormatErrNNilComment(prototype, instruction.Bx))}",
            LuaOpcode.VarArgPrep => $"{instruction.A}",
            LuaOpcode.ExtraArg => $"{instruction.Ax}",
            _ => $"{instruction.A} {instruction.B} {instruction.C}"
        };
    }

    private static string FormatSetTabUpComment(LuaPrototype prototype, LuaInstruction instruction)
    {
        var builder = new StringBuilder();
        builder.Append(GetUpvalueName(prototype, instruction.A))
            .Append(' ')
            .Append(FormatConstant(prototype, instruction.B));

        if (instruction.K == 1)
        {
            builder.Append(' ').Append(FormatConstant(prototype, instruction.C));
        }

        return builder.ToString();
    }

    private static string FormatSetFieldComment(LuaPrototype prototype, LuaInstruction instruction)
    {
        var builder = new StringBuilder();
        builder.Append(FormatConstant(prototype, instruction.B));

        if (instruction.K == 1)
        {
            builder.Append(' ').Append(FormatConstant(prototype, instruction.C));
        }

        return builder.ToString();
    }

    private static string GetNewTableComment(LuaPrototype prototype, int pc, LuaInstruction instruction)
    {
        var arrayCount = instruction.VC;
        if (instruction.K == 1)
        {
            var extraArg = GetExtraArg(prototype, pc).Ax;
            arrayCount += extraArg * (LuaInstructionLayout.MaxArgC + 1);
        }

        return arrayCount.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatMmBinIComment(LuaInstruction instruction)
    {
        var builder = new StringBuilder();
        builder.Append(GetMetamethodName(instruction.C));
        if (instruction.K == 1)
        {
            builder.Append(" flip");
        }

        return builder.ToString();
    }

    private static string FormatCallComment(LuaInstruction instruction)
    {
        var inputs = instruction.B == 0 ? "all in" : $"{instruction.B - 1} in";
        var outputs = instruction.C == 0 ? "all out" : $"{instruction.C - 1} out";
        return $"{inputs} {outputs}";
    }

    private static string FormatReturnComment(LuaInstruction instruction)
    {
        return instruction.B == 0 ? "all out" : $"{instruction.B - 1} out";
    }

    private static string FormatSetListComment(LuaPrototype prototype, int pc, LuaInstruction instruction)
    {
        if (instruction.K == 0)
        {
            return string.Empty;
        }

        var extraArg = GetExtraArg(prototype, pc).Ax;
        return Comment((instruction.C + extraArg * (LuaInstructionLayout.MaxArgC + 1)).ToString(CultureInfo.InvariantCulture));
    }

    private static string FormatVarArgComment(LuaInstruction instruction)
    {
        return instruction.C == 0 ? "all out" : $"{instruction.C - 1} out";
    }

    private static string FormatErrNNilComment(LuaPrototype prototype, int bx)
    {
        return bx == 0 ? "?" : FormatConstant(prototype, bx - 1);
    }

    private static string OptionalConstantComment(LuaPrototype prototype, LuaInstruction instruction)
    {
        return instruction.K == 1 ? Comment(FormatConstant(prototype, instruction.C)) : string.Empty;
    }

    private static string KSuffix(LuaInstruction instruction) => instruction.K == 1 ? "k" : string.Empty;

    private static string FlipSuffix(LuaInstruction instruction) => instruction.K == 1 ? " flip" : string.Empty;

    private static LuaInstruction GetExtraArg(LuaPrototype prototype, int pc)
    {
        if (pc + 1 >= prototype.Code.Length)
        {
            return default;
        }

        return LuaInstruction.FromRaw(prototype.Code[pc + 1]);
    }

    private static string GetUpvalueName(LuaPrototype prototype, int index)
    {
        return index >= 0 && index < prototype.Upvalues.Length
            ? prototype.Upvalues[index].Name ?? "-"
            : "-";
    }

    private static int ToSignedC(int value) => value - LuaInstructionLayout.OffsetSC;

    private static int ToSignedB(int value) => value - LuaInstructionLayout.OffsetSC;

    private static string FormatConstant(LuaPrototype prototype, int index)
    {
        return index >= 0 && index < prototype.Constants.Length
            ? FormatConstant(prototype.Constants[index])
            : "?";
    }

    private static string FormatConstant(LuaConstant constant)
    {
        return constant.Kind switch
        {
            LuaConstantKind.Nil => "nil",
            LuaConstantKind.Boolean => constant.AsBoolean() ? "true" : "false",
            LuaConstantKind.Integer => constant.AsInteger().ToString(CultureInfo.InvariantCulture),
            LuaConstantKind.Float => FormatFloatConstant(constant.AsFloat()),
            LuaConstantKind.String => QuoteString(constant.AsString()),
            _ => constant.ToString()
        };
    }

    private static string FormatFloatConstant(double value)
    {
        var text = value.ToString("G17", CultureInfo.InvariantCulture);
        return text.IndexOfAny(['.', 'e', 'E']) >= 0 ? text : $"{text}.0";
    }

    private static string QuoteString(string value)
    {
        var builder = new StringBuilder();
        builder.Append('"');

        foreach (var ch in value)
        {
            builder.Append(ch switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\a' => "\\a",
                '\b' => "\\b",
                '\f' => "\\f",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\v' => "\\v",
                _ when !char.IsControl(ch) => ch.ToString(),
                _ => $"\\{(int)ch:000}"
            });
        }

        builder.Append('"');
        return builder.ToString();
    }

    private static string GetConstantType(LuaConstant constant)
    {
        return constant.Kind switch
        {
            LuaConstantKind.Nil => "N",
            LuaConstantKind.Boolean => "B",
            LuaConstantKind.Integer => "I",
            LuaConstantKind.Float => "F",
            LuaConstantKind.String => "S",
            _ => "?"
        };
    }

    private static string GetMetamethodName(int index)
    {
        return index >= 0 && index < MetamethodNames.Length
            ? MetamethodNames[index]
            : $"tm[{index}]";
    }

    private static string GetDisplaySource(string? source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return "=?";
        }

        return source[0] switch
        {
            '@' or '=' => source[1..],
            '\u001b' => "(bstring)",
            _ => source
        };
    }

    private static string Comment(string text) => string.IsNullOrEmpty(text) ? string.Empty : $"\t; {text}";
}
