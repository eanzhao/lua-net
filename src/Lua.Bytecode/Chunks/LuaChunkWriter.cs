using System.Text;

namespace Lua.Bytecode.Chunks;

public sealed class LuaChunkWriter
{
    private const byte LuaVNil = 0x00;
    private const byte LuaVFalse = 0x01;
    private const byte LuaVTrue = 0x11;
    private const byte LuaVNumInt = 0x03;
    private const byte LuaVNumFlt = 0x13;
    private const byte LuaVShrStr = 0x04;
    private const byte LuaVLngStr = 0x14;
    private const int MaxShortStringLength = 40;

    private BinaryWriter? _writer;
    private ulong _offset;

    public byte[] Write(LuaChunk chunk, bool stripDebugInformation = false)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ArgumentNullException.ThrowIfNull(chunk.Header);
        ArgumentNullException.ThrowIfNull(chunk.MainFunction);

        ValidateHeader(chunk.Header);

        if (chunk.MainUpvalueCount != chunk.MainFunction.Upvalues.Length)
        {
            throw new InvalidDataException("main upvalue count mismatch");
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        _writer = writer;
        _offset = 0;

        WriteHeader(chunk.Header);
        WriteByte(chunk.MainUpvalueCount);
        WritePrototype(chunk.MainFunction, stripDebugInformation);

        return stream.ToArray();
    }

    private static void ValidateHeader(LuaChunkHeader header)
    {
        if (header.IntSize != sizeof(int))
        {
            throw new InvalidDataException("int size mismatch");
        }

        if (header.InstructionSize != sizeof(uint))
        {
            throw new InvalidDataException("instruction size mismatch");
        }

        if (header.LuaIntegerSize != sizeof(long))
        {
            throw new InvalidDataException("Lua integer size mismatch");
        }

        if (header.LuaNumberSize != sizeof(double))
        {
            throw new InvalidDataException("Lua number size mismatch");
        }
    }

    private void WriteHeader(LuaChunkHeader header)
    {
        WriteBytes(LuaChunkHeaderConstants.LuaSignature);
        WriteByte(header.Version);
        WriteByte(header.Format);
        WriteBytes(LuaChunkHeaderConstants.LuacData);
        WriteNumericHeader(header.IntSize, BitConverter.GetBytes(header.IntFormatMarker));
        WriteNumericHeader(header.InstructionSize, BitConverter.GetBytes(header.InstructionFormatMarker));
        WriteNumericHeader(header.LuaIntegerSize, BitConverter.GetBytes(header.LuaIntegerFormatMarker));
        WriteNumericHeader(header.LuaNumberSize, BitConverter.GetBytes(header.LuaNumberFormatMarker));
    }

    private void WriteNumericHeader(byte size, ReadOnlySpan<byte> bytes)
    {
        if (size != bytes.Length)
        {
            throw new InvalidDataException("numeric header size mismatch");
        }

        WriteByte(size);
        WriteBytes(bytes);
    }

    private void WritePrototype(LuaPrototype prototype, bool stripDebugInformation)
    {
        WriteInt(prototype.LineDefined);
        WriteInt(prototype.LastLineDefined);
        WriteByte(prototype.NumberOfParameters);
        WriteByte(prototype.Flags);
        WriteByte(prototype.MaxStackSize);
        WriteCode(prototype.Code);
        WriteConstants(prototype.Constants);
        WriteUpvalues(prototype.Upvalues);
        WritePrototypes(prototype.NestedPrototypes, stripDebugInformation);
        WriteString(stripDebugInformation ? null : prototype.Source);
        WriteDebugInformation(prototype, stripDebugInformation);
    }

    private void WriteCode(uint[] code)
    {
        WriteInt(code.Length);
        Align(sizeof(uint));

        foreach (var instruction in code)
        {
            WriteUInt32(instruction);
        }
    }

    private void WriteConstants(LuaConstant[] constants)
    {
        WriteInt(constants.Length);

        foreach (var constant in constants)
        {
            WriteConstant(constant);
        }
    }

    private void WriteConstant(LuaConstant constant)
    {
        switch (constant.Kind)
        {
            case LuaConstantKind.Nil:
                WriteByte(LuaVNil);
                break;
            case LuaConstantKind.Boolean:
                WriteByte(constant.AsBoolean() ? LuaVTrue : LuaVFalse);
                break;
            case LuaConstantKind.Integer:
                WriteByte(LuaVNumInt);
                WriteLuaInteger(constant.AsInteger());
                break;
            case LuaConstantKind.Float:
                WriteByte(LuaVNumFlt);
                WriteDouble(constant.AsFloat());
                break;
            case LuaConstantKind.String:
            {
                var bytes = Encoding.UTF8.GetBytes(constant.AsString());
                WriteByte(bytes.Length <= MaxShortStringLength ? LuaVShrStr : LuaVLngStr);
                WriteStringBytes(bytes);
                break;
            }
            default:
                throw new InvalidDataException($"Unsupported constant kind '{constant.Kind}'.");
        }
    }

    private void WriteUpvalues(LuaUpvalueDescriptor[] upvalues)
    {
        WriteInt(upvalues.Length);

        foreach (var upvalue in upvalues)
        {
            WriteByte(upvalue.InStack);
            WriteByte(upvalue.Index);
            WriteByte(upvalue.Kind);
        }
    }

    private void WritePrototypes(LuaPrototype[] nestedPrototypes, bool stripDebugInformation)
    {
        WriteInt(nestedPrototypes.Length);

        foreach (var nested in nestedPrototypes)
        {
            WritePrototype(nested, stripDebugInformation);
        }
    }

    private void WriteDebugInformation(LuaPrototype prototype, bool stripDebugInformation)
    {
        if (stripDebugInformation)
        {
            WriteInt(0);
            WriteInt(0);
            WriteInt(0);
            WriteInt(0);
            return;
        }

        WriteLineInfo(prototype.LineInfo);
        WriteAbsoluteLineInfo(prototype.AbsoluteLineInfo);
        WriteLocalVariables(prototype.LocalVariables);
        WriteUpvalueNames(prototype.Upvalues);
    }

    private void WriteLineInfo(sbyte[] lineInfo)
    {
        WriteInt(lineInfo.Length);
        if (lineInfo.Length == 0)
        {
            return;
        }

        var bytes = new byte[lineInfo.Length];
        for (var index = 0; index < lineInfo.Length; index++)
        {
            bytes[index] = unchecked((byte)lineInfo[index]);
        }

        WriteBytes(bytes);
    }

    private void WriteAbsoluteLineInfo(LuaAbsoluteLineInfo[] absoluteLineInfo)
    {
        WriteInt(absoluteLineInfo.Length);
        if (absoluteLineInfo.Length == 0)
        {
            return;
        }

        Align(sizeof(int));
        foreach (var item in absoluteLineInfo)
        {
            WriteInt32Raw(item.ProgramCounter);
            WriteInt32Raw(item.Line);
        }
    }

    private void WriteLocalVariables(LuaLocalVariable[] localVariables)
    {
        WriteInt(localVariables.Length);

        foreach (var local in localVariables)
        {
            WriteString(local.Name);
            WriteInt(local.StartProgramCounter);
            WriteInt(local.EndProgramCounter);
        }
    }

    private void WriteUpvalueNames(LuaUpvalueDescriptor[] upvalues)
    {
        WriteInt(upvalues.Length);

        foreach (var upvalue in upvalues)
        {
            WriteString(upvalue.Name);
        }
    }

    private void WriteString(string? value)
    {
        if (value is null)
        {
            WriteSize(0);
            WriteVarUInt64(0);
            return;
        }

        WriteStringBytes(Encoding.UTF8.GetBytes(value));
    }

    private void WriteStringBytes(ReadOnlySpan<byte> bytes)
    {
        WriteSize(checked((ulong)bytes.Length + 1));
        WriteBytes(bytes);
        WriteByte(0);
    }

    private void WriteSize(ulong size)
    {
        WriteVarUInt64(size);
    }

    private void WriteInt(int value)
    {
        if (value < 0)
        {
            throw new InvalidDataException("negative integer field");
        }

        WriteVarUInt64((ulong)value);
    }

    private void WriteLuaInteger(long value)
    {
        var encoded = value >= 0
            ? checked((ulong)value * 2)
            : checked((ulong)(~value) * 2 + 1);
        WriteVarUInt64(encoded);
    }

    private void WriteVarUInt64(ulong value)
    {
        Span<byte> buffer = stackalloc byte[10];
        var length = 1;
        buffer[^1] = (byte)(value & 0x7F);

        while ((value >>= 7) != 0)
        {
            buffer[^(++length)] = (byte)((value & 0x7F) | 0x80);
        }

        WriteBytes(buffer[(buffer.Length - length)..]);
    }

    private void Align(int alignment)
    {
        var padding = alignment - (int)(_offset % (ulong)alignment);
        if (padding < alignment)
        {
            WriteBytes(new byte[padding]);
        }
    }

    private void WriteDouble(double value)
    {
        EnsureWriter().Write(value);
        _offset += sizeof(double);
    }

    private void WriteUInt32(uint value)
    {
        EnsureWriter().Write(value);
        _offset += sizeof(uint);
    }

    private void WriteInt32Raw(int value)
    {
        EnsureWriter().Write(value);
        _offset += sizeof(int);
    }

    private void WriteByte(byte value)
    {
        EnsureWriter().Write(value);
        _offset += sizeof(byte);
    }

    private void WriteBytes(ReadOnlySpan<byte> bytes)
    {
        EnsureWriter().Write(bytes);
        _offset += (ulong)bytes.Length;
    }

    private BinaryWriter EnsureWriter()
    {
        return _writer ?? throw new InvalidOperationException("Chunk writer is not initialized.");
    }
}
