using Lua.Runtime.Values;

namespace Lua.Runtime.Objects;

public sealed class LuaUserData : IMetatableOwner
{
    private static readonly object RegistrySync = new();
    private static readonly List<WeakReference<LuaUserData>> RegisteredUserData = [];
    private static readonly List<LuaUserData> PendingFinalizationUserData = [];
    private readonly LuaValue[] _userValues;
    private bool _isMarkedForFinalization;
    private bool _hasFinalizerRun;

    public LuaUserData(object? value = null, int userValueCount = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(userValueCount);
        Value = value;
        _userValues = new LuaValue[userValueCount];

        lock (RegistrySync)
        {
            RegisteredUserData.Add(new WeakReference<LuaUserData>(this));
        }
    }

    public object? Value { get; }

    public LuaTable? Metatable { get; private set; }

    public int UserValueCount => _userValues.Length;

    internal bool IsMarkedForFinalization => _isMarkedForFinalization;

    internal bool HasFinalizerRun => _hasFinalizerRun;

    internal LuaValue AsValue() => LuaValue.FromUserData(this);

    internal static List<LuaUserData> GetRegisteredUserDataSnapshot()
    {
        lock (RegistrySync)
        {
            var snapshot = new List<LuaUserData>(RegisteredUserData.Count);
            for (var index = RegisteredUserData.Count - 1; index >= 0; index--)
            {
                if (!RegisteredUserData[index].TryGetTarget(out var userdata))
                {
                    RegisteredUserData.RemoveAt(index);
                    continue;
                }

                snapshot.Add(userdata);
            }

            snapshot.Reverse();
            return snapshot;
        }
    }

    public void SetMetatable(LuaTable? metatable)
    {
        Metatable = metatable;
        TrackFinalizerRegistration();
    }

    internal void VisitStrongReferences(Action<LuaValue> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        if (Metatable is not null)
        {
            visitor(LuaValue.FromTable(Metatable));
        }

        for (var slot = 1; slot <= _userValues.Length; slot++)
        {
            var value = _userValues[slot - 1];
            if (!value.IsNil)
            {
                visitor(value);
            }
        }
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
            PendingFinalizationUserData.Remove(this);
        }
    }

    public bool TryGetUserValue(int slot, out LuaValue value)
    {
        if ((uint)(slot - 1) >= (uint)_userValues.Length)
        {
            value = LuaValue.Nil;
            return false;
        }

        value = _userValues[slot - 1];
        return true;
    }

    public bool TrySetUserValue(int slot, LuaValue value)
    {
        if ((uint)(slot - 1) >= (uint)_userValues.Length)
        {
            return false;
        }

        _userValues[slot - 1] = value;
        return true;
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
            PendingFinalizationUserData.Add(this);
        }
    }
}
