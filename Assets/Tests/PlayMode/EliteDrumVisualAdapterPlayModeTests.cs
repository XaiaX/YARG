using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Serialization;
using NUnit.Framework;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.TestTools;
using YARG.Core;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Drums;
using YARG.Core.Game;
using YARG.Core.Input;

namespace YARG.Tests.PlayMode
{
    public sealed class EliteDrumVisualAdapterPlayModeTests
    {
        private GameObject _root;
        private Component _pool;
        private object _adapter;
        private Type _poolType;
        private Type _adapterType;
        private Type _descriptorType;
        private Type _difficultyType;
        private Type _instrumentType;
        private Type _resolverType;
        private Type _finalPadType;
        private Type _appearanceType;
        private Type _drumsPlayerType;
        private int _resolvedPosition;
        private Color _resolvedColor;
        private int _resolvedPad;
        private UnityEngine.Object _harnessGameManager;
        private UnityEngine.Object _harnessPlayer;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _poolType = ProductionType("YARG.Gameplay.Pool");
            _adapterType = ProductionType("YARG.Gameplay.Visuals.EliteDrumVisualAdapter");
            _descriptorType = ProductionType("YARG.Core.Engine.Drums.EliteDrumVisualDescriptorV1");
            _difficultyType = ProductionType("YARG.Core.Difficulty");
            _instrumentType = ProductionType("YARG.Core.Instrument");
            _finalPadType = ProductionType("YARG.Core.Chart.EliteDrumFinalPadIdentity");
            _appearanceType = ProductionType("YARG.Gameplay.Visuals.EliteDrumVisualAppearance");
            _resolverType = typeof(Func<,>).MakeGenericType(_finalPadType, _appearanceType);
            _drumsPlayerType = ProductionType("YARG.Gameplay.Player.DrumsPlayer");

            _root = new GameObject("EliteDrumVisualAdapterTests");
            // Defer pool Awake until the limits and the production prefab are configured:
            // an Awake on an active root prewarms with serialized defaults (300/500) and a
            // null prefab, logging an Instantiate(null) exception that fails every test.
            _root.SetActive(false);
            _pool = _root.AddComponent(_poolType);
            SetPoolLimits(_pool, 0, 1);
#if UNITY_EDITOR
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Gameplay/Visual/TrackElements/Lane.prefab");
#else
            var prefab = Resources.Load<GameObject>("Lane");
#endif
            Assert.That(prefab, Is.Not.Null, "The PlayMode harness requires the production Lane.prefab.");
            _poolType.GetMethod("SetPrefabAndReset").Invoke(_pool, new object[] { prefab });

            // Seed the static lane-scale cache for the fixture instrument: a fresh batchmode
            // session has no prior gameplay to define it, and lane rendering from an
            // undefined (or zero-subdivision) scale logs invalid localScale errors.
            GetLaneElementType()
                .GetMethod("DefineLaneScale", BindingFlags.Static | BindingFlags.Public)
                .Invoke(null, new object[] { Enum.Parse(_instrumentType, "FourLaneDrums"), 4, true });

            // The pool root stays inactive for the whole fixture. The production Lane.prefab
            // root is active, so creating lanes under an active root runs
            // GameplayBehaviour.Awake during Pool.CreateNew; without a gameplay scene that
            // logs a warning and defers Destroy, discarding the lane at the first frame
            // boundary. Under an inactive root the pooled lanes stay dormant, exactly like
            // production lanes waiting in the pool, and the harness supplies the
            // gameplay-scene context (GameManager/Player visual timing) that the real lane
            // configuration paths read.
            var instrument = Enum.Parse(_instrumentType, "FourLaneDrums");
            _adapter = _adapterType.GetConstructor(new[] { _poolType, _instrumentType, typeof(int), _resolverType })
                .Invoke(new object[] { _pool, instrument, 4, CreateAppearanceResolver() });

            // Fixture-wide harness context, injected into every lane the tests prewarm or
            // wire: a GameManager holding the timing surfaces production reads
            // (SongRunner.VisualTime/SongSpeed, EngineManager, BeatEventHandler) and a wired
            // DrumsPlayer whose profile supplies NoteSpeed. Tests that arrange the pool free
            // stack by hand create their own context via these same helpers.
            _harnessGameManager = (UnityEngine.Object) CreateInactiveGameManager();
            _harnessPlayer = (UnityEngine.Object) CreateWiredDrumsPlayer(
                "EliteAdapterTimingHarnessPlayer", activate: false);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            UnityEngine.Object.Destroy(_root);
            if (_harnessPlayer is not null)
            {
                UnityEngine.Object.Destroy(_harnessPlayer);
            }

            if (_harnessGameManager is not null)
            {
                UnityEngine.Object.Destroy(_harnessGameManager);
            }

            yield return null;
        }

        private object AppearanceResolver(object finalPad)
        {
            _resolvedPad = (int)finalPad.GetType().GetProperty("Pad").GetValue(finalPad);
            return Activator.CreateInstance(_appearanceType, _resolvedPosition, _resolvedColor);
        }

        private Delegate CreateAppearanceResolver()
        {
            var finalPad = Expression.Parameter(_finalPadType, "finalPad");
            var resolver = Expression.Call(Expression.Constant(this),
                GetType().GetMethod(nameof(AppearanceResolver), BindingFlags.Instance | BindingFlags.NonPublic),
                Expression.Convert(finalPad, typeof(object)));
            return Expression.Lambda(_resolverType, Expression.Convert(resolver, _appearanceType), finalPad).Compile();
        }

        [UnityTest]
        public IEnumerator PoolExhaustion_QueuesAndRetrySpawnsAfterReturn()
        {
            PrewarmWiredLanes(1);
            var expert = Enum.Parse(_difficultyType, "Expert");
            var first = Descriptor("first", 0d, true, false);
            Assert.That(Invoke<bool>("TrySpawn", first, expert, false), Is.True);
            Assert.That(Invoke<bool>("TrySpawn", Descriptor("second", 1d, true, false), expert, false), Is.False);
            Assert.That(GetProperty<int>("PendingCount"), Is.EqualTo(1));

            var owned = (IList)GetProperty<object>("OwnedLanes");
            _poolType.GetMethod("Return").Invoke(_pool, new[] { owned[0] });
            Assert.That(Invoke<int>("RetryPending", expert, false), Is.EqualTo(1));
            Assert.That(GetProperty<int>("PendingCount"), Is.Zero);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CodaPending_PreservesDescriptorUntilCodaEnds()
        {
            PrewarmWiredLanes(1);
            var expert = Enum.Parse(_difficultyType, "Expert");
            var descriptor = Descriptor("coda-pending", 0d, true, false);

            Assert.That(Invoke<bool>("TrySpawn", descriptor, expert, true), Is.False);
            Assert.That(GetProperty<int>("PendingCount"), Is.EqualTo(1));
            Assert.That(Invoke<int>("RetryPending", expert, true), Is.Zero);
            Assert.That(GetProperty<int>("PendingCount"), Is.EqualTo(1));
            Assert.That(Invoke<int>("RetryPending", expert, false), Is.EqualTo(1));
            Assert.That(GetProperty<int>("PendingCount"), Is.Zero);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ExpiredLane_RetiresDescriptorWithoutRequeueChurn()
        {
            PrewarmWiredLanes(1);
            var expert = Enum.Parse(_difficultyType, "Expert");
            var descriptor = Descriptor("expires", 0d, true, false);
            Assert.That(Invoke<bool>("TrySpawn", descriptor, expert, false), Is.True);

            var owned = (IList)GetProperty<object>("OwnedLanes");
            _poolType.GetMethod("Return").Invoke(_pool, new[] { owned[0] });
            Assert.That(Invoke<int>("RetryPending", expert, false), Is.Zero);
            Assert.That(GetProperty<int>("PendingCount"), Is.Zero);
            Assert.That(Invoke<bool>("TrySpawn", descriptor, expert, false), Is.True,
                "An ordinarily expired lane must retire ownership, not create a retry loop.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Reset_ReturnsOnlyAdapterOwnedLanesAndClearsPending()
        {
            PrewarmWiredLanes(1);
            var expert = Enum.Parse(_difficultyType, "Expert");
            Assert.That(Invoke<bool>("TrySpawn", Descriptor("owned", 0d, true, false), expert, false), Is.True);
            Assert.That(Invoke<bool>("TrySpawn", Descriptor("pending", 1d, true, false), expert, false), Is.False);
            _adapterType.GetMethod("Reset").Invoke(_adapter, null);

            Assert.That(((ICollection)GetProperty<object>("OwnedLanes")).Count, Is.Zero);
            Assert.That(GetProperty<int>("PendingCount"), Is.Zero);
            Assert.That(((ICollection)_poolType.GetProperty("AllSpawned").GetValue(_pool)).Count, Is.Zero);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DrumsPlayerProductionSeam_EnumeratesCoreDescriptorIntoAdapter()
        {
            PrewarmWiredLanes(1);
            var drumsPlayerType = ProductionType("YARG.Gameplay.Player.DrumsPlayer");
            var playerObject = new GameObject("DrumsPlayerProductionSeam");
            var drumsPlayer = playerObject.AddComponent(drumsPlayerType);
            var adapterField = drumsPlayerType.GetField("_eliteDrumVisualAdapter",
                BindingFlags.Instance | BindingFlags.NonPublic);
            adapterField.SetValue(drumsPlayer, _adapter);

            var descriptor = Descriptor("core-production", 2d, true, false);
            var descriptors = Array.CreateInstance(_descriptorType, 1);
            descriptors.SetValue(descriptor, 0);
            var seam = drumsPlayerType.GetMethod("SpawnEliteVisualDescriptorsForTest",
                BindingFlags.Instance | BindingFlags.NonPublic);
            seam.Invoke(drumsPlayer, new object[] { descriptors, Enum.Parse(_difficultyType, "Expert"), false, 2d });

            Assert.That(((ICollection)GetProperty<object>("OwnedLanes")).Count, Is.EqualTo(1));
            var lane = ((IList)GetProperty<object>("OwnedLanes"))[0];
            Assert.That((double)lane.GetType().GetProperty("ElementTime").GetValue(lane), Is.EqualTo(2d));
            // EndTime is a public field on LaneElement, not a property.
            Assert.That((double)lane.GetType().GetField("EndTime").GetValue(lane), Is.EqualTo(3d));

            _adapterType.GetMethod("Reset").Invoke(_adapter, null);
            var kickDescriptor = Descriptor("generated-kick", 3d, true, false, 0);
            var kickDescriptors = Array.CreateInstance(_descriptorType, 1);
            kickDescriptors.SetValue(kickDescriptor, 0);
            seam.Invoke(drumsPlayer, new object[] { kickDescriptors, Enum.Parse(_difficultyType, "Expert"), false, 3d });
            Assert.That(((ICollection)GetProperty<object>("OwnedLanes")).Count, Is.Zero,
                "generated kick descriptors must not allocate an adapter hand lane");

            UnityEngine.Object.Destroy(playerObject);
            yield return null;
        }

        [Test]
        public void GeneratedHandComponent_HasOneAdapterOwner_WhileNativeTremoloRemainsEligible()
        {
            var source = new EliteDrumSourceDefinition("generated-hand", 0, 1, 0, 480);
            var origin = new EliteDrumConversionOrigin(source);
            var generatedNote = new DrumNote(1, DrumNoteType.Neutral, DrumNoteFlags.None,
                NoteFlags.Tremolo | NoteFlags.LaneStart | NoteFlags.LaneEnd, 0d, 0, conversionOrigin: origin);
            var descriptor = Descriptor("generated-hand", 0d, true, false, origin: origin);
            var descriptors = Array.CreateInstance(_descriptorType, 1);
            descriptors.SetValue(descriptor, 0);

            var drumsPlayerObject = new GameObject("GeneratedHandLaneOwnership");
            var drumsPlayer = drumsPlayerObject.AddComponent(_drumsPlayerType);
            _drumsPlayerType.GetField("_eliteVisualDescriptors",
                BindingFlags.Instance | BindingFlags.NonPublic).SetValue(drumsPlayer, descriptors);
            var nativeLanePredicate = _drumsPlayerType.GetMethod("ShouldSpawnNativeLane",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That((bool)nativeLanePredicate.Invoke(drumsPlayer, new object[] { generatedNote }), Is.False,
                "A generated hand component owned by its descriptor must not spawn a duplicate native lane.");
            Assert.That((bool)nativeLanePredicate.Invoke(drumsPlayer, new object[] {
                new DrumNote(1, DrumNoteType.Neutral, DrumNoteFlags.None,
                    NoteFlags.Tremolo | NoteFlags.LaneStart | NoteFlags.LaneEnd, 0d, 0)
            }), Is.True, "A native tremolo without generated provenance remains TrackPlayer-owned.");

            PrewarmWiredLanes(1);
            var expert = Enum.Parse(_difficultyType, "Expert");
            Assert.That(Invoke<bool>("TrySpawn", descriptor, expert, false), Is.True,
                "The generated component must still render through the descriptor adapter.");
            Assert.That(((ICollection)GetProperty<object>("OwnedLanes")).Count, Is.EqualTo(1));
            UnityEngine.Object.DestroyImmediate(drumsPlayerObject);
        }

        [UnityTest]
        public IEnumerator MultiEventDescriptor_RendersExactlyOneLaneSpanningFirstToLastEvent()
        {
            PrewarmWiredLanes(1);
            var expert = Enum.Parse(_difficultyType, "Expert");
            const double firstEventTime = 2d;
            const double middleEventTime = 3.5d;
            const double lastEventTime = 5d;
            const int cymbalPad = 7;

            // One authored phrase: three separated same-final-pad events (gaps of 1.5s,
            // far above LaneElement.COMBINE_LANE_THRESHOLD = 0.1s). Core publishes this
            // as ONE descriptor; Unity must render it as ONE contiguous lane, not one
            // mini-lane per cymbal event.
            var events = new[] { firstEventTime, middleEventTime, lastEventTime };
            var descriptor = PhraseDescriptor("generated-cymbal-lane", events, cymbalPad);

            var playerObject = new GameObject("MultiEventDescriptorLaneSpan");
            var drumsPlayer = playerObject.AddComponent(_drumsPlayerType);
            _drumsPlayerType.GetField("_eliteDrumVisualAdapter",
                BindingFlags.Instance | BindingFlags.NonPublic).SetValue(drumsPlayer, _adapter);
            var seam = _drumsPlayerType.GetMethod("SpawnEliteVisualDescriptorsForTest",
                BindingFlags.Instance | BindingFlags.NonPublic);

            var descriptors = Array.CreateInstance(_descriptorType, 1);
            descriptors.SetValue(descriptor, 0);
            seam.Invoke(drumsPlayer, new object[] { descriptors, expert, false, firstEventTime });

            Assert.That(((ICollection)GetProperty<object>("OwnedLanes")).Count, Is.EqualTo(1),
                "A multi-event descriptor must own exactly one lane, not one lane per event.");
            Assert.That(((ICollection)_poolType.GetProperty("AllSpawned").GetValue(_pool)).Count,
                Is.EqualTo(1), "The descriptor's lane must be the only spawned lane (no duplicates).");

            var lane = ((IList)GetProperty<object>("OwnedLanes"))[0];
            var laneType = lane.GetType();
            Assert.That((double)laneType.GetProperty("ElementTime").GetValue(lane),
                Is.EqualTo(firstEventTime), "The owned lane must start at the phrase's first physical event.");
            // EndTime is a public field on LaneElement, not a property.
            Assert.That((double)laneType.GetField("EndTime").GetValue(lane),
                Is.EqualTo(lastEventTime), "The owned lane must end at the phrase's last physical event.");

            var zLength = (float)laneType.GetField("_zLength", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(lane);
            Assert.That(zLength, Is.GreaterThan(0f),
                "A separated multi-event lane must have positive visual length (not a per-note mini-lane).");

            // Advancing the visual clock through the remaining events must not spawn
            // additional lanes for the same phrase, and nothing may sit in the retry queue.
            seam.Invoke(drumsPlayer, new object[] { descriptors, expert, false, lastEventTime });
            Assert.That(((ICollection)GetProperty<object>("OwnedLanes")).Count, Is.EqualTo(1),
                "The one phrase lane must persist through its last event.");
            Assert.That(((ICollection)_poolType.GetProperty("AllSpawned").GetValue(_pool)).Count,
                Is.EqualTo(1), "No duplicate lane may appear while the phrase lane is alive.");
            Assert.That(GetProperty<int>("PendingCount"), Is.Zero,
                "A spawned phrase lane must not leave pending retry churn.");

            _adapterType.GetMethod("Reset").Invoke(_adapter, null);
            UnityEngine.Object.Destroy(playerObject);
            yield return null;
        }

        [Test]
        public void ThreeEventGeneratedPhrase_AllMemberNotesOwned_NoNativeDuplicateLanes()
        {
            var events = new[] { 0d, 0.25d, 0.5d };
            var descriptor = PhraseDescriptor("generated-cymbal-lane", events, 7);
            var descriptors = Array.CreateInstance(_descriptorType, 1);
            descriptors.SetValue(descriptor, 0);

            var drumsPlayerObject = new GameObject("ThreeEventPhraseOwnership");
            var drumsPlayer = drumsPlayerObject.AddComponent(_drumsPlayerType);
            _drumsPlayerType.GetField("_eliteVisualDescriptors",
                BindingFlags.Instance | BindingFlags.NonPublic).SetValue(drumsPlayer, descriptors);
            var nativeLanePredicate = _drumsPlayerType.GetMethod("ShouldSpawnNativeLane",
                BindingFlags.Instance | BindingFlags.NonPublic);

            // Every physical member of the phrase carries the phrase's suppression, not just
            // the first note: the start, the middle (lane-continuation) event, and the end.
            for (var i = 0; i < events.Length; i++)
            {
                var flags = NoteFlags.Tremolo;
                if (i == 0) flags |= NoteFlags.LaneStart;
                if (i == events.Length - 1) flags |= NoteFlags.LaneEnd;
                var member = new DrumNote(1, DrumNoteType.Neutral, DrumNoteFlags.None, flags,
                    events[i], 0, conversionOrigin: PhraseOrigin(i));
                Assert.That((bool)nativeLanePredicate.Invoke(drumsPlayer, new object[] { member }), Is.False,
                    $"Generated phrase member {i} is descriptor-owned; a native lane here would duplicate the phrase lane.");
            }

            // Ordinary native lanes keep TrackPlayer ownership: same flag shapes, no
            // generated provenance.
            Assert.That((bool)nativeLanePredicate.Invoke(drumsPlayer, new object[] {
                new DrumNote(1, DrumNoteType.Neutral, DrumNoteFlags.None,
                    NoteFlags.Tremolo | NoteFlags.LaneStart, 0d, 0)
            }), Is.True, "A native tremolo lane start without generated provenance remains TrackPlayer-owned.");
            Assert.That((bool)nativeLanePredicate.Invoke(drumsPlayer, new object[] {
                new DrumNote(1, DrumNoteType.Neutral, DrumNoteFlags.None, NoteFlags.Tremolo, 0.25d, 0)
            }), Is.True, "A native tremolo continuation without generated provenance remains TrackPlayer-owned.");
            Assert.That((bool)nativeLanePredicate.Invoke(drumsPlayer, new object[] {
                new DrumNote(1, DrumNoteType.Neutral, DrumNoteFlags.None,
                    NoteFlags.Tremolo | NoteFlags.LaneEnd, 0.5d, 0)
            }), Is.True, "A native tremolo lane end without generated provenance remains TrackPlayer-owned.");

            UnityEngine.Object.DestroyImmediate(drumsPlayerObject);
        }

        [UnityTest]
        public IEnumerator NativeCombiner_NeverExtendsAdapterOwnedLane()
        {
            SetPoolLimits(_pool, 0, 2);
            PrewarmWiredLanes(1);
            var expert = Enum.Parse(_difficultyType, "Expert");

            // One adapter-owned descriptor lane spanning [2, 5] on pad 7.
            var descriptor = PhraseDescriptor("generated-cymbal-lane", new[] { 2d, 3.5d, 5d }, 7);
            Assert.That(Invoke<bool>("TrySpawn", descriptor, expert, false), Is.True);
            var ownedLane = ((IList)GetProperty<object>("OwnedLanes"))[0];

            // A second, native-owned lane taken from the same pool.
            var nativeLane = _poolType.GetMethod("TakeWithoutEnabling").Invoke(_pool, null);
            Assert.That(nativeLane, Is.Not.Null);

            var ownsLane = _adapterType.GetMethod("OwnsLane");
            Assert.That((bool)ownsLane.Invoke(_adapter, new[] { ownedLane }), Is.True,
                "The adapter must report ownership of its descriptor lane.");
            Assert.That((bool)ownsLane.Invoke(_adapter, new[] { nativeLane }), Is.False,
                "A plain pooled lane is not adapter-owned.");

            var playerObject = new GameObject("NativeCombinerOwnershipIsolation");
            var drumsPlayer = playerObject.AddComponent(_drumsPlayerType);
            _drumsPlayerType.GetField("_eliteDrumVisualAdapter",
                BindingFlags.Instance | BindingFlags.NonPublic).SetValue(drumsPlayer, _adapter);
            var seam = _drumsPlayerType.GetMethod("ShouldExtendExistingLane",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That((bool)seam.Invoke(drumsPlayer, new[] { ownedLane }), Is.False,
                "The native adjacency combiner must never extend an adapter-owned descriptor lane.");
            Assert.That((bool)seam.Invoke(drumsPlayer, new[] { nativeLane }), Is.True,
                "Ordinary native lanes keep native combiner eligibility (tremolo/trill untouched).");

            _adapterType.GetMethod("Reset").Invoke(_adapter, null);
            Assert.That((bool)ownsLane.Invoke(_adapter, new[] { ownedLane }), Is.False,
                "Reset must clear adapter ownership.");
            UnityEngine.Object.Destroy(playerObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DedicatedKickCapacityPressure_ReleasesAdapterLaneOnlyWhenExhausted()
        {
            SetPoolLimits(_pool, 0, 2);
            PrewarmWiredLanes(1);
            var expert = Enum.Parse(_difficultyType, "Expert");
            Assert.That(Invoke<bool>("TrySpawn", Descriptor("kick-pressure", 0d, true, false), expert, false), Is.True);
            var owned = (IList)GetProperty<object>("OwnedLanes");
            Assert.That(owned.Count, Is.EqualTo(1));
            var canSpawn = _poolType.GetMethod("CanSpawnAmount");

            Assert.That((bool)canSpawn.Invoke(_pool, new object[] { 1 }), Is.True);
            Assert.That(((ICollection)GetProperty<object>("OwnedLanes")).Count, Is.EqualTo(1),
                "Dedicated kicks must retain adapter-owned visuals while the pool has capacity.");

            var nativeLane = _poolType.GetMethod("TakeWithoutEnabling").Invoke(_pool, null);
            Assert.That(nativeLane, Is.Not.Null);
            Assert.That((bool)canSpawn.Invoke(_pool, new object[] { 1 }), Is.False);
            Assert.That((int)_adapterType.GetMethod("ReleaseForNative").Invoke(_adapter, new object[] { 1 }), Is.EqualTo(1));
            Assert.That(((ICollection)GetProperty<object>("OwnedLanes")).Count, Is.Zero);
            Assert.That(GetProperty<int>("PendingCount"), Is.EqualTo(1),
                "An adapter descriptor is requeued only after native allocation encounters exhaustion.");

            _poolType.GetMethod("Return").Invoke(_pool, new[] { nativeLane });
            yield return null;
        }

        [Test]
        public void PracticeProjection_KeepsOnlyDescriptorsIntersectingHalfOpenTickRange()
        {
            var drumsPlayerType = ProductionType("YARG.Gameplay.Player.DrumsPlayer");
            var projection = drumsPlayerType.GetMethod("ProjectEliteVisualDescriptors",
                BindingFlags.Static | BindingFlags.NonPublic);
            var descriptors = Array.CreateInstance(_descriptorType, 5);
            descriptors.SetValue(Descriptor("before", 1d, true, false, 1, 0, 5), 0);
            descriptors.SetValue(Descriptor("at-start-end", 2d, true, false, 1, 20, 20), 1);
            descriptors.SetValue(Descriptor("inside", 3d, true, false, 1, 20, 30), 2);
            descriptors.SetValue(Descriptor("spanning", 4d, true, false, 1, 30, 45), 3);
            descriptors.SetValue(Descriptor("end", 5d, true, false, 1, 40, 50), 4);

            var projected = (IList)projection.Invoke(null, new object[] { descriptors, 20u, 40u });
            Assert.That(projected.Count, Is.EqualTo(2));
            Assert.That(EliteId(projected[0]), Is.EqualTo("inside"));
            Assert.That(EliteId(projected[1]), Is.EqualTo("spanning"));
        }

        [Test]
        public void RewindCursor_KeepsDescriptorWhosePhysicalIntervalSpansHorizon()
        {
            var drumsPlayerType = ProductionType("YARG.Gameplay.Player.DrumsPlayer");
            var cursor = drumsPlayerType.GetMethod("GetDescriptorIndexAtOrAfter",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var playerObject = new GameObject("RewindCursorProductionSeam");
            var player = playerObject.AddComponent(drumsPlayerType);
            var descriptorsField = drumsPlayerType.GetField("_eliteVisualDescriptors",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var descriptors = Array.CreateInstance(_descriptorType, 2);
            descriptors.SetValue(Descriptor("spanning", 2d, true, false), 0);
            descriptors.SetValue(Descriptor("future", 8d, true, false), 1);
            descriptorsField.SetValue(player, descriptors);

            Assert.That((int)cursor.Invoke(player, new object[] { 2.5d }), Is.Zero);
            Assert.That((int)cursor.Invoke(player, new object[] { 3d }), Is.EqualTo(1));
            UnityEngine.Object.DestroyImmediate(playerObject);
        }

        [Test]
        public void Gate_SuppressesRulesetIneligibleCodaBeginnerAndKickDescriptors()
        {
            var beginner = Enum.Parse(_difficultyType, "Beginner");
            var expert = Enum.Parse(_difficultyType, "Expert");
            Assert.That(Invoke<bool>("TrySpawn", Descriptor("ruleset", 0d, false, false), expert, false), Is.False);
            Assert.That(Invoke<bool>("TrySpawn", Descriptor("coda", 0d, true, true), expert, false), Is.False);
            Assert.That(Invoke<bool>("TrySpawn", Descriptor("beginner", 0d, true, false), beginner, false), Is.False);
            Assert.That(Invoke<bool>("TrySpawn", Descriptor("kick", 0d, true, false, 0), expert, false), Is.False);
        }

        [UnityTest]
        public IEnumerator Appearance_UsesFinalPadIdentityAndResolvedHighwayAppearance()
        {
            PrewarmWiredLanes(1);
            var expert = Enum.Parse(_difficultyType, "Expert");
            var expectedColor = new Color(0.17f, 0.29f, 0.83f, 1f);

            yield return AssertAppearance(expert, "FourLaneDrums", 7, 3, expectedColor);
            yield return AssertAppearance(expert, "ProDrums", 6, 2, expectedColor);
            yield return AssertAppearance(expert, "FiveLaneDrums", 5, 4, expectedColor);
        }

        [Test]
        public void DrumsPlayerResolver_UsesMappedFinalPadPositionAndConfiguredColor()
        {
            var playerObject = new GameObject("DrumsPlayerAppearanceResolver");
            var player = playerObject.AddComponent(_drumsPlayerType);
            var orderingType = ProductionType("YARG.Gameplay.Visuals.HighwayOrderingInfo");
            var ordering = Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(int), orderingType));
            var orderingIndexer = ordering.GetType().GetProperty("Item");
            var orderingInfo = Activator.CreateInstance(orderingType, 1, 2);
            orderingIndexer.SetValue(ordering, orderingInfo, new object[] { 7 });
            _drumsPlayerType.GetField("_highwayOrdering", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(player, ordering);
            _drumsPlayerType.GetField("_fiveLaneMode", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(player, false);
            var profile = new YargProfile();
            var yargPlayerType = ProductionType("YARG.Player.YargPlayer");
            var yargPlayer = Activator.CreateInstance(yargPlayerType, profile, null);
            var basePlayerType = _drumsPlayerType.BaseType.BaseType.BaseType;
            var playerBackingField = basePlayerType.GetField("<Player>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(playerBackingField, Is.Not.Null, "BasePlayer.Player backing field is missing.");
            playerBackingField.SetValue(player, yargPlayer);
            yargPlayerType.GetMethod("RefreshPresets").Invoke(yargPlayer, null);

            var resolver = _drumsPlayerType.GetMethod("ResolveEliteVisualAppearance",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var finalPadType = ProductionType("YARG.Core.Chart.EliteDrumFinalPadIdentity");
            var finalPad = Activator.CreateInstance(finalPadType, Enum.Parse(_instrumentType, "ProDrums"), 7);
            var appearance = resolver.Invoke(player, new[] { finalPad });

            Assert.That((int)appearance.GetType().GetProperty("Position").GetValue(appearance), Is.EqualTo(1));
            var expectedColor = new Color(
                ColorProfile.Default.FourLaneDrums.GetNoteColor(2).R / 255f,
                ColorProfile.Default.FourLaneDrums.GetNoteColor(2).G / 255f,
                ColorProfile.Default.FourLaneDrums.GetNoteColor(2).B / 255f,
                ColorProfile.Default.FourLaneDrums.GetNoteColor(2).A / 255f);
            Assert.That((Color)appearance.GetType().GetProperty("Color").GetValue(appearance), Is.EqualTo(expectedColor));
            UnityEngine.Object.DestroyImmediate(playerObject);
        }

        private IEnumerator AssertAppearance(object expert, string instrumentName, int pad,
            int position, Color color)
        {
            _adapterType.GetMethod("Reset").Invoke(_adapter, null);
            var instrument = Enum.Parse(_instrumentType, instrumentName);
            _resolvedPosition = position;
            _resolvedColor = color;
            _resolvedPad = 0;
            _adapter = _adapterType.GetConstructor(new[] { _poolType, _instrumentType, typeof(int), _resolverType })
                .Invoke(new object[] { _pool, instrument, instrumentName == "FiveLaneDrums" ? 5 : 4, CreateAppearanceResolver() });

            Assert.That(Invoke<bool>("TrySpawn", Descriptor("appearance-" + instrumentName, 0d, true, false,
                pad, 0, 480, (Instrument)instrument), expert, false), Is.True);
            var lane = ((IList)GetProperty<object>("OwnedLanes"))[0];
            var laneType = lane.GetType();
            Assert.That((int)laneType.GetField("_startIndex", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(lane), Is.EqualTo(pad));
            Assert.That((int)laneType.GetField("_endIndex", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(lane), Is.EqualTo(-1));
            Assert.That((bool)laneType.GetMethod("ContainsIndex").Invoke(lane, new object[] { pad }), Is.True);
            Assert.That((bool)laneType.GetMethod("ContainsIndex").Invoke(lane, new object[] { pad + 1 }), Is.False);
            Assert.That((Color)laneType.GetField("_color", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(lane), Is.EqualTo(color));
            Assert.That(_resolvedPad, Is.EqualTo(pad));
            yield return null;
        }

        [UnityTest]
        public IEnumerator NativeCombiner_SkipsAdapterOwnedLaneAndStillCombinesNativeLanesAtSameIndex()
        {
            SetPoolLimits(_pool, 0, 4);
            PrewarmWiredLanes(2);
            var expert = Enum.Parse(_difficultyType, "Expert");

            // Adapter lane on pad 3, taken FIRST so it precedes native lanes in Pool.AllSpawned.
            var descriptor = PhraseDescriptor("generated-cymbal-lane", new[] { 11.9d, 12.2d }, 3);
            Assert.That(Invoke<bool>("TrySpawn", descriptor, expert, false), Is.True);
            var adapterLane = ((IList) GetProperty<object>("OwnedLanes"))[0];
            var laneType = adapterLane.GetType();
            var adapterStart = (double) laneType.GetProperty("ElementTime").GetValue(adapterLane, null);
            var adapterEnd = (double) laneType.GetField("EndTime").GetValue(adapterLane);

            var player = CreateWiredDrumsPlayer("NativeAdjacencyCombinerIntegration");
            // The production guard (ShouldExtendExistingLane) consults the player's adapter to
            // recognize descriptor-owned lanes; wire the fixture adapter so the combiner sees
            // the adapter-owned lane exactly as a real player would.
            _drumsPlayerType.GetField("_eliteDrumVisualAdapter",
                BindingFlags.Instance | BindingFlags.NonPublic).SetValue(player, _adapter);
            var spawnMethod = _drumsPlayerType.GetMethod("SpawnLanesFromNote",
                BindingFlags.Instance | BindingFlags.NonPublic);

            // Native phrase [10, 12] on pad 3: no native lane exists yet, so a new lane spawns
            // after the adapter lane in Pool.AllSpawned.
            var phrase1 = Phrase(10d, 12d, 3);
            spawnMethod.Invoke(player, new object[] { phrase1 });
            var allSpawned = GetPoolSpawnedList();
            Assert.That(allSpawned.Count, Is.EqualTo(2), "Sanity: adapter lane plus first native lane.");
            var nativeLane = allSpawned[1];
            Assert.That((double) nativeLane.GetType().GetField("EndTime").GetValue(nativeLane),
                Is.EqualTo(12d).Within(1e-9));

            // Native phrase [12.05, 12.3] on pad 3: starts within COMBINE_LANE_THRESHOLD of the
            // first native lane's end. The adapter-owned lane (same index) precedes the native
            // lane in Pool.AllSpawned; the combiner must skip it and still combine the two
            // NATIVE lanes instead of suppressing extension or spawning a duplicate.
            var phrase2 = Phrase(12.05d, 12.3d, 3);
            phrase1.NextNote.NextNote = phrase2;
            phrase2.PreviousNote = phrase1.NextNote;
            spawnMethod.Invoke(player, new object[] { phrase2 });

            Assert.That((double) nativeLane.GetType().GetField("EndTime").GetValue(nativeLane),
                Is.EqualTo(12.3d).Within(1e-9),
                "Native adjacency combining must still extend the existing native lane.");
            allSpawned = GetPoolSpawnedList();
            Assert.That(allSpawned.Count, Is.EqualTo(2),
                "An adapter-owned lane must never shadow a native lane at the same index; " +
                "no duplicate native lane may spawn.");
            Assert.That((double) laneType.GetProperty("ElementTime").GetValue(adapterLane, null),
                Is.EqualTo(adapterStart).Within(1e-9));
            Assert.That((double) laneType.GetField("EndTime").GetValue(adapterLane),
                Is.EqualTo(adapterEnd).Within(1e-9),
                "The native combiner must never mutate a descriptor-owned adapter lane.");
            Assert.That(Invoke<bool>("OwnsLane", adapterLane), Is.True,
                "Adapter ownership must be unaffected by native combining.");

            UnityEngine.Object.DestroyImmediate((UnityEngine.Object) player);
            yield return null;
        }

        [UnityTest]
        public IEnumerator NativeLaneTake_SurvivesForeignPoolableAndExhaustedPool()
        {
            SetPoolLimits(_pool, 0, 3);

            var playerObject = new GameObject("NativeLaneTakeRobustness");
            var player = playerObject.AddComponent(_drumsPlayerType);
            _drumsPlayerType.GetField("LanePool", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(player, _pool);
            var takeMethod = _drumsPlayerType.GetMethod("TakeNativeLane",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(takeMethod, Is.Not.Null, "TakeNativeLane seam is missing.");

            // A foreign (non-LaneElement) poolable in the free stack must fail the native take
            // softly: no cast crash and no mutation of the foreign item. Matching the adapter,
            // the foreign item is parked out of the free stack so it can never circulate back
            // and poison later takes.
            var foreignObject = new GameObject("ForeignPoolable");
            var foreign = foreignObject.AddComponent(ProductionType("YARG.Gameplay.Visuals.BeatlineElement"));
            var pooledStack = _poolType.GetField("_pooled", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(_pool);
            pooledStack.GetType().GetMethod("Push").Invoke(pooledStack, new[] { foreign });

            Assert.That(takeMethod.Invoke(player, null), Is.Null,
                "A non-LaneElement poolable is not a native lane; the take must fail soft.");
            Assert.That(foreignObject.activeSelf, Is.True,
                "The foreign poolable must not be disabled into the pool.");
            Assert.That(GetPoolSpawnedList(), Does.Contain(foreign),
                "The foreign poolable stays parked out of the free stack, unchanged.");
            Assert.That(((IEnumerable) pooledStack).Cast<object>().Contains(foreign), Is.False,
                "The foreign poolable must NOT be pushed back onto the free stack.");

            // No repeated poison: the very next native take must produce a real lane, not
            // hand out the same foreign item again.
            Assert.That(takeMethod.Invoke(player, null), Is.Not.Null,
                "A parked foreign poolable must not poison the next native take.");

            takeMethod.Invoke(player, null);
            Assert.That(GetPoolSpawnedList(), Has.Count.EqualTo(3),
                "Sanity: cap 3 is filled by the parked foreign item plus two real lanes.");
            Assert.That(takeMethod.Invoke(player, null), Is.Null,
                "An exhausted pool must yield a null take, not a cast failure.");
            Assert.That(GetPoolSpawnedList(), Has.Count.EqualTo(3),
                "A failed take must not add anything to Pool.AllSpawned.");

            UnityEngine.Object.DestroyImmediate(foreignObject);
            UnityEngine.Object.DestroyImmediate(playerObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator NativeLaneStart_AfterForeignPark_SpawnsAndCombinesRealLanesWithoutThrowing()
        {
            // The confirmed blocker: TakeNativeLane parks a foreign IPoolable in
            // Pool.AllSpawned; the next native lane start iterated AllSpawned with a direct
            // LaneElement foreach cast and threw InvalidCastException. This regression drives
            // the real production path (wired DrumsPlayer + real shared Pool + production
            // Lane.prefab) through native lane start/spawn with a foreign item parked in
            // Pool.AllSpawned.
            SetPoolLimits(_pool, 0, 4);

            var player = CreateWiredDrumsPlayer("NativeLaneStartAfterForeignPark");
            var takeMethod = _drumsPlayerType.GetMethod("TakeNativeLane",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(takeMethod, Is.Not.Null, "TakeNativeLane seam is missing.");

            // Pre-warm two real lanes and give them the minimal visual-timing state, then
            // return them: pooled lanes keep their GameManager/Player across pool cycles
            // (in production they spawn under the player's track), which the real lane
            // configuration path (SetTimeRange -> GetZPositionAtTime) requires.
            var pooledLaneA = (Component) _poolType.GetMethod("TakeWithoutEnabling").Invoke(_pool, null);
            var pooledLaneB = (Component) _poolType.GetMethod("TakeWithoutEnabling").Invoke(_pool, null);
            var gameManager = CreateInactiveGameManager();
            InjectVisualTiming(gameManager, (UnityEngine.Object) player, pooledLaneA, pooledLaneB);
            _poolType.GetMethod("Return").Invoke(_pool, new[] { pooledLaneA });
            _poolType.GetMethod("Return").Invoke(_pool, new[] { pooledLaneB });

            // Park a foreign poolable via the native take: the foreign item sits on top of
            // the free stack, so the take hands it out and parks it in Pool.AllSpawned
            // unchanged (fail-soft exhaustion).
            var foreignObject = new GameObject("ForeignParkedBeforeLaneStart");
            var foreign = foreignObject.AddComponent(ProductionType("YARG.Gameplay.Visuals.BeatlineElement"));
            var pooledStack = GetPoolFreeStack();
            pooledStack.GetType().GetMethod("Push").Invoke(pooledStack, new[] { foreign });
            Assert.That(takeMethod.Invoke(player, null), Is.Null,
                "Sanity: the native take fails softly and parks the foreign poolable.");
            Assert.That(GetPoolSpawnedList(), Does.Contain(foreign),
                "Sanity: the foreign poolable sits in Pool.AllSpawned before the lane start.");

            var spawnMethod = _drumsPlayerType.GetMethod("SpawnLanesFromNote",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(spawnMethod, Is.Not.Null, "SpawnLanesFromNote seam is missing.");

            // First native lane start with Pool.AllSpawned = [foreign]: must not throw and
            // must allocate a real LaneElement (the parked foreign item must not shadow it).
            var phrase1 = Phrase(10d, 12d, 3);
            Assert.DoesNotThrow(() => spawnMethod.Invoke(player, new object[] { phrase1 }),
                "A native lane start must skip the parked foreign poolable instead of throwing InvalidCastException.");
            var allSpawned = GetPoolSpawnedList();
            Assert.That(allSpawned, Has.Count.EqualTo(2),
                "Sanity: the parked foreign item plus exactly one new native lane.");
            var nativeLane = allSpawned[1];
            Assert.That(GetLaneElementType().IsInstanceOfType(nativeLane), Is.True,
                "The native lane start must allocate a real LaneElement.");
            Assert.That((double) nativeLane.GetType().GetProperty("ElementTime").GetValue(nativeLane),
                Is.EqualTo(10d).Within(1e-9));
            Assert.That((double) nativeLane.GetType().GetField("EndTime").GetValue(nativeLane),
                Is.EqualTo(12d).Within(1e-9));

            // Second native lane start at the same index within COMBINE_LANE_THRESHOLD of the
            // first lane's end: the combiner must skip the parked foreign item, match the real
            // native lane, and extend it instead of spawning a duplicate.
            var phrase2 = Phrase(12.05d, 12.3d, 3);
            phrase1.NextNote.NextNote = phrase2;
            phrase2.PreviousNote = phrase1.NextNote;
            Assert.DoesNotThrow(() => spawnMethod.Invoke(player, new object[] { phrase2 }),
                "A native lane start must keep combining across a parked foreign poolable.");
            allSpawned = GetPoolSpawnedList();
            Assert.That(allSpawned, Has.Count.EqualTo(2),
                "The parked foreign item must not shadow native combining; no duplicate lane may spawn.");
            Assert.That((double) nativeLane.GetType().GetField("EndTime").GetValue(nativeLane),
                Is.EqualTo(12.3d).Within(1e-9),
                "Native adjacency combining must extend the existing real lane.");
            Assert.That((double) nativeLane.GetType().GetProperty("ElementTime").GetValue(nativeLane),
                Is.EqualTo(10d).Within(1e-9));

            // The foreign item stays parked and unchanged through both lane starts.
            Assert.That(foreignObject.activeSelf, Is.True,
                "The foreign poolable must not be disabled into the pool.");
            Assert.That(allSpawned, Does.Contain(foreign),
                "The foreign poolable must remain parked out of the free stack.");

            UnityEngine.Object.DestroyImmediate(foreignObject);

            // The harness player carries real ProfileBindings (see CreateWiredDrumsPlayer),
            // so the full GameplayDestroy lifecycle - including input unsubscription - runs
            // cleanly instead of logging the teardown NullReferenceException v4 masked here.
            UnityEngine.Object.DestroyImmediate((UnityEngine.Object) player);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AdapterTryTake_ForeignPoolable_FailsSafelyWithoutCorruption()
        {
            // Pool capacity 1: once the foreign item is parked out of the free stack, every
            // later take must fail as plain exhaustion (no new LaneElement can be created),
            // proving the foreign item cannot circulate or poison the pool.
            SetPoolLimits(_pool, 0, 1);
            var expert = Enum.Parse(_difficultyType, "Expert");

            var foreignObject = new GameObject("AdapterForeignPoolable");
            var foreign = foreignObject.AddComponent(ProductionType("YARG.Gameplay.Visuals.BeatlineElement"));
            var pooledStack = GetPoolFreeStack();
            pooledStack.GetType().GetMethod("Push").Invoke(pooledStack, new[] { foreign });

            var descriptor = Descriptor("foreign-park", 0d, true, false);

            // Must fail safely: no exception, pool-exhaustion semantics.
            Assert.That(Invoke<bool>("TrySpawn", descriptor, expert, false), Is.False,
                "A foreign poolable must fail the take softly, without throwing.");
            Assert.That(GetProperty<int>("PendingCount"), Is.EqualTo(1),
                "The descriptor must be queued as pending, mirroring pool exhaustion.");
            Assert.That(((ICollection)GetProperty<object>("OwnedLanes")).Count, Is.Zero,
                "No lane may become adapter-owned from a foreign take.");

            // The foreign item must be returned UNCHANGED: parked in AllSpawned, still active
            // (no DisableIntoPool side effect), and NOT pushed back onto the free stack where
            // it would be handed out again to native LaneElement consumers.
            Assert.That(foreignObject.activeSelf, Is.True,
                "The foreign poolable must not be disabled into the pool.");
            Assert.That(GetPoolSpawnedList(), Does.Contain(foreign),
                "The foreign poolable stays parked out of the free stack, unchanged.");
            Assert.That(((IEnumerable) pooledStack).Cast<object>().Contains(foreign), Is.False,
                "The foreign poolable must NOT be pushed back onto the free stack.");

            // Adapter ownership/pending bookkeeping stays consistent: the gameplay id is
            // pending, not tracked (a tracked-but-never-spawned id would strand the descriptor).
            var trackedIds = (IEnumerable) _adapterType
                .GetField("_trackedGameplayIds", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(_adapter);
            var pendingIds = (IEnumerable) _adapterType
                .GetField("_pendingGameplayIds", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(_adapter);
            Assert.That(trackedIds.Cast<string>().Contains(EliteId(descriptor)), Is.False,
                "A failed take must not leave the descriptor tracked without a lane.");
            Assert.That(pendingIds.Cast<string>().Contains(EliteId(descriptor)), Is.True,
                "A failed take must leave the descriptor queued as pending.");

            // Retrying stays safe and consistent while the foreign item remains parked.
            Assert.That(Invoke<int>("RetryPending", expert, false), Is.Zero);
            Assert.That(GetProperty<int>("PendingCount"), Is.EqualTo(1),
                "The descriptor stays queued; the retry must not throw or lose it.");

            UnityEngine.Object.DestroyImmediate(foreignObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator StartBRE_FirstTakeExhaustion_ReleasesPriorLanesRollsBackAndParksForeign()
        {
            SetPoolLimits(_pool, 0, 8);
            var player = CreateWiredDrumsPlayer("BREFirstTakeExhaustion");
            SetBRELanes(player, 4);
            _drumsPlayerType.GetField("_highwayOrdering", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(player, BuildHighwayOrdering(4));

            // References from a previous successful BRE attempt occupy two slots.
            var sentinelA = _poolType.GetMethod("TakeWithoutEnabling").Invoke(_pool, null);
            var sentinelB = _poolType.GetMethod("TakeWithoutEnabling").Invoke(_pool, null);
            var gameManager = CreateInactiveGameManager();
            InjectVisualTiming(gameManager, (UnityEngine.Object) player,
                (Component) sentinelA, (Component) sentinelB);
            var breLanes = GetBRELanes(player);
            breLanes.SetValue(sentinelA, 1);
            breLanes.SetValue(sentinelB, 3);

            // A foreign poolable sits on top of the free stack, so the attempt fails on its
            // third take (after the two released prior lanes are recycled).
            var foreignObject = new GameObject("BREForeignPoolable");
            var foreign = foreignObject.AddComponent(ProductionType("YARG.Gameplay.Visuals.BeatlineElement"));
            var pooledStack = GetPoolFreeStack();
            pooledStack.GetType().GetMethod("Push").Invoke(pooledStack, new[] { foreign });

            var startBre = _drumsPlayerType.GetMethod("StartBRE", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(startBre, Is.Not.Null, "StartBRE seam is missing.");
            Assert.DoesNotThrow(() => startBre.Invoke(player, new object[] { 2d, 5d }),
                "A failed BRE acquisition must degrade safely instead of throwing.");

            // Every slot was cleared: later coda emissions can never dereference a stale or
            // unfilled slot.
            foreach (var slot in breLanes)
            {
                Assert.That(slot, Is.Null, "All BRE lane slots must be null after a failed attempt.");
            }

            // The prior-attempt lanes were released up front (before the new allocation
            // attempt), so the failed reentry must not orphan them: they are back in the
            // pool's free stack, ready for reuse, and no longer spawned.
            var freeContents = ((IEnumerable) pooledStack).Cast<object>().ToList();
            Assert.That(freeContents, Does.Contain(sentinelA),
                "A lane held by a previous successful BRE must be returned to the pool, not orphaned.");
            Assert.That(freeContents, Does.Contain(sentinelB),
                "A lane held by a previous successful BRE must be returned to the pool, not orphaned.");
            var spawned = GetPoolSpawnedList();
            Assert.That(spawned, Has.None.EqualTo(sentinelA));
            Assert.That(spawned, Has.None.EqualTo(sentinelB));

            // The foreign poolable is parked out of the free stack, unchanged (adapter parity).
            Assert.That(foreignObject.activeSelf, Is.True,
                "The foreign poolable must not be disabled into the pool.");
            Assert.That(spawned, Does.Contain(foreign),
                "The foreign poolable stays parked out of the free stack, unchanged.");
            Assert.That(((IEnumerable) pooledStack).Cast<object>().Contains(foreign), Is.False,
                "The native take must not push the foreign poolable back onto the free stack.");

            UnityEngine.Object.DestroyImmediate(foreignObject);
            UnityEngine.Object.DestroyImmediate((UnityEngine.Object) player);
            yield return null;
        }

        [UnityTest]
        public IEnumerator StartBRE_ExhaustionMidway_RollsBackAcquiredLanes_NoUnsafeLaterUse_FullSuccessRetried()
        {
            var player = CreateWiredDrumsPlayer("BREMidwayExhaustion");
            // LaneCount is declared on the generic TrackPlayer base; DerivedType.GetField
            // does not return private base-declared backing fields, so walk the hierarchy.
            GetFieldInHierarchy(_drumsPlayerType, "<LaneCount>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic).SetValue(player, 4);
            SetBRELanes(player, 3);

            // InitializeBRELane requires a highway ordering entry per BRE lane index.
            _drumsPlayerType.GetField("_highwayOrdering", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(player, BuildHighwayOrdering(3));
            _drumsPlayerType.GetField("_highwayOrderingIndexToBreLaneIndex", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(player, BuildBreLaneIndexMap(3));
            _drumsPlayerType.GetField("_breLaneIndexToMostRecentTime", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(player, BuildBreRecentTimeMap(3));

            SetPoolLimits(_pool, 0, 8);

            // Create and pre-inject three real lanes, then stage the pool free stack so takes
            // resolve to: lane, lane, FOREIGN. The foreign item exhausts the attempt midway,
            // after two lanes were acquired. (Lanes must be pre-injected because StartBRE
            // configures each lane immediately after taking it.)
            var laneA = _poolType.GetMethod("TakeWithoutEnabling").Invoke(_pool, null);
            var laneB = _poolType.GetMethod("TakeWithoutEnabling").Invoke(_pool, null);
            var laneC = _poolType.GetMethod("TakeWithoutEnabling").Invoke(_pool, null);
            var gameManager = CreateInactiveGameManager();
            InjectVisualTiming(gameManager, (UnityEngine.Object) player,
                (Component) laneA, (Component) laneB, (Component) laneC);

            var foreign = MakeForeignPoolable();
            var pooledStack = GetPoolFreeStack();
            var pop = pooledStack.GetType().GetMethod("Pop");
            var push = pooledStack.GetType().GetMethod("Push");
            _poolType.GetMethod("Return").Invoke(_pool, new[] { laneA });
            _poolType.GetMethod("Return").Invoke(_pool, new[] { laneB });
            pop.Invoke(pooledStack, null); // laneB back to hand
            push.Invoke(pooledStack, new[] { foreign });
            push.Invoke(pooledStack, new[] { laneB });
            _poolType.GetMethod("Return").Invoke(_pool, new[] { laneC });
            // Free stack (bottom -> top): laneA, foreign, laneB, laneC.

            var startBre = _drumsPlayerType.GetMethod("StartBRE", BindingFlags.Instance | BindingFlags.NonPublic);

            // --- Exhaustion midway: two lanes acquired, third take fails. ---
            Assert.DoesNotThrow(() => startBre.Invoke(player, new object[] { 2d, 5d }),
                "Exhaustion midway through BRE acquisition must degrade safely.");

            var breLanes = GetBRELanes(player);
            for (var i = 0; i < breLanes.Length; i++)
            {
                Assert.That(breLanes.GetValue(i), Is.Null,
                    $"Every BRE lane slot must be cleared after a rolled-back attempt (slot {i}).");
            }

            var spawnedAfterRollback = GetPoolSpawnedList();
            Assert.That(spawnedAfterRollback, Has.Count.EqualTo(1),
                "Every lane acquired by the failed attempt must be returned to the pool; " +
                "only the parked foreign item remains spawned.");
            Assert.That(spawnedAfterRollback, Does.Contain(foreign),
                "The foreign poolable must stay parked out of the free stack, unchanged.");
            Assert.That(((IEnumerable) pooledStack).Cast<object>().Count(), Is.EqualTo(3),
                "The rollback must return both acquired lanes behind the untaken one; " +
                "the foreign item is parked in Pool.AllSpawned, not on the free stack.");
            Assert.That(((Component) foreign).gameObject.activeSelf, Is.True,
                "The foreign poolable must not be disabled into the pool.");

            // --- No unsafe later use: coda emissions over cleared slots must be a no-op. ---
            var emissions = _drumsPlayerType.GetMethod("UpdateBreLaneEmissions",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(emissions, Is.Not.Null, "UpdateBreLaneEmissions seam is missing.");
            Assert.DoesNotThrow(() => emissions.Invoke(player, new object[] { 7d }),
                "Coda emissions must skip cleared BRE slots instead of dereferencing them.");
            for (var i = 0; i < breLanes.Length; i++)
            {
                Assert.That(breLanes.GetValue(i), Is.Null,
                    "Emissions over a rolled-back BRE must not resurrect any slot.");
            }

            // --- Reentry: with the misconfiguration removed, the same BRE fully succeeds. ---
            // The foreign item is parked in Pool.AllSpawned now, so remove it from the pool's
            // spawned list (and destroy it) before the clean retry.
            var poolSpawnedList = _poolType.GetField("_spawnedObjects", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(_pool);
            poolSpawnedList.GetType().GetMethod("Remove").Invoke(poolSpawnedList, new[] { foreign });
            UnityEngine.Object.DestroyImmediate((UnityEngine.Object) foreign);
            Assert.DoesNotThrow(() => startBre.Invoke(player, new object[] { 2d, 5d }),
                "A retry after a rolled-back attempt must retain normal full-success behavior.");

            var spawned = GetPoolSpawnedList();
            Assert.That(spawned, Has.Count.EqualTo(3), "A complete BRE acquires one lane per slot.");
            for (var i = 0; i < breLanes.Length; i++)
            {
                var lane = breLanes.GetValue(i);
                Assert.That(lane, Is.Not.Null, $"Slot {i} must hold its lane after full success.");
                Assert.That((double) lane.GetType().GetProperty("ElementTime").GetValue(lane),
                    Is.EqualTo(2d).Within(1e-9), $"Slot {i} lane must span the BRE start time.");
                Assert.That((double) lane.GetType().GetField("EndTime").GetValue(lane),
                    Is.EqualTo(5d).Within(1e-9), $"Slot {i} lane must span the BRE end time.");
            }

            // Emissions over a fully-committed BRE run the real lane path without error.
            Assert.DoesNotThrow(() => emissions.Invoke(player, new object[] { 7d }),
                "Coda emissions must light every committed BRE lane.");

            UnityEngine.Object.DestroyImmediate((UnityEngine.Object) foreign);
            UnityEngine.Object.DestroyImmediate((UnityEngine.Object) player);
            yield return null;
        }

        [UnityTest]
        public IEnumerator StartBRE_ReentrySuccess_ReleasesPriorLanesForReuseWithoutLeak()
        {
            SetPoolLimits(_pool, 0, 8);
            var player = CreateWiredDrumsPlayer("BREReentrySuccessRecycle");
            SetBRELanes(player, 3);
            _drumsPlayerType.GetField("_highwayOrdering", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(player, BuildHighwayOrdering(3));

            var laneA = _poolType.GetMethod("TakeWithoutEnabling").Invoke(_pool, null);
            var laneB = _poolType.GetMethod("TakeWithoutEnabling").Invoke(_pool, null);
            var laneC = _poolType.GetMethod("TakeWithoutEnabling").Invoke(_pool, null);
            var gameManager = CreateInactiveGameManager();
            InjectVisualTiming(gameManager, (UnityEngine.Object) player,
                (Component) laneA, (Component) laneB, (Component) laneC);
            _poolType.GetMethod("Return").Invoke(_pool, new[] { laneA });
            _poolType.GetMethod("Return").Invoke(_pool, new[] { laneB });
            _poolType.GetMethod("Return").Invoke(_pool, new[] { laneC });

            var startBre = _drumsPlayerType.GetMethod("StartBRE", BindingFlags.Instance | BindingFlags.NonPublic);

            // First BRE fully succeeds: three lanes committed.
            Assert.DoesNotThrow(() => startBre.Invoke(player, new object[] { 2d, 5d }));
            var breLanes = GetBRELanes(player);
            var priorLanes = new[] { breLanes.GetValue(0), breLanes.GetValue(1), breLanes.GetValue(2) };
            Assert.That(priorLanes, Has.All.Not.Null, "Sanity: the first BRE committed all of its lanes.");
            Assert.That(GetPoolSpawnedList(), Has.Count.EqualTo(3), "Sanity: the first BRE holds three lanes.");

            // Reentry (a second BRE phrase) poisons the free stack with a foreign item, but
            // the released prior lanes sit above it, so the attempt succeeds by recycling
            // them. Nothing may be orphaned or duplicated, and the foreign item must remain
            // untouched at the bottom of the free stack.
            var foreign = MakeForeignPoolable();
            var pooledStack = GetPoolFreeStack();
            pooledStack.GetType().GetMethod("Push").Invoke(pooledStack, new[] { foreign });

            Assert.DoesNotThrow(() => startBre.Invoke(player, new object[] { 4d, 7d }),
                "A reentrant StartBRE must be able to reuse the lanes released from the prior attempt.");

            breLanes = GetBRELanes(player);
            for (var i = 0; i < breLanes.Length; i++)
            {
                var lane = breLanes.GetValue(i);
                Assert.That(lane, Is.Not.Null, $"Slot {i} must hold its lane after reentry.");
                Assert.That(priorLanes, Does.Contain(lane),
                    $"Slot {i} must reuse a released prior lane instead of allocating a new one.");
                Assert.That((double) lane.GetType().GetProperty("ElementTime").GetValue(lane),
                    Is.EqualTo(4d).Within(1e-9), $"Slot {i} must be reconfigured to the new BRE start.");
                Assert.That((double) lane.GetType().GetField("EndTime").GetValue(lane),
                    Is.EqualTo(7d).Within(1e-9), $"Slot {i} must be reconfigured to the new BRE end.");
            }

            Assert.That(GetPoolSpawnedList(), Has.Count.EqualTo(3),
                "A reentrant BRE must not grow the spawned set beyond its slot count.");
            var freeContents = ((IEnumerable) pooledStack).Cast<object>().ToList();
            Assert.That(freeContents, Does.Contain(foreign),
                "The buried foreign poolable must remain parked on the free stack, untouched.");
            Assert.That(freeContents.Count, Is.EqualTo(1),
                "The reentry must consume exactly the released prior lanes.");

            UnityEngine.Object.DestroyImmediate((UnityEngine.Object) foreign);
            UnityEngine.Object.DestroyImmediate((UnityEngine.Object) player);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PracticeCreateTrack_SwapResetsCursorAdapterLanesAndProjectsCollection()
        {
            SetPoolLimits(_pool, 0, 8);
            // Normal play below spawns one adapter lane per descriptor (4 total), so the
            // pool needs four gameplay-wired lanes before the spawn seam runs.
            PrewarmWiredLanes(4);
            var playerObject = new GameObject("PracticeDescriptorSwap");
            var player = playerObject.AddComponent(_drumsPlayerType);
            _drumsPlayerType.GetField("_eliteDrumVisualAdapter", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(player, _adapter);

            // Full song descriptor collection: one descriptor per 480-tick section.
            var full = Array.CreateInstance(_descriptorType, 4);
            full.SetValue(Descriptor("d0", 1d, true, false, 1, 0, 480), 0);
            full.SetValue(Descriptor("d1", 2d, true, false, 1, 480, 960), 1);
            full.SetValue(Descriptor("d2", 3d, true, false, 1, 960, 1440), 2);
            full.SetValue(Descriptor("d3", 4d, true, false, 1, 1440, 1920), 3);

            var fullTrack = new InstrumentDifficulty<DrumNote>(Instrument.FourLaneDrums, Difficulty.Expert,
                new List<DrumNote>(), null, null, null);
            fullTrack.SetEliteDrumVisualDescriptors(
                (IReadOnlyList<EliteDrumVisualDescriptorV1>) full);
            // OriginalNoteTrack is declared on the generic TrackPlayer base; DerivedType.GetField
            // does not return private base-declared backing fields, so walk the hierarchy.
            GetFieldInHierarchy(_drumsPlayerType, "<OriginalNoteTrack>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(player, fullTrack);

            // Normal play below spawns one adapter lane per descriptor (visualTime 100 is past
            // every descriptor), advancing the cursor past the whole collection - beyond any
            // practice projection.
            _drumsPlayerType.GetField("_eliteVisualDescriptors", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(player, full);
            _drumsPlayerType.GetField("_eliteVisualDescriptorIndex", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(player, 0);
            var spawnSeam = _drumsPlayerType.GetMethod("SpawnEliteVisualDescriptorsForTest",
                BindingFlags.Instance | BindingFlags.NonPublic);
            spawnSeam.Invoke(player, new object[] { full, Enum.Parse(_difficultyType, "Expert"), false, 100d });
            Assert.That(((ICollection) GetProperty<object>("OwnedLanes")).Count, Is.EqualTo(4),
                "Sanity: normal play spawned one adapter lane per descriptor.");

            var createPracticeTrack = _drumsPlayerType.GetMethod("CreatePracticeTrack",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(createPracticeTrack, Is.Not.Null, "CreatePracticeTrack seam is missing.");

            // Entering practice on section [480, 960): the swap to the projected list must
            // drop the adapter-owned lanes, reset the cursor, and carry the projection.
            var practiceTrack = createPracticeTrack.Invoke(player, new object[] { 480u, 960u });
            var practiceDescriptors = (IEnumerable) practiceTrack.GetType()
                .GetProperty("EliteDrumVisualDescriptors").GetValue(practiceTrack);
            var projectedIds = practiceDescriptors.Cast<object>().Select(EliteId).ToList();
            Assert.That(projectedIds, Is.EqualTo(new[] { "d1" }),
                "The practice track must carry only descriptors intersecting the section.");

            var cachedDescriptors = (IEnumerable) _drumsPlayerType.GetField("_eliteVisualDescriptors",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(player);
            Assert.That(cachedDescriptors.Cast<object>().Select(EliteId), Is.EqualTo(new[] { "d1" }),
                "The player-side descriptor cache must match the projection after the swap.");
            Assert.That((int) _drumsPlayerType.GetField("_eliteVisualDescriptorIndex",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(player), Is.Zero,
                "A cursor left over from the full collection (3 > 1) must be reset at the swap.");
            Assert.That(((ICollection) GetProperty<object>("OwnedLanes")).Count, Is.Zero,
                "Adapter-owned lanes spawned from the full collection must not survive the swap.");
            Assert.That(GetPoolSpawnedList(), Is.Empty,
                "No stale descriptor lane may linger in the pool after entering practice.");

            // Switching practice sections must behave the same on the second swap.
            practiceTrack = createPracticeTrack.Invoke(player, new object[] { 960u, 1440u });
            practiceDescriptors = (IEnumerable) practiceTrack.GetType()
                .GetProperty("EliteDrumVisualDescriptors").GetValue(practiceTrack);
            projectedIds = practiceDescriptors.Cast<object>().Select(EliteId).ToList();
            Assert.That(projectedIds, Is.EqualTo(new[] { "d2" }),
                "Switching sections must re-project the descriptor collection.");
            Assert.That((int) _drumsPlayerType.GetField("_eliteVisualDescriptorIndex",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(player), Is.Zero,
                "The descriptor cursor must stay reset across a practice section switch.");
            Assert.That(((ICollection) GetProperty<object>("OwnedLanes")).Count, Is.Zero,
                "No adapter lane may be owned across a practice section switch.");
            Assert.That(GetPoolSpawnedList(), Is.Empty,
                "No stale descriptor lane may linger after switching practice sections.");

            UnityEngine.Object.Destroy(playerObject);
            _adapterType.GetMethod("Reset").Invoke(_adapter, null);
            yield return null;
        }

        /// <summary>
        /// Creates a foreign (non-LaneElement) IPoolable the same way the native take test does.
        /// </summary>
        private object MakeForeignPoolable()
        {
            var foreignObject = new GameObject("BREMidwayForeignPoolable");
            return foreignObject.AddComponent(ProductionType("YARG.Gameplay.Visuals.BeatlineElement"));
        }

        /// <summary>
        /// Instantiates the real DrumsPlayer with the minimum wiring the native lane paths
        /// require: the shared lane pool, a lanes-enabled engine, and an appearance-capable
        /// player profile.
        /// </summary>
        private object CreateWiredDrumsPlayer(string name, bool activate = true)
        {
            var playerObject = new GameObject(name);
            if (!activate)
            {
                // Keep the harness player's GameObject inactive so GameplayBehaviour.Awake
                // (which expects a live gameplay scene) never runs and the object survives
                // frame boundaries; the gameplay-scene context is injected explicitly.
                playerObject.SetActive(false);
            }

            var player = playerObject.AddComponent(_drumsPlayerType);

            _drumsPlayerType.GetField("LanePool", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(player, _pool);

            // The native spawn path consumes LaneCount (SetAppearance subdivision math);
            // mirror the four-lane default production initializes (see BRE tests).
            GetFieldInHierarchy(_drumsPlayerType, "<LaneCount>k__BackingField",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(player, 4);

            // The Engine auto-property is declared on the generic base TrackPlayer<,>;
            // Type.GetField on the derived type does not see private base fields, so
            // walk the hierarchy to the declaring type.
            var engineField = (System.Reflection.FieldInfo) null;
            for (var t = (Type) _drumsPlayerType; t is not null && engineField is null; t = t.BaseType)
            {
                engineField = t.GetField("<Engine>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            }

            engineField.SetValue(player, new FakeDrumsEngine(new DrumsEngineParameters(
                    new HitWindowSettings(0.15, 0.03, 0.35, false, 0.5, 1, 1, 0.03, 0.03),
                    4, Array.Empty<float>(), Array.Empty<float>(),
                    DrumsEngineParameters.DrumMode.NonProFourLane, false, true)));

            var profile = new YargProfile { CurrentInstrument = Instrument.FourLaneDrums };
            var yargPlayerType = ProductionType("YARG.Player.YargPlayer");
            // A real ProfileBindings, as production players always have: BasePlayer's
            // lifecycle (GameplayDestroy -> UnsubscribeFromInputEvents) dereferences
            // Player.Bindings, so a null-bindings harness would break teardown.
            var yargPlayer = Activator.CreateInstance(yargPlayerType, profile,
                Activator.CreateInstance(ProductionType("YARG.Input.ProfileBindings"), profile));
            var basePlayerType = _drumsPlayerType.BaseType.BaseType.BaseType;
            basePlayerType.GetField("<Player>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(player, yargPlayer);
            yargPlayerType.GetMethod("RefreshPresets").Invoke(yargPlayer, null);
            // BasePlayer.GameplayAwake normally scales note speed by difficulty; the harness
            // player never awakes, so pin the scale directly. Without it every lane's
            // GetZPositionAtTime computes a zero-length span (profile speed 6 * scale 0).
            basePlayerType.GetField("_noteSpeedDifficultyScale", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(player, 1f);

            var orderingType = ProductionType("YARG.Gameplay.Visuals.HighwayOrderingInfo");
            var ordering = Activator.CreateInstance(
                typeof(Dictionary<,>).MakeGenericType(typeof(int), orderingType));
            _drumsPlayerType.GetField("_highwayOrdering", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(player, ordering);
            _drumsPlayerType.GetField("_eliteVisualDescriptors", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(player, Array.CreateInstance(_descriptorType, 0));

            // TrackPlayer.FinishDestruction binds SunburstEffects.PulseSunburst into a
            // delegate; a null component there throws ArgumentException during teardown
            // (Mono rejects delegate binding against a null target). Production players
            // carry this component on their track view; the harness attaches an inert one.
            var sunburstObject = new GameObject("SunburstEffects");
            sunburstObject.transform.SetParent(playerObject.transform, false);
            sunburstObject.SetActive(false);
            GetFieldInHierarchy(_drumsPlayerType, "SunburstEffects",
                BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(player, sunburstObject.AddComponent(
                    ProductionType("YARG.Gameplay.Visuals.SunburstEffects")));

            return player;
        }

        /// <summary>
        /// Creates and wires <paramref name="count"/> real lanes into the pool's free stack.
        /// Each lane receives the gameplay-scene timing context (GameManager + Player backing
        /// fields) that production lanes get from GameplayBehaviour.Awake and
        /// TrackElement.GameplayAwake in the real gameplay scene, so the real configuration
        /// paths (SetTimeRange -> GetZPositionAtTime, NoteSpeed) work during adapter, native,
        /// and BRE takes. Must be called before any free-stack arrangement.
        /// </summary>
        private void PrewarmWiredLanes(int count)
        {
            var take = _poolType.GetMethod("TakeWithoutEnabling");
            var returnMethod = _poolType.GetMethod("Return");

            // TakeWithoutEnabling recycles from the free stack before creating new lanes, so
            // drain the stack first: otherwise each iteration would pop the lane the previous
            // one just wired and returned, and the pool would end up with a single lane.
            // Every drained or created lane is distinct, so lanes.Count alone tracks how many
            // the pool will hold once they are returned below.
            var lanes = new List<Component>();
            while (((IEnumerable) GetPoolFreeStack()).Cast<object>().Any())
            {
                lanes.Add((Component) take.Invoke(_pool, null));
            }

            while (lanes.Count < count)
            {
                var lane = (Component) take.Invoke(_pool, null);
                Assert.That(lane, Is.Not.Null,
                    "The pool cap must allow PrewarmWiredLanes to create its lanes.");
                lanes.Add(lane);
            }

            // Wiring is idempotent, so this covers both the drained and the fresh lanes.
            foreach (var lane in lanes)
            {
                InjectVisualTiming(_harnessGameManager, _harnessPlayer, lane);
                returnMethod.Invoke(_pool, new[] { lane });
            }
        }

        private static DrumNote Phrase(double startTime, double endTime, int pad)
        {
            var start = new DrumNote(pad, DrumNoteType.Neutral, DrumNoteFlags.None,
                NoteFlags.Tremolo | NoteFlags.LaneStart, startTime, (uint) (startTime * 480));
            var end = new DrumNote(pad, DrumNoteType.Neutral, DrumNoteFlags.None,
                NoteFlags.Tremolo | NoteFlags.LaneEnd, endTime, (uint) (endTime * 480));
            start.NextNote = end;
            end.PreviousNote = start;
            return start;
        }

        private List<object> GetPoolSpawnedList() =>
            ((IEnumerable) _poolType.GetProperty("AllSpawned").GetValue(_pool, null)).Cast<object>().ToList();

        private object GetPoolFreeStack() =>
            _poolType.GetField("_pooled", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(_pool);

        private Array GetBRELanes(object player) =>
            (Array) _drumsPlayerType.GetField("BRELanes", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(player);

        private void SetBRELanes(object player, int length)
        {
            _drumsPlayerType.GetField("BRELanes", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(player, Array.CreateInstance(GetLaneElementType(), length));
        }

        private Type GetLaneElementType() => ProductionType("YARG.Gameplay.Visuals.LaneElement");

        /// <summary>
        /// The bare test scene has no live GameManager, so real lane configuration paths
        /// (SetTimeRange -> GetZPositionAtTime, BasePlayer.NoteSpeed) would dereference null
        /// GameplayBehaviour state. This injects the minimal non-null resolvers the production
        /// code reads: an inactive GameManager (Awake deferred, never activated) holding an
        /// uninitialized SongRunner whose SongSpeed backing field is pinned to 1, wired into the
        /// lane elements' and the player's GameplayBehaviour backing fields.
        /// </summary>
        private object CreateInactiveGameManager()
        {
            var gameManagerType = ProductionType("YARG.Gameplay.GameManager");
            var gameObject = new GameObject("BRETestGameManager");
            gameObject.SetActive(false); // Awake (and its scene dependencies) never runs.
            var gameManager = gameObject.AddComponent(gameManagerType);

            var songRunnerType = ProductionType("YARG.Playback.SongRunner");
            var songRunner = FormatterServices.GetUninitializedObject(songRunnerType);
            songRunnerType.GetField("<SongSpeed>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(songRunner, 1f);
            gameManagerType.GetField("_songRunner", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(gameManager, songRunner);

            // Player lifecycle reads these GameManager surfaces during GameplayDestroy:
            // TrackPlayer unsubscribes from EngineManager events and BeatEventHandler.Visual,
            // both normally created while loading the song (which never happens here).
            gameManagerType.GetField("<EngineManager>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(gameManager, new EngineManager());
            gameManagerType.GetField("<BeatEventHandler>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(gameManager, Activator.CreateInstance(
                    ProductionType("YARG.Playback.BeatEventHandler"), new SyncTrack(480u)));

            return gameManager;
        }

        private void InjectVisualTiming(object gameManager, UnityEngine.Object player,
            params Component[] lanes)
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            foreach (var lane in lanes)
            {
                GetFieldInHierarchy(lane.GetType(), "<GameManager>k__BackingField", flags)
                    .SetValue(lane, gameManager);
                GetFieldInHierarchy(lane.GetType(), "<Player>k__BackingField", flags)
                    .SetValue(lane, player);
            }

            GetFieldInHierarchy(player.GetType(), "<GameManager>k__BackingField", flags)
                .SetValue(player, gameManager);
        }

        /// <summary>
        /// GetField on a derived type does not return private fields declared on base types;
        /// the gameplay backing fields live on generic bases (TrackElement&lt;TPlayer&gt;,
        /// TrackPlayer&lt;,&gt;), so walk the hierarchy to the declaring type.
        /// </summary>
        private static System.Reflection.FieldInfo GetFieldInHierarchy(Type type, string name,
            BindingFlags flags)
        {
            for (var t = type; t is not null; t = t.BaseType)
            {
                var field = t.GetField(name, flags);
                if (field is not null)
                {
                    return field;
                }
            }

            Assert.Fail($"Field {name} was not found in the hierarchy of {type}.");
            return null;
        }

        /// <summary>Dictionary&lt;int, HighwayOrderingInfo&gt; with Position/ColorIndex == index.</summary>
        private object BuildHighwayOrdering(int laneCount)
        {
            var orderingType = ProductionType("YARG.Gameplay.Visuals.HighwayOrderingInfo");
            var ordering = Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(int), orderingType));
            var indexer = ordering.GetType().GetProperty("Item");
            for (var i = 0; i < laneCount; i++)
            {
                indexer.SetValue(ordering, Activator.CreateInstance(orderingType, i, i), new object[] { i });
            }

            return ordering;
        }

        /// <summary>Dictionary&lt;int, DrumsBreLaneIndex&gt; mapping each lane slot to a BRE lane.</summary>
        private object BuildBreLaneIndexMap(int laneCount)
        {
            var enumType = _drumsPlayerType.GetNestedType("DrumsBreLaneIndex");
            var map = Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(int), enumType));
            var indexer = map.GetType().GetProperty("Item");
            var values = Enum.GetValues(enumType);
            for (var i = 0; i < laneCount; i++)
            {
                indexer.SetValue(map, values.GetValue(i), new object[] { i });
            }

            return map;
        }

        /// <summary>Dictionary&lt;DrumsBreLaneIndex, double&gt; of most-recent hit times (all zero).</summary>
        private object BuildBreRecentTimeMap(int laneCount)
        {
            var enumType = _drumsPlayerType.GetNestedType("DrumsBreLaneIndex");
            var map = Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(enumType, typeof(double)));
            var indexer = map.GetType().GetProperty("Item");
            var values = Enum.GetValues(enumType);
            for (var i = 0; i < laneCount; i++)
            {
                indexer.SetValue(map, 0d, new object[] { values.GetValue(i) });
            }

            return map;
        }

        /// <summary>
        /// Minimal concrete DrumsEngine whose only job is to report EnableLanes=true for the
        /// native lane code paths; all engine logic stays inert.
        /// </summary>
        private sealed class FakeDrumsEngine : DrumsEngine
        {
            private readonly DrumsEngineParameters _parameters;

            public FakeDrumsEngine(DrumsEngineParameters parameters)
                // Empty (non-null) phrase/text/shift lists: BaseEngine's constructor LINQs
                // over Chart.Phrases, so null collections throw ArgumentNullException.
                : base(new InstrumentDifficulty<DrumNote>(Instrument.FourLaneDrums, Difficulty.Expert,
                        new List<DrumNote>(), new List<Phrase>(), new List<TextEvent>(), new List<RangeShift>()),
                    new SyncTrack(480u), parameters, false, false)
            {
                _parameters = parameters;
            }

            public override BaseEngineParameters BaseParameters => _parameters;

            public override BaseStats BaseStats => null;

            protected override void UpdateBot(double time)
            {
            }

            protected override void UpdateTimeVariables(double time)
            {
            }

            protected override void MutateStateWithInput(GameInput gameInput)
            {
            }

            protected override void UpdateHitLogic(double time)
            {
            }

            protected override void UpdateStarPower()
            {
            }

            protected override void RebaseSustains(uint baseTick)
            {
            }

            public override void AllowStarPower(bool isAllowed)
            {
            }

            protected override void CheckForNoteHit()
            {
            }

            protected override bool CanNoteBeHit(DrumNote note) => true;
        }

        private string EliteId(object descriptor) =>
            (string)_descriptorType.GetProperty("GameplayId").GetValue(descriptor);

        private EliteDrumConversionOrigin PhraseOrigin(int ordinal) =>
            new(new EliteDrumSourceDefinition($"generated-cymbal-lane:{ordinal}", ordinal, 1, 0, 480), ordinal);

        /// <summary>
        /// Builds one authored-phrase descriptor that spans several separated physical
        /// events on one final pad, mirroring Core's published FirstPhysicalEventTime /
        /// LastPhysicalEventTime span and per-event ownership origins.
        /// </summary>
        private object PhraseDescriptor(string id, double[] eventTimes, int pad,
            uint authoredStartTick = 0, uint authoredEndTick = 1920,
            Instrument instrument = Instrument.FourLaneDrums)
        {
            var finalPadType = ProductionType("YARG.Core.Chart.EliteDrumFinalPadIdentity");
            var finalPad = Activator.CreateInstance(finalPadType,
                Enum.Parse(_instrumentType, instrument.ToString()), pad);
            var componentType = ProductionType("YARG.Core.Engine.Drums.EliteDrumComponentVisualDescriptor");
            var metadataType = ProductionType("YARG.Core.Engine.Drums.EliteDrumComponentMetadata");
            var kindType = ProductionType("YARG.Core.Engine.Drums.EliteDrumComponentKind");
            // Explicit enabled/isAppearanceOwner: Activator.CreateInstance does not bind
            // to optional parameters, and this Core pin's metadata ctor has six parameters.
            var metadata = Activator.CreateInstance(metadataType, id, Enum.Parse(kindType, "Cymbal"), 0, "lane", true, false);
            var metadataArray = Array.CreateInstance(metadataType, 1);
            metadataArray.SetValue(metadata, 0);
            var component = Activator.CreateInstance(componentType,
                id, true, id, 0, "lane", metadataArray);

            var originType = ProductionType("YARG.Core.Chart.EliteDrumConversionOrigin");
            var origins = Array.CreateInstance(originType, eventTimes.Length);
            for (var i = 0; i < eventTimes.Length; i++)
            {
                origins.SetValue(PhraseOrigin(i), i);
            }

            var lastTime = eventTimes[^1];
            return Activator.CreateInstance(_descriptorType, id,
                new[] { id }, origins, finalPad, component,
                authoredStartTick, authoredEndTick, eventTimes[0], lastTime,
                eventTimes[0], lastTime, true, false);
        }


        private object Descriptor(string id, double time, bool rulesetEligible, bool codaExcluded,
            int pad = 1, uint authoredStartTick = 0, uint authoredEndTick = 480,
            Instrument instrument = Instrument.FourLaneDrums, EliteDrumConversionOrigin origin = null)
        {
            var instrumentValue = Enum.Parse(_instrumentType, instrument.ToString());
            var finalPadType = ProductionType("YARG.Core.Chart.EliteDrumFinalPadIdentity");
            var finalPad = Activator.CreateInstance(finalPadType, instrumentValue, pad);
            var componentType = ProductionType("YARG.Core.Engine.Drums.EliteDrumComponentVisualDescriptor");
            var metadataType = ProductionType("YARG.Core.Engine.Drums.EliteDrumComponentMetadata");
            var kindType = ProductionType("YARG.Core.Engine.Drums.EliteDrumComponentKind");
            var metadata = Activator.CreateInstance(metadataType, id, Enum.Parse(kindType, "Drum"), 0, "lane", true, false);
            var metadataArray = Array.CreateInstance(metadataType, 1);
            metadataArray.SetValue(metadata, 0);
            var component = Activator.CreateInstance(componentType,
                id, true, id, 0, "lane", metadataArray);
            var originType = ProductionType("YARG.Core.Chart.EliteDrumConversionOrigin");
            var origins = Array.CreateInstance(originType, origin == null ? 0 : 1);
            if (origin != null)
            {
                origins.SetValue(origin, 0);
            }
            return Activator.CreateInstance(_descriptorType, id,
                new[] { id }, origins, finalPad, component,
                authoredStartTick, authoredEndTick, time, time + 1d, time, time + 1d,
                rulesetEligible, codaExcluded);
        }

        private void SetPoolLimits(Component pool, int prewarm, int cap)
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            _poolType.GetField("_prewarmAmount", flags).SetValue(pool, prewarm);
            _poolType.GetField("_objectCap", flags).SetValue(pool, cap);
        }

        private Type ProductionType(string name)
        {
            var type = Type.GetType(name + ", Assembly-CSharp");
            if (type is null)
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    type = assembly.GetType(name);
                    if (type is not null) break;
                }
            }

            Assert.That(type, Is.Not.Null, $"Production type {name} is missing from loaded Unity assemblies.");
            return type;
        }

        private T Invoke<T>(string name, params object[] args) =>
            (T)_adapterType.GetMethod(name).Invoke(_adapter, args);

        private T GetProperty<T>(string name) =>
            (T)_adapterType.GetProperty(name).GetValue(_adapter);
    }
}
