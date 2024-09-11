using System.Text;

namespace Lua.Core.BinChunk;

public struct BinaryChunkReader
{
    private BinaryReader _reader;

    public Stream Data
    {
        set => _reader = new BinaryReader(value);
    }

    public byte ReadByte()
    {
        return _reader.ReadByte();
    }

    public uint ReadUInt32()
    {
        return _reader.ReadUInt32();
    }

    public ulong ReadUInt64()
    {
        return _reader.ReadUInt64();
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
        return _reader.ReadBytes((int)n);
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

    public object?[] ReadConstants()
    {
        var constants = new object?[ReadUInt32()];
        for (var i = 0; i < constants.Length; i++)
        {
            constants[i] = ReadConstant();
        }

        return constants;
    }

    private object? ReadConstant()
    {
        return ReadByte() switch
        {
            BinaryChunkConstants.TagNil => null,
            BinaryChunkConstants.TagFalse => ReadByte() != 0,
            BinaryChunkConstants.TagTrue => ReadByte() != 0,
            BinaryChunkConstants.TagInteger => ReadLuaInteger(),
            BinaryChunkConstants.TagNumber => ReadLuaNumber(),
            BinaryChunkConstants.TagShortStr => ReadString(),
            BinaryChunkConstants.TagLongStr => ReadString(),
            _ => throw new Exception("Corrupted!")
        };
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