using Lua.Runtime.Objects;
using Lua.Runtime.Values;
using Shouldly;

namespace Lua.Runtime.Tests;

public class LuaTableTests
{
    [Fact]
    public void SetValue_ShouldRoundTripStringAndIntegerKeys()
    {
        var table = new LuaTable();

        table.SetValue(LuaValue.FromString("answer"), LuaValue.FromInteger(42));
        table.SetValue(LuaValue.FromInteger(1), LuaValue.FromString("lua"));

        table.GetValue(LuaValue.FromString("answer")).AsInteger().ShouldBe(42);
        table.GetValue(LuaValue.FromInteger(1)).AsString().ShouldBe("lua");
    }

    [Fact]
    public void NumericKeys_ShouldNormalizeIntegralFloats()
    {
        var table = new LuaTable();

        table.SetValue(LuaValue.FromFloat(1.0), LuaValue.FromString("normalized"));

        table.GetValue(LuaValue.FromInteger(1)).AsString().ShouldBe("normalized");
        table.GetSequenceLength().ShouldBe(1);
    }

    [Fact]
    public void AssigningNil_ShouldRemoveEntry()
    {
        var table = new LuaTable();

        table.SetValue(LuaValue.FromString("answer"), LuaValue.FromInteger(42));
        table.SetValue(LuaValue.FromString("answer"), LuaValue.Nil);

        table.GetValue(LuaValue.FromString("answer")).IsNil.ShouldBeTrue();
    }

    [Fact]
    public void SetMetatable_ShouldExposeMetamethodLookup()
    {
        var table = new LuaTable();
        var metatable = new LuaTable();

        metatable.SetValue(LuaValue.FromString("__close"), LuaValue.FromString("handler"));
        table.SetMetatable(metatable);

        table.TryGetMetamethod("__close", out var handler).ShouldBeTrue();
        handler.AsString().ShouldBe("handler");
    }
}
