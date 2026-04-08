using Lua.Bytecode.Chunks;
using Lua.Bytecode.Disassembly;
using Shouldly;

namespace Lua.Bytecode.Tests;

public class LuaDisassemblerTests
{
    [Fact]
    public void Disassemble_ShouldProduceReadableListingForFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "nested_chunk.luac")), "nested_chunk.luac");
        var disassembler = new LuaDisassembler();

        var listing = disassembler.Disassemble(chunk);

        listing.ShouldContain("main <test/fixtures/lua55/source/nested_chunk.lua:0,0> (8 instructions)");
        listing.ShouldContain("VARARGPREP");
        listing.ShouldContain("CLOSURE");
        listing.ShouldContain("TAILCALL");
        listing.ShouldContain("function <test/fixtures/lua55/source/nested_chunk.lua:1,4> (6 instructions)");
        listing.ShouldContain("LOADK");
        listing.ShouldContain("; \"lua\"");
        listing.ShouldContain("MMBIN");
        listing.ShouldContain("__add");
        listing.ShouldContain("locals (3):");
        listing.ShouldContain("upvalues (1):");
    }

    [Fact]
    public void GetLine_ShouldResolveLineInfoForNestedFixture()
    {
        var reader = new LuaChunkReader();
        var chunk = reader.Read(File.ReadAllBytes(GetFixturePath("chunks", "nested_chunk.luac")), "nested_chunk.luac");
        var nested = chunk.MainFunction.NestedPrototypes[0];

        LuaLineInfoResolver.GetLine(nested, 0).ShouldBe(2);
        LuaLineInfoResolver.GetLine(nested, 1).ShouldBe(3);
        LuaLineInfoResolver.GetLine(nested, 5).ShouldBe(4);
    }

    private static string GetFixturePath(string folder, string fileName)
    {
        return Path.Combine(AppContext.BaseDirectory, "fixtures", "lua55", folder, fileName);
    }
}
