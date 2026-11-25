using System.Collections.Generic;
using UnityEngine;
using YARG.Core;
using YARG.Core.Chart;
using YARG.Core.Game;
using YARG.Gameplay.Player;
using YARG.Helpers.Extensions;
using YARG.Settings;
using YARG.Themes;

namespace YARG.Gameplay.Visuals
{
    // pattern: Mixed (needs refactoring)
    /// <summary>Typed native Elite notes assembled from the selected theme’s Elite models.</summary>
    public sealed class EliteDrumsNoteElement : NoteElement<EliteDrumNote, EliteDrumsPlayer>
    {
        private const int NORMAL = 0;
        private const int CYMBAL = 1;
        private const int KICK = 2;
        private const int ACCENT = 3;
        private const int GHOST = 4;
        private const int CYMBAL_ACCENT = 5;
        private const int CYMBAL_GHOST = 6;
        private const int DEDICATED_KICK = 7;
        private const int OPEN_HI_HAT = 8;
        private const int OPEN_HI_HAT_ACCENT = 9;
        private const int OPEN_HI_HAT_GHOST = 10;
        private const int CLOSED_HI_HAT = 11;
        private const int CLOSED_HI_HAT_ACCENT = 12;
        private const int CLOSED_HI_HAT_GHOST = 13;
        private const int WILDCARD = 14;
        private const int HAT_STOMP = 15;
        private const int HAT_SPLASH = 16;
        private const int KICK_VARIANTS_START = 17;
        private const int COUNT = 27;
        private const int FUSED_FLAM_START = COUNT;
        private static readonly ThemeNoteType[] BaseTypes =
        {
            ThemeNoteType.Normal, ThemeNoteType.Cymbal, ThemeNoteType.Kick, ThemeNoteType.Accent,
            ThemeNoteType.Ghost, ThemeNoteType.CymbalAccent, ThemeNoteType.CymbalGhost, ThemeNoteType.DedicatedLaneKick,
            ThemeNoteType.OpenHiHat, ThemeNoteType.OpenHiHatAccent, ThemeNoteType.OpenHiHatGhost,
            ThemeNoteType.ClosedHiHat, ThemeNoteType.ClosedHiHatAccent, ThemeNoteType.ClosedHiHatGhost,
            ThemeNoteType.Wildcard, ThemeNoteType.HatStomp, ThemeNoteType.HatSplash
        };
        private static readonly ThemeNoteType[] FusedFlamTypes = ThemeNoteModelFallbacks.EliteFusedFlamTypes;
        [SerializeField] private Vector3 _normalStompScale;
        [SerializeField] private Vector3 _starStompScale;
        [SerializeField] private Vector3 _normalStompPosition;
        [SerializeField] private Vector3 _starStompPosition;
        // Theme models are assembled on the prefab before pool copies are made.
        // These baselines must be serialized into those copies, or reset collapses
        // every ordinary gem group to Vector3.zero on first spawn.
        [SerializeField] private Vector3[] _normalGemScales = new Vector3[COUNT];
        [SerializeField] private Vector3[] _starGemScales = new Vector3[COUNT];
        [SerializeField] private Vector3[] _normalGemPositions = new Vector3[COUNT];
        [SerializeField] private Vector3[] _starGemPositions = new Vector3[COUNT];
        [SerializeField] private NoteGroup[] _splitGemGroups = new NoteGroup[COUNT * 2];
        [SerializeField] private NoteGroup[] _splitStarGemGroups = new NoteGroup[COUNT * 2];
        [SerializeField] private Vector3[] _splitGemScales = new Vector3[COUNT * 2];
        [SerializeField] private Vector3[] _splitStarGemScales = new Vector3[COUNT * 2];
        [SerializeField] private Vector3[] _splitGemPositions = new Vector3[COUNT * 2];
        [SerializeField] private Vector3[] _splitStarGemPositions = new Vector3[COUNT * 2];

        private const int SPLIT_LEFT = 0;
        private const int SPLIT_RIGHT = 1;
        private const int SPLIT_NORMAL = 0;
        private const int SPLIT_STAR = 1;
        private readonly Dictionary<NoteGroup, EliteDrumsRuntimeGeometry.ModelGeometry> _geometry = new();
        private bool _lastCollisionPartnerStar;
        private bool _hasRollSlot;

        private readonly Dictionary<EliteDrumsGeometryCache.PairKey, EliteDrumsRuntimeGeometry.PairPlacement>
            _preparedPlacements = new();
        private EliteDrumsNoteElement _geometryTemplate;

        internal bool CymbalLeftyFlip => Player != null && Player.Player?.Profile?.LeftyFlip == true;

        internal Quaternion GetCymbalRotation(float beatsPerRevolution)
        {
            if (NoteRef == null || GameManager == null || !GameManager.Started || GameManager.Chart?.SyncTrack == null)
                return Quaternion.identity;
            return GameManager.GetCymbalRotation(beatsPerRevolution, CymbalLeftyFlip);
        }

        private NoteGroup[] GeometryGroups(int kind) => kind switch
        {
            0 => NoteGroups, 1 => StarPowerNoteGroups, 2 => _splitGemGroups, _ => _splitStarGemGroups
        };

        private EliteDrumsRuntimeGeometry.ModelGeometry Geometry(int kind, int index)
        {
            var group = GeometryGroups(kind)[index];
            if (!_geometry.TryGetValue(group, out var geometry))
            {
                // Resolve the corresponding authored source slot directly. No
                // hierarchy traversal, mesh reads, or slot search on first spawn.
                if (_geometryTemplate == null && ParentPool != null && ParentPool.Prefab != gameObject)
                    _geometryTemplate = ParentPool.Prefab.GetComponent<EliteDrumsNoteElement>();
                geometry = _geometryTemplate != null
                    ? new EliteDrumsRuntimeGeometry.ModelGeometry(_geometryTemplate.Geometry(kind, index))
                    : new EliteDrumsRuntimeGeometry.ModelGeometry(group.transform)
                        { PreparedPlacements = _preparedPlacements };
                _geometry.Add(group, geometry);
            }
            return geometry;
        }

        internal void PrepareSongGeometry(EliteDrumsPlayer player, IEnumerable<EliteDrumNote> notes)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                foreach (var parent in notes)
                foreach (var note in parent.AllNotes)
                {
                    if (note.IsInvisibleTerminator) continue;
                    bool hasRollSlot = GetFittingRegion(player, note, out float center, out float width,
                        out _, out _);
                    var profile = player.Player.Profile;
                    int fused = GetFusedFlamGroup(note, profile);
                    if (fused < 0 && IsSplitFlam(note, true))
                    {
                        int group = GetSplitGroup(note, profile.UseCymbalModels);
                        for (int variant = 0; variant < 2; variant++)
                            EliteDrumsRuntimeGeometry.FitPair(Geometry(2 + variant, group * 2), null,
                                Geometry(2 + variant, group * 2 + 1), null,
                                player.Layout.Lanes.Count, center, width, 0f, !note.IsFlatFlam,
                                player.Player.Profile.FlamLayout, IsCymbalGroup(group),
                                player.Player.Profile.LeftyFlip);
                    }
                    else if (!hasRollSlot && EliteDrumsRuntimeGeometry.GetCollisionPartner(player, note) is { } partner)
                    {
                        int group = fused >= 0 ? fused : GetGemGroup(note, profile.UseCymbalModels);
                        int otherGroup = SelectModelGroup(partner, profile);
                        bool right = EliteDrumsPlayer.IsCymbal(note.Pad) != player.Player.Profile.LeftyFlip;
                        // Either member can independently lose SP. Prepare all
                        // four visible combinations, including overlap suppression.
                        for (int own = 0; own < 2; own++)
                        for (int other = 0; other < 2; other++)
                            EliteDrumsRuntimeGeometry.FitPair(
                                Geometry(right ? other : own, right ? otherGroup : group), null,
                                Geometry(right ? own : other, right ? group : otherGroup), null,
                                player.Layout.Lanes.Count, center, width, 0f, false);
                    }
                    else if (fused >= 0 || hasRollSlot)
                    {
                        int group = fused >= 0 ? fused : GetGemGroup(note, profile.UseCymbalModels);
                        Geometry(0, group);
                        Geometry(1, group);
                    }
                }
            }
            finally
            {
                EliteDrumsRuntimeGeometry.LastPreparationMilliseconds = clock.Elapsed.TotalMilliseconds;
            }
        }

        private static bool IsCymbalGroup(int group) =>
            group is CYMBAL or CYMBAL_ACCENT or CYMBAL_GHOST or OPEN_HI_HAT or OPEN_HI_HAT_ACCENT
                or OPEN_HI_HAT_GHOST or CLOSED_HI_HAT or CLOSED_HI_HAT_ACCENT or CLOSED_HI_HAT_GHOST;

        private static bool GetFittingRegion(EliteDrumsPlayer player, EliteDrumNote note,
            out float center, out float width, out float slotCenter, out float slotWidth)
        {
            if (player.TryGetRollSlot(note, out slotCenter, out slotWidth))
            {
                // Fit the complete glyph around a local origin before placing it in
                // its strip. Highway-edge corrections belong to standalone flams.
                center = 0f;
                width = player.Layout.GetLaneWidth(TrackPlayer.TRACK_WIDTH);
                return true;
            }
            EliteDrumsRuntimeGeometry.GetRegion(player, note, false, out center, out width);
            return false;
        }

        public override void SetThemeModels(Dictionary<ThemeNoteType, GameObject> models,
            Dictionary<ThemeNoteType, GameObject> starpowerModels)
        {
            (models, starpowerModels) = ThemeNoteModelFallbacks.ResolveEliteModels(models, starpowerModels);
            int modelCount = COUNT + FusedFlamTypes.Length;
            CreateNoteGroupArrays(modelCount);
            _normalGemScales = new Vector3[modelCount];
            _starGemScales = new Vector3[modelCount];
            _normalGemPositions = new Vector3[modelCount];
            _starGemPositions = new Vector3[modelCount];
            for (int i = 0; i < BaseTypes.Length; i++)
                AssignNoteGroup(models, starpowerModels, i, BaseTypes[i]);

            for (int i = 0; i < EliteKickAppearance.OptionalTypes.Count; i++)
                AssignNoteGroup(models, starpowerModels, KICK_VARIANTS_START + i, EliteKickAppearance.OptionalTypes[i]);

            for (int i = 0; i < FusedFlamTypes.Length; i++)
            {
                var type = FusedFlamTypes[i];
                if (models.ContainsKey(type))
                    AssignNoteGroup(models, starpowerModels, FUSED_FLAM_START + i, type);
            }

            ScaleStompModel(NoteGroups[DEDICATED_KICK]);
            if (StarPowerNoteGroups[DEDICATED_KICK] != NoteGroups[DEDICATED_KICK])
                ScaleStompModel(StarPowerNoteGroups[DEDICATED_KICK]);
            _normalStompScale = NoteGroups[DEDICATED_KICK].transform.localScale;
            _starStompScale = StarPowerNoteGroups[DEDICATED_KICK].transform.localScale;
            _normalStompPosition = NoteGroups[DEDICATED_KICK].transform.localPosition;
            _starStompPosition = StarPowerNoteGroups[DEDICATED_KICK].transform.localPosition;
            CreateSplitGemGroups();
            for (int i = 0; i < NoteGroups.Length; i++)
            {
                if (NoteGroups[i] == null) continue;
                _normalGemScales[i] = NoteGroups[i].transform.localScale;
                _normalGemPositions[i] = NoteGroups[i].transform.localPosition;
                _starGemScales[i] = StarPowerNoteGroups[i].transform.localScale;
                _starGemPositions[i] = StarPowerNoteGroups[i].transform.localPosition;
            }
            // One traversal during theme assembly, never during note activation.
            // Bind after creating split copies so every model points at this note.
            foreach (var spin in GetComponentsInChildren<CymbalSpinAnimation>(true))
                spin.BindOwner(this);
        }

        // Layout for same-tick tom/cymbal pairs that share one native Elite lane.
        // Returns the selected note's center and rendered width in highway units.
        [System.Obsolete("Use the initialized player's resolved geometry. This helper represents Compact only.")]
        internal static bool TryGetTomCymbalLayout(EliteDrumNote note, bool lefty, bool offset,
            out float x, out float targetWidth)
        {
            x = 0;
            targetWidth = 1;
            var pad = (EliteDrumNote.EliteDrumPad) note.Pad;
            bool isTom = pad is EliteDrumNote.EliteDrumPad.Tom1 or EliteDrumNote.EliteDrumPad.Tom2 or EliteDrumNote.EliteDrumPad.Tom3;
            bool isCymbal = pad is EliteDrumNote.EliteDrumPad.LeftCrash or EliteDrumNote.EliteDrumPad.Ride or EliteDrumNote.EliteDrumPad.RightCrash;
            if (!isTom && !isCymbal) return false;

            EliteDrumNote.EliteDrumPad tomPad = pad switch
            {
                EliteDrumNote.EliteDrumPad.Tom1 or EliteDrumNote.EliteDrumPad.LeftCrash => EliteDrumNote.EliteDrumPad.Tom1,
                EliteDrumNote.EliteDrumPad.Tom2 or EliteDrumNote.EliteDrumPad.Ride => EliteDrumNote.EliteDrumPad.Tom2,
                EliteDrumNote.EliteDrumPad.Tom3 or EliteDrumNote.EliteDrumPad.RightCrash => EliteDrumNote.EliteDrumPad.Tom3,
                _ => (EliteDrumNote.EliteDrumPad) (-1),
            };
            EliteDrumNote.EliteDrumPad cymbalPad = tomPad switch
            {
                EliteDrumNote.EliteDrumPad.Tom1 => EliteDrumNote.EliteDrumPad.LeftCrash,
                EliteDrumNote.EliteDrumPad.Tom2 => EliteDrumNote.EliteDrumPad.Ride,
                EliteDrumNote.EliteDrumPad.Tom3 => EliteDrumNote.EliteDrumPad.RightCrash,
                _ => (EliteDrumNote.EliteDrumPad) (-1),
            };
            bool hasCounterpart = false;
            foreach (var member in note.ParentOrSelf.AllNotes)
            {
                if (isTom && member.Pad == (int) cymbalPad ||
                    isCymbal && member.Pad == (int) tomPad)
                    hasCounterpart = true;
            }
            if (!hasCounterpart) return false;

            var compact = EliteDrumsLayoutResolver.Resolve(EliteDrumsLayoutPreset.Compact);
            float laneWidth = compact.GetLaneWidth(TrackPlayer.TRACK_WIDTH);
            float sharedLaneCenter = compact.GetLaneX(EliteDrumsPlayer.GetLane((int) tomPad),
                TrackPlayer.TRACK_WIDTH, lefty);
            // Compatibility lane query only; measured pair placement is handled by FitPair.
            x = sharedLaneCenter;
            targetWidth = laneWidth;
            return true;
        }

        private static void ScaleStompModel(NoteGroup stompModel)
        {
            var scale = stompModel.transform.localScale;
            stompModel.transform.localScale = new Vector3(scale.x * 3f, scale.y, scale.z);
        }

        private void CreateSplitGemGroups()
        {
            EnsureSplitGemArraySizes();
            for (int source = NORMAL; source < COUNT; source++)
            {
                for (int side = SPLIT_LEFT; side <= SPLIT_RIGHT; side++)
                {
                    int index = source * 2 + side;
                    _splitGemGroups[index] = Instantiate(NoteGroups[source], transform);
                    _splitGemGroups[index].name = $"Split flam {source} {side}";
                    _splitStarGemGroups[index] = Instantiate(StarPowerNoteGroups[source], transform);
                    _splitStarGemGroups[index].name = $"Split flam star {source} {side}";
                    _splitGemScales[index] = _splitGemGroups[index].transform.localScale;
                    _splitStarGemScales[index] = _splitStarGemGroups[index].transform.localScale;
                    _splitGemPositions[index] = _splitGemGroups[index].transform.localPosition;
                    _splitStarGemPositions[index] = _splitStarGemGroups[index].transform.localPosition;
                    _splitGemGroups[index].SetActive(false);
                    _splitStarGemGroups[index].SetActive(false);
                }
            }
        }

        private void EnsureSplitGemArraySizes()
        {
            int size = COUNT * 2;
            if (_splitGemGroups == null || _splitGemGroups.Length != size) _splitGemGroups = new NoteGroup[size];
            if (_splitStarGemGroups == null || _splitStarGemGroups.Length != size)
                _splitStarGemGroups = new NoteGroup[size];
            if (_splitGemScales == null || _splitGemScales.Length != size) _splitGemScales = new Vector3[size];
            if (_splitStarGemScales == null || _splitStarGemScales.Length != size)
                _splitStarGemScales = new Vector3[size];
            if (_splitGemPositions == null || _splitGemPositions.Length != size)
                _splitGemPositions = new Vector3[size];
            if (_splitStarGemPositions == null || _splitStarGemPositions.Length != size)
                _splitStarGemPositions = new Vector3[size];
        }

        private void ResetSplitGemGroups()
        {
            for (int i = 0; i < _splitGemGroups.Length; i++)
            {
                if (_splitGemGroups[i] != null)
                {
                    _splitGemGroups[i].transform.localScale = _splitGemScales[i];
                    _splitGemGroups[i].transform.localPosition = _splitGemPositions[i];
                    _splitGemGroups[i].SetActive(false);
                }
                if (_splitStarGemGroups[i] != null)
                {
                    _splitStarGemGroups[i].transform.localScale = _splitStarGemScales[i];
                    _splitStarGemGroups[i].transform.localPosition = _splitStarGemPositions[i];
                    _splitStarGemGroups[i].SetActive(false);
                }
            }
        }

        private void InitializeSplitFlam(int group, float laneCenter, float regionWidth)
        {
            int baseIndex = group * 2;
            bool starPower = IsStarPowerVisible;
            NoteGroup left = starPower ? _splitStarGemGroups[baseIndex + SPLIT_LEFT] :
                _splitGemGroups[baseIndex + SPLIT_LEFT];
            NoteGroup right = starPower ? _splitStarGemGroups[baseIndex + SPLIT_RIGHT] :
                _splitGemGroups[baseIndex + SPLIT_RIGHT];
            var source = starPower ? StarPowerNoteGroups[group] : NoteGroups[group];
            source.SetActive(false);

            for (int variant = 0; variant < 2; variant++)
            {
                var splitGroups = variant == 0 ? _splitGemGroups : _splitStarGemGroups;
                var l = splitGroups[baseIndex + SPLIT_LEFT];
                var r = splitGroups[baseIndex + SPLIT_RIGHT];
                EliteDrumsRuntimeGeometry.FitPair(Geometry(2 + variant, baseIndex + SPLIT_LEFT), l.transform,
                    Geometry(2 + variant, baseIndex + SPLIT_RIGHT), r.transform,
                    Player.Layout.Lanes.Count, laneCenter, regionWidth, transform.localPosition.x,
                    !NoteRef.IsFlatFlam, Player.Player.Profile.FlamLayout,
                    IsCymbalGroup(group),
                    Player.Player.Profile.LeftyFlip);
            }
            left.SetActive(true);
            right.SetActive(true);
            left.Initialize();
            right.Initialize();
            NoteGroup = left;
        }

        internal static int GetSplitFlamTickOffset(int side, bool flatFlam, bool lefty)
        {
            return 0; // Compatibility helper: both physical gems use the source note time.
        }

        internal static bool IsHandFlamPad(int pad) => (EliteDrumNote.EliteDrumPad) pad is
            EliteDrumNote.EliteDrumPad.Snare or EliteDrumNote.EliteDrumPad.HiHat or
            EliteDrumNote.EliteDrumPad.LeftCrash or EliteDrumNote.EliteDrumPad.Tom1 or
            EliteDrumNote.EliteDrumPad.Tom2 or EliteDrumNote.EliteDrumPad.Ride or
            EliteDrumNote.EliteDrumPad.Tom3 or EliteDrumNote.EliteDrumPad.RightCrash;

        internal static bool IsSplitFlam(EliteDrumNote note, bool enabled) =>
            (note.IsFlam || note.IsFlatFlam) && IsHandFlamPad(note.Pad);

        private int GetFusedFlamGroup(EliteDrumNote note, YargProfile profile)
        {
            if (!note.IsHandFlam) return -1;
            var single = BaseTypes[GetSplitGroup(note, profile.UseCymbalModels)];
            if (ThemeNoteModelFallbacks.GetEliteFusedFlamType(single, note.IsFlatFlam, profile.FlamLayout,
                profile.UseVolumeCymbals) is not { } type)
                return -1;
            int slot = FUSED_FLAM_START + System.Array.IndexOf(FusedFlamTypes, type);
            return slot < NoteGroups.Length && NoteGroups[slot] != null ? slot : -1;
        }

        private bool UsesSplitFlam(EliteDrumNote note, YargProfile profile) =>
            IsSplitFlam(note, true) && GetFusedFlamGroup(note, profile) < 0;

        private int SelectModelGroup(EliteDrumNote note, YargProfile profile)
        {
            int fused = GetFusedFlamGroup(note, profile);
            return fused >= 0 ? fused : GetGemGroup(note, profile.UseCymbalModels);
        }

        private int GetSplitGroup() => GetSplitGroup(NoteRef, Player.Player.Profile.UseCymbalModels);

        internal static int GetSplitGroup(EliteDrumNote note, bool useCymbalModels)
        {
            if (!useCymbalModels)
                return note.IsAccent ? ACCENT : note.IsGhost ? GHOST : NORMAL;
            if (note.Pad == (int) EliteDrumNote.EliteDrumPad.HiHat && (note.IsOpen || note.IsClosed))
            {
                if (note.IsAccent) return note.IsOpen ? OPEN_HI_HAT_ACCENT : CLOSED_HI_HAT_ACCENT;
                if (note.IsGhost) return note.IsOpen ? OPEN_HI_HAT_GHOST : CLOSED_HI_HAT_GHOST;
                if (useCymbalModels) return note.IsOpen ? OPEN_HI_HAT : CLOSED_HI_HAT;
            }
            bool cymbal = EliteDrumsPlayer.IsCymbal(note.Pad);
            if (note.IsAccent) return cymbal ? CYMBAL_ACCENT : ACCENT;
            if (note.IsGhost) return cymbal ? CYMBAL_GHOST : GHOST;
            return cymbal && useCymbalModels ? CYMBAL : NORMAL;
        }

        internal static void GetSplitFlamCenters(float laneCenter, float laneWidth, bool lefty,
            out float leftCenter, out float rightCenter)
        {
            float center = lefty ? -laneCenter : laneCenter;
            float gemWidth = laneWidth * 2f / 3f;
            // Keep interior pairs centered on the fret. Only outer pairs need to
            // move inward, by the half-width that would overhang the highway.
            float maxCenter = TrackPlayer.TRACK_WIDTH / 2f - gemWidth;
            center = Mathf.Clamp(center, -maxCenter, maxCenter);
            leftCenter = center - gemWidth / 2f;
            rightCenter = center + gemWidth / 2f;
        }

        internal static double GetFlamStaggerTime(YARG.Core.Chart.SyncTrack syncTrack, uint noteTick, int tickOffset)
        {
            return 0d; // Retained for historical callers; hand flams have no temporal offset.
        }

        private void ResetGemGroups()
        {
            // Only pair fitting needs measured mesh geometry. Calling Geometry
            // here builds every unused variant on a pooled note's first spawn,
            // deferring a large mesh-read/allocation burst until gameplay.
            // Restore previously fitted rotations before applying the serialized
            // scale/position baselines; untouched groups already have their
            // authored rotation.
            foreach (var entry in _geometry)
            {
                entry.Value.Reset(entry.Key.transform);
            }
            ResetSplitGemGroups();
            for (int i = 0; i < NoteGroups.Length; i++)
            {
                if (NoteGroups[i] == null) continue;
                NoteGroups[i].transform.localScale = _normalGemScales[i];
                NoteGroups[i].transform.localPosition = _normalGemPositions[i];
                if (StarPowerNoteGroups[i] == NoteGroups[i]) continue;
                StarPowerNoteGroups[i].transform.localScale = _starGemScales[i];
                StarPowerNoteGroups[i].transform.localPosition = _starGemPositions[i];
            }
        }

        private void ResetStompGroups()
        {
            var normal = NoteGroups[DEDICATED_KICK].transform;
            normal.localScale = _normalStompScale;
            normal.localPosition = _normalStompPosition;
            if (StarPowerNoteGroups[DEDICATED_KICK] == NoteGroups[DEDICATED_KICK]) return;
            var star = StarPowerNoteGroups[DEDICATED_KICK].transform;
            star.localScale = _starStompScale;
            star.localPosition = _starStompPosition;
        }

        private void FitGemWidth(int groupIndex, float targetWidth, float targetCenter)
        {
            if (groupIndex >= FUSED_FLAM_START)
            {
                int columns = Player.Layout.Lanes.Count;
                float elementCenter = transform.localPosition.x;
                EliteDrumsRuntimeGeometry.FitFusedFlam(Geometry(0, groupIndex), NoteGroups[groupIndex].transform,
                    columns, targetCenter, targetWidth, elementCenter);
                if (StarPowerNoteGroups[groupIndex] != NoteGroups[groupIndex])
                    EliteDrumsRuntimeGeometry.FitFusedFlam(Geometry(1, groupIndex), StarPowerNoteGroups[groupIndex].transform,
                        columns, targetCenter, targetWidth, elementCenter);
                return;
            }

            ScaleGroupToFiveLaneWidth(NoteGroups[groupIndex], _normalGemScales[groupIndex], targetWidth, targetCenter);
            if (StarPowerNoteGroups[groupIndex] != NoteGroups[groupIndex])
                ScaleGroupToFiveLaneWidth(StarPowerNoteGroups[groupIndex], _starGemScales[groupIndex], targetWidth, targetCenter);
        }

        private void ScaleGroupToFiveLaneWidth(NoteGroup group, Vector3 authoredScale,
            float targetWidth, float targetCenter)
        {
            // Scale all note models from the same five-lane baseline. Unlike fitting
            // renderer bounds independently, this preserves authored model ratios.
            var groupTransform = group.transform;
            float factor = EliteDrumsRuntimeGeometry.GetAuthoredLaneScale(targetWidth,
                TrackPlayer.TRACK_WIDTH / 5f);
            groupTransform.localScale = new Vector3(authoredScale.x * factor, authoredScale.y, authoredScale.z);
            var position = groupTransform.localPosition;
            groupTransform.localPosition = new Vector3(targetCenter - transform.localPosition.x, position.y, position.z);
        }

        private void PlaceInRollSlot(int group, bool splitFlam, float columnWidth, float center, float width)
        {
            float min = float.PositiveInfinity;
            float max = float.NegativeInfinity;
            for (int variant = 0; variant < 2; variant++)
            {
                if (splitFlam)
                {
                    Include(2 + variant, group * 2 + SPLIT_LEFT);
                    Include(2 + variant, group * 2 + SPLIT_RIGHT);
                }
                else Include(variant, group);
            }
            // A small ghost keeps its authored size relative to a neutral.
            // Only an oversized glyph (e.g. a fused or angled flam) needs more
            // compression than the column itself. The root scales both halves
            // together while preserving their depth, height and contact seam.
            float scale = width / Mathf.Max(columnWidth, max - min);
            float midpoint = min <= max ? (min + max) * .5f : 0f;
            transform.localScale = transform.localScale.WithX(scale);
            transform.localPosition = transform.localPosition.WithX(center - scale * midpoint);

            void Include(int kind, int index)
            {
                var model = GeometryGroups(kind)[index].transform;
                Geometry(kind, index).EncapsulatePlacedX(model, ref min, ref max);
            }
        }

        private void FitFootWidth(int groupIndex, float targetWidth, float targetCenter)
        {
            // Fit both variants: a later star-power swap must not restore the overlap.
            FitFootWidth(NoteGroups[groupIndex], targetWidth, targetCenter);
            if (StarPowerNoteGroups[groupIndex] != NoteGroups[groupIndex])
                FitFootWidth(StarPowerNoteGroups[groupIndex], targetWidth, targetCenter);
        }

        private void FitFootWidth(NoteGroup group, float targetWidth, float targetCenter) =>
            FitGroupWidth(transform, group, targetWidth, targetCenter);

        private static void FitGroupWidth(Transform elementTransform, NoteGroup group,
            float targetWidth, float targetCenter)
        {
            // Include the theme's end caps and highlights rather than assuming
            // its mesh fills a particular fraction of the highway.
            var renderers = group.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;
            float left = float.PositiveInfinity;
            float right = float.NegativeInfinity;
            foreach (var renderer in renderers)
            {
                var bounds = renderer.bounds;
                left = Mathf.Min(left, bounds.min.x);
                right = Mathf.Max(right, bounds.max.x);
            }
            float width = right - left;
            float parentScale = Mathf.Abs(elementTransform.lossyScale.x);
            if (width <= 0 || parentScale <= 0) return;
            var groupTransform = group.transform;
            var scale = groupTransform.localScale;
            float factor = targetWidth * parentScale / width;
            groupTransform.localScale = new Vector3(scale.x * factor, scale.y, scale.z);
            float center = (left + right) * 0.5f;
            var position = groupTransform.localPosition;
            // Renderer bounds are world-space; correct the scaled world center
            // back to the element's intended half-highway center.
            float scaledCenter = groupTransform.position.x + (center - groupTransform.position.x) * factor;
            float targetWorldCenter = elementTransform.TransformPoint(new Vector3(
                targetCenter - elementTransform.localPosition.x, 0, 0)).x;
            groupTransform.localPosition = new Vector3(position.x +
                (targetWorldCenter - scaledCenter) / parentScale, position.y, position.z);
        }

        protected override bool CalcStarPowerVisible() => NoteRef.IsStarPower &&
            !(((YARG.Core.Engine.Drums.DrumsEngineParameters) Player.BaseParameters).NoStarPowerOverlap &&
                Player.BaseStats.IsStarPowerActive);

        protected override void HideElement()
        {
            HideNotes();
            if (_splitGemGroups != null)
                foreach (var group in _splitGemGroups) if (group != null) group.SetActive(false);
            if (_splitStarGemGroups != null)
                foreach (var group in _splitStarGemGroups) if (group != null) group.SetActive(false);
        }

        protected override void InitializeElement()
        {
            base.InitializeElement();
            ResetGemGroups();
            ResetStompGroups();
            _hasRollSlot = GetFittingRegion(Player, NoteRef, out float center, out float width,
                out float slotCenter, out float slotWidth);
            transform.localPosition = new Vector3(center, 0, 0);
            bool kick = EliteDrumsPlayer.IsFootPad(NoteRef.Pad);
            var groups = IsStarPowerVisible ? StarPowerNoteGroups : NoteGroups;
            var profile = Player.Player.Profile;
            bool splitFlam = UsesSplitFlam(NoteRef, profile);
            int group = kick ? GetKickGroup(NoteRef, !Player.UsesKickBars) : splitFlam ? GetSplitGroup() :
                SelectModelGroup(NoteRef, profile);
            NoteGroup = groups[group];
            if (splitFlam)
                InitializeSplitFlam(group, center, width);
            else if (kick || group is HAT_STOMP or HAT_SPLASH)
            {
                if (group is HAT_STOMP or HAT_SPLASH)
                {
                    int sections = EliteDrumsRuntimeGeometry.GetPedalRegion(Player.GetVisualPosition(NoteRef),
                        Player.Layout.Lanes.Count, TrackPlayer.TRACK_WIDTH, out _, out _);
                    NoteGroups[group].ConfigurePedalSections(sections);
                    if (StarPowerNoteGroups[group] != NoteGroups[group])
                        StarPowerNoteGroups[group].ConfigurePedalSections(sections);
                }
                else if (Player.UsesKickBars)
                {
                    int laneCount = Player.Layout.Lanes.Count;
                    var boundaries = Player.TryGetKickBoundaries(NoteRef);
                    NoteGroups[group].ConfigureKickLanes(laneCount, boundaries);
                    if (StarPowerNoteGroups[group] != NoteGroups[group])
                        StarPowerNoteGroups[group].ConfigureKickLanes(laneCount, boundaries);
                }
                FitFootWidth(group, width, center);
            }
            else if (!_hasRollSlot && EliteDrumsRuntimeGeometry.GetCollisionPartner(Player, NoteRef) is { } partner)
            {
                FitCollision(group, partner, center, width);
            }
            else
                FitGemWidth(group, width, center);
            if (_hasRollSlot)
                PlaceInRollSlot(group, splitFlam, width, slotCenter, slotWidth);
            if (!splitFlam)
            {
                NoteGroup.SetFlatFlamMarker(NoteRef.IsFlatFlam);
                NoteGroup.SetActive(true);
                NoteGroup.Initialize();
            }
            ApplyPedalVisualLead(NoteGroup);
            UpdateColor();
        }

        private void FitCollision(int group, EliteDrumNote partner, float center, float width)
        {
            int otherGroup = SelectModelGroup(partner, Player.Player.Profile);
            bool partnerStar = partner.IsStarPower &&
                !(((YARG.Core.Engine.Drums.DrumsEngineParameters)Player.BaseParameters).NoStarPowerOverlap &&
                  Player.BaseStats.IsStarPowerActive);
            _lastCollisionPartnerStar = partnerStar;
            var other = (partnerStar ? StarPowerNoteGroups : NoteGroups)[otherGroup];
            bool right = EliteDrumsPlayer.IsCymbal(NoteRef.Pad) != LeftyFlip;
            for (int variant = 0; variant < 2; variant++)
            {
                var own = (variant == 0 ? NoteGroups : StarPowerNoteGroups)[group];
                EliteDrumsRuntimeGeometry.FitPair(Geometry(right ? (partnerStar ? 1 : 0) : variant, right ? otherGroup : group), right ? null : own.transform,
                    Geometry(right ? variant : (partnerStar ? 1 : 0), right ? group : otherGroup), right ? own.transform : null,
                    Player.Layout.Lanes.Count, center, width, transform.localPosition.x, false);
            }
        }

        protected override void UpdateElement()
        {
            base.UpdateElement();
            UpdateColor();
            // The paired note is judged independently and can lose SP without this
            // note changing. Both envelopes must use the same currently visible models.
            if (!_hasRollSlot && !UsesSplitFlam(NoteRef, Player.Player.Profile) &&
                EliteDrumsRuntimeGeometry.GetCollisionPartner(Player, NoteRef) is { } partner)
            {
                bool partnerStar = partner.IsStarPower &&
                    !(((YARG.Core.Engine.Drums.DrumsEngineParameters)Player.BaseParameters).NoStarPowerOverlap &&
                      Player.BaseStats.IsStarPowerActive);
                if (partnerStar != _lastCollisionPartnerStar)
                {
                    EliteDrumsRuntimeGeometry.GetRegion(Player, NoteRef, false, out float center, out float width);
                    FitCollision(SelectModelGroup(NoteRef, Player.Player.Profile), partner, center, width);
                }
            }
            ApplyPedalVisualLead(NoteGroup);
        }

        private void ApplyPedalVisualLead(NoteGroup renderedGroup)
        {
            if (renderedGroup == null) return;
            var note = NoteRef;
            if (note.Pad != (int) EliteDrumNote.EliteDrumPad.HatPedal || !(note.IsStomp || note.IsSplash))
                return;

            var sync = GameManager.Chart?.SyncTrack;
            double tickDuration = EliteDrumsRuntimeGeometry.GetNextTickDuration(sync, note.Tick);
            var renderedTransform = renderedGroup.transform;
            renderedTransform.localPosition = renderedTransform.localPosition.WithZ(
                renderedGroup.PedalStrikeAligned ? 0f :
                    EliteDrumsRuntimeGeometry.GetVisualLeadOffset(Player.NoteSpeed, tickDuration));
        }

        public override void HitNote()
        {
            base.HitNote();
            ParentPool.Return(this);
        }

        public override void OnStarPowerUpdated()
        {
            if (UsesSplitFlam(NoteRef, Player.Player.Profile))
            {
                foreach (var group in _splitGemGroups) group.SetActive(false);
                foreach (var group in _splitStarGemGroups) group.SetActive(false);
                int baseIndex = GetSplitGroup() * 2;
                var selected = IsStarPowerVisible ? _splitStarGemGroups : _splitGemGroups;
                selected[baseIndex + SPLIT_LEFT].SetActive(!NoteRef.WasHit);
                selected[baseIndex + SPLIT_RIGHT].SetActive(!NoteRef.WasHit);
                if (!NoteRef.WasHit)
                {
                    selected[baseIndex + SPLIT_LEFT].Initialize();
                    selected[baseIndex + SPLIT_RIGHT].Initialize();
                }
                NoteGroup = selected[baseIndex + SPLIT_LEFT];
            }
            else
            {
                base.OnStarPowerUpdated();
                NoteGroup.SetFlatFlamMarker(NoteRef.IsFlatFlam);
                if (!_hasRollSlot && EliteDrumsRuntimeGeometry.GetCollisionPartner(Player, NoteRef) is { } partner)
                {
                    EliteDrumsRuntimeGeometry.GetRegion(Player, NoteRef, false, out float center, out float width);
                    FitCollision(SelectModelGroup(NoteRef, Player.Player.Profile), partner, center, width);
                }
            }
            ApplyPedalVisualLead(NoteGroup);
            UpdateColor();
        }

        internal static int GetGemGroup(EliteDrumNote note, bool useCymbalModels)
        {
            return GetGemGroupForSplitFlam(note, useCymbalModels, false);
        }

        internal static int GetGemGroupForSplitFlam(EliteDrumNote note, bool useCymbalModels,
            bool splitEliteFlamGems)
        {
            if (IsSplitFlam(note, splitEliteFlamGems))
                return GetSplitGroup(note, useCymbalModels);
            if (note.Pad == (int) EliteDrumNote.EliteDrumPad.Wildcard) return WILDCARD;
            if (EliteDrumsPlayer.IsFootPad(note.Pad)) return GetKickGroup(note, false);
            // The authored flam flag is a visual cue in native V1, not a second scored hit.
            if (note.IsFlam || note.IsFlatFlam) return ACCENT;
            // Pedal gestures have independent optional models; legacy themes retain their own bar.
            if (note.Pad == (int) EliteDrumNote.EliteDrumPad.HatPedal)
                return note.IsSplash ? HAT_SPLASH : HAT_STOMP;
            return GetSplitGroup(note, useCymbalModels);
        }

        private static int GetKickGroup(EliteDrumNote note, bool dedicated)
        {
            var type = EliteKickAppearance.Select(note.IsAccent, note.IsGhost,
                note.IsFlam || note.IsFlatFlam, dedicated);
            if (type == ThemeNoteType.Kick) return KICK;
            if (type == ThemeNoteType.DedicatedLaneKick) return DEDICATED_KICK;
            return KICK_VARIANTS_START + EliteKickAppearance.OptionalTypes.IndexOf(type);
        }

        internal static (System.Drawing.Color Body, System.Drawing.Color Emission, System.Drawing.Color Metal)
            GetGemColors(ColorProfile.EliteDrumsColors colors, EliteDrumNote note, bool splitEliteFlamGems,
                bool starPower, bool missed) => GetGemColorsWithCymbalPreference(colors, note,
                    splitEliteFlamGems, starPower, missed, false);

        internal static (System.Drawing.Color Body, System.Drawing.Color Emission, System.Drawing.Color Metal)
            GetGemColorsWithCymbalPreference(ColorProfile.EliteDrumsColors colors, EliteDrumNote note,
                bool splitEliteFlamGems, bool starPower, bool missed, bool keepOriginalCymbalColors)
        {
            // Core owns note identity, including future playable roles such as wildcard.
            // Mirrored placement and shared tom/cymbal positions never select colors.
            var role = EliteDrumsPlayer.IsFootPad(note.Pad) && (note.IsFlam || note.IsFlatFlam)
                ? EliteDrumsColorRole.KickFlam
                : EliteDrumsColorRoles.GetRole(note, IsSplitFlam(note, splitEliteFlamGems), keepOriginalCymbalColors);
            var original = colors.GetNoteColor(role);
            var body = missed ? colors.Miss : starPower ? colors.GetNoteStarPowerColor(role) : original;
            return (body, original, colors.GetMetalColor(starPower));
        }

        // Match regular drums' beat pulse without borrowing another mode's palette.
        internal static System.Drawing.Color GetActivationColor(System.Drawing.Color color, bool activation,
            float phase)
        {
            if (!activation) return color;
            float intensity = Mathf.Pow(Mathf.Clamp01(phase) - 1f, 3f) + 1f;
            return System.Drawing.Color.FromArgb(color.A, (int)(color.R * intensity),
                (int)(color.G * intensity), (int)(color.B * intensity));
        }

        internal void RefreshOpenHatGlow()
        {
            if (NoteGroup == null || NoteRef == null || NoteRef.WasHit) return;
            bool openHat = NoteRef.IsOpen && NoteRef.Pad == (int)EliteDrumNote.EliteDrumPad.HiHat;
            if (!openHat && !NoteRef.IsFlam && !NoteRef.IsFlatFlam) return;

            var profile = Player.Player.Profile;
            float rate = Player.OpenHatPulsesPerQuarter;
            NoteGroup.ConfigureOpenHatGlow(profile.OpenHatGlow, rate, preview: false, leftyFlip: LeftyFlip);
            if (openHat && UsesSplitFlam(NoteRef, profile))
            {
                // For a split flam NoteGroup is the left half.
                var groups = IsStarPowerVisible ? _splitStarGemGroups : _splitGemGroups;
                groups[GetSplitGroup() * 2 + SPLIT_RIGHT].ConfigureOpenHatGlow(profile.OpenHatGlow, rate,
                    preview: false, leftyFlip: LeftyFlip);
            }
        }

        private void UpdateColor()
        {
            if (NoteGroup == null || NoteRef.WasHit) return;
            bool splitFlam = IsSplitFlam(NoteRef, true);
            var (color, original, metal) = GetGemColorsWithCymbalPreference(Player.Player.ColorProfile.EliteDrums,
                NoteRef, splitFlam, IsStarPowerVisible, NoteRef.WasMissed,
                Player.Player.Profile.KeepOriginalCymbalColors);
            bool activation = NoteRef.IsStarPowerActivator && !NoteRef.WasMissed &&
                Player.Engine.CanStarPowerActivate && !Player.Engine.BaseStats.IsStarPowerActive;
            float phase = activation ? (float)GameManager.BeatEventHandler.Visual.StrongBeat.CurrentPercentage : 0f;
            color = GetActivationColor(activation ? original : color, activation, phase);
            NoteGroup.SetColorWithEmission(color.ToUnityColor(), original.ToUnityColor());
            RefreshOpenHatGlow();
            NoteGroup.SetMetalColor(metal.ToUnityColor());
            if (EliteDrumsPlayer.IsFootPad(NoteRef.Pad) && (NoteRef.IsFlam || NoteRef.IsFlatFlam)
                && NoteGroup.HasKickFlamColors)
            {
                var (lower, upper) = EliteKickAppearance.FlamColors(Player.Player.ColorProfile.EliteDrums,
                    IsStarPowerVisible, NoteRef.WasMissed);
                if (activation)
                    (lower, upper) = EliteKickAppearance.FlamColors(Player.Player.ColorProfile.EliteDrums, false, false);
                lower = GetActivationColor(lower, activation, phase);
                upper = GetActivationColor(upper, activation, phase);
                NoteGroup.SetKickFlamColors(lower.ToUnityColor(), upper.ToUnityColor(), LeftyFlip);
            }
            if (UsesSplitFlam(NoteRef, Player.Player.Profile))
            {
                int baseIndex = GetSplitGroup() * 2;
                var groups = IsStarPowerVisible ? _splitStarGemGroups : _splitGemGroups;
                for (int side = SPLIT_LEFT; side <= SPLIT_RIGHT; side++)
                {
                    groups[baseIndex + side].SetColorWithEmission(color.ToUnityColor(), original.ToUnityColor());
                    groups[baseIndex + side].SetMetalColor(metal.ToUnityColor());
                }
            }
        }
    }
}
