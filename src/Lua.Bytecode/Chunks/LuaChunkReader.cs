using System.Buffers.Binary;
using System.Text;

namespace Lua.Bytecode.Chunks;

public sealed class LuaChunkReader
{
    private const byte LuaVNil = 0x00;
    private const byte LuaVFalse = 0x01;
    private const byte LuaVTrue = 0x11;
    private const byte LuaVNumInt = 0x03;
    private const byte LuaVNumFlt = 0x13;
    private const byte LuaVShrStr = 0x04;
    private const byte LuaVLngStr = 0x14;

    private readonly List<string?> _savedStrings = [];
    private BinaryReader? _reader;
    private ulong _offset;
    private string _chunkName = "binary chunk";

    public LuaChunk Read(byte[] bytes, string? chunkName = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        using var stream = new MemoryStream(bytes, writable: false);
        return Read(stream, chunkName);
    }

    public LuaChunk Read(Stream stream, string? chunkName = null)
    {
        ArgumentNullException.ThrowIfNull(stream);

        _reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        _savedStrings.Clear();
        _offset = 0;
        _chunkName = string.IsNullOrWhiteSpace(chunkName) ? "binary chunk" : chunkName!;

        var header = ReadHeader();
        var mainUpvalueCount = ReadByte();
        var mainFunction = ReadPrototype();

        if (mainUpvalueCount != mainFunction.Upvalues.Length)
        {
            throw InvalidChunk("corrupted chunk");
        }

        return new LuaChunk
        {
            Header = header,
            MainUpvalueCount = mainUpvalueCount,
            MainFunction = mainFunction
        };
    }

    private LuaChunkHeader ReadHeader()
    {
        ExpectBytes(LuaChunkHeaderConstants.LuaSignature, "not a binary chunk");

        var version = ReadByte();
        if (version != LuaChunkHeaderConstants.LuacVersion)
        {
            throw InvalidChunk("version mismatch");
        }

        var format = ReadByte();
        if (format != LuaChunkHeaderConstants.LuacFormat)
        {
            throw InvalidChunk("format mismatch");
        }

        ExpectBytes(LuaChunkHeaderConstants.LuacData, "corrupted chunk");

        var intSize = ReadAndCheckNumericHeader(
            BitConverter.GetBytes(LuaChunkHeaderConstants.LuacInt),
            "int");

        var instructionSize = ReadAndCheckNumericHeader(
            BitConverter.GetBytes(LuaChunkHeaderConstants.LuacInstruction),
            "instruction");

        var luaIntegerSize = ReadAndCheckNumericHeader(
            BitConverter.GetBytes((long)LuaChunkHeaderConstants.LuacInt),
            "Lua integer");

        var luaNumberSize = ReadAndCheckNumericHeader(
            BitConverter.GetBytes(LuaChunkHeaderConstants.LuacNumber),
            "Lua number");

        return new LuaChunkHeader
        {
            Version = version,
            Format = format,
            IntSize = intSize,
            IntFormatMarker = LuaChunkHeaderConstants.LuacInt,
            InstructionSize = instructionSize,
            InstructionFormatMarker = LuaChunkHeaderConstants.LuacInstruction,
            LuaIntegerSize = luaIntegerSize,
            LuaIntegerFormatMarker = LuaChunkHeaderConstants.LuacInt,
            LuaNumberSize = luaNumberSize,
            LuaNumberFormatMarker = LuaChunkHeaderConstants.LuacNumber
        };
    }

    private LuaPrototype ReadPrototype()
    {
        var lineDefined = ReadInt();
        var lastLineDefined = ReadInt();
        var numberOfParameters = ReadByte();
        var flags = ReadByte();
        var maxStackSize = ReadByte();
        var code = ReadCode();
        var constants = ReadConstants();
        var upvalues = ReadUpvalues();
        var nestedPrototypes = ReadPrototypes();
        var source = ReadString();
        var lineInfo = ReadLineInfo();
        var absoluteLineInfo = ReadAbsoluteLineInfo();
        var localVariables = ReadLocalVariables();
        ReadUpvalueNames(upvalues);

        return new LuaPrototype
        {
            LineDefined = lineDefined,
            LastLineDefined = lastLineDefined,
            NumberOfParameters = numberOfParameters,
            Flags = flags,
            MaxStackSize = maxStackSize,
            Code = code,
            Constants = constants,
            Upvalues = upvalues,
            NestedPrototypes = nestedPrototypes,
            Source = source,
            LineInfo = lineInfo,
            AbsoluteLineInfo = absoluteLineInfo,
            LocalVariables = localVariables
        };
    }

    private uint[] ReadCode()
    {
        var count = ReadInt();
        Align(4);

        var code = new uint[count];
        for (var index = 0; index < count; index++)
        {
            code[index] = ReadUInt32();
        }

        return code;
    }

    private LuaConstant[] ReadConstants()
    {
        var count = ReadInt();
        var constants = new LuaConstant[count];

        for (var index = 0; index < count; index++)
        {
            constants[index] = ReadConstant();
        }

        return constants;
    }

    private LuaConstant ReadConstant()
    {
        return ReadByte() switch
        {
            LuaVNil => LuaConstant.Nil,
            LuaVFalse => LuaConstant.FromBoolean(false),
            LuaVTrue => LuaConstant.FromBoolean(true),
            LuaVNumFlt => LuaConstant.FromFloat(ReadDouble()),
            LuaVNumInt => LuaConstant.FromInteger(ReadLuaInteger()),
            LuaVShrStr or LuaVLngStr => LuaConstant.FromString(ReadRequiredString("constant string")),
            _ => throw InvalidChunk("invalid constant")
        };
    }

    private LuaUpvalueDescriptor[] ReadUpvalues()
    {
        var count = ReadInt();
        var upvalues = new LuaUpvalueDescriptor[count];

        for (var index = 0; index < count; index++)
        {
            upvalues[index] = new LuaUpvalueDescriptor
            {
                InStack = ReadByte(),
                Index = ReadByte(),
                Kind = ReadByte()
            };
        }

        return upvalues;
    }

    private LuaPrototype[] ReadPrototypes()
    {
        var count = ReadInt();
        var prototypes = new LuaPrototype[count];

        for (var index = 0; index < count; index++)
        {
            prototypes[index] = ReadPrototype();
        }

        return prototypes;
    }

    private sbyte[] ReadLineInfo()
    {
        var count = ReadInt();
        var bytes = ReadBytesExact(count);
        var lineInfo = new sbyte[count];

        for (var index = 0; index < count; index++)
        {
            lineInfo[index] = unchecked((sbyte)bytes[index]);
        }

        return lineInfo;
    }

    private LuaAbsoluteLineInfo[] ReadAbsoluteLineInfo()
    {
        var count = ReadInt();
        if (count == 0)
        {
            return [];
        }

        Align(4);

        var result = new LuaAbsoluteLineInfo[count];
        for (var index = 0; index < count; index++)
        {
            result[index] = new LuaAbsoluteLineInfo(
                ReadInt32Raw(),
                ReadInt32Raw());
        }

        return result;
    }

    private LuaLocalVariable[] ReadLocalVariables()
    {
        var count = ReadInt();
        var locals = new LuaLocalVariable[count];

        for (var index = 0; index < count; index++)
        {
            locals[index] = new LuaLocalVariable
            {
                Name = ReadString(),
                StartProgramCounter = ReadInt(),
                EndProgramCounter = ReadInt()
            };
        }

        return locals;
    }

    private void ReadUpvalueNames(LuaUpvalueDescriptor[] upvalues)
    {
        var count = ReadInt();
        if (count != 0)
        {
            count = upvalues.Length;
        }

        for (var index = 0; index < count; index++)
        {
            upvalues[index].Name = ReadString();
        }
    }

    private string? ReadString()
    {
        var size = ReadSize();
        if (size == 0)
        {
            var index = ReadVarUInt64(ulong.MaxValue);
            if (index == 0)
            {
                return null;
            }

            if (index > (ulong)_savedStrings.Count)
            {
                throw InvalidChunk("invalid string index");
            }

            return _savedStrings[(int)index - 1];
        }

        if (size > int.MaxValue)
        {
            throw InvalidChunk("string size overflow");
        }

        var bytes = ReadBytesExact((int)size);
        if (bytes.Length == 0 || bytes[^1] != 0)
        {
            throw InvalidChunk("invalid string payload");
        }

        var value = Encoding.UTF8.GetString(bytes, 0, bytes.Length - 1);
        _savedStrings.Add(value);
        return value;
    }

    private string ReadRequiredString(string description)
    {
        return ReadString() ?? throw InvalidChunk($"bad format for {description}");
    }

    private byte ReadAndCheckNumericHeader(byte[] expected, string name)
    {
        var size = ReadByte();
        if (size != expected.Length)
        {
            throw InvalidChunk($"{name} size mismatch");
        }

        var actual = ReadBytesExact(size);
        if (!actual.SequenceEqual(expected))
        {
            throw InvalidChunk($"{name} format mismatch");
        }

        return size;
    }

    private ulong ReadSize() => ReadVarUInt64(ulong.MaxValue);

    private int ReadInt()
    {
        var value = ReadVarUInt64(int.MaxValue);
        return checked((int)value);
    }

    private long ReadLuaInteger()
    {
        var encoded = ReadVarUInt64(ulong.MaxValue);
        return (encoded & 1UL) != 0
            ? ~unchecked((long)(encoded >> 1))
            : unchecked((long)(encoded >> 1));
    }

    private ulong ReadVarUInt64(ulong limit)
    {
        ulong value = 0;
        var shiftedLimit = limit >> 7;

        while (true)
        {
            var current = ReadByte();
            if (value > shiftedLimit)
            {
                throw InvalidChunk("integer overflow");
            }

            value = (value << 7) | ((ulong)current & 0x7FUL);
            if ((current & 0x80) == 0)
            {
                return value;
            }
        }
    }

    private void Align(int alignment)
    {
        var padding = alignment - (int)(_offset % (ulong)alignment);
        if (padding < alignment)
        {
            ReadBytesExact(padding);
        }
    }

    private double ReadDouble()
    {
        var bytes = ReadBytesExact(sizeof(double));
        return BinaryPrimitives.ReadDoubleLittleEndian(bytes);
    }

    private int ReadInt32Raw()
    {
        var bytes = ReadBytesExact(sizeof(int));
        return BinaryPrimitives.ReadInt32LittleEndian(bytes);
    }

    private uint ReadUInt32()
    {
        var bytes = ReadBytesExact(sizeof(uint));
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }

    private void ExpectBytes(ReadOnlySpan<byte> expected, string message)
    {
        var actual = ReadBytesExact(expected.Length);
        if (!actual.AsSpan().SequenceEqual(expected))
        {
            throw InvalidChunk(message);
        }
    }

    private byte[] ReadBytesExact(int count)
    {
        var reader = EnsureReader();
        var bytes = reader.ReadBytes(count);
        _offset += (ulong)bytes.Length;

        if (bytes.Length != count)
        {
            throw InvalidChunk("truncated chunk");
        }

        return bytes;
    }

    private byte ReadByte()
    {
        var reader = EnsureReader();
        try
        {
            var value = reader.ReadByte();
            _offset++;
            return value;
        }
        catch (EndOfStreamException)
        {
            throw InvalidChunk("truncated chunk");
        }
    }

    private BinaryReader EnsureReader() =>
        _reader ?? throw new InvalidOperationException("Reader has not been initialized.");

    private InvalidDataException InvalidChunk(string reason) =>
        new($"{_chunkName}: bad binary format ({reason})");
}
