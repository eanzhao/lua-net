using Lua.Runtime.Objects;
using Lua.Runtime.Values;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Lua.Runtime.Execution;

public sealed partial class LuaState
{
    private const int NativeShortSize = 2;
    private const int NativeIntSize = 4;
    private const int NativeLongSize = 8;
    private const int NativeSizeTSize = 8;
    private const int NativeLuaIntegerSize = 8;
    private const int NativeLuaNumberSize = 8;
    private const int NativeFloatSize = 4;
    private const int NativeDoubleSize = 8;
    private const int NativeAlignment = 8;

    private static LuaValue[] StringPack(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var formatText = RequireStringArgument(arguments, 0, "string.pack");
        var format = GetLuaStringBytes(formatText);
        var packState = PackFormatState.CreateDefault();
        var output = new List<byte>();
        var argumentIndex = 1;
        var offset = 0;
        var position = 0;

        while (position < format.Length)
        {
            var item = ParseNextPackItem(format, ref position, ref packState, "string.pack");
            if (item.Kind == PackItemKind.None)
            {
                continue;
            }

            if (item.Kind == PackItemKind.Align)
            {
                var padding = GetAlignmentPadding(offset, item.Alignment);
                AppendZeroPadding(output, padding);
                offset += padding;
                continue;
            }

            if (item.Kind == PackItemKind.PadByte)
            {
                output.Add(0);
                offset += 1;
                continue;
            }

            var alignment = item.Alignment;
            if (alignment > 1)
            {
                var padding = GetAlignmentPadding(offset, alignment);
                AppendZeroPadding(output, padding);
                offset += padding;
            }

            switch (item.Kind)
            {
                case PackItemKind.SignedInteger:
                {
                    var value = RequireArgument(arguments, argumentIndex, "string.pack");
                    argumentIndex++;
                    var integer = RequireIntegerArgument([value], 0, "string.pack");
                    ValidateSignedPackRange(integer, item.Size);
                    WriteInteger(output, unchecked((ulong)integer), item.Size, packState.IsLittleEndian, integer < 0);
                    offset += item.Size;
                    break;
                }
                case PackItemKind.UnsignedInteger:
                {
                    var value = RequireArgument(arguments, argumentIndex, "string.pack");
                    argumentIndex++;
                    var integer = RequireIntegerArgument([value], 0, "string.pack");
                    ValidateUnsignedPackRange(integer, item.Size);
                    WriteInteger(output, unchecked((ulong)integer), item.Size, packState.IsLittleEndian, false);
                    offset += item.Size;
                    break;
                }
                case PackItemKind.Float32:
                {
                    var number = RequireDoubleArgument(arguments, argumentIndex, "string.pack");
                    argumentIndex++;
                    var bytes = BitConverter.GetBytes((float)number);
                    AppendEndianBytes(output, bytes, packState.IsLittleEndian);
                    offset += item.Size;
                    break;
                }
                case PackItemKind.Float64:
                case PackItemKind.LuaNumber:
                {
                    var number = RequireDoubleArgument(arguments, argumentIndex, "string.pack");
                    argumentIndex++;
                    var bytes = BitConverter.GetBytes(number);
                    AppendEndianBytes(output, bytes, packState.IsLittleEndian);
                    offset += item.Size;
                    break;
                }
                case PackItemKind.FixedString:
                {
                    var text = RequireStringArgument(arguments, argumentIndex, "string.pack");
                    argumentIndex++;
                    var bytes = GetLuaStringBytes(text);
                    if (bytes.Length > item.Size)
                    {
                        throw CreateArgumentError("string.pack", argumentIndex, "string longer than given size");
                    }

                    output.AddRange(bytes);
                    AppendZeroPadding(output, item.Size - bytes.Length);
                    offset += item.Size;
                    break;
                }
                case PackItemKind.ZeroString:
                {
                    var text = RequireStringArgument(arguments, argumentIndex, "string.pack");
                    argumentIndex++;
                    var bytes = GetLuaStringBytes(text);
                    if (bytes.Contains((byte)0))
                    {
                        throw CreateArgumentError("string.pack", argumentIndex, "string contains zeros");
                    }

                    output.AddRange(bytes);
                    output.Add(0);
                    offset += bytes.Length + 1;
                    break;
                }
                case PackItemKind.SizedString:
                {
                    var text = RequireStringArgument(arguments, argumentIndex, "string.pack");
                    argumentIndex++;
                    var bytes = GetLuaStringBytes(text);
                    ValidateUnsignedLength(bytes.Length, item.Size, argumentIndex);
                    WriteInteger(output, (ulong)bytes.Length, item.Size, packState.IsLittleEndian, false);
                    output.AddRange(bytes);
                    offset += item.Size + bytes.Length;
                    break;
                }
                default:
                    throw CreateRuntimeError("invalid pack option");
            }
        }

        return [LuaValue.FromString(CreateLuaString(output.ToArray()))];
    }

    private static LuaValue[] StringPackSize(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var formatText = RequireStringArgument(arguments, 0, "string.packsize");
        var format = GetLuaStringBytes(formatText);
        var packState = PackFormatState.CreateDefault();
        var totalSize = 0;
        var position = 0;

        while (position < format.Length)
        {
            var item = ParseNextPackItem(format, ref position, ref packState, "string.packsize");
            if (item.Kind == PackItemKind.None)
            {
                continue;
            }

            if (item.Kind == PackItemKind.ZeroString || item.Kind == PackItemKind.SizedString)
            {
                throw CreateRuntimeError("variable-length format");
            }

            if (item.Kind == PackItemKind.Align)
            {
                totalSize += GetAlignmentPadding(totalSize, item.Alignment);
                continue;
            }

            if (item.Alignment > 1)
            {
                totalSize += GetAlignmentPadding(totalSize, item.Alignment);
            }

            totalSize += item.Size;
        }

        return [LuaValue.FromInteger(totalSize)];
    }

    private static LuaValue[] StringUnpack(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var formatText = RequireStringArgument(arguments, 0, "string.unpack");
        var text = RequireStringArgument(arguments, 1, "string.unpack");
        var format = GetLuaStringBytes(formatText);
        var data = GetLuaStringBytes(text);
        var startPosition = ResolveRelativePosition(
            arguments.Count > 2 && !arguments[2].IsNil
                ? RequireIntegerArgument(arguments, 2, "string.unpack")
                : 1L,
            data.Length);
        if (startPosition < 1 || startPosition > data.Length + 1L)
        {
            throw CreateArgumentError("string.unpack", 3, "initial position out of string");
        }

        var packState = PackFormatState.CreateDefault();
        var offset = (int)startPosition - 1;
        var formatPosition = 0;
        var results = new List<LuaValue>();

        while (formatPosition < format.Length)
        {
            var item = ParseNextPackItem(format, ref formatPosition, ref packState, "string.unpack");
            if (item.Kind == PackItemKind.None)
            {
                continue;
            }

            if (item.Kind == PackItemKind.Align)
            {
                offset += GetAlignmentPadding(offset, item.Alignment);
                continue;
            }

            if (item.Alignment > 1)
            {
                offset += GetAlignmentPadding(offset, item.Alignment);
            }

            if (item.Kind == PackItemKind.PadByte)
            {
                EnsureAvailableBytes(data, offset, 1, "string.unpack");
                offset += 1;
                continue;
            }

            EnsureAvailableBytes(data, offset, item.Size, "string.unpack");
            switch (item.Kind)
            {
                case PackItemKind.SignedInteger:
                    results.Add(LuaValue.FromInteger(ReadPackedInteger(data, offset, item.Size, packState.IsLittleEndian, signed: true)));
                    offset += item.Size;
                    break;
                case PackItemKind.UnsignedInteger:
                    results.Add(LuaValue.FromInteger(ReadPackedInteger(data, offset, item.Size, packState.IsLittleEndian, signed: false)));
                    offset += item.Size;
                    break;
                case PackItemKind.Float32:
                    results.Add(LuaValue.FromFloat(ReadFloat32(data, offset, packState.IsLittleEndian)));
                    offset += item.Size;
                    break;
                case PackItemKind.Float64:
                case PackItemKind.LuaNumber:
                    results.Add(LuaValue.FromFloat(ReadFloat64(data, offset, packState.IsLittleEndian)));
                    offset += item.Size;
                    break;
                case PackItemKind.FixedString:
                    results.Add(LuaValue.FromString(CreateLuaString(data.AsSpan(offset, item.Size))));
                    offset += item.Size;
                    break;
                case PackItemKind.SizedString:
                {
                    var length = ReadPackedInteger(data, offset, item.Size, packState.IsLittleEndian, signed: false);
                    if (length < 0)
                    {
                        throw CreateRuntimeError("data string too short");
                    }

                    EnsureAvailableBytes(data, offset + item.Size, checked((int)length), "string.unpack");
                    results.Add(LuaValue.FromString(CreateLuaString(data.AsSpan(offset + item.Size, (int)length))));
                    offset += item.Size + (int)length;
                    break;
                }
                case PackItemKind.ZeroString:
                {
                    var end = Array.IndexOf(data, (byte)0, offset);
                    if (end < 0)
                    {
                        throw CreateRuntimeError("unfinished string for format 'z'");
                    }

                    results.Add(LuaValue.FromString(CreateLuaString(data.AsSpan(offset, end - offset))));
                    offset = end + 1;
                    break;
                }
                default:
                    throw CreateRuntimeError("invalid unpack option");
            }
        }

        results.Add(LuaValue.FromInteger(offset + 1L));
        return results.ToArray();
    }

    private static PackItem ParseNextPackItem(
        byte[] format,
        ref int position,
        ref PackFormatState state,
        string functionName)
    {
        while (position < format.Length && format[position] == (byte)' ')
        {
            position++;
        }

        if (position >= format.Length)
        {
            return PackItem.None;
        }

        var code = (char)format[position++];
        switch (code)
        {
            case '<':
                state.IsLittleEndian = true;
                return PackItem.None;
            case '>':
                state.IsLittleEndian = false;
                return PackItem.None;
            case '=':
                state.IsLittleEndian = BitConverter.IsLittleEndian;
                return PackItem.None;
            case '!':
                state.MaxAlignment = ParseOptionalPackNumber(format, ref position, NativeAlignment, functionName);
                ValidatePackSizeRange(state.MaxAlignment, functionName);
                return PackItem.None;
            case 'x':
                return new PackItem(PackItemKind.PadByte, 1, 1);
            case 'X':
            {
                var localState = state;
                var alignItem = ParseNextPackItem(format, ref position, ref localState, functionName);
                if (!alignItem.HasAlignment)
                {
                    throw CreateRuntimeError("invalid next option for option 'X'");
                }

                return new PackItem(PackItemKind.Align, 0, alignItem.Alignment);
            }
            case 'b':
                return new PackItem(PackItemKind.SignedInteger, 1, 1);
            case 'B':
                return new PackItem(PackItemKind.UnsignedInteger, 1, 1);
            case 'h':
                return CreateIntegerPackItem(signed: true, NativeShortSize, state.MaxAlignment);
            case 'H':
                return CreateIntegerPackItem(signed: false, NativeShortSize, state.MaxAlignment);
            case 'l':
                return CreateIntegerPackItem(signed: true, NativeLongSize, state.MaxAlignment);
            case 'L':
                return CreateIntegerPackItem(signed: false, NativeLongSize, state.MaxAlignment);
            case 'j':
                return CreateIntegerPackItem(signed: true, NativeLuaIntegerSize, state.MaxAlignment);
            case 'J':
                return CreateIntegerPackItem(signed: false, NativeLuaIntegerSize, state.MaxAlignment);
            case 'T':
                return CreateIntegerPackItem(signed: false, NativeSizeTSize, state.MaxAlignment);
            case 'i':
            {
                var size = ParseOptionalPackNumber(format, ref position, NativeIntSize, functionName);
                ValidatePackSizeRange(size, functionName);
                return CreateIntegerPackItem(signed: true, size, state.MaxAlignment);
            }
            case 'I':
            {
                var size = ParseOptionalPackNumber(format, ref position, NativeIntSize, functionName);
                ValidatePackSizeRange(size, functionName);
                return CreateIntegerPackItem(signed: false, size, state.MaxAlignment);
            }
            case 'f':
                return new PackItem(PackItemKind.Float32, NativeFloatSize, ComputeAlignment(NativeFloatSize, state.MaxAlignment));
            case 'd':
                return new PackItem(PackItemKind.Float64, NativeDoubleSize, ComputeAlignment(NativeDoubleSize, state.MaxAlignment));
            case 'n':
                return new PackItem(PackItemKind.LuaNumber, NativeLuaNumberSize, ComputeAlignment(NativeLuaNumberSize, state.MaxAlignment));
            case 'c':
            {
                var size = ParseRequiredPackNumber(format, ref position, functionName);
                return new PackItem(PackItemKind.FixedString, size, 1);
            }
            case 'z':
                return new PackItem(PackItemKind.ZeroString, 1, 1);
            case 's':
            {
                var size = ParseOptionalPackNumber(format, ref position, NativeSizeTSize, functionName);
                ValidatePackSizeRange(size, functionName);
                return new PackItem(PackItemKind.SizedString, size, ComputeAlignment(size, state.MaxAlignment));
            }
            default:
                throw CreateRuntimeError($"invalid format option '{code}'");
        }
    }

    private static PackItem CreateIntegerPackItem(bool signed, int size, int maxAlignment)
    {
        return new PackItem(signed ? PackItemKind.SignedInteger : PackItemKind.UnsignedInteger, size, ComputeAlignment(size, maxAlignment));
    }

    private static int ParseOptionalPackNumber(byte[] format, ref int position, int defaultValue, string functionName)
    {
        if (position >= format.Length || !char.IsDigit((char)format[position]))
        {
            return defaultValue;
        }

        return ParseRequiredPackNumber(format, ref position, functionName);
    }

    private static int ParseRequiredPackNumber(byte[] format, ref int position, string functionName)
    {
        if (position >= format.Length || !char.IsDigit((char)format[position]))
        {
            throw CreateRuntimeError($"missing size for format option in '{functionName}'");
        }

        var start = position;
        while (position < format.Length && char.IsDigit((char)format[position]))
        {
            position++;
        }

        return int.Parse(Encoding.UTF8.GetString(format, start, position - start), CultureInfo.InvariantCulture);
    }

    private static void ValidatePackSizeRange(int size, string functionName)
    {
        if (size is < 1 or > 16)
        {
            throw CreateRuntimeError($"integral size out of limits in '{functionName}'");
        }
    }

    private static int ComputeAlignment(int size, int maxAlignment)
    {
        var alignment = Math.Min(size, maxAlignment);
        if (alignment <= 1)
        {
            return 1;
        }

        if ((alignment & (alignment - 1)) != 0)
        {
            throw CreateRuntimeError("format asks for alignment not power of 2");
        }

        return alignment;
    }

    private static int GetAlignmentPadding(int offset, int alignment)
    {
        if (alignment <= 1)
        {
            return 0;
        }

        var remainder = offset % alignment;
        return remainder == 0 ? 0 : alignment - remainder;
    }

    private static void ValidateSignedPackRange(long value, int size)
    {
        if (size >= sizeof(long))
        {
            return;
        }

        var limit = 1L << ((size * 8) - 1);
        if (value < -limit || value >= limit)
        {
            throw CreateRuntimeError("integer overflow");
        }
    }

    private static void ValidateUnsignedPackRange(long value, int size)
    {
        if (size >= sizeof(long))
        {
            return;
        }

        if (unchecked((ulong)value) >= (1UL << (size * 8)))
        {
            throw CreateRuntimeError("unsigned overflow");
        }
    }

    private static void ValidateUnsignedLength(int length, int size, int argumentIndex)
    {
        if (size >= sizeof(ulong) || (ulong)length < (1UL << (size * 8)))
        {
            return;
        }

        throw CreateArgumentError("string.pack", argumentIndex, "string length does not fit in given size");
    }

    private static void WriteInteger(List<byte> output, ulong value, int size, bool littleEndian, bool negative)
    {
        var bytes = new byte[size];
        bytes[littleEndian ? 0 : size - 1] = (byte)(value & 0xFF);
        for (var index = 1; index < size; index++)
        {
            value >>= 8;
            bytes[littleEndian ? index : size - 1 - index] = (byte)(value & 0xFF);
        }

        if (negative && size > sizeof(long))
        {
            for (var index = sizeof(long); index < size; index++)
            {
                bytes[littleEndian ? index : size - 1 - index] = 0xFF;
            }
        }

        output.AddRange(bytes);
    }

    private static void AppendEndianBytes(List<byte> output, byte[] bytes, bool littleEndian)
    {
        if (littleEndian == BitConverter.IsLittleEndian)
        {
            output.AddRange(bytes);
            return;
        }

        output.AddRange(bytes.Reverse());
    }

    private static void AppendZeroPadding(List<byte> output, int count)
    {
        for (var index = 0; index < count; index++)
        {
            output.Add(0);
        }
    }

    private static void EnsureAvailableBytes(byte[] data, int offset, int count, string functionName)
    {
        if (count > data.Length - offset)
        {
            throw CreateRuntimeError("data string too short");
        }
    }

    private static long ReadPackedInteger(byte[] data, int offset, int size, bool littleEndian, bool signed)
    {
        ulong result = 0;
        var limit = Math.Min(size, sizeof(long));
        for (var index = limit - 1; index >= 0; index--)
        {
            result <<= 8;
            result |= data[offset + (littleEndian ? index : size - 1 - index)];
        }

        if (size < sizeof(long))
        {
            if (signed)
            {
                var mask = 1UL << (size * 8 - 1);
                result = (result ^ mask) - mask;
            }
        }
        else if (size > sizeof(long))
        {
            var mask = !signed || unchecked((long)result) >= 0 ? (byte)0 : (byte)0xFF;
            for (var index = limit; index < size; index++)
            {
                if (data[offset + (littleEndian ? index : size - 1 - index)] != mask)
                {
                    throw CreateRuntimeError($"{size.ToString(CultureInfo.InvariantCulture)}-byte integer does not fit into Lua Integer");
                }
            }
        }

        return unchecked((long)result);
    }

    private static double ReadFloat32(byte[] data, int offset, bool littleEndian)
    {
        Span<byte> bytes = stackalloc byte[sizeof(float)];
        CopyEndianBytes(data.AsSpan(offset, sizeof(float)), bytes, littleEndian);
        return BitConverter.ToSingle(bytes);
    }

    private static double ReadFloat64(byte[] data, int offset, bool littleEndian)
    {
        Span<byte> bytes = stackalloc byte[sizeof(double)];
        CopyEndianBytes(data.AsSpan(offset, sizeof(double)), bytes, littleEndian);
        return BitConverter.ToDouble(bytes);
    }

    private static void CopyEndianBytes(ReadOnlySpan<byte> source, Span<byte> destination, bool littleEndian)
    {
        if (littleEndian == BitConverter.IsLittleEndian)
        {
            source.CopyTo(destination);
            return;
        }

        for (var index = 0; index < source.Length; index++)
        {
            destination[source.Length - 1 - index] = source[index];
        }
    }

    private enum PackItemKind
    {
        None,
        Align,
        PadByte,
        SignedInteger,
        UnsignedInteger,
        Float32,
        Float64,
        LuaNumber,
        FixedString,
        ZeroString,
        SizedString
    }

    private readonly record struct PackItem(PackItemKind Kind, int Size, int Alignment)
    {
        public static PackItem None => new(PackItemKind.None, 0, 0);

        public bool HasAlignment =>
            Kind is PackItemKind.SignedInteger or
                PackItemKind.UnsignedInteger or
                PackItemKind.Float32 or
                PackItemKind.Float64 or
                PackItemKind.LuaNumber or
                PackItemKind.SizedString;
    }

    private struct PackFormatState
    {
        public bool IsLittleEndian;
        public int MaxAlignment;

        public static PackFormatState CreateDefault()
        {
            return new PackFormatState
            {
                IsLittleEndian = BitConverter.IsLittleEndian,
                MaxAlignment = 1
            };
        }
    }
}
