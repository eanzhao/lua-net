using Lua.Core.BinChunk;
using Shouldly;

namespace Lua.Core.Test;

public class BinChunkTests
{
    [Fact]
    public void CheckHeaderTest()
    {
        var data = File.ReadAllBytes("luaCode/luac.out");
        var reader = new BinaryChunkReader { Data = data };
        reader.CheckHeader().IsSuccess.ShouldBeTrue();
    }
}