using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YARG.Core;
using YARG.Core.Chart;
using YARG.Core.Engine.Drums;

namespace YARG.Gameplay.Visuals
{
    /// <summary>
    /// The small set of conditions under which an Elite visual descriptor may render.
    /// This is a visual adapter gate, not an Elite gameplay implementation.
    /// </summary>
    public static class EliteDrumVisualGate
    {
        public static bool CanRender(EliteDrumVisualDescriptorV1 descriptor,
            Difficulty difficulty, bool codaActive)
        {
            return descriptor != null && descriptor.RulesetEligible && !descriptor.CodaExcluded &&
                descriptor.VisualIdentity != null && descriptor.VisualIdentity.Enabled &&
                difficulty != Difficulty.Beginner && !codaActive;
        }

        // Kept as a narrow compatibility seam for callers/tests that validate the visual identity
        // independently of the immutable final descriptor.
        public static bool CanRender(EliteDrumComponentVisualDescriptor descriptor,
            Difficulty difficulty, int rulesetVersion, bool codaActive)
        {
            return descriptor != null && descriptor.Enabled &&
                difficulty != Difficulty.Beginner && rulesetVersion > 0 && !codaActive;
        }
    }

    /// <summary>Resolved appearance for a final output pad on the active drums highway.</summary>
    public readonly struct EliteDrumVisualAppearance
    {
        public EliteDrumVisualAppearance(int position, Color color)
        {
            Position = position;
            Color = color;
        }

        public int Position { get; }
        public Color Color { get; }
    }

    /// <summary>
    /// Adapts Core's immutable descriptor output to the existing LaneElement prefab and Pool.
    /// It deliberately does not use TrackPlayer's adjacency combiner: each descriptor owns one
    /// pool item, and a failed allocation remains queued for an explicit retry.
    /// </summary>
    public sealed class EliteDrumVisualAdapter
    {
        private readonly Pool _lanePool;
        private readonly Instrument _instrument;
        private readonly int _laneCount;
        private readonly Func<EliteDrumFinalPadIdentity, EliteDrumVisualAppearance> _appearanceResolver;
        private readonly List<LaneElement> _ownedLanes = new();
        private readonly Dictionary<LaneElement, EliteDrumVisualDescriptorV1> _descriptorByLane = new();
        private readonly Queue<EliteDrumVisualDescriptorV1> _pending = new();
        private readonly HashSet<string> _trackedGameplayIds = new();
        private readonly HashSet<string> _pendingGameplayIds = new();

        public EliteDrumVisualAdapter(Pool lanePool, Instrument instrument, int laneCount,
            Func<EliteDrumFinalPadIdentity, EliteDrumVisualAppearance> appearanceResolver)
        {
            _lanePool = lanePool ?? throw new ArgumentNullException(nameof(lanePool));
            if (laneCount <= 0) throw new ArgumentOutOfRangeException(nameof(laneCount));
            _appearanceResolver = appearanceResolver ?? throw new ArgumentNullException(nameof(appearanceResolver));
            _instrument = instrument;
            _laneCount = laneCount;
            LaneElement.DefineLaneScale(instrument, laneCount);
        }

        public IReadOnlyList<LaneElement> OwnedLanes => _ownedLanes;
        public int PendingCount => _pending.Count;

        /// <summary>
        /// Whether this pool item currently carries adapter-owned descriptor state. Native
        /// lane logic (TrackPlayer's adjacency combiner) must never extend, combine into,
        /// or otherwise mutate these lanes: each descriptor owns one pool item spanning
        /// exactly its FirstPhysicalEventTime..LastPhysicalEventTime interval.
        /// </summary>
        public bool OwnsLane(LaneElement lane) => lane != null && _descriptorByLane.ContainsKey(lane);

        public bool TrySpawn(EliteDrumVisualDescriptorV1 descriptor, Difficulty difficulty, bool codaActive)
        {
            ReconcilePoolOwnership();
            if (!IsPermanentlyEligible(descriptor, difficulty) ||
                _trackedGameplayIds.Contains(descriptor.GameplayId) ||
                _pendingGameplayIds.Contains(descriptor.GameplayId))
            {
                return false;
            }

            _trackedGameplayIds.Add(descriptor.GameplayId);
            if (codaActive || !TryTake(out var lane))
            {
                _trackedGameplayIds.Remove(descriptor.GameplayId);
                QueuePending(descriptor);
                return false;
            }

            ConfigureAndEnable(lane, descriptor);
            _descriptorByLane[lane] = descriptor;
            return true;
        }

        /// <summary>
        /// Releases adapter lanes before native lanes are allocated. Released descriptors are queued
        /// again, while lanes no longer present in Pool.AllSpawned are forgotten without returning them.
        /// </summary>
        public int ReleaseForNative(int count)
        {
            ReconcilePoolOwnership();
            int released = 0;
            for (int index = _ownedLanes.Count - 1; index >= 0 && released < count; index--)
            {
                var lane = _ownedLanes[index];
                if (!_descriptorByLane.TryGetValue(lane, out var descriptor))
                {
                    RemoveOwnership(lane);
                    continue;
                }

                RemoveOwnership(lane);
                QueuePending(descriptor);
                _lanePool.Return(lane);
                released++;
            }

            return released;
        }

        public void ForgetIfReused(LaneElement lane)
        {
            if (lane == null || !_descriptorByLane.TryGetValue(lane, out var descriptor))
            {
                return;
            }

            if (_pendingGameplayIds.Add(descriptor.GameplayId))
            {
                _pending.Enqueue(descriptor);
            }
            RemoveOwnership(lane);
        }

        /// <summary>Retries descriptors that previously exhausted the shared lane pool.</summary>
        public int RetryPending(Difficulty difficulty, bool codaActive)
        {
            int spawned = 0;
            int count = _pending.Count;
            for (int i = 0; i < count; i++)
            {
                var descriptor = _pending.Dequeue();
                _pendingGameplayIds.Remove(descriptor.GameplayId);
                if (TrySpawn(descriptor, difficulty, codaActive))
                {
                    spawned++;
                }
                else if (codaActive && IsPermanentlyEligible(descriptor, difficulty))
                {
                    // Coda is transient. TrySpawn has already restored this descriptor to the
                    // pending queue; do not turn a temporary visual gate into permanent loss.
                    continue;
                }
            }

            return spawned;
        }

        /// <summary>Returns only lanes allocated by this adapter and clears pending work.</summary>
        public void Reset()
        {
            ReconcilePoolOwnership();
            foreach (var lane in _ownedLanes.ToList())
            {
                if (lane != null && _lanePool.AllSpawned.Contains(lane))
                {
                    _lanePool.Return(lane);
                }
            }

            _ownedLanes.Clear();
            _descriptorByLane.Clear();
            _pending.Clear();
            _trackedGameplayIds.Clear();
            _pendingGameplayIds.Clear();
        }

        private void ReconcilePoolOwnership()
        {
            for (int index = _ownedLanes.Count - 1; index >= 0; index--)
            {
                var lane = _ownedLanes[index];
                if (lane == null || !_lanePool.AllSpawned.Contains(lane))
                {
                    // LaneElement auto-expiry and ordinary Pool.Return are terminal for this
                    // descriptor. Only ReleaseForNative/ForgetIfReused explicitly queue work.
                    RemoveOwnership(lane);
                }
            }
        }

        private bool IsPermanentlyEligible(EliteDrumVisualDescriptorV1 descriptor, Difficulty difficulty) =>
            descriptor != null && descriptor.FinalPad.Pad != 0 &&
            EliteDrumVisualGate.CanRender(descriptor, difficulty, false);

        private void QueuePending(EliteDrumVisualDescriptorV1 descriptor)
        {
            if (descriptor != null && _pendingGameplayIds.Add(descriptor.GameplayId))
            {
                _pending.Enqueue(descriptor);
            }
        }

        private void RemoveOwnership(LaneElement lane)
        {
            if (lane != null && _descriptorByLane.TryGetValue(lane, out var descriptor))
            {
                _trackedGameplayIds.Remove(descriptor.GameplayId);
                _descriptorByLane.Remove(lane);
            }

            _ownedLanes.Remove(lane);
        }

        private bool TryTake(out LaneElement lane)
        {
            lane = null;
            var poolable = _lanePool.TakeWithoutEnabling();
            if (poolable == null)
            {
                return false;
            }

            lane = poolable as LaneElement;
            if (lane == null)
            {
                // Foreign IPoolable (misconfigured pool): fail safely. Leave the foreign item
                // completely unchanged - do not Return() it (that would run DisableIntoPool on
                // an item this adapter does not own and push it back onto the free stack, where
                // it would be handed out again and poison every later take, including native
                // LaneElement takes), and do not throw into the gameplay loop. Treat this as
                // pool exhaustion so TrySpawn queues the descriptor as pending. Adapter
                // ownership/pending state is untouched: nothing was registered before this
                // check, and the parked foreign item simply stays out of the free stack.
                lane = null;
                return false;
            }

            _ownedLanes.Add(lane);
            return true;
        }

        private void ConfigureAndEnable(LaneElement lane, EliteDrumVisualDescriptorV1 descriptor)
        {
            var appearance = _appearanceResolver(descriptor.FinalPad);

            lane.SetTimeRange(descriptor.FirstPhysicalEventTime, descriptor.LastPhysicalEventTime);
            lane.SetIndexRange(descriptor.FinalPad.Pad, descriptor.FinalPad.Pad);
            lane.SetAppearance(_instrument, descriptor.FinalPad.Pad, appearance.Position, _laneCount, appearance.Color);
            lane.EnableFromPool();
        }
    }

    /// <summary>Factory kept deliberately narrow so no Elite direct runtime is introduced.</summary>
    public static class EliteDrumVisualAdapterFactory
    {
        public static EliteDrumVisualAdapter Create(Pool lanePool, Instrument outputInstrument,
            int laneCount,
            Func<EliteDrumFinalPadIdentity, EliteDrumVisualAppearance> appearanceResolver)
        {
            return new EliteDrumVisualAdapter(lanePool, outputInstrument, laneCount, appearanceResolver);
        }
    }
}
