using CSharpFunctionalExtensions;

namespace Lua.Core.BinChunk;

public static class BinaryChunkExtensions
{
    public static Result<Prototype> Undump(this BinaryChunkReader reader)
    {
        var checkHeaderResult = reader.CheckHeader();
        if (checkHeaderResult.IsSuccess)
        {
            reader.ReadByte();
            return reader.ReadProto(string.Empty);
        }

        return Result.Failure<Prototype>("failed to undump");
    }

    public static Result<BinaryChunkReader> CheckHeader(this BinaryChunkReader chunk)
    {
        // Check LuaSignature
        if (!chunk.ReadBytes(4).SequenceEqual(BinaryChunkConstants.LuaSignature))
        {
            return Result.Failure<BinaryChunkReader>("not a binary chunk");
        }

        // Check LuacVersion
        if (chunk.ReadByte().Equals(BinaryChunkConstants.LuacVersion))
        {
            return Result.Failure<BinaryChunkReader>("version mismatch");
        }

        // Check LuacFormat
        if (chunk.ReadByte() != BinaryChunkConstants.LuacFormat)
        {
            return Result.Failure<BinaryChunkReader>("format mismatch");
        }

        // Check LuacData
        if (!chunk.ReadBytes(6).SequenceEqual(BinaryChunkConstants.LuacData))
        {
            return Result.Failure<BinaryChunkReader>("corrupted chunk");
        }

        // Check InstructionSize
        if (chunk.ReadByte() != BinaryChunkConstants.InstructionSize)
        {
            return Result.Failure<BinaryChunkReader>("instruction size mismatch");
        }

        // Check LuaIntegerSize
        if (chunk.ReadByte() != BinaryChunkConstants.LuaIntegerSize)
        {
            return Result.Failure<BinaryChunkReader>("lua integer size mismatch");
        }

        // Check LuaNumberSize
        if (chunk.ReadByte() != BinaryChunkConstants.LuaNumberSize)
        {
            return Result.Failure<BinaryChunkReader>("lua number size mismatch");
        }

        // Check LuacInt
        if (chunk.ReadLuaInteger() != BinaryChunkConstants.LuacInt)
        {
            return Result.Failure<BinaryChunkReader>("integer format mismatch");
        }

        // Check LuacNum
        if (Math.Abs(chunk.ReadLuaNumber() - BinaryChunkConstants.LuacNum) > 0)
        {
            return Result.Failure<BinaryChunkReader>("float format mismatch");
        }

        // If all checks pass, return a successful result
        return Result.Success(chunk);
    }
}