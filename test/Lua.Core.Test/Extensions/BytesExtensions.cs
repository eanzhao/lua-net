namespace Lua.Core.Test.Extensions;

public static class BytesExtensions
{
    public static string ToHex(this byte[] bytes, bool withPrefix = false)
    {
        var offset = withPrefix ? 2 : 0;
        var length = bytes.Length * 2 + offset;
        var c = new char[length];

        byte b;

        if (withPrefix)
        {
            c[0] = '0';
            c[1] = 'x';
        }

        for (int bx = 0, cx = offset; bx < bytes.Length; ++bx, ++cx)
        {
            b = (byte)(bytes[bx] >> 4);
            c[cx] = (char)(b > 9 ? b + 0x37 + 0x20 : b + 0x30);

            b = (byte)(bytes[bx] & 0x0F);
            c[++cx] = (char)(b > 9 ? b + 0x37 + 0x20 : b + 0x30);
        }

        return new string(c);
    }
}