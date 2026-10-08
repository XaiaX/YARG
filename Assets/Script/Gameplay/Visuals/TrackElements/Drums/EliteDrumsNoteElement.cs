using System.Collections.Generic;
using UnityEngine;
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
        private const int STOMP = 7;
        private const int OPEN_HI_HAT = 8;
        private const int OPEN_HI_HAT_ACCENT = 9;
        private const int OPEN_HI_HAT_GHOST = 10;
        private const int CLOSED_HI_HAT = 11;
        private const int CLOSED_HI_HAT_ACCENT = 12;
        private const int CLOSED_HI_HAT_GHOST = 13;
        private const int WILDCARD = 14;
        private const int COUNT = 15;
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
        private const int FLAM_STAGGER_TICKS = 7;

        public override void SetThemeModels(Dictionary<ThemeNoteType, GameObject> models,
            Dictionary<ThemeNoteType, GameObject> starpowerModels)
        {
            (models, starpowerModels) = ThemeNoteModelFallbacks.ResolveEliteModels(models, starpowerModels);
            CreateNoteGroupArrays(COUNT);
            AssignNoteGroup(models, starpowerModels, NORMAL, ThemeNoteType.Normal);
            AssignNoteGroup(models, starpowerModels, CYMBAL, ThemeNoteType.Cymbal);
            AssignNoteGroup(models, starpowerModels, KICK, ThemeNoteType.Kick);
            AssignNoteGroup(models, starpowerModels, ACCENT, ThemeNoteType.Accent);
            AssignNoteGroup(models, starpowerModels, GHOST, ThemeNoteType.Ghost);
            AssignNoteGroup(models, starpowerModels, CYMBAL_ACCENT, ThemeNoteType.CymbalAccent);
            AssignNoteGroup(models, starpowerModels, CYMBAL_GHOST, ThemeNoteType.CymbalGhost);
            AssignNoteGroup(models, starpowerModels, STOMP, ThemeNoteType.DedicatedLaneKick);
            AssignNoteGroup(models, starpowerModels, OPEN_HI_HAT, ThemeNoteType.OpenHiHat);
            AssignNoteGroup(models, starpowerModels, OPEN_HI_HAT_ACCENT, ThemeNoteType.OpenHiHatAccent);
            AssignNoteGroup(models, starpowerModels, OPEN_HI_HAT_GHOST, ThemeNoteType.OpenHiHatGhost);
            AssignNoteGroup(models, starpowerModels, CLOSED_HI_HAT, ThemeNoteType.ClosedHiHat);
            AssignNoteGroup(models, starpowerModels, CLOSED_HI_HAT_ACCENT, ThemeNoteType.ClosedHiHatAccent);
            AssignNoteGroup(models, starpowerModels, CLOSED_HI_HAT_GHOST, ThemeNoteType.ClosedHiHatGhost);

            AssignNoteGroup(models, starpowerModels, WILDCARD, ThemeNoteType.Wildcard);

            ScaleStompModel(NoteGroups[STOMP]);
            if (StarPowerNoteGroups[STOMP] != NoteGroups[STOMP])
                ScaleStompModel(StarPowerNoteGroups[STOMP]);
            _normalStompScale = NoteGroups[STOMP].transform.localScale;
            _starStompScale = StarPowerNoteGroups[STOMP].transform.localScale;
            _normalStompPosition = NoteGroups[STOMP].transform.localPosition;
            _starStompPosition = StarPowerNoteGroups[STOMP].transform.localPosition;
            CreateSplitGemGroups();
            for (int i = 0; i < COUNT; i++)
            {
                _normalGemScales[i] = NoteGroups[i].transform.localScale;
                _normalGemPositions[i] = NoteGroups[i].transform.localPosition;
                _starGemScales[i] = StarPowerNoteGroups[i].transform.localScale;
                _starGemPositions[i] = StarPowerNoteGroups[i].transform.localPosition;
            }
        }

        // Layout for same-tick tom/cymbal pairs that share one native Elite lane.
        // Returns the selected note's center and rendered width in highway units.
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

            float laneWidth = TrackPlayer.TRACK_WIDTH / 5f;
            float mirror = lefty ? -1f : 1f;
            float sharedLaneCenter = mirror * GetElementX(EliteDrumsPlayer.GetLane((int) tomPad), 5);
            targetWidth = offset ? laneWidth * 5f / 6f : laneWidth / 2f;

            if (offset)
            {
                // For the inner pairs, contact at the lane center and 1/3 lane
                // beyond its outer boundary require gems 5/6 lane wide.
                // At the outside edge, use the requested 2/3-lane gems instead: right crash
                // stays inside the highway, and tom3 reaches toward ride.
                if (tomPad == EliteDrumNote.EliteDrumPad.Tom3)
                {
                    targetWidth = laneWidth * 2f / 3f;
                    float cymbalCenter = mirror * (TrackPlayer.TRACK_WIDTH / 2f - targetWidth / 2f);
                    x = isCymbal ? cymbalCenter : cymbalCenter - mirror * targetWidth;
                }
                else
                    x = sharedLaneCenter + (isTom ? -1f : 1f) * mirror * targetWidth / 2f;
            }
            else
            {
                float halfSeparation = targetWidth / 2f;
                x = sharedLaneCenter + (isTom ? -1f : 1f) * mirror * halfSeparation;
            }
            return true;
        }

        private static void ScaleStompModel(NoteGroup stompModel)
        {
            var scale = stompModel.transform.localScale;
            stompModel.transform.localScale = new Vector3(scale.x * 3f, scale.y, scale.z);
        }

        // In a pedal+kick chord, both use the same bar model and occupy opposite
        // halves of the highway. Keep this choice independent of chord parent order.
        internal static bool TryGetPairedPedalKickLayout(EliteDrumNote note, bool lefty,
            out float x, out float targetWidth)
        {
            x = 0;
            targetWidth = 1;
            bool isKick = note.Pad == (int) EliteDrumNote.EliteDrumPad.Kick;
            bool isPedal = note.Pad == (int) EliteDrumNote.EliteDrumPad.HatPedal &&
                !note.IsInvisibleTerminator;
            if (!isKick && !isPedal) return false;

            bool hasCounterpart = false;
            foreach (var member in note.ParentOrSelf.AllNotes)
            {
                if (isKick && !note.IsDoubleKick &&
                    member.Pad == (int) EliteDrumNote.EliteDrumPad.HatPedal &&
                    !member.IsInvisibleTerminator)
                    hasCounterpart = true;
                if (isPedal && member.Pad == (int) EliteDrumNote.EliteDrumPad.Kick &&
                    !member.IsDoubleKick)
                    hasCounterpart = true;
            }
            if (!hasCounterpart) return false;

            x = (isPedal == lefty ? 1f : -1f) * TrackPlayer.TRACK_WIDTH / 4f;
            targetWidth = TrackPlayer.TRACK_WIDTH / 2f;
            return true;
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

        private void InitializeSplitFlam(int group, float laneCenter)
        {
            int baseIndex = group * 2;
            bool starPower = IsStarPowerVisible;
            NoteGroup left = starPower ? _splitStarGemGroups[baseIndex + SPLIT_LEFT] :
                _splitGemGroups[baseIndex + SPLIT_LEFT];
            NoteGroup right = starPower ? _splitStarGemGroups[baseIndex + SPLIT_RIGHT] :
                _splitGemGroups[baseIndex + SPLIT_RIGHT];
            var source = starPower ? StarPowerNoteGroups[group] : NoteGroups[group];
            source.SetActive(false);

            float laneWidth = TrackPlayer.TRACK_WIDTH / Player.LaneCount;
            GetSplitFlamCenters(laneCenter, laneWidth, LeftyFlip, out float leftCenter, out float rightCenter);
            float width = laneWidth * 2f / 3f;
            FitGroupWidth(transform, left, width, leftCenter);
            FitGroupWidth(transform, right, width, rightCenter);
            bool lefty = LeftyFlip;
            int leftOffset = GetSplitFlamTickOffset(SPLIT_LEFT, NoteRef.IsFlatFlam, lefty);
            int rightOffset = GetSplitFlamTickOffset(SPLIT_RIGHT, NoteRef.IsFlatFlam, lefty);
            float leftTime = leftOffset == 0 ? 0f : (float) GetFlamStaggerTime(GameManager.Chart.SyncTrack, NoteRef.Tick, leftOffset);
            float rightTime = rightOffset == 0 ? 0f : (float) GetFlamStaggerTime(GameManager.Chart.SyncTrack, NoteRef.Tick, rightOffset);
            left.transform.localPosition = left.transform.localPosition.WithZ((float) (leftTime * Player.NoteSpeed));
            right.transform.localPosition = right.transform.localPosition.WithZ((float) (rightTime * Player.NoteSpeed));
            left.SetActive(true);
            right.SetActive(true);
            left.Initialize();
            right.Initialize();
            NoteGroup = left;
        }

        internal static int GetSplitFlamTickOffset(int side, bool flatFlam, bool lefty)
        {
            if (flatFlam) return 0;
            // The leading visual gem is early; the other remains exactly on the authored beat.
            // Mirroring the layout for lefty reverses which visual side leads.
            return side == (lefty ? SPLIT_RIGHT : SPLIT_LEFT) ? -FLAM_STAGGER_TICKS : 0;
        }

        internal static bool IsHandFlamPad(int pad) => (EliteDrumNote.EliteDrumPad) pad is
            EliteDrumNote.EliteDrumPad.Snare or EliteDrumNote.EliteDrumPad.HiHat or
            EliteDrumNote.EliteDrumPad.LeftCrash or EliteDrumNote.EliteDrumPad.Tom1 or
            EliteDrumNote.EliteDrumPad.Tom2 or EliteDrumNote.EliteDrumPad.Ride or
            EliteDrumNote.EliteDrumPad.Tom3 or EliteDrumNote.EliteDrumPad.RightCrash;

        internal static bool IsSplitFlam(EliteDrumNote note, bool enabled) =>
            enabled && (note.IsFlam || note.IsFlatFlam) && IsHandFlamPad(note.Pad);

        private int GetSplitGroup() => GetSplitGroup(NoteRef, Player.Player.Profile.UseCymbalModels);

        internal static int GetSplitGroup(EliteDrumNote note, bool useCymbalModels)
        {
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
            uint staggerTick = tickOffset < 0 ? noteTick - (uint) Mathf.Min((int) noteTick, -tickOffset) :
                noteTick + (uint) tickOffset;
            return syncTrack.TickToTime(staggerTick) - syncTrack.TickToTime(noteTick);
        }

        private void ResetGemGroups()
        {
            ResetSplitGemGroups();
            for (int i = 0; i < COUNT; i++)
            {
                NoteGroups[i].transform.localScale = _normalGemScales[i];
                NoteGroups[i].transform.localPosition = _normalGemPositions[i];
                if (StarPowerNoteGroups[i] == NoteGroups[i]) continue;
                StarPowerNoteGroups[i].transform.localScale = _starGemScales[i];
                StarPowerNoteGroups[i].transform.localPosition = _starGemPositions[i];
            }
        }

        private void ResetStompGroups()
        {
            var normal = NoteGroups[STOMP].transform;
            normal.localScale = _normalStompScale;
            normal.localPosition = _normalStompPosition;
            if (StarPowerNoteGroups[STOMP] == NoteGroups[STOMP]) return;
            var star = StarPowerNoteGroups[STOMP].transform;
            star.localScale = _starStompScale;
            star.localPosition = _starStompPosition;
        }

        private void FitGemWidth(int groupIndex, float targetWidth, float targetCenter)
        {
            FitGroupWidth(transform, NoteGroups[groupIndex], targetWidth, targetCenter);
            if (StarPowerNoteGroups[groupIndex] != NoteGroups[groupIndex])
                FitGroupWidth(transform, StarPowerNoteGroups[groupIndex], targetWidth, targetCenter);
        }

        private void FitBarWidth(float targetWidth, float targetCenter)
        {
            // Fit both variants: a later star-power swap must not restore the overlap.
            FitBarWidth(NoteGroups[STOMP], targetWidth, targetCenter);
            if (StarPowerNoteGroups[STOMP] != NoteGroups[STOMP])
                FitBarWidth(StarPowerNoteGroups[STOMP], targetWidth, targetCenter);
        }

        private void FitBarWidth(NoteGroup group, float targetWidth, float targetCenter) =>
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
            bool wildcard = NoteRef.Pad == (int) EliteDrumNote.EliteDrumPad.Wildcard;
            bool foot = EliteDrumsPlayer.IsFootPad(NoteRef.Pad) || wildcard;
            int lane = Player.GetVisualPosition(NoteRef.Pad);
            transform.localPosition = foot ? Vector3.zero :
                new Vector3(GetElementX(lane, Player.LaneCount), 0, 0);
            bool paired = TryGetPairedPedalKickLayout(NoteRef, Player.Player.Profile.LeftyFlip,
                out float pairedX, out float targetWidth);
            if (paired)
                transform.localPosition = new Vector3(pairedX, 0, 0);
            float tomCymbalX = 0;
            float tomCymbalWidth = 1;
            bool pairedTomCymbal = !paired &&
                TryGetTomCymbalLayout(NoteRef, Player.Player.Profile.LeftyFlip,
                    SettingsManager.Settings.OffsetEliteTomCymbalGems.Value, out tomCymbalX,
                    out tomCymbalWidth);
            if (pairedTomCymbal)
            {
                transform.localPosition = new Vector3(tomCymbalX, 0, 0);
                targetWidth = tomCymbalWidth;
            }
            var groups = IsStarPowerVisible ? StarPowerNoteGroups : NoteGroups;
            bool splitFlam = IsSplitFlam(NoteRef, SettingsManager.Settings.SplitEliteFlamGems.Value) && !paired;
            int group = paired ? STOMP : splitFlam ? GetSplitGroup() :
                GetGemGroup(NoteRef, Player.Player.Profile.UseCymbalModels);
            NoteGroup = groups[group];
            if (splitFlam)
                InitializeSplitFlam(group, GetElementX(EliteDrumsPlayer.GetLane(NoteRef.Pad), Player.LaneCount));
            if (wildcard)
                FitGemWidth(group, TrackPlayer.TRACK_WIDTH, 0f);
            else if (paired)
                FitBarWidth(targetWidth, pairedX);
            else if (pairedTomCymbal)
            {
                FitGemWidth(group, targetWidth, tomCymbalX);
            }
            if (!splitFlam)
            {
                NoteGroup.SetActive(true);
                NoteGroup.Initialize();
            }
            UpdateColor();
        }

        protected override void UpdateElement() => base.UpdateElement();

        public override void HitNote()
        {
            base.HitNote();
            ParentPool.Return(this);
        }

        public override void OnStarPowerUpdated()
        {
            if (IsSplitFlam(NoteRef, SettingsManager.Settings.SplitEliteFlamGems.Value))
            {
                foreach (var group in _splitGemGroups) group.SetActive(false);
                foreach (var group in _splitStarGemGroups) group.SetActive(false);
                int baseIndex = GetSplitGroup() * 2;
                var selected = IsStarPowerVisible ? _splitStarGemGroups : _splitGemGroups;
                selected[baseIndex + SPLIT_LEFT].SetActive(!NoteRef.WasHit);
                selected[baseIndex + SPLIT_RIGHT].SetActive(!NoteRef.WasHit);
                NoteGroup = selected[baseIndex + SPLIT_LEFT];
            }
            else
            {
                base.OnStarPowerUpdated();
            }
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
            if (EliteDrumsPlayer.IsFootPad(note.Pad)) return KICK;
            // The authored flam flag is a visual cue in native V1, not a second scored hit.
            if (note.IsFlam || note.IsFlatFlam) return ACCENT;
            // Both playable pedal events use the bar; dedicated roles distinguish stomp and splash.
            if (note.Pad == (int) EliteDrumNote.EliteDrumPad.HatPedal)
                return STOMP;
            return GetSplitGroup(note, useCymbalModels);
        }

        internal static (System.Drawing.Color Body, System.Drawing.Color Emission, System.Drawing.Color Metal)
            GetGemColors(ColorProfile.EliteDrumsColors colors, EliteDrumNote note, bool splitEliteFlamGems,
                bool starPower, bool missed)
        {
            // Core owns note identity, including future playable roles such as wildcard.
            // Mirrored placement and shared tom/cymbal positions never select colors.
            var role = EliteDrumsColorRoles.GetRole(note, IsSplitFlam(note, splitEliteFlamGems));
            var original = colors.GetNoteColor(role);
            var body = missed ? colors.Miss : starPower ? colors.GetNoteStarPowerColor(role) : original;
            return (body, original, colors.GetMetalColor(starPower));
        }

        private void UpdateColor()
        {
            if (NoteGroup == null || NoteRef.WasHit) return;
            bool splitFlam = IsSplitFlam(NoteRef, SettingsManager.Settings.SplitEliteFlamGems.Value);
            var (color, original, metal) = GetGemColors(Player.Player.ColorProfile.EliteDrums,
                NoteRef, splitFlam, IsStarPowerVisible, NoteRef.WasMissed);
            NoteGroup.SetColorWithEmission(color.ToUnityColor(), original.ToUnityColor());
            NoteGroup.SetMetalColor(metal.ToUnityColor());
            if (IsSplitFlam(NoteRef, SettingsManager.Settings.SplitEliteFlamGems.Value))
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
