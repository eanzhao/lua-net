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

    private static string GetFixturePath(string folder, string fileName)
    {
        return Path.Combine(AppContext.BaseDirectory, "fixtures", "lua55", folder, fileName);
    }
}
