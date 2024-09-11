using Lua.Core.BinChunk;
using Lua.Core.Extensions;
using Shouldly;

namespace Lua.Core.Test;

public class BinChunkTests
{
    [Fact]
    public void CheckHeaderTest()
    {
        using var stream = new FileStream("luaCode/luac.out", FileMode.Open, FileAccess.Read);
        var reader = new BinaryChunkReader { Data = stream };
        var result = reader.CheckHeader();
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void UndumpTest()
    {
        using var stream = new FileStream("luaCode/luac.out", FileMode.Open, FileAccess.Read);
        var reader = new BinaryChunkReader { Data = stream };
        var result = reader.Undump();
        result.Error.ShouldBeEmpty();
        result.IsSuccess.ShouldBeTrue();
        result.Value.List().ShouldBe("");
    }
}