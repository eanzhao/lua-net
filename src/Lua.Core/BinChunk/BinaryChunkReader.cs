using System.Text;

namespace Lua.Core.BinChunk;

public struct BinaryChunkReader
{
    private int _position;

    public byte[] Data { get; set; }

    public byte ReadByte()
    {
        if (_position >= Data.Length)
        {
            throw new IndexOutOfRangeException("No more data to read.");
        }

        return Data[_position++];
    }

    public uint ReadUInt32()
    {
        if (_position + 4 > Data.Length)
        {
            throw new IndexOutOfRangeException("No more data to read.");
        }

        var result = BitConverter.ToUInt32(Data, _position);
        _position += 4;

        if (!BitConverter.IsLittleEndian)
        {
            result = (result >> 24) |
                     ((result << 8) & 0x00FF0000) |
                     ((result >> 8) & 0x0000FF00) |
                     (result << 24);
        }

        return result;
    }

    public ulong ReadUInt64()
    {
        if (_position + 8 > Data.Length)
        {
            throw new IndexOutOfRangeException("No more data to read.");
        }

        var result = BitConverter.ToUInt64(Data, _position);
        _position += 8;

        if (!BitConverter.IsLittleEndian)
        {
            result = (result >> 56) |
                     ((result << 40) & 0x00FF000000000000) |
                     ((result << 24) & 0x0000FF0000000000) |
                     ((result << 8) & 0x000000FF00000000) |
                     ((result >> 8) & 0x00000000FF000000) |
                     ((result >> 24) & 0x0000000000FF0000) |
                     ((result >> 40) & 0x000000000000FF00) |
                     (result << 56);
        }

        return result;
    }

    public long ReadLuaInteger()
    {
        return (long)ReadUInt64();
    }

    public double ReadLuaNumber()
    {
        return BitConverter.Int64BitsToDouble((long)ReadUInt64());
    }

    public string ReadString()
    {
        uint size = ReadByte();
        switch (size)
        {
            case 0:
                return string.Empty;
            case 0xFF:
                size = (uint)ReadUInt64();
                break;
        }

        var bytes = ReadBytes(size - 1);
        return Encoding.UTF8.GetString(bytes);
    }

    public byte[] ReadBytes(uint n)
    {
        if (_position + n > Data.Length)
        {
            throw new IndexOutOfRangeException("No more data to read.");
        }

        var bytes = new byte[n];
        Array.Copy(Data, _position, bytes, 0, n);
        _position += (int)n;

        return bytes;
    }

    public uint[] ReadCode()
    {
        var code = new uint[ReadUInt32()];
        for (var i = 0; i < code.Length; i++)
        {
            code[i] = ReadUInt32();
        }

        return code;
    }

    public object[] ReadConstants()
    {
        var constants = new object[ReadUInt32()];
        for (var i = 0; i < constants.Length; i++)
        {
            constants[i] = ReadConstant();
        }

        return constants;
    }

    private object ReadConstant()
    {
        switch (ReadByte())
        {
            case BinaryChunkConstants.TagNil: return null;
            case BinaryChunkConstants.TagFalse: return ReadByte() != 0;
            case BinaryChunkConstants.TagTrue: return ReadByte() != 0;
            case BinaryChunkConstants.TagInteger: return ReadLuaInteger();
            case BinaryChunkConstants.TagNumber: return ReadLuaNumber();
            case BinaryChunkConstants.TagShortStr: return ReadString();
            case BinaryChunkConstants.TagLongStr: return ReadString();
            default: throw new Exception("Corrupted!");
        }
    }

    public Prototype ReadProto(string parentSource)
    {
        var source = ReadString();
        if (string.IsNullOrEmpty(source))
        {
            source = parentSource;
        }

        return new Prototype
        {
            Source = source,
            LineDefined = ReadUInt32(),
            LastLineDefined = ReadUInt32(),
            NumParams = ReadByte(),
            IsVararg = ReadByte(),
            MaxStackSize = ReadByte(),
            Code = ReadCode(),
            Constants = ReadConstants(),
            Upvalues = ReadUpvalues(),
            Protos = ReadProtos(source),
            LineInfo = ReadLineInfo(),
            LocVars = ReadLocVars(),
            UpvalueNames = ReadUpvalueNames()
        };
    }

    public Prototype[] ReadProtos(string parentSource)
    {
        var protos = new Prototype[ReadUInt32()];
        for (var i = 0; i < protos.Length; i++)
        {
            protos[i] = ReadProto(parentSource);
        }

        return protos;
    }

    public Upvalue[] ReadUpvalues()
    {
        var upvalues = new Upvalue[ReadUInt32()];
        for (var i = 0; i < upvalues.Length; i++)
        {
            upvalues[i] = new Upvalue
            {
                Instack = ReadByte(),
                Idx = ReadByte()
            };
        }

        return upvalues;
    }

    public uint[] ReadLineInfo()
    {
        var lineInfo = new uint[ReadUInt32()];
        for (var i = 0; i < lineInfo.Length; i++)
        {
            lineInfo[i] = ReadUInt32();
        }

        return lineInfo;
    }

    private LocVar[] ReadLocVars()
    {
        var locVars = new LocVar[ReadUInt32()];
        for (var i = 0; i < locVars.Length; i++)
        {
            locVars[i] = new LocVar
            {
                VarName = ReadString(),
                StartPC = ReadUInt32(),
                EndPC = ReadUInt32()
            };
        }

        return locVars;
    }

    public string[] ReadUpvalueNames()
    {
        var names = new string[ReadUInt32()];
        for (var i = 0; i < names.Length; i++)
        {
            names[i] = ReadString();
        }

        return names;
    }
}