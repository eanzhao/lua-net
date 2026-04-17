using Lua.Runtime.Objects;
using Lua.Runtime.Values;

namespace Lua.Runtime.Execution;

public sealed partial class LuaState
{
    private const int MaxTableLibraryMetamethodDepth = 2000;

    private static long GetTableLibraryLength(LuaState state, LuaTable table)
    {
        var tableValue = LuaValue.FromTable(table);
        if (state.TryGetMetamethod(tableValue, "__len", out var metamethod))
        {
            var results = state.InvokeCallable(metamethod, [tableValue, tableValue]);
            return results.Length == 0
                ? 0
                : RequireIntegerArgument(results, 0, "__len");
        }

        return table.GetSequenceLength();
    }

    private static LuaValue GetTableLibraryValue(LuaState state, LuaTable table, LuaValue key)
    {
        LuaValue currentTarget = LuaValue.FromTable(table);

        for (var depth = 0; depth < MaxTableLibraryMetamethodDepth; depth++)
        {
            if (currentTarget.Kind == LuaValueKind.Table)
            {
                var currentTable = currentTarget.AsTable();
                if (currentTable.TryGetValue(key, out var value))
                {
                    return value;
                }

                if (!currentTable.TryGetMetamethod("__index", out var metamethod))
                {
                    return LuaValue.Nil;
                }

                if (metamethod.Kind == LuaValueKind.Function)
                {
                    var results = state.InvokeCallable(metamethod, [currentTarget, key]);
                    return results.Length == 0 ? LuaValue.Nil : results[0];
                }

                currentTarget = metamethod;
                continue;
            }

            return LuaValue.Nil;
        }

        throw CreateRuntimeError("'__index' chain too long; possible loop");
    }

    private static void SetTableLibraryValue(LuaState state, LuaTable table, LuaValue key, LuaValue value)
    {
        LuaValue currentTarget = LuaValue.FromTable(table);

        for (var depth = 0; depth < MaxTableLibraryMetamethodDepth; depth++)
        {
            if (currentTarget.Kind == LuaValueKind.Table)
            {
                var currentTable = currentTarget.AsTable();
                if (currentTable.TryGetValue(key, out _))
                {
                    currentTable.SetValue(key, value);
                    return;
                }

                if (!currentTable.TryGetMetamethod("__newindex", out var metamethod))
                {
                    currentTable.SetValue(key, value);
                    return;
                }

                if (metamethod.Kind == LuaValueKind.Function)
                {
                    state.InvokeCallable(metamethod, [currentTarget, key, value]);
                    return;
                }

                currentTarget = metamethod;
                continue;
            }

            throw CreateRuntimeError("attempt to index a non-table value");
        }

        throw CreateRuntimeError("'__newindex' chain too long; possible loop");
    }
}
