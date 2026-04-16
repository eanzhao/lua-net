using Lua.Runtime.Objects;
using Lua.Runtime.Values;
using System.Runtime;
using System.Runtime.CompilerServices;
using static Lua.Runtime.Values.LuaValueHelper;

namespace Lua.Runtime.Execution;

public sealed partial class LuaState
{
    private const int AutomaticGcInstructionThreshold = 64;
    private int _gcInstructionDebt;
    private bool _gcCollecting;

    public void MaybeRunAutomaticGarbageCollection()
    {
        if (!_gcRunning || _gcCollecting || !HasPendingAutomaticGarbageCollectionWork())
        {
            return;
        }

        _gcInstructionDebt += 1;
        if (_gcInstructionDebt < AutomaticGcInstructionThreshold)
        {
            return;
        }

        RunLuaGarbageCollection(resetStepDebt: false, runHostCollection: false);
    }

    private void RunLuaGarbageCollection(bool resetStepDebt, bool runHostCollection = true)
    {
        if (_gcCollecting)
        {
            return;
        }

        _gcCollecting = true;
        try
        {
            RunCustomGarbageCollectorCycle();

            if (runHostCollection)
            {
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                LuaTable.CleanupWeakEntries();
            }

            _gcInstructionDebt = 0;
            if (resetStepDebt)
            {
                _gcStepDebt = 0;
            }
        }
        finally
        {
            _gcCollecting = false;
        }
    }

    private bool RunGarbageCollectorStepCore(int stepSize)
    {
        if (_gcStepSize == FullGcStepSize)
        {
            RunLuaGarbageCollection(resetStepDebt: false);
            _gcStepDebt = 0;
            return true;
        }

        RunLuaGarbageCollection(resetStepDebt: false, runHostCollection: false);

        var increment = stepSize <= 0 ? 1 : stepSize;
        _gcStepDebt = checked(_gcStepDebt + increment);
        if (_gcStepDebt >= _gcStepSize)
        {
            RunLuaGarbageCollection(resetStepDebt: false);
            _gcStepDebt = 0;
            return true;
        }

        return false;
    }

    private void RunCustomGarbageCollectorCycle()
    {
        var allTables = LuaTable.GetRegisteredTablesSnapshot();
        var reachableObjects = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var pendingObjects = new Queue<object>();

        MarkGarbageCollectorRoots(reachableObjects, pendingObjects);
        TraverseGarbageCollectorReachability(allTables, reachableObjects, pendingObjects);

        foreach (var table in allTables)
        {
            table.ClearWeakValues(reachableObjects);
        }

        var finalizerCandidates = CollectFinalizerCandidates(reachableObjects);
        if (finalizerCandidates.Count != 0)
        {
            var finalizingObjects = new HashSet<object>(ReferenceEqualityComparer.Instance);
            foreach (var candidate in finalizerCandidates)
            {
                finalizingObjects.Add(candidate.Target);
                if (reachableObjects.Add(candidate.Target))
                {
                    pendingObjects.Enqueue(candidate.Target);
                }
            }

            TraverseGarbageCollectorReachability(allTables, reachableObjects, pendingObjects);
            foreach (var table in allTables)
            {
                table.ClearWeakKeys(reachableObjects, finalizingObjects);
            }

            RunFinalizers(finalizerCandidates);
            return;
        }

        var noPreservedKeys = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var table in allTables)
        {
            table.ClearWeakKeys(reachableObjects, noPreservedKeys);
        }
    }

    private void MarkGarbageCollectorRoots(
        HashSet<object> reachableObjects,
        Queue<object> pendingObjects)
    {
        EnqueueValue(LuaValue.FromTable(GlobalEnvironment), reachableObjects, pendingObjects);
        EnqueueValue(LuaValue.FromTable(StringLibrary), reachableObjects, pendingObjects);
        EnqueueValue(LuaValue.FromTable(TableLibrary), reachableObjects, pendingObjects);
        EnqueueValue(LuaValue.FromTable(MathLibrary), reachableObjects, pendingObjects);
        EnqueueValue(LuaValue.FromTable(Utf8Library), reachableObjects, pendingObjects);
        EnqueueValue(LuaValue.FromTable(CoroutineLibrary), reachableObjects, pendingObjects);
        EnqueueValue(LuaValue.FromTable(PackageLibrary), reachableObjects, pendingObjects);
        EnqueueValue(LuaValue.FromTable(OsLibrary), reachableObjects, pendingObjects);
        EnqueueValue(LuaValue.FromTable(IoLibrary), reachableObjects, pendingObjects);
        EnqueueValue(LuaValue.FromTable(DebugLibrary), reachableObjects, pendingObjects);
        EnqueueValue(LuaValue.FromTable(PackageLoaded), reachableObjects, pendingObjects);
        EnqueueValue(LuaValue.FromTable(PackagePreload), reachableObjects, pendingObjects);
        EnqueueValue(LuaValue.FromTable(PackageSearchers), reachableObjects, pendingObjects);

        foreach (var metatable in _typeMetatables.Values)
        {
            EnqueueValue(LuaValue.FromTable(metatable), reachableObjects, pendingObjects);
        }

        EnqueueThread(MainThread, reachableObjects, pendingObjects);
        if (!ReferenceEquals(CurrentThread, MainThread))
        {
            EnqueueThread(CurrentThread, reachableObjects, pendingObjects);
        }
    }

    private void TraverseGarbageCollectorReachability(
        IReadOnlyList<LuaTable> allTables,
        HashSet<object> reachableObjects,
        Queue<object> pendingObjects)
    {
        while (true)
        {
            while (pendingObjects.Count != 0)
            {
                switch (pendingObjects.Dequeue())
                {
                    case LuaTable table:
                        table.VisitStrongReferences(value => EnqueueValue(value, reachableObjects, pendingObjects));
                        break;
                    case LuaClosure closure:
                        foreach (var upvalue in closure.Upvalues)
                        {
                            EnqueueValue(upvalue.GetValue(this), reachableObjects, pendingObjects);
                        }

                        break;
                    case LuaThread thread:
                        VisitThread(thread, reachableObjects, pendingObjects);
                        break;
                    case LuaUserData userdata:
                        userdata.VisitStrongReferences(value => EnqueueValue(value, reachableObjects, pendingObjects));
                        break;
                }
            }

            var changed = false;
            foreach (var table in allTables)
            {
                if (!reachableObjects.Contains(table))
                {
                    continue;
                }

                changed |= table.PropagateEphemeronValues(reachableObjects, pendingObjects);
            }

            if (!changed && pendingObjects.Count == 0)
            {
                return;
            }
        }
    }

    private List<LuaFinalizerCandidate> CollectFinalizerCandidates(HashSet<object> reachableObjects)
    {
        var candidates = new List<LuaFinalizerCandidate>();
        foreach (var table in LuaTable.GetRegisteredTablesSnapshot())
        {
            if (table.IsMarkedForFinalization &&
                !table.HasFinalizerRun &&
                !reachableObjects.Contains(table))
            {
                candidates.Add(new LuaFinalizerCandidate(
                    table,
                    table.AsValue(),
                    () => table.Metatable?.GetValue(LuaValue.FromString("__gc")) ?? LuaValue.Nil,
                    table.MarkFinalizerRun));
            }
        }

        foreach (var userdata in LuaUserData.GetRegisteredUserDataSnapshot())
        {
            if (userdata.IsMarkedForFinalization &&
                !userdata.HasFinalizerRun &&
                !reachableObjects.Contains(userdata))
            {
                candidates.Add(new LuaFinalizerCandidate(
                    userdata,
                    userdata.AsValue(),
                    () => userdata.Metatable?.GetValue(LuaValue.FromString("__gc")) ?? LuaValue.Nil,
                    userdata.MarkFinalizerRun));
            }
        }

        return candidates;
    }

    private void RunFinalizers(IReadOnlyList<LuaFinalizerCandidate> finalizerCandidates)
    {
        for (var index = finalizerCandidates.Count - 1; index >= 0; index--)
        {
            var candidate = finalizerCandidates[index];
            candidate.MarkFinalizerRun();

            var finalizer = candidate.GetFinalizer();
            if (finalizer.IsNil)
            {
                continue;
            }

            CurrentThread.EnterNonYieldableCall();
            try
            {
                InvokeCallable(finalizer, [candidate.Value]);
            }
            catch (LuaRuntimeException ex)
            {
                EmitFinalizerWarning(ex.ErrorObject);
            }
            catch (LuaYieldException)
            {
                EmitFinalizerWarning(LuaValue.FromString("attempt to yield from __gc"));
            }
            catch (Exception ex)
            {
                EmitFinalizerWarning(LuaValue.FromString(ex.Message));
            }
            finally
            {
                CurrentThread.ExitNonYieldableCall();
            }
        }
    }

    private void EmitFinalizerWarning(LuaValue errorObject)
    {
        var message = errorObject.Kind == LuaValueKind.String
            ? errorObject.AsString()
            : FormatLuaValue(errorObject);
        EmitWarning($"error in __gc ({message})", toContinue: false);
    }

    private readonly record struct LuaFinalizerCandidate(
        object Target,
        LuaValue Value,
        Func<LuaValue> GetFinalizer,
        Action MarkFinalizerRun);

    private double EstimateLuaMemoryUsageInKilobytes()
    {
        long totalBytes = 0;
        var accountedStrings = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var closures = LuaClosure.GetRegisteredClosuresSnapshot();
        var tables = LuaTable.GetRegisteredTablesSnapshot();
        var threads = LuaThread.GetRegisteredThreadsSnapshot();
        var userDataObjects = LuaUserData.GetRegisteredUserDataSnapshot();

        foreach (var table in tables)
        {
            totalBytes += table.GetApproximateMemorySize();
            table.VisitLiveValues(AccountValue);
        }

        foreach (var userdata in userDataObjects)
        {
            totalBytes += userdata.GetApproximateMemorySize();
            userdata.VisitStrongReferences(AccountValue);
        }

        foreach (var closure in closures)
        {
            totalBytes += closure.GetApproximateMemorySize();
            closure.VisitReferencedStrings(AccountString);
            foreach (var upvalue in closure.Upvalues)
            {
                AccountValue(upvalue.GetValue(this));
            }
        }

        foreach (var thread in threads)
        {
            totalBytes += thread.GetApproximateMemorySize();
            if (thread.DebugName is not null)
            {
                AccountString(thread.DebugName);
            }

            if (!string.IsNullOrEmpty(thread.HookMask))
            {
                AccountString(thread.HookMask);
            }

            thread.VisitReferencedValues(AccountValue);
            AccountThreadStack(thread);
        }

        return totalBytes / 1024d;

        void AccountThreadStack(LuaThread thread)
        {
            foreach (var frame in thread.Frames)
            {
                if (frame.Closure.DebugName is not null)
                {
                    AccountString(frame.Closure.DebugName);
                }
            }

            foreach (var frame in thread.Frames)
            {
                var liveRegisterTop = Math.Max(frame.LiveRegisterTop, frame.RegisterTop);
                var maxRegisterCount = Math.Min(liveRegisterTop, Math.Max(0, thread.Stack.Count - frame.BaseIndex));
                for (var registerIndex = 0; registerIndex < maxRegisterCount; registerIndex++)
                {
                    AccountValue(thread.Stack[frame.BaseIndex + registerIndex]);
                }
            }
        }

        void AccountValue(LuaValue value)
        {
            if (value.Kind == LuaValueKind.String)
            {
                AccountString(value.AsString());
            }
        }

        void AccountString(string text)
        {
            if (!accountedStrings.Add(text))
            {
                return;
            }

            totalBytes += EstimateStringMemoryUsage(text);
        }
    }

    private static int EstimateStringMemoryUsage(string text)
    {
        var byteCount = LuaStringBytes.GetBytes(text).Length;
        return IntPtr.Size == 8
            ? 24 + (text.Length * 2) + byteCount
            : 12 + (text.Length * 2) + byteCount;
    }

    private static bool HasPendingAutomaticGarbageCollectionWork()
    {
        return LuaTable.HasPendingFinalizers() || LuaUserData.HasPendingFinalizers();
    }
}
