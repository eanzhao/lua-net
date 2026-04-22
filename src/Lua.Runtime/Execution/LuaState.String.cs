using Lua.Runtime.Objects;
using Lua.Runtime.Values;
using System.Runtime.InteropServices;
using System.Text;
using static Lua.Runtime.Values.LuaValueHelper;

namespace Lua.Runtime.Execution;

public sealed partial class LuaState
{
    private static LuaValue[] StringByte(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var text = RequireStringArgument(arguments, 0, "string.byte");
        var bytes = GetLuaStringBytes(text);
        var start = ResolveRelativePosition(
            arguments.Count > 1 && !arguments[1].IsNil
                ? RequireIntegerArgument(arguments, 1, "string.byte")
                : 1L,
            bytes.Length);
        var end = ResolveRelativePosition(
            arguments.Count > 2 && !arguments[2].IsNil
                ? RequireIntegerArgument(arguments, 2, "string.byte")
                : start,
            bytes.Length);

        start = Math.Max(start, 1);
        end = Math.Min(end, bytes.Length);
        if (start > end)
        {
            return [];
        }

        var result = new LuaValue[end - start + 1];
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = LuaValue.FromInteger(bytes[(int)start - 1 + index]);
        }

        return result;
    }

    private static LuaValue[] StringChar(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var bytes = new byte[arguments.Count];
        for (var index = 0; index < arguments.Count; index++)
        {
            var value = RequireIntegerArgument(arguments, index, "string.char");
            if (value is < byte.MinValue or > byte.MaxValue)
            {
                throw CreateArgumentError("string.char", index + 1, "value out of range");
            }

            bytes[index] = (byte)value;
        }

        return [LuaValue.FromString(CreateLuaString(bytes))];
    }

    private static LuaValue[] StringDump(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var functionValue = RequireArgument(arguments, 0, "string.dump");
        var stripDebugInformation = arguments.Count > 1 && IsTruthy(arguments[1]);

        if (functionValue.Kind != LuaValueKind.Function ||
            !state.TryDumpBytecodeChunk(functionValue.AsFunction(), stripDebugInformation, out var dumpedChunk))
        {
            throw CreateArgumentError("string.dump", 1, "Lua function expected");
        }

        return [LuaValue.FromString(CreateLuaString(dumpedChunk.Span))];
    }

    private static LuaValue[] StringReverse(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var text = RequireStringArgument(arguments, 0, "string.reverse");
        var bytes = GetLuaStringBytes(text);
        Array.Reverse(bytes);
        return [LuaValue.FromString(CreateLuaString(bytes))];
    }

    private static LuaValue[] StringRep(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var text = RequireStringArgument(arguments, 0, "string.rep");
        var count = RequireIntegerArgument(arguments, 1, "string.rep");
        var separator = GetOptionalStringArgument(arguments, 2, string.Empty, "string.rep");
        if (count <= 0)
        {
            return [LuaValue.FromString(string.Empty)];
        }

        var bytes = GetLuaStringBytes(text);
        var separatorBytes = GetLuaStringBytes(separator);
        checked
        {
            var totalLength = bytes.Length * count + separatorBytes.Length * (count - 1);
            var result = new byte[totalLength];
            var offset = 0;
            for (var index = 0L; index < count; index++)
            {
                bytes.CopyTo(result, offset);
                offset += bytes.Length;
                if (index + 1 < count && separatorBytes.Length > 0)
                {
                    separatorBytes.CopyTo(result, offset);
                    offset += separatorBytes.Length;
                }
            }

            return [LuaValue.FromString(CreateLuaString(result))];
        }
    }

    private static LuaValue[] StringSub(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var text = RequireStringArgument(arguments, 0, "string.sub");
        var bytes = GetLuaStringBytes(text);
        var start = ResolveRelativePosition(
            RequireIntegerArgument(arguments, 1, "string.sub"),
            bytes.Length);
        var end = ResolveRelativePosition(
            arguments.Count > 2 && !arguments[2].IsNil
                ? RequireIntegerArgument(arguments, 2, "string.sub")
                : -1L,
            bytes.Length);

        start = Math.Max(start, 1);
        end = Math.Min(end, bytes.Length);
        if (start > end)
        {
            return [LuaValue.FromString(string.Empty)];
        }

        return [LuaValue.FromString(CreateLuaString(bytes.AsSpan((int)start - 1, (int)(end - start + 1))))];
    }

    private static LuaValue[] StringFind(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var text = RequireStringArgument(arguments, 0, "string.find");
        var patternText = RequireStringArgument(arguments, 1, "string.find");
        var subject = GetLuaStringBytes(text);
        var startIndex = ResolvePatternSearchStart(arguments, 2, subject.Length, "string.find");
        var plain = arguments.Count > 3 && IsTruthy(arguments[3]);

        // Lua 5.5: init > #s + 1 → no match.
        if (startIndex > subject.Length)
        {
            return [LuaValue.Nil];
        }

        if (plain)
        {
            var needle = GetLuaStringBytes(patternText);
            var match = FindPlainBytes(subject, needle, startIndex);
            return match is null
                ? [LuaValue.Nil]
                : [LuaValue.FromInteger(match.Value.Start + 1), LuaValue.FromInteger(match.Value.End)];
        }

        var pattern = CompilePattern(patternText);
        var patternMatch = pattern.Find(subject, startIndex);
        return patternMatch is null
            ? [LuaValue.Nil]
            : BuildPatternResults(subject, patternMatch.Value, includeIndices: true, wholeMatchWhenNoCapture: false);
    }

    private static LuaValue[] StringMatch(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var text = RequireStringArgument(arguments, 0, "string.match");
        var patternText = RequireStringArgument(arguments, 1, "string.match");
        var subject = GetLuaStringBytes(text);
        var startIndex = ResolvePatternSearchStart(arguments, 2, subject.Length, "string.match");

        // Lua 5.5: init > #s + 1 → no match.
        if (startIndex > subject.Length)
        {
            return [LuaValue.Nil];
        }

        var pattern = CompilePattern(patternText);
        var match = pattern.Find(subject, startIndex);
        return match is null
            ? [LuaValue.Nil]
            : BuildPatternResults(subject, match.Value, includeIndices: false, wholeMatchWhenNoCapture: true);
    }

    private static LuaValue[] StringGMatch(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var text = RequireStringArgument(arguments, 0, "string.gmatch");
        var patternText = RequireStringArgument(arguments, 1, "string.gmatch");
        var subject = GetLuaStringBytes(text);
        var pattern = CompilePattern(patternText);
        var nextIndex = ResolvePatternSearchStart(arguments, 2, subject.Length, "string.gmatch");
        int? lastMatchEnd = null;

        var iterator = new LuaClosure(
            "string.gmatch.iter",
            body: new LuaNativeClosureBody((innerState, _, _) =>
            {
                while (nextIndex <= subject.Length)
                {
                    var match = pattern.Find(subject, nextIndex);
                    if (match is null)
                    {
                        return [];
                    }

                    if (match.Value.Start == nextIndex &&
                        match.Value.End == nextIndex &&
                        lastMatchEnd == nextIndex)
                    {
                        if (nextIndex >= subject.Length)
                        {
                            return [];
                        }

                        nextIndex++;
                        continue;
                    }

                    var result = BuildPatternResults(subject, match.Value, includeIndices: false, wholeMatchWhenNoCapture: true);
                    lastMatchEnd = match.Value.End;
                    nextIndex = GetNextPatternSearchIndex(match.Value, subject.Length);
                    return result;
                }

                return [];
            }));

        return [LuaValue.FromFunction(iterator)];
    }

    private static LuaValue[] StringGSub(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var text = RequireStringArgument(arguments, 0, "string.gsub");
        var patternText = RequireStringArgument(arguments, 1, "string.gsub");
        var replacement = RequireArgument(arguments, 2, "string.gsub");
        var limit = arguments.Count > 3 && !arguments[3].IsNil
            ? Math.Max(RequireIntegerArgument(arguments, 3, "string.gsub"), 0)
            : long.MaxValue;
        var subject = GetLuaStringBytes(text);
        var pattern = CompilePattern(patternText);

        var output = new List<byte>(subject.Length);
        var copiedUntil = 0;
        var searchIndex = 0;
        long substitutions = 0;
        int? lastMatchEnd = null;
        var performedReplacement = false;

        while (searchIndex <= subject.Length && substitutions < limit)
        {
            var match = pattern.Find(subject, searchIndex);
            if (match is null)
            {
                break;
            }

            if (match.Value.Start == searchIndex &&
                match.Value.End == searchIndex &&
                lastMatchEnd == searchIndex)
            {
                if (searchIndex >= subject.Length)
                {
                    break;
                }

                output.AddRange(subject.AsSpan(copiedUntil, searchIndex - copiedUntil).ToArray());
                output.Add(subject[searchIndex]);
                copiedUntil = searchIndex + 1;
                searchIndex = copiedUntil;
                continue;
            }

            output.AddRange(subject.AsSpan(copiedUntil, match.Value.Start - copiedUntil).ToArray());

            var replacementResult = ResolveGSubReplacement(state, replacement, subject, match.Value);
            output.AddRange(replacementResult.Bytes);
            substitutions++;
            lastMatchEnd = match.Value.End;
            performedReplacement |= replacementResult.PerformedReplacement;

            if (match.Value.End == match.Value.Start)
            {
                if (match.Value.Start < subject.Length)
                {
                    output.Add(subject[match.Value.Start]);
                    copiedUntil = match.Value.Start + 1;
                    searchIndex = copiedUntil;
                    continue;
                }

                copiedUntil = subject.Length;
                searchIndex = subject.Length + 1;
                break;
            }

            copiedUntil = match.Value.End;
            searchIndex = match.Value.End;
        }

        if (copiedUntil < subject.Length)
        {
            output.AddRange(subject.AsSpan(copiedUntil).ToArray());
        }

        var resultText = performedReplacement
            ? CreateLuaString(CollectionsMarshal.AsSpan(output))
            : text;
        return [LuaValue.FromString(resultText), LuaValue.FromInteger(substitutions)];
    }

    private static (byte[] Bytes, bool PerformedReplacement) ResolveGSubReplacement(
        LuaState state,
        LuaValue replacement,
        byte[] subject,
        LuaPattern.LuaPatternMatch match)
    {
        if (replacement.Kind == LuaValueKind.String)
        {
            return (ExpandGSubReplacementString(replacement.AsString(), subject, match), true);
        }

        if (replacement.Kind == LuaValueKind.Table)
        {
            var key = GetGSubLookupKey(subject, match);
            var value = GetTableLibraryValue(state, replacement.AsTable(), key);
            return ConvertReplacementResult(subject, match, value);
        }

        if (replacement.Kind == LuaValueKind.Function)
        {
            var callArguments = BuildReplacementCallArguments(subject, match);
            var results = state.InvokeCallable(replacement, callArguments);
            var value = results.Length == 0 ? LuaValue.Nil : results[0];
            return ConvertReplacementResult(subject, match, value);
        }

        throw CreateArgumentTypeError("string.gsub", 3, "string/table/function", replacement);
    }

    private static (byte[] Bytes, bool PerformedReplacement) ConvertReplacementResult(
        byte[] subject,
        LuaPattern.LuaPatternMatch match,
        LuaValue value)
    {
        if (value.IsNil || (value.Kind == LuaValueKind.Boolean && !value.AsBoolean()))
        {
            return (match.GetWholeMatchBytes(subject).ToArray(), false);
        }

        if (TryConvertToStringArgument(value, out var text))
        {
            return (GetLuaStringBytes(text), true);
        }

        throw CreateRuntimeError($"invalid replacement value ({DescribeValueType(value)})");
    }

    private static string DescribeValueType(LuaValue value)
    {
        return value.Kind switch
        {
            LuaValueKind.Boolean => "a boolean",
            LuaValueKind.Integer => "an integer",
            LuaValueKind.Float => "a float",
            LuaValueKind.String => "a string",
            LuaValueKind.Table => "a table",
            LuaValueKind.Function => "a function",
            LuaValueKind.Thread => "a thread",
            LuaValueKind.UserData => "a userdata",
            _ => GetTypeName(value)
        };
    }

    private static LuaValue GetGSubLookupKey(byte[] subject, LuaPattern.LuaPatternMatch match)
    {
        if (match.Captures.Length > 0)
        {
            return ConvertPatternCapture(subject, match.Captures[0]);
        }

        return LuaValue.FromString(CreateLuaString(match.GetWholeMatchBytes(subject)));
    }

    private static LuaValue[] BuildReplacementCallArguments(byte[] subject, LuaPattern.LuaPatternMatch match)
    {
        if (match.Captures.Length == 0)
        {
            return [LuaValue.FromString(CreateLuaString(match.GetWholeMatchBytes(subject)))];
        }

        var values = new LuaValue[match.Captures.Length];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = ConvertPatternCapture(subject, match.Captures[index]);
        }

        return values;
    }

    private static byte[] ExpandGSubReplacementString(string replacementText, byte[] subject, LuaPattern.LuaPatternMatch match)
    {
        var replacementBytes = GetLuaStringBytes(replacementText);
        var output = new List<byte>(replacementBytes.Length);
        for (var index = 0; index < replacementBytes.Length; index++)
        {
            if (replacementBytes[index] != (byte)'%' || index + 1 >= replacementBytes.Length)
            {
                output.Add(replacementBytes[index]);
                continue;
            }

            index++;
            var code = replacementBytes[index];
            if (code == (byte)'%')
            {
                output.Add((byte)'%');
                continue;
            }

            if (code == (byte)'0')
            {
                output.AddRange(match.GetWholeMatchBytes(subject).ToArray());
                continue;
            }

            if (code is >= (byte)'1' and <= (byte)'9')
            {
                if (match.Captures.Length == 0)
                {
                    if (code != (byte)'1')
                    {
                        throw CreateRuntimeError($"invalid capture index %{(char)code}");
                    }

                    output.AddRange(match.GetWholeMatchBytes(subject).ToArray());
                    continue;
                }

                var captureIndex = code - (byte)'1';
                if (captureIndex >= match.Captures.Length)
                {
                    throw CreateRuntimeError($"invalid capture index %{(char)code}");
                }

                var capture = match.Captures[captureIndex];
                if (capture.IsPosition)
                {
                    output.AddRange(Encoding.UTF8.GetBytes(capture.Position.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                }
                else
                {
                    output.AddRange(subject.AsSpan(capture.Start, capture.End - capture.Start).ToArray());
                }

                continue;
            }

            throw CreateRuntimeError("invalid use of '%' in replacement string");
        }

        return CollectionsMarshal.AsSpan(output).ToArray();
    }

    private static LuaValue[] BuildPatternResults(
        byte[] subject,
        LuaPattern.LuaPatternMatch match,
        bool includeIndices,
        bool wholeMatchWhenNoCapture)
    {
        var results = new List<LuaValue>();
        if (includeIndices)
        {
            results.Add(LuaValue.FromInteger(match.Start + 1));
            results.Add(LuaValue.FromInteger(match.End));
        }

        if (match.Captures.Length == 0)
        {
            if (wholeMatchWhenNoCapture)
            {
                results.Add(LuaValue.FromString(CreateLuaString(match.GetWholeMatchBytes(subject))));
            }

            return results.ToArray();
        }

        foreach (var capture in match.Captures)
        {
            results.Add(ConvertPatternCapture(subject, capture));
        }

        return results.ToArray();
    }

    private static LuaValue ConvertPatternCapture(byte[] subject, LuaPattern.CaptureState capture)
    {
        if (capture.IsPosition)
        {
            return LuaValue.FromInteger(capture.Position);
        }

        return LuaValue.FromString(CreateLuaString(subject.AsSpan(capture.Start, capture.End - capture.Start)));
    }

    private static (int Start, int End)? FindPlainBytes(byte[] subject, byte[] needle, int startIndex)
    {
        if (startIndex > subject.Length)
        {
            return null;
        }

        if (needle.Length == 0)
        {
            return (startIndex, startIndex);
        }

        var relativeIndex = subject.AsSpan(startIndex).IndexOf(needle);
        if (relativeIndex < 0)
        {
            return null;
        }

        var absoluteStart = startIndex + relativeIndex;
        return (absoluteStart, absoluteStart + needle.Length);
    }

    private static int GetNextPatternSearchIndex(LuaPattern.LuaPatternMatch match, int subjectLength)
    {
        if (match.End > match.Start)
        {
            return match.End;
        }

        return match.Start < subjectLength
            ? match.Start + 1
            : subjectLength + 1;
    }

    private static LuaPattern CompilePattern(string patternText)
    {
        try
        {
            return LuaPattern.Compile(patternText);
        }
        catch (LuaPatternException ex)
        {
            throw CreateRuntimeError(ex.Message);
        }
    }

    private static int ResolvePatternSearchStart(
        IReadOnlyList<LuaValue> arguments,
        int argumentIndex,
        int length,
        string functionName)
    {
        var rawPosition = arguments.Count > argumentIndex && !arguments[argumentIndex].IsNil
            ? RequireIntegerArgument(arguments, argumentIndex, functionName)
            : 1L;

        // Lua 5.5: if init > #s + 1, the function returns no match.
        // Signal this with a sentinel (length + 2 as 0-based = length + 1 absolute).
        if (rawPosition > (long)length + 1)
        {
            return length + 1;
        }

        var position = ResolveRelativePosition(rawPosition, length);
        position = Math.Clamp(position, 1, length + 1);
        return (int)(position - 1);
    }

    private static byte[] GetLuaStringBytes(string text)
    {
        try
        {
            return LuaStringBytes.GetBytes(text);
        }
        catch (EncoderFallbackException)
        {
            throw CreateRuntimeError(InvalidUtf8CodeMessage);
        }
    }

    private static string CreateLuaString(ReadOnlySpan<byte> bytes)
    {
        return LuaStringBytes.FromBytes(bytes);
    }

}
