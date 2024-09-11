namespace Lua.Core.BinChunk;

public struct BinaryChunk
{
    /// <summary>
    /// 30 bytes.
    /// </summary>
    public Header Header { get; set; }
    
    /// <summary>
    /// Upvalue count of the main function.
    /// </summary>
    public byte SizeUpvalues { get; set; }

    public Prototype MainFunc { get; set; }
}

public struct Header
{
    /// <summary>
    /// 0x1B4C7561
    /// </summary>
    public byte[] Signature { get; set; }

    /// <summary>
    /// MajorVersion * 16 + MinorVersion
    /// 5.3.4: 0x53
    /// </summary>
    public byte Version { get; set; }

    /// <summary>
    /// 0x00
    /// </summary>
    public byte Format { get; set; }

    /// <summary>
    /// 0x19930D0A1A0A
    /// </summary>
    public byte[] LuacData { get; set; }

    /// <summary>
    /// 0x04
    /// </summary>
    public byte CintSize { get; set; }

    /// <summary>
    /// 0x08
    /// </summary>
    public byte SizetSize { get; set; }

    /// <summary>
    /// 0x04
    /// </summary>
    public byte InstructionSize { get; set; }

    /// <summary>
    /// 0x08
    /// </summary>
    public byte LuaIntegerSize { get; set; }

    /// <summary>
    /// 0x08
    /// </summary>
    public byte LuaNumberSize { get; set; }

    /// <summary>
    /// Little Endian: 0x7856
    /// </summary>
    public long LuacInt { get; set; }

    /// <summary>
    /// 370.5
    /// </summary>
    public double LuacNum { get; set; }
}

public struct Prototype
{
    /// <summary>
    /// Source file name.
    /// </summary>
    public string Source { get; set; }

    /// <summary>
    /// First line number of the function definition.
    /// </summary>
    public uint LineDefined { get; set; }

    /// <summary>
    /// Last line number of the function definition.
    /// </summary>
    public uint LastLineDefined { get; set; }

    /// <summary>
    /// Number of fixed parameters.
    /// </summary>
    public byte NumParams { get; set; }

    /// <summary>
    /// Whether the function is a vararg function.
    /// </summary>
    public byte IsVararg { get; set; }

    /// <summary>
    /// Maximum stack size used by this function.
    /// </summary>
    public byte MaxStackSize { get; set; }

    /// <summary>
    /// Instructions.
    /// </summary>
    public uint[] Code { get; set; }

    /// <summary>
    /// Constants.
    /// </summary>
    public object?[] Constants { get; set; }

    /// <summary>
    /// Upvalues.
    /// </summary>
    public Upvalue[] Upvalues { get; set; }

    /// <summary>
    /// Nested functions.
    /// </summary>
    public Prototype[] Protos { get; set; }

    /// <summary>
    /// Line information.
    /// </summary>
    public uint[] LineInfo { get; set; }

    /// <summary>
    /// Local variables.
    /// </summary>
    public LocVar[] LocVars { get; set; }

    /// <summary>
    /// Upvalue names.
    /// </summary>
    public string[] UpvalueNames { get; set; }
}

public struct Upvalue
{
    /// <summary>
    /// TODO
    /// </summary>
    public byte Instack { get; set; }
    
    /// <summary>
    /// TODO
    /// </summary>
    public byte Idx { get; set; }
}

public struct LocVar
{
    /// <summary>
    /// Local variable name.
    /// </summary>
    public string VarName { get; set; }
    
    /// <summary>
    /// Start Program Counter of the local variable scope.
    /// </summary>
    public uint StartPC { get; set; }
    
    /// <summary>
    /// End Program Counter of the local variable scope.
    /// </summary>
    public uint EndPC { get; set; }
}