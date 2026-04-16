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
    public void TryGetValue_ShouldNormalizeLargeExactlyRepresentableFloatKeys()
    {
        var table = new LuaTable();

        table.SetValue(LuaValue.FromFloat(1125899906842624.0), LuaValue.FromString("lua"));

        table.TryGetValue(LuaValue.FromInteger(1125899906842624), out var value).ShouldBeTrue();
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
    public void GetValue_ShouldReturnNilForNilKey()
    {
        var table = new LuaTable();

        table.GetValue(LuaValue.Nil).IsNil.ShouldBeTrue();
        table.TryGetValue(LuaValue.Nil, out _).ShouldBeFalse();
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
    public void TryGetNextEntry_ShouldNormalizeIntegralFloatKeys()
    {
        var table = new LuaTable();

        table.SetValue(LuaValue.FromString("first"), LuaValue.FromInteger(1));
        table.SetValue(LuaValue.FromFloat(2.0), LuaValue.FromInteger(2));

        table.TryGetNextEntry(LuaValue.Nil, out var firstKey, out var firstValue).ShouldBeTrue();
        firstKey.AsString().ShouldBe("first");
        firstValue.AsInteger().ShouldBe(1);

        table.TryGetNextEntry(firstKey, out var secondKey, out var secondValue).ShouldBeTrue();
        secondKey.AsInteger().ShouldBe(2);
        secondValue.AsInteger().ShouldBe(2);
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

    [Fact]
    public void WeakKeyTables_ShouldDiscardCollectedCollectableKeys()
    {
        var table = CreateWeakTable("k");

        table.SetValue(LuaValue.FromInteger(1), LuaValue.FromString("strong"));
        AddEphemeralKeyEntry(table, LuaValue.FromString("weak"));

        ForceWeakCollection();

        table.GetValue(LuaValue.FromInteger(1)).AsString().ShouldBe("strong");
        table.TryGetValue(LuaValue.FromTable(new LuaTable()), out _).ShouldBeFalse();

        var entryCount = CountEntries(table);
        entryCount.ShouldBe(1);
    }

    [Fact]
    public void WeakValueTables_ShouldDiscardCollectedCollectableValues()
    {
        var table = CreateWeakTable("v");

        table.SetValue(LuaValue.FromInteger(1), LuaValue.FromString("strong"));
        AddEphemeralValueEntry(table, LuaValue.FromInteger(2));

        ForceWeakCollection();

        table.GetValue(LuaValue.FromInteger(1)).AsString().ShouldBe("strong");
        table.GetValue(LuaValue.FromInteger(2)).IsNil.ShouldBeTrue();
        CountEntries(table).ShouldBe(1);
    }

    [Fact]
    public void WeakModeChanges_ShouldRebuildExistingEntries()
    {
        var table = new LuaTable();
        var metatable = new LuaTable();

        table.SetMetatable(metatable);
        AddEphemeralKeyEntry(table, LuaValue.FromInteger(1));
        metatable.SetValue(LuaValue.FromString("__mode"), LuaValue.FromString("k"));

        ForceWeakCollection();
        ForceWeakCollection();

        CountEntries(table).ShouldBe(0);
    }

    private static LuaTable CreateWeakTable(string mode)
    {
        var table = new LuaTable();
        var metatable = new LuaTable();
        metatable.SetValue(LuaValue.FromString("__mode"), LuaValue.FromString(mode));
        table.SetMetatable(metatable);
        return table;
    }

    private static void AddEphemeralKeyEntry(LuaTable table, LuaValue value)
    {
        table.SetValue(LuaValue.FromTable(new LuaTable()), value);
    }

    private static void AddEphemeralValueEntry(LuaTable table, LuaValue key)
    {
        table.SetValue(key, LuaValue.FromTable(new LuaTable()));
    }

    private static void ForceWeakCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        LuaTable.CleanupWeakEntries();
    }

    private static int CountEntries(LuaTable table)
    {
        var count = 0;
        var key = LuaValue.Nil;
        while (table.TryGetNextEntry(key, out var nextKey, out _))
        {
            count += 1;
            key = nextKey;
        }

        return count;
    }
}
