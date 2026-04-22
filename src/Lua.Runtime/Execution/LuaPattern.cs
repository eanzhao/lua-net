namespace Lua.Runtime.Execution;

internal sealed class LuaPattern
{
    private readonly byte[] _pattern;
    private readonly List<Token> _tokens = [];
    private readonly bool _anchorStart;
    private readonly bool _anchorEnd;
    private readonly int _captureCount;

    private LuaPattern(byte[] pattern)
    {
        _pattern = pattern;

        var position = 0;
        if (_pattern.Length > 0 && _pattern[0] == (byte)'^')
        {
            _anchorStart = true;
            position = 1;
        }

        var end = _pattern.Length;
        if (end > position &&
            _pattern[^1] == (byte)'$' &&
            (end == position + 1 || _pattern[^2] != (byte)'%'))
        {
            _anchorEnd = true;
            end -= 1;
        }

        var captureIndex = 0;
        var parsedEnd = ParseTokens(position, end, inCapture: false, ref captureIndex);
        if (parsedEnd != end)
        {
            throw LuaPatternException.Malformed("invalid pattern capture");
        }

        _captureCount = captureIndex;
    }

    public static LuaPattern Compile(string pattern)
    {
        return new LuaPattern(LuaStringBytes.GetBytes(pattern));
    }

    public LuaPatternMatch? Find(byte[] subject, int startIndex)
    {
        if (startIndex < 0)
        {
            startIndex = 0;
        }

        if (_anchorStart)
        {
            if (startIndex > 0)
            {
                return null;
            }

            return TryMatchAt(subject, 0, out var anchoredMatch) ? anchoredMatch : null;
        }

        for (var index = startIndex; index <= subject.Length; index++)
        {
            if (TryMatchAt(subject, index, out var match))
            {
                return match;
            }
        }

        return null;
    }

    private bool TryMatchAt(byte[] subject, int startIndex, out LuaPatternMatch match)
    {
        var captures = CreateCaptures();
        if (TryMatchTokens(subject, startIndex, tokenIndex: 0, captures, out var endIndex, out var finalCaptures))
        {
            match = new LuaPatternMatch(startIndex, endIndex, finalCaptures);
            return true;
        }

        match = default;
        return false;
    }

    private bool TryMatchTokens(
        byte[] subject,
        int position,
        int tokenIndex,
        CaptureState[] captures,
        out int endPosition,
        out CaptureState[] finalCaptures)
    {
        if (tokenIndex >= _tokens.Count)
        {
            if (_anchorEnd && position != subject.Length)
            {
                endPosition = default;
                finalCaptures = Array.Empty<CaptureState>();
                return false;
            }

            endPosition = position;
            finalCaptures = captures;
            return true;
        }

        var token = _tokens[tokenIndex];
        switch (token.Kind)
        {
            case TokenKind.CaptureStart:
            {
                var nextCaptures = CloneCaptures(captures);
                nextCaptures[token.CaptureIndex] = new CaptureState(position, -1, false);
                return TryMatchTokens(subject, position, tokenIndex + 1, nextCaptures, out endPosition, out finalCaptures);
            }
            case TokenKind.CaptureEnd:
            {
                if (token.CaptureIndex < 0 ||
                    token.CaptureIndex >= captures.Length ||
                    captures[token.CaptureIndex].Start < 0 ||
                    captures[token.CaptureIndex].IsPosition)
                {
                    endPosition = default;
                    finalCaptures = Array.Empty<CaptureState>();
                    return false;
                }

                var nextCaptures = CloneCaptures(captures);
                nextCaptures[token.CaptureIndex] = nextCaptures[token.CaptureIndex] with { End = position };
                return TryMatchTokens(subject, position, tokenIndex + 1, nextCaptures, out endPosition, out finalCaptures);
            }
            case TokenKind.PositionCapture:
            {
                var nextCaptures = CloneCaptures(captures);
                nextCaptures[token.CaptureIndex] = new CaptureState(position, position, true);
                return TryMatchTokens(subject, position, tokenIndex + 1, nextCaptures, out endPosition, out finalCaptures);
            }
            case TokenKind.Atom:
                return TryMatchQuantifiedAtom(subject, position, tokenIndex, token, captures, out endPosition, out finalCaptures);
            default:
                throw new InvalidOperationException("Unknown pattern token.");
        }
    }

    private bool TryMatchQuantifiedAtom(
        byte[] subject,
        int position,
        int tokenIndex,
        Token token,
        CaptureState[] captures,
        out int endPosition,
        out CaptureState[] finalCaptures)
    {
        switch (token.Quantifier)
        {
            case Quantifier.None:
                if (TryMatchAtomOnce(subject, position, token.Atom, captures, out var nextPosition))
                {
                    return TryMatchTokens(subject, nextPosition, tokenIndex + 1, captures, out endPosition, out finalCaptures);
                }

                endPosition = default;
                finalCaptures = Array.Empty<CaptureState>();
                return false;
            case Quantifier.Optional:
                if (TryMatchAtomOnce(subject, position, token.Atom, captures, out nextPosition) &&
                    TryMatchTokens(subject, nextPosition, tokenIndex + 1, captures, out endPosition, out finalCaptures))
                {
                    return true;
                }

                return TryMatchTokens(subject, position, tokenIndex + 1, captures, out endPosition, out finalCaptures);
            case Quantifier.ZeroOrMore:
                return TryMatchRepeatedAtom(subject, position, tokenIndex + 1, token.Atom, captures, minimumCount: 0, greedy: true, out endPosition, out finalCaptures);
            case Quantifier.OneOrMore:
                return TryMatchRepeatedAtom(subject, position, tokenIndex + 1, token.Atom, captures, minimumCount: 1, greedy: true, out endPosition, out finalCaptures);
            case Quantifier.ZeroOrMoreMinimal:
                return TryMatchRepeatedAtom(subject, position, tokenIndex + 1, token.Atom, captures, minimumCount: 0, greedy: false, out endPosition, out finalCaptures);
            default:
                throw new InvalidOperationException("Unknown pattern quantifier.");
        }
    }

    private bool TryMatchRepeatedAtom(
        byte[] subject,
        int position,
        int nextTokenIndex,
        PatternAtom atom,
        CaptureState[] captures,
        int minimumCount,
        bool greedy,
        out int endPosition,
        out CaptureState[] finalCaptures)
    {
        var positions = new List<int> { position };
        var current = position;
        while (TryMatchAtomOnce(subject, current, atom, captures, out var nextPosition))
        {
            if (nextPosition <= current)
            {
                break;
            }

            positions.Add(nextPosition);
            current = nextPosition;
        }

        if (positions.Count - 1 < minimumCount)
        {
            endPosition = default;
            finalCaptures = Array.Empty<CaptureState>();
            return false;
        }

        if (greedy)
        {
            for (var index = positions.Count - 1; index >= minimumCount; index--)
            {
                if (TryMatchTokens(subject, positions[index], nextTokenIndex, captures, out endPosition, out finalCaptures))
                {
                    return true;
                }
            }
        }
        else
        {
            for (var index = minimumCount; index < positions.Count; index++)
            {
                if (TryMatchTokens(subject, positions[index], nextTokenIndex, captures, out endPosition, out finalCaptures))
                {
                    return true;
                }
            }
        }

        endPosition = default;
        finalCaptures = Array.Empty<CaptureState>();
        return false;
    }

    private static bool TryMatchAtomOnce(
        byte[] subject,
        int position,
        PatternAtom atom,
        CaptureState[] captures,
        out int nextPosition)
    {
        nextPosition = default;
        if (position > subject.Length)
        {
            return false;
        }

        switch (atom.Kind)
        {
            case PatternAtomKind.Literal:
                if (position < subject.Length && subject[position] == atom.Value)
                {
                    nextPosition = position + 1;
                    return true;
                }

                return false;
            case PatternAtomKind.Any:
                if (position < subject.Length)
                {
                    nextPosition = position + 1;
                    return true;
                }

                return false;
            case PatternAtomKind.Class:
                if (position < subject.Length && MatchesCharacterClass(subject[position], atom.Value))
                {
                    nextPosition = position + 1;
                    return true;
                }

                return false;
            case PatternAtomKind.Set:
                if (position < subject.Length && atom.Set is not null && atom.Set.Contains(subject[position]))
                {
                    nextPosition = position + 1;
                    return true;
                }

                return false;
            case PatternAtomKind.Frontier:
                if (atom.Set is null)
                {
                    return false;
                }

                var previous = position == 0 ? (byte)0 : subject[position - 1];
                var current = position < subject.Length ? subject[position] : (byte)0;
                if (!atom.Set.Contains(previous) && atom.Set.Contains(current))
                {
                    nextPosition = position;
                    return true;
                }

                return false;
            case PatternAtomKind.Balanced:
                return TryMatchBalanced(subject, position, atom.Value, atom.Value2, out nextPosition);
            case PatternAtomKind.BackReference:
                return TryMatchCaptureReference(subject, position, captures, atom.Value, out nextPosition);
            default:
                throw new InvalidOperationException("Unknown pattern atom.");
        }
    }

    private int ParseTokens(int start, int end, bool inCapture, ref int captureIndex)
    {
        var position = start;
        while (position < end)
        {
            if (_pattern[position] == (byte)')')
            {
                if (!inCapture)
                {
                    throw LuaPatternException.Malformed("invalid pattern capture");
                }

                return position + 1;
            }

            if (_pattern[position] == (byte)'(')
            {
                if (position + 1 < end && _pattern[position + 1] == (byte)')')
                {
                    _tokens.Add(Token.CreatePositionCapture(captureIndex++));
                    position += 2;
                    continue;
                }

                var currentCapture = captureIndex++;
                _tokens.Add(Token.CreateCaptureStart(currentCapture));
                position = ParseTokens(position + 1, end, inCapture: true, ref captureIndex);
                _tokens.Add(Token.CreateCaptureEnd(currentCapture));
                continue;
            }

            var atom = ParseAtom(ref position, end);
            var quantifier = Quantifier.None;
            if (position < end)
            {
                quantifier = _pattern[position] switch
                {
                    (byte)'*' => Quantifier.ZeroOrMore,
                    (byte)'+' => Quantifier.OneOrMore,
                    (byte)'-' => Quantifier.ZeroOrMoreMinimal,
                    (byte)'?' => Quantifier.Optional,
                    _ => Quantifier.None
                };

                if (quantifier != Quantifier.None)
                {
                    position += 1;
                }
            }

            _tokens.Add(Token.CreateAtom(atom, quantifier));
        }

        if (inCapture)
        {
            throw LuaPatternException.Malformed("unfinished capture");
        }

        return position;
    }

    private PatternAtom ParseAtom(ref int position, int end)
    {
        var current = _pattern[position];
        switch (current)
        {
            case (byte)'.':
                position += 1;
                return new PatternAtom(PatternAtomKind.Any, 0, 0, null);
            case (byte)'[':
                return ParseSet(ref position, end);
            case (byte)'%':
                return ParseEscapedAtom(ref position, end);
            default:
                position += 1;
                return new PatternAtom(PatternAtomKind.Literal, current, 0, null);
        }
    }

    private PatternAtom ParseEscapedAtom(ref int position, int end)
    {
        if (position + 1 >= end)
        {
            throw LuaPatternException.Malformed("malformed pattern (ends with '%')");
        }

        var code = _pattern[position + 1];
        position += 2;

        if (code == (byte)'b')
        {
            if (position + 1 >= end)
            {
                throw LuaPatternException.Malformed("malformed pattern (missing arguments to '%b')");
            }

            var open = _pattern[position];
            var close = _pattern[position + 1];
            position += 2;
            return new PatternAtom(PatternAtomKind.Balanced, open, close, null);
        }

        if (code == (byte)'f')
        {
            if (position >= end || _pattern[position] != (byte)'[')
            {
                throw LuaPatternException.Malformed("missing '[' after '%f' in pattern");
            }

            var set = ParseSet(ref position, end);
            return new PatternAtom(PatternAtomKind.Frontier, 0, 0, set.Set);
        }

        if (code == (byte)'0')
        {
            throw LuaPatternException.Malformed("invalid capture index %0");
        }

        if (code is >= (byte)'1' and <= (byte)'9')
        {
            return new PatternAtom(PatternAtomKind.BackReference, (byte)(code - (byte)'1'), 0, null);
        }

        if (IsCharacterClass(code))
        {
            return new PatternAtom(PatternAtomKind.Class, code, 0, null);
        }

        return new PatternAtom(PatternAtomKind.Literal, code, 0, null);
    }

    private PatternAtom ParseSet(ref int position, int end)
    {
        position += 1;
        if (position >= end)
        {
            throw LuaPatternException.Malformed("malformed pattern (missing ']')");
        }

        var negate = false;
        if (_pattern[position] == (byte)'^')
        {
            negate = true;
            position += 1;
        }

        var set = new ByteSet(negate);
        var hasAny = false;
        byte? pendingRangeStart = null;

        if (position < end && _pattern[position] == (byte)']')
        {
            set.AddLiteral((byte)']');
            hasAny = true;
            position += 1;
        }

        while (position < end && _pattern[position] != (byte)']')
        {
            if (_pattern[position] == (byte)'%' && position + 1 < end)
            {
                if (pendingRangeStart is not null)
                {
                    set.AddLiteral(pendingRangeStart.Value);
                    pendingRangeStart = null;
                }

                var escape = _pattern[position + 1];
                if (IsCharacterClass(escape))
                {
                    set.AddClass(escape);
                    hasAny = true;
                    position += 2;
                    continue;
                }

                pendingRangeStart = escape;
                position += 2;
                hasAny = true;
                continue;
            }

            var value = _pattern[position];
            if (value == (byte)'-' &&
                pendingRangeStart is not null &&
                position + 1 < end &&
                _pattern[position + 1] != (byte)']')
            {
                position += 1;
                var rangeEnd = _pattern[position] == (byte)'%' && position + 1 < end
                    ? _pattern[position + 1]
                    : _pattern[position];
                if (_pattern[position] == (byte)'%')
                {
                    position += 2;
                }
                else
                {
                    position += 1;
                }

                if (pendingRangeStart.Value > rangeEnd)
                {
                    (pendingRangeStart, rangeEnd) = ((byte?)rangeEnd, pendingRangeStart.Value);
                }

                set.AddRange(pendingRangeStart.Value, rangeEnd);
                pendingRangeStart = null;
                hasAny = true;
                continue;
            }

            if (pendingRangeStart is not null)
            {
                set.AddLiteral(pendingRangeStart.Value);
            }

            pendingRangeStart = value;
            hasAny = true;
            position += 1;
        }

        if (pendingRangeStart is not null)
        {
            set.AddLiteral(pendingRangeStart.Value);
        }

        if (position >= end || _pattern[position] != (byte)']')
        {
            throw LuaPatternException.Malformed("malformed pattern (missing ']')");
        }

        if (!hasAny)
        {
            throw LuaPatternException.Malformed("malformed pattern (empty set)");
        }

        position += 1;
        return new PatternAtom(PatternAtomKind.Set, 0, 0, set);
    }

    private static bool TryMatchBalanced(byte[] subject, int position, byte open, byte close, out int nextPosition)
    {
        nextPosition = default;
        if (position >= subject.Length || subject[position] != open)
        {
            return false;
        }

        if (open == close)
        {
            for (var index = position + 1; index < subject.Length; index++)
            {
                if (subject[index] == close)
                {
                    nextPosition = index + 1;
                    return true;
                }
            }

            return false;
        }

        var depth = 1;
        for (var index = position + 1; index < subject.Length; index++)
        {
            if (subject[index] == open)
            {
                depth++;
            }
            else if (subject[index] == close)
            {
                depth--;
                if (depth == 0)
                {
                    nextPosition = index + 1;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryMatchCaptureReference(byte[] subject, int position, CaptureState[] captures, byte captureIndex, out int nextPosition)
    {
        nextPosition = default;
        if (captureIndex >= captures.Length)
        {
            throw LuaPatternException.Malformed($"invalid capture index %{captureIndex + 1}");
        }

        var capture = captures[captureIndex];
        if (capture.Start < 0 || capture.End < capture.Start || capture.IsPosition)
        {
            throw LuaPatternException.Malformed($"invalid capture index %{captureIndex + 1}");
        }

        var length = capture.End - capture.Start;
        if (position + length > subject.Length)
        {
            return false;
        }

        if (subject.AsSpan(capture.Start, length).SequenceEqual(subject.AsSpan(position, length)))
        {
            nextPosition = position + length;
            return true;
        }

        return false;
    }

    private static bool IsCharacterClass(byte code)
    {
        return code is
            (byte)'a' or (byte)'A' or
            (byte)'c' or (byte)'C' or
            (byte)'d' or (byte)'D' or
            (byte)'g' or (byte)'G' or
            (byte)'l' or (byte)'L' or
            (byte)'p' or (byte)'P' or
            (byte)'s' or (byte)'S' or
            (byte)'u' or (byte)'U' or
            (byte)'w' or (byte)'W' or
            (byte)'x' or (byte)'X' or
            (byte)'z' or (byte)'Z';
    }

    private static bool MatchesCharacterClass(byte value, byte classCode)
    {
        var upper = false;
        if (classCode is >= (byte)'A' and <= (byte)'Z')
        {
            classCode = (byte)(classCode + 32);
            upper = true;
        }

        var result = classCode switch
        {
            (byte)'a' => value is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z',
            (byte)'c' => value < 0x20 || value == 0x7F,
            (byte)'d' => value is >= (byte)'0' and <= (byte)'9',
            (byte)'g' => value is >= 0x21 and <= 0x7E,
            (byte)'l' => value is >= (byte)'a' and <= (byte)'z',
            (byte)'p' => value is >= 0x21 and <= 0x7E &&
                         !(value is >= (byte)'0' and <= (byte)'9') &&
                         !(value is >= (byte)'A' and <= (byte)'Z') &&
                         !(value is >= (byte)'a' and <= (byte)'z'),
            (byte)'s' => value is (byte)' ' or (byte)'\f' or (byte)'\n' or (byte)'\r' or (byte)'\t' or (byte)'\v',
            (byte)'u' => value is >= (byte)'A' and <= (byte)'Z',
            (byte)'w' => value is >= (byte)'0' and <= (byte)'9' or >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z',
            (byte)'x' => value is >= (byte)'0' and <= (byte)'9' or >= (byte)'A' and <= (byte)'F' or >= (byte)'a' and <= (byte)'f',
            (byte)'z' => value == 0,
            _ => value == classCode
        };

        return upper ? !result : result;
    }

    private static CaptureState[] CloneCaptures(CaptureState[] captures)
    {
        var clone = new CaptureState[captures.Length];
        Array.Copy(captures, clone, captures.Length);
        return clone;
    }

    private CaptureState[] CreateCaptures()
    {
        var captures = new CaptureState[_captureCount];
        for (var index = 0; index < captures.Length; index++)
        {
            captures[index] = new CaptureState(-1, -1, false);
        }

        return captures;
    }

    internal readonly record struct LuaPatternMatch(int Start, int End, CaptureState[] Captures)
    {
        public ReadOnlySpan<byte> GetWholeMatchBytes(byte[] subject) => subject.AsSpan(Start, End - Start);

        public ReadOnlySpan<byte> GetCaptureBytes(byte[] subject, int index)
        {
            var capture = Captures[index];
            return subject.AsSpan(capture.Start, capture.End - capture.Start);
        }
    }

    internal readonly record struct CaptureState(int Start, int End, bool IsPosition)
    {
        public int Position => Start + 1;
    }

    private readonly record struct PatternAtom(PatternAtomKind Kind, byte Value, byte Value2, ByteSet? Set);

    private readonly record struct Token(TokenKind Kind, int CaptureIndex, PatternAtom Atom, Quantifier Quantifier)
    {
        public static Token CreateCaptureStart(int captureIndex) => new(TokenKind.CaptureStart, captureIndex, default, Quantifier.None);

        public static Token CreateCaptureEnd(int captureIndex) => new(TokenKind.CaptureEnd, captureIndex, default, Quantifier.None);

        public static Token CreatePositionCapture(int captureIndex) => new(TokenKind.PositionCapture, captureIndex, default, Quantifier.None);

        public static Token CreateAtom(PatternAtom atom, Quantifier quantifier) => new(TokenKind.Atom, -1, atom, quantifier);
    }

    private enum TokenKind
    {
        CaptureStart,
        CaptureEnd,
        PositionCapture,
        Atom
    }

    private enum Quantifier
    {
        None,
        Optional,
        ZeroOrMore,
        OneOrMore,
        ZeroOrMoreMinimal
    }

    private enum PatternAtomKind
    {
        Literal,
        Any,
        Class,
        Set,
        Frontier,
        Balanced,
        BackReference
    }

    private sealed class ByteSet
    {
        private readonly List<(byte Start, byte End)> _ranges = [];
        private readonly List<byte> _literals = [];
        private readonly List<byte> _classes = [];

        public ByteSet(bool negate)
        {
            Negate = negate;
        }

        public bool Negate { get; }

        public void AddLiteral(byte value)
        {
            _literals.Add(value);
        }

        public void AddRange(byte start, byte end)
        {
            _ranges.Add((start, end));
        }

        public void AddClass(byte value)
        {
            _classes.Add(value);
        }

        public bool Contains(byte value)
        {
            var result = _literals.Contains(value) ||
                         _ranges.Any(range => value >= range.Start && value <= range.End) ||
                         _classes.Any(@class => MatchesCharacterClass(value, @class));
            return Negate ? !result : result;
        }
    }
}

internal sealed class LuaPatternException : Exception
{
    private LuaPatternException(string message)
        : base(message)
    {
    }

    public static LuaPatternException Malformed(string message)
    {
        return new LuaPatternException(message);
    }
}
