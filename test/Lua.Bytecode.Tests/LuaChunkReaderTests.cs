using Lua.Bytecode.Chunks;
using Shouldly;

namespace Lua.Bytecode.Tests;

public class LuaChunkReaderTests
{
    [Fact]
    public void Read_ShouldParseOfficialLua55ChunkFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "nested_chunk.luac")), "nested_chunk.luac");

        chunk.Header.Version.ShouldBe(LuaChunkHeaderConstants.LuacVersion);
        chunk.Header.Format.ShouldBe(LuaChunkHeaderConstants.LuacFormat);
        chunk.Header.IntSize.ShouldBe((byte)sizeof(int));
        chunk.Header.InstructionSize.ShouldBe((byte)sizeof(uint));
        chunk.Header.LuaIntegerSize.ShouldBe((byte)sizeof(long));
        chunk.Header.LuaNumberSize.ShouldBe((byte)sizeof(double));

        chunk.MainUpvalueCount.ShouldBe((byte)chunk.MainFunction.Upvalues.Length);
        chunk.MainFunction.Code.Length.ShouldBeGreaterThan(0);
        chunk.MainFunction.NestedPrototypes.Length.ShouldBe(1);
        chunk.MainFunction.Source.ShouldEndWith("nested_chunk.lua");

        var nested = chunk.MainFunction.NestedPrototypes[0];
        nested.Source.ShouldEndWith("nested_chunk.lua");
        nested.Code.Length.ShouldBeGreaterThan(0);
        nested.LineInfo.Length.ShouldBe(nested.Code.Length);
        nested.LocalVariables.Select(variable => variable.Name).ShouldContain("a");
        nested.LocalVariables.Select(variable => variable.Name).ShouldContain("b");
        nested.LocalVariables.Select(variable => variable.Name).ShouldContain("name");
        nested.Constants.ShouldContain(LuaConstant.FromString("lua"));
    }

    [Fact]
    public void Read_ShouldRejectVersionMismatch()
    {
        var bytes = File.ReadAllBytes(GetFixturePath("chunks", "nested_chunk.luac"));
        bytes[4] = 0x54;

        var reader = new LuaChunkReader();
        var exception = Should.Throw<InvalidDataException>(() => reader.Read(bytes, "nested_chunk.luac"));

        exception.Message.ShouldContain("version mismatch");
    }

    [Fact]
    public void Write_ShouldRoundTripChunkStructure()
    {
        var original = ReadFixtureChunk("nested_chunk.luac");
        var writer = new LuaChunkWriter();
        var reader = new LuaChunkReader();

        var bytes = writer.Write(original);
        var roundTripped = reader.Read(bytes, "roundtrip.luac");

        AssertChunkEquivalent(original, roundTripped, stripped: false);
    }

    [Fact]
    public void Write_ShouldStripDebugInformationWhenRequested()
    {
        var original = ReadFixtureChunk("nested_chunk.luac");
        var writer = new LuaChunkWriter();
        var reader = new LuaChunkReader();

        var bytes = writer.Write(original, stripDebugInformation: true);
        var stripped = reader.Read(bytes, "stripped.luac");

        AssertChunkEquivalent(original, stripped, stripped: true);
    }

    private static LuaChunk ReadFixtureChunk(string fileName)
    {
        var reader = new LuaChunkReader();
        return reader.Read(File.ReadAllBytes(GetFixturePath("chunks", fileName)), fileName);
    }

    private static void AssertChunkEquivalent(LuaChunk expected, LuaChunk actual, bool stripped)
    {
        actual.Header.Version.ShouldBe(expected.Header.Version);
        actual.Header.Format.ShouldBe(expected.Header.Format);
        actual.Header.IntSize.ShouldBe(expected.Header.IntSize);
        actual.Header.IntFormatMarker.ShouldBe(expected.Header.IntFormatMarker);
        actual.Header.InstructionSize.ShouldBe(expected.Header.InstructionSize);
        actual.Header.InstructionFormatMarker.ShouldBe(expected.Header.InstructionFormatMarker);
        actual.Header.LuaIntegerSize.ShouldBe(expected.Header.LuaIntegerSize);
        actual.Header.LuaIntegerFormatMarker.ShouldBe(expected.Header.LuaIntegerFormatMarker);
        actual.Header.LuaNumberSize.ShouldBe(expected.Header.LuaNumberSize);
        actual.Header.LuaNumberFormatMarker.ShouldBe(expected.Header.LuaNumberFormatMarker);
        actual.MainUpvalueCount.ShouldBe(expected.MainUpvalueCount);

        AssertPrototypeEquivalent(expected.MainFunction, actual.MainFunction, stripped);
    }

    private static void AssertPrototypeEquivalent(LuaPrototype expected, LuaPrototype actual, bool stripped)
    {
        actual.LineDefined.ShouldBe(expected.LineDefined);
        actual.LastLineDefined.ShouldBe(expected.LastLineDefined);
        actual.NumberOfParameters.ShouldBe(expected.NumberOfParameters);
        actual.Flags.ShouldBe(expected.Flags);
        actual.MaxStackSize.ShouldBe(expected.MaxStackSize);
        actual.Code.ShouldBe(expected.Code);
        actual.Constants.ShouldBe(expected.Constants);
        actual.NestedPrototypes.Length.ShouldBe(expected.NestedPrototypes.Length);

        actual.Upvalues.Length.ShouldBe(expected.Upvalues.Length);
        for (var index = 0; index < expected.Upvalues.Length; index++)
        {
            actual.Upvalues[index].InStack.ShouldBe(expected.Upvalues[index].InStack);
            actual.Upvalues[index].Index.ShouldBe(expected.Upvalues[index].Index);
            actual.Upvalues[index].Kind.ShouldBe(expected.Upvalues[index].Kind);
            actual.Upvalues[index].Name.ShouldBe(stripped ? null : expected.Upvalues[index].Name);
        }

        if (stripped)
        {
            actual.Source.ShouldBeNull();
            actual.LineInfo.ShouldBeEmpty();
            actual.AbsoluteLineInfo.ShouldBeEmpty();
            actual.LocalVariables.ShouldBeEmpty();
        }
        else
        {
            actual.Source.ShouldBe(expected.Source);
            actual.LineInfo.ShouldBe(expected.LineInfo);
            actual.AbsoluteLineInfo.ShouldBe(expected.AbsoluteLineInfo);
            actual.LocalVariables.Length.ShouldBe(expected.LocalVariables.Length);
            for (var index = 0; index < expected.LocalVariables.Length; index++)
            {
                actual.LocalVariables[index].Name.ShouldBe(expected.LocalVariables[index].Name);
                actual.LocalVariables[index].StartProgramCounter.ShouldBe(expected.LocalVariables[index].StartProgramCounter);
                actual.LocalVariables[index].EndProgramCounter.ShouldBe(expected.LocalVariables[index].EndProgramCounter);
            }
        }

        for (var index = 0; index < expected.NestedPrototypes.Length; index++)
        {
            AssertPrototypeEquivalent(expected.NestedPrototypes[index], actual.NestedPrototypes[index], stripped);
        }
    }

    private static string GetFixturePath(string folder, string fileName)
    {
        return Path.Combine(AppContext.BaseDirectory, "fixtures", "lua55", folder, fileName);
    }
}
