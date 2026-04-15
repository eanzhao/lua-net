using System.Runtime.CompilerServices;
using System.Text;

namespace Lua.Runtime.Execution;

public static class LuaStringBytes
{
    private static readonly ConditionalWeakTable<string, byte[]> RawByteMap = new();
    private static readonly UTF8Encoding StrictUtf8Encoding = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static bool HasRawBytes(string text, out byte[] bytes)
    {
        if (RawByteMap.TryGetValue(text, out var mapped))
        {
            bytes = mapped;
            return true;
        }

        bytes = Array.Empty<byte>();
        return false;
    }

    public static byte[] GetBytes(string text)
    {
        if (RawByteMap.TryGetValue(text, out var mapped))
        {
            return mapped;
        }

        return StrictUtf8Encoding.GetBytes(text);
    }

    public static string FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return string.Empty;
        }

        var buffer = bytes.ToArray();
        string text;
        try
        {
            text = StrictUtf8Encoding.GetString(buffer);
        }
        catch (DecoderFallbackException)
        {
            text = Encoding.Latin1.GetString(buffer);
        }

        RawByteMap.GetValue(text, _ => buffer);
        return text;
    }

    public static string Concat(string left, string right)
    {
        if (!HasRawBytes(left, out var leftBytes) &&
            !HasRawBytes(right, out var rightBytes))
        {
            return left + right;
        }

        leftBytes = GetBytes(left);
        rightBytes = GetBytes(right);
        var bytes = new byte[leftBytes.Length + rightBytes.Length];
        leftBytes.CopyTo(bytes, 0);
        rightBytes.CopyTo(bytes, leftBytes.Length);
        return FromBytes(bytes);
    }
}
