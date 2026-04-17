using Lua.Runtime.Objects;
using Lua.Runtime.Values;
using System.Security.Cryptography;

namespace Lua.Runtime.Execution;

public sealed partial class LuaState
{
    private static LuaValue[] MathRandom(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        return arguments.Count switch
        {
            0 => [LuaValue.FromFloat(NextRandomDouble(state))],
            1 => [LuaValue.FromInteger(NextRandomInteger(state, 1, RequireIntegerArgument(arguments, 0, "math.random")))],
            _ => [LuaValue.FromInteger(NextRandomInteger(
                state,
                RequireIntegerArgument(arguments, 0, "math.random"),
                RequireIntegerArgument(arguments, 1, "math.random")))]
        };
    }

    private static LuaValue[] MathRandomSeed(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        long seed0;
        long seed1;

        if (arguments.Count == 0 || arguments[0].IsNil)
        {
            Span<byte> seedBytes = stackalloc byte[16];
            RandomNumberGenerator.Fill(seedBytes);
            seed0 = BitConverter.ToInt64(seedBytes[..8]);
            seed1 = BitConverter.ToInt64(seedBytes[8..]);
        }
        else
        {
            seed0 = RequireIntegerArgument(arguments, 0, "math.randomseed");
            seed1 = arguments.Count > 1 && !arguments[1].IsNil
                ? RequireIntegerArgument(arguments, 1, "math.randomseed")
                : 0L;
        }

        SeedRandom(state, unchecked((ulong)seed0), unchecked((ulong)seed1));
        return [LuaValue.FromInteger(seed0), LuaValue.FromInteger(seed1)];
    }

    private static long NextRandomInteger(LuaState state, long lowerInclusive, long upperInclusive)
    {
        if (upperInclusive < lowerInclusive)
        {
            throw CreateArgumentError("math.random", 1, "interval is empty");
        }

        var range = unchecked((ulong)(upperInclusive - lowerInclusive)) + 1UL;
        var offset = NextRandomBounded(state, range);
        return unchecked((long)(unchecked((ulong)lowerInclusive) + offset));
    }

    private static double NextRandomDouble(LuaState state)
    {
        return (NextRandomUInt64(state) >> 11) * (1.0 / (1UL << 53));
    }

    private static ulong NextRandomBounded(LuaState state, ulong bound)
    {
        if (bound == 0UL)
        {
            return NextRandomUInt64(state);
        }

        var threshold = unchecked(0UL - bound) % bound;
        while (true)
        {
            var value = NextRandomUInt64(state);
            if (value >= threshold)
            {
                return value % bound;
            }
        }
    }

    private static ulong NextRandomUInt64(LuaState state)
    {
        var s0 = state._randomState0;
        var s1 = state._randomState1;
        var result = RotateLeft(s0 + s1, 17) + s0;

        s1 ^= s0;
        state._randomState0 = RotateLeft(s0, 49) ^ s1 ^ (s1 << 21);
        state._randomState1 = RotateLeft(s1, 28);

        return result;
    }

    private static void SeedRandom(LuaState state, ulong seed0, ulong seed1)
    {
        if (seed0 == 0UL && seed1 == 0UL)
        {
            seed1 = 0x9E3779B97F4A7C15UL;
        }

        state._randomState0 = seed0;
        state._randomState1 = seed1;
        _ = NextRandomUInt64(state);
    }

    private static ulong RotateLeft(ulong value, int shift)
    {
        return (value << shift) | (value >> (64 - shift));
    }
}
