using Lua.Runtime.Execution;
using Lua.Runtime.Values;

namespace Lua.Runtime.Objects;

public sealed class LuaTable : IMetatableOwner
{
    private static readonly object RegistrySync = new();
    private static readonly List<WeakReference<LuaTable>> RegisteredTables = [];
    private static readonly List<LuaTable> PendingFinalizationTables = [];
    private readonly Dictionary<LuaValue, TableEntry> _strongKeyEntries;
    private readonly List<TableEntry> _entriesInOrder;
    private WeakMode _weakMode;
    private bool _isMarkedForFinalization;
    private bool _hasFinalizerRun;

    public LuaTable(string? debugName = null, int arrayCapacity = 0, int hashCapacity = 0)
    {
        DebugName = debugName;
        _strongKeyEntries = new Dictionary<LuaValue, TableEntry>(Math.Max(arrayCapacity, 0) + Math.Max(hashCapacity, 0));
        _entriesInOrder = [];

        lock (RegistrySync)
        {
            RegisteredTables.Add(new WeakReference<LuaTable>(this));
        }
    }

    public string? DebugName { get; }

    public LuaTable? Metatable { get; private set; }

    internal bool IsMarkedForFinalization => _isMarkedForFinalization;

    internal bool HasFinalizerRun => _hasFinalizerRun;

    internal LuaValue AsValue() => LuaValue.FromTable(this);

    public static void CleanupWeakEntries()
    {
        lock (RegistrySync)
        {
            for (var index = RegisteredTables.Count - 1; index >= 0; index--)
            {
                if (!RegisteredTables[index].TryGetTarget(out var table))
                {
                    RegisteredTables.RemoveAt(index);
                    continue;
                }

                table.RefreshEntries();
            }
        }
    }

    internal static List<LuaTable> GetRegisteredTablesSnapshot()
    {
        lock (RegistrySync)
        {
            var snapshot = new List<LuaTable>(RegisteredTables.Count);
            for (var index = RegisteredTables.Count - 1; index >= 0; index--)
            {
                if (!RegisteredTables[index].TryGetTarget(out var table))
                {
                    RegisteredTables.RemoveAt(index);
                    continue;
                }

                table.RefreshEntries();
                snapshot.Add(table);
            }

            snapshot.Reverse();
            return snapshot;
        }
    }

    internal static bool HasPendingFinalizers()
    {
        lock (RegistrySync)
        {
            for (var index = PendingFinalizationTables.Count - 1; index >= 0; index--)
            {
                if (PendingFinalizationTables[index].HasFinalizerRun)
                {
                    PendingFinalizationTables.RemoveAt(index);
                }
            }

            return PendingFinalizationTables.Count != 0;
        }
    }

    internal void VisitStrongReferences(Action<LuaValue> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        RefreshEntries();

        if (Metatable is not null)
        {
            visitor(LuaValue.FromTable(Metatable));
        }

        foreach (var entry in _entriesInOrder)
        {
            if (!entry.TryGetKey(out var key) ||
                !entry.TryGetValue(out var value))
            {
                continue;
            }

            if (!entry.HasWeakKey)
            {
                visitor(key);
            }

            if (!entry.HasWeakKey && !entry.HasWeakValue)
            {
                visitor(value);
            }
        }
    }

    internal void VisitLiveValues(Action<LuaValue> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        RefreshEntries();

        if (Metatable is not null)
        {
            visitor(LuaValue.FromTable(Metatable));
        }

        foreach (var entry in _entriesInOrder)
        {
            if (entry.TryGetKey(out var key))
            {
                visitor(key);
            }

            if (entry.TryGetValue(out var value))
            {
                visitor(value);
            }
        }
    }

    internal bool PropagateEphemeronValues(HashSet<object> reachableObjects, Queue<object> pendingObjects)
    {
        ArgumentNullException.ThrowIfNull(reachableObjects);
        ArgumentNullException.ThrowIfNull(pendingObjects);

        RefreshEntries();
        if (!_weakMode.HasFlag(WeakMode.Keys) || _weakMode.HasFlag(WeakMode.Values))
        {
            return false;
        }

        var changed = false;
        foreach (var entry in _entriesInOrder)
        {
            if (!entry.HasWeakKey ||
                !entry.IsWeakKeyReachable(reachableObjects) ||
                !entry.TryGetValue(out var value))
            {
                continue;
            }

            changed |= EnqueueCollectableValue(value, reachableObjects, pendingObjects);
        }

        return changed;
    }

    internal void ClearWeakValues(HashSet<object> reachableObjects)
    {
        ArgumentNullException.ThrowIfNull(reachableObjects);
        RefreshEntries();

        for (var index = _entriesInOrder.Count - 1; index >= 0; index--)
        {
            var entry = _entriesInOrder[index];
            if (!entry.HasWeakValue || entry.IsWeakValueReachable(reachableObjects))
            {
                continue;
            }

            RemoveEntryAt(index, entry);
        }
    }

    internal void ClearWeakKeys(HashSet<object> reachableObjects, HashSet<object> preservedKeys)
    {
        ArgumentNullException.ThrowIfNull(reachableObjects);
        ArgumentNullException.ThrowIfNull(preservedKeys);
        RefreshEntries();

        for (var index = _entriesInOrder.Count - 1; index >= 0; index--)
        {
            var entry = _entriesInOrder[index];
            if (!entry.HasWeakKey ||
                entry.IsWeakKeyReachable(reachableObjects) ||
                entry.HasWeakKeyTarget(preservedKeys))
            {
                continue;
            }

            RemoveEntryAt(index, entry);
        }
    }

    internal void SweepWeakEntries(HashSet<object> reachableObjects)
    {
        ArgumentNullException.ThrowIfNull(reachableObjects);
        RefreshEntries();

        for (var index = _entriesInOrder.Count - 1; index >= 0; index--)
        {
            var entry = _entriesInOrder[index];
            var removeEntry =
                (entry.HasWeakKey && !entry.IsWeakKeyReachable(reachableObjects)) ||
                (entry.HasWeakValue && !entry.IsWeakValueReachable(reachableObjects));
            if (!removeEntry)
            {
                continue;
            }

            if (!entry.HasWeakKey && entry.TryGetKey(out var strongKey))
            {
                _strongKeyEntries.Remove(strongKey);
            }

            _entriesInOrder.RemoveAt(index);
        }
    }

    public LuaValue GetValue(LuaValue key)
    {
        return TryGetValue(key, out var value) ? value : LuaValue.Nil;
    }

    public bool TryGetValue(LuaValue key, out LuaValue value)
    {
        RefreshEntries();
        if (key.IsNil)
        {
            value = LuaValue.Nil;
            return false;
        }

        var normalizedKey = NormalizeKey(key);
        if (_strongKeyEntries.TryGetValue(normalizedKey, out var strongEntry))
        {
            if (strongEntry.TryGetValue(out value))
            {
                return true;
            }

            RemoveEntry(normalizedKey);
        }

        if (!ShouldUseWeakKeyStorage(normalizedKey))
        {
            value = LuaValue.Nil;
            return false;
        }

        foreach (var entry in _entriesInOrder)
        {
            if (!entry.HasWeakKey ||
                !entry.TryGetKey(out var entryKey) ||
                entryKey != normalizedKey)
            {
                continue;
            }

            if (entry.TryGetValue(out value))
            {
                return true;
            }

            RemoveEntry(normalizedKey);
            break;
        }

        value = LuaValue.Nil;
        return false;
    }

    public void SetValue(LuaValue key, LuaValue value)
    {
        RefreshEntries();
        var normalizedKey = NormalizeKey(key);
        if (value.IsNil)
        {
            RemoveEntry(normalizedKey);
            return;
        }

        if (TryFindEntry(normalizedKey, out var existingEntry))
        {
            existingEntry.SetValue(value, ShouldUseWeakValueStorage(value));
            return;
        }

        var entry = TableEntry.Create(
            normalizedKey,
            value,
            useWeakKey: ShouldUseWeakKeyStorage(normalizedKey),
            useWeakValue: ShouldUseWeakValueStorage(value));
        if (!entry.HasWeakKey)
        {
            _strongKeyEntries[normalizedKey] = entry;
        }

        _entriesInOrder.Add(entry);
    }

    public void SetMetatable(LuaTable? metatable)
    {
        Metatable = metatable;
        TrackFinalizerRegistration();
        RefreshEntries();
    }

    internal void MarkFinalizerRun()
    {
        if (_hasFinalizerRun)
        {
            return;
        }

        _hasFinalizerRun = true;
        lock (RegistrySync)
        {
            PendingFinalizationTables.Remove(this);
        }
    }

    internal int GetApproximateMemorySize()
    {
        RefreshEntries();
        return 80 + (_entriesInOrder.Count * 40);
    }

    public long GetSequenceLength()
    {
        RefreshEntries();
        long length = 0;
        while (TryGetValue(LuaValue.FromInteger(length + 1), out var value) && !value.IsNil)
        {
            length += 1;
        }

        return length;
    }

    public bool TryGetNextEntry(LuaValue currentKey, out LuaValue nextKey, out LuaValue nextValue)
    {
        RefreshEntries();
        var liveEntries = GetLiveEntries();
        if (currentKey.IsNil)
        {
            if (liveEntries.Count == 0)
            {
                nextKey = LuaValue.Nil;
                nextValue = LuaValue.Nil;
                return false;
            }

            nextKey = liveEntries[0].Key;
            nextValue = liveEntries[0].Value;
            return true;
        }

        var normalizedKey = NormalizeNextKey(currentKey);
        var currentIndex = liveEntries.FindIndex(entry => entry.Key == normalizedKey);
        if (currentIndex < 0)
        {
            throw new LuaRuntimeException(LuaValue.FromString("invalid key to 'next'"));
        }

        var nextIndex = currentIndex + 1;
        if (nextIndex >= liveEntries.Count)
        {
            nextKey = LuaValue.Nil;
            nextValue = LuaValue.Nil;
            return false;
        }

        nextKey = liveEntries[nextIndex].Key;
        nextValue = liveEntries[nextIndex].Value;
        return true;
    }

    private void RefreshEntries()
    {
        var newWeakMode = GetWeakMode();
        if (newWeakMode != _weakMode)
        {
            RebuildEntries(newWeakMode);
            _weakMode = newWeakMode;
            return;
        }

        PurgeCollectedEntries();
    }

    private WeakMode GetWeakMode()
    {
        // Use a direct lookup that does NOT call RefreshEntries to avoid infinite recursion
        // when a metatable chain refers back to this table (or forms a cycle).
        if (Metatable is null ||
            !Metatable.TryGetValueRaw(LuaValue.FromString("__mode"), out var modeValue) ||
            modeValue.Kind != LuaValueKind.String)
        {
            return WeakMode.None;
        }

        var modeText = modeValue.AsString();
        var mode = WeakMode.None;
        if (modeText.Contains('k'))
        {
            mode |= WeakMode.Keys;
        }

        if (modeText.Contains('v'))
        {
            mode |= WeakMode.Values;
        }

        return mode;
    }

    /// <summary>
    /// Raw dictionary lookup without triggering RefreshEntries. Used by GetWeakMode and
    /// other internal paths that must avoid recursion through the weak-mode refresh.
    /// </summary>
    private bool TryGetValueRaw(LuaValue key, out LuaValue value)
    {
        if (key.IsNil)
        {
            value = LuaValue.Nil;
            return false;
        }

        var normalizedKey = NormalizeKey(key);
        if (_strongKeyEntries.TryGetValue(normalizedKey, out var strongEntry) &&
            strongEntry.TryGetValue(out value))
        {
            return true;
        }

        value = LuaValue.Nil;
        return false;
    }

    private void RebuildEntries(WeakMode newWeakMode)
    {
        var liveEntries = GetLiveEntries();
        _strongKeyEntries.Clear();
        _entriesInOrder.Clear();
        foreach (var liveEntry in liveEntries)
        {
            var entry = TableEntry.Create(
                liveEntry.Key,
                liveEntry.Value,
                useWeakKey: ShouldUseWeakKeyStorage(liveEntry.Key, newWeakMode),
                useWeakValue: ShouldUseWeakValueStorage(liveEntry.Value, newWeakMode));
            if (!entry.HasWeakKey)
            {
                _strongKeyEntries[liveEntry.Key] = entry;
            }

            _entriesInOrder.Add(entry);
        }
    }

    private void PurgeCollectedEntries()
    {
        for (var index = _entriesInOrder.Count - 1; index >= 0; index--)
        {
            var entry = _entriesInOrder[index];
            if (entry.IsAlive())
            {
                continue;
            }

            RemoveEntryAt(index, entry);
        }
    }

    private bool TryFindEntry(LuaValue normalizedKey, out TableEntry entry)
    {
        if (_strongKeyEntries.TryGetValue(normalizedKey, out entry!))
        {
            return true;
        }

        if (!ShouldUseWeakKeyStorage(normalizedKey))
        {
            entry = null!;
            return false;
        }

        foreach (var candidate in _entriesInOrder)
        {
            if (!candidate.HasWeakKey ||
                !candidate.TryGetKey(out var candidateKey) ||
                candidateKey != normalizedKey)
            {
                continue;
            }

            entry = candidate;
            return true;
        }

        entry = null!;
        return false;
    }

    private void RemoveEntry(LuaValue normalizedKey)
    {
        if (_strongKeyEntries.Remove(normalizedKey, out var strongEntry))
        {
            _entriesInOrder.Remove(strongEntry);
            return;
        }

        if (!ShouldUseWeakKeyStorage(normalizedKey))
        {
            return;
        }

        for (var index = 0; index < _entriesInOrder.Count; index++)
        {
            var entry = _entriesInOrder[index];
            if (!entry.HasWeakKey ||
                !entry.TryGetKey(out var entryKey) ||
                entryKey != normalizedKey)
            {
                continue;
            }

            _entriesInOrder.RemoveAt(index);
            return;
        }
    }

    private void RemoveEntryAt(int index, TableEntry entry)
    {
        if (!entry.HasWeakKey && entry.TryGetKey(out var strongKey))
        {
            _strongKeyEntries.Remove(strongKey);
        }

        _entriesInOrder.RemoveAt(index);
    }

    private List<KeyValuePair<LuaValue, LuaValue>> GetLiveEntries()
    {
        var liveEntries = new List<KeyValuePair<LuaValue, LuaValue>>(_entriesInOrder.Count);
        foreach (var entry in _entriesInOrder)
        {
            if (!entry.TryGetKey(out var key) ||
                !entry.TryGetValue(out var value))
            {
                continue;
            }

            liveEntries.Add(new KeyValuePair<LuaValue, LuaValue>(key, value));
        }

        return liveEntries;
    }

    private bool ShouldUseWeakKeyStorage(LuaValue key) => ShouldUseWeakKeyStorage(key, _weakMode);

    private static bool ShouldUseWeakKeyStorage(LuaValue key, WeakMode weakMode)
    {
        return weakMode.HasFlag(WeakMode.Keys) && IsWeakReferenceCandidate(key);
    }

    private bool ShouldUseWeakValueStorage(LuaValue value) => ShouldUseWeakValueStorage(value, _weakMode);

    private static bool ShouldUseWeakValueStorage(LuaValue value, WeakMode weakMode)
    {
        return weakMode.HasFlag(WeakMode.Values) && IsWeakReferenceCandidate(value);
    }

    private static bool IsWeakReferenceCandidate(LuaValue value)
    {
        return value.Kind is LuaValueKind.Table or
            LuaValueKind.Function or
            LuaValueKind.Thread or
            LuaValueKind.UserData;
    }

    private void TrackFinalizerRegistration()
    {
        if (_isMarkedForFinalization ||
            Metatable is null ||
            !Metatable.TryGetValue(LuaValue.FromString("__gc"), out var finalizerValue) ||
            finalizerValue.IsNil)
        {
            return;
        }

        _isMarkedForFinalization = true;
        lock (RegistrySync)
        {
            PendingFinalizationTables.Add(this);
        }
    }

    private static bool EnqueueCollectableValue(
        LuaValue value,
        HashSet<object> reachableObjects,
        Queue<object> pendingObjects)
    {
        object? target = value.Kind switch
        {
            LuaValueKind.Table => value.AsTable(),
            LuaValueKind.Function => value.AsFunction(),
            LuaValueKind.Thread => value.AsThread(),
            LuaValueKind.UserData => value.AsUserData(),
            _ => null
        };
        if (target is null || !reachableObjects.Add(target))
        {
            return false;
        }

        pendingObjects.Enqueue(target);
        return true;
    }

    private static LuaValue NormalizeKey(LuaValue key)
    {
        if (key.IsNil)
        {
            throw new LuaRuntimeException(LuaValue.FromString("table index is nil"));
        }

        if (key.Kind == LuaValueKind.Float)
        {
            if (double.IsNaN(key.AsFloat()))
            {
                throw new LuaRuntimeException(LuaValue.FromString("table index is NaN"));
            }

            if (LuaValueHelper.TryGetInteger(key, out var integer))
            {
                return LuaValue.FromInteger(integer);
            }
        }

        return key;
    }

    private static LuaValue NormalizeNextKey(LuaValue key)
    {
        if (key.Kind == LuaValueKind.Float)
        {
            var number = key.AsFloat();
            if (double.IsNaN(number))
            {
                throw new LuaRuntimeException(LuaValue.FromString("invalid key to 'next'"));
            }

            if (double.IsFinite(number) &&
                number >= long.MinValue &&
                number <= long.MaxValue &&
                Math.Truncate(number) == number)
            {
                return LuaValue.FromInteger((long)number);
            }
        }

        return key;
    }

    [Flags]
    private enum WeakMode
    {
        None = 0,
        Keys = 1,
        Values = 2
    }

    private sealed class TableEntry
    {
        private readonly LuaValue _strongKey;
        private readonly WeakLuaReference? _weakKey;
        private LuaValue _strongValue;
        private WeakLuaReference? _weakValue;

        private TableEntry(
            LuaValue strongKey,
            WeakLuaReference? weakKey,
            LuaValue strongValue,
            WeakLuaReference? weakValue)
        {
            _strongKey = strongKey;
            _weakKey = weakKey;
            _strongValue = strongValue;
            _weakValue = weakValue;
        }

        public bool HasWeakKey => _weakKey is not null;

        public bool HasWeakValue => _weakValue is not null;

        public static TableEntry Create(LuaValue key, LuaValue value, bool useWeakKey, bool useWeakValue)
        {
            return new TableEntry(
                strongKey: useWeakKey ? LuaValue.Nil : key,
                weakKey: useWeakKey ? WeakLuaReference.Create(key) : null,
                strongValue: useWeakValue ? LuaValue.Nil : value,
                weakValue: useWeakValue ? WeakLuaReference.Create(value) : null);
        }

        public bool TryGetKey(out LuaValue key)
        {
            if (_weakKey is null)
            {
                key = _strongKey;
                return true;
            }

            return _weakKey.TryGetValue(out key);
        }

        public bool TryGetValue(out LuaValue value)
        {
            if (_weakValue is null)
            {
                value = _strongValue;
                return true;
            }

            return _weakValue.TryGetValue(out value);
        }

        public bool IsAlive()
        {
            return TryGetKey(out _) && TryGetValue(out _);
        }

        public bool IsWeakKeyReachable(HashSet<object> reachableObjects)
        {
            return _weakKey is null || _weakKey.IsReachable(reachableObjects);
        }

        public bool HasWeakKeyTarget(HashSet<object> targets)
        {
            return _weakKey is not null && _weakKey.Matches(targets);
        }

        public bool IsWeakValueReachable(HashSet<object> reachableObjects)
        {
            return _weakValue is null || _weakValue.IsReachable(reachableObjects);
        }

        public void SetValue(LuaValue value, bool useWeakValue)
        {
            _strongValue = useWeakValue ? LuaValue.Nil : value;
            _weakValue = useWeakValue ? WeakLuaReference.Create(value) : null;
        }
    }

    private sealed class WeakLuaReference
    {
        private readonly LuaValueKind _kind;
        private readonly WeakReference<object> _reference;

        private WeakLuaReference(LuaValueKind kind, object target)
        {
            _kind = kind;
            _reference = new WeakReference<object>(target);
        }

        public static WeakLuaReference Create(LuaValue value)
        {
            var target = value.Kind switch
            {
                LuaValueKind.Table => (object)value.AsTable(),
                LuaValueKind.Function => value.AsFunction(),
                LuaValueKind.Thread => value.AsThread(),
                LuaValueKind.UserData => value.AsUserData(),
                _ => throw new InvalidOperationException($"Cannot store {value.Kind} as a weak Lua reference.")
            };

            return new WeakLuaReference(value.Kind, target);
        }

        public bool TryGetValue(out LuaValue value)
        {
            if (!_reference.TryGetTarget(out var target))
            {
                value = LuaValue.Nil;
                return false;
            }

            value = _kind switch
            {
                LuaValueKind.Table => LuaValue.FromTable((LuaTable)target),
                LuaValueKind.Function => LuaValue.FromFunction((LuaClosure)target),
                LuaValueKind.Thread => LuaValue.FromThread((LuaThread)target),
                LuaValueKind.UserData => LuaValue.FromUserData((LuaUserData)target),
                _ => throw new InvalidOperationException($"Unsupported weak Lua reference kind {_kind}.")
            };

            return true;
        }

        public bool IsReachable(HashSet<object> reachableObjects)
        {
            return _reference.TryGetTarget(out var target) && reachableObjects.Contains(target);
        }

        public bool Matches(HashSet<object> targets)
        {
            return _reference.TryGetTarget(out var target) && targets.Contains(target);
        }
    }
}
