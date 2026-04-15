using Lua.Runtime.Execution;
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
    public void TryGetValue_ShouldDistinguishMissingEntries()
    {
        var table = new LuaTable();

        table.SetValue(LuaValue.FromString("answer"), LuaValue.FromInteger(42));

        table.TryGetValue(LuaValue.FromString("answer"), out var existing).ShouldBeTrue();
        existing.AsInteger().ShouldBe(42);
        table.TryGetValue(LuaValue.FromString("missing"), out _).ShouldBeFalse();
    }

    [Fact]
    public void TryGetValue_ShouldNormalizeIntegralFloatKeys()
    {
        var table = new LuaTable();

        table.SetValue(LuaValue.FromInteger(1), LuaValue.FromString("lua"));

        table.TryGetValue(LuaValue.FromFloat(1.0), out var value).ShouldBeTrue();
        value.AsString().ShouldBe("lua");
    }

    [Fact]
    public void SetValue_ShouldRejectNilKey()
    {
        var table = new LuaTable();

        var exception = Should.Throw<LuaRuntimeException>(() =>
            table.SetValue(LuaValue.Nil, LuaValue.FromInteger(1)));

        exception.ErrorObject.AsString().ShouldBe("table index is nil");
    }

    [Fact]
    public void SetValue_ShouldRejectNaNKey()
    {
        var table = new LuaTable();

        var exception = Should.Throw<LuaRuntimeException>(() =>
            table.SetValue(LuaValue.FromFloat(double.NaN), LuaValue.FromInteger(1)));

        exception.ErrorObject.AsString().ShouldBe("table index is NaN");
    }

    [Fact]
    public void GetSequenceLength_ShouldHandleGaps()
    {
        var table = new LuaTable();

        table.SetValue(LuaValue.FromInteger(1), LuaValue.FromString("a"));
        table.SetValue(LuaValue.FromInteger(3), LuaValue.FromString("c"));

        table.GetSequenceLength().ShouldBe(1);
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
