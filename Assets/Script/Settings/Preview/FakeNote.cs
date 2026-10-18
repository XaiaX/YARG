using System;
using System.Collections.Generic;
using UnityEngine;
using YARG.Core;
using YARG.Core.Game;
using YARG.Gameplay;
using YARG.Gameplay.Player;
using YARG.Gameplay.Visuals;
using YARG.Helpers.Extensions;
using YARG.Settings.Customization;
using YARG.Settings.Metadata;
using YARG.Themes;

namespace YARG.Settings.Preview
{
    // pattern: Imperative Shell
    public class FakeNote : MonoBehaviour, IPoolable
    {
        [Serializable]
        public struct NoteTypePair
        {
            public ThemeNoteType NoteType;
            public bool StarPower;
            public NoteGroup Group;
        }

        public Pool ParentPool { get; set; }

        public FakeNoteData NoteRef { get; set; }
        public FakeTrackPlayer FakeTrackPlayer { get; set; }

        private NoteGroup _currentNoteGroup;
        private readonly Dictionary<NoteGroup, Vector3> _authoredGroupScales = new();
        private readonly Dictionary<NoteGroup, Vector3> _authoredGroupPositions = new();
        private readonly Dictionary<NoteGroup, EliteDrumsRuntimeGeometry.ModelGeometry> _geometry = new();
        private readonly Dictionary<NoteGroup, NoteGroup> _flamCompanions = new();

        private EliteDrumsRuntimeGeometry.ModelGeometry Geometry(NoteGroup group)
        {
            if (!_geometry.TryGetValue(group, out var geometry))
            {
                geometry = new EliteDrumsRuntimeGeometry.ModelGeometry(group.transform);
                _geometry.Add(group, geometry);
            }
            return geometry;
        }

        // We can't use a dictionary here (Unity L)
        [SerializeField]
        private List<NoteTypePair> _noteGroups;

        private readonly Dictionary<Material, Material> _materialDefaults = new();

        public void EnableFromPool()
        {
            if (_materialDefaults.Count == 0)
            {
                foreach (var renderer in GetComponentsInChildren<MeshRenderer>(true))
                {
                    foreach (var material in renderer.materials)
                    {
                        if (!_materialDefaults.ContainsKey(material))
                            _materialDefaults.Add(material, new Material(material));
                    }
                }
            }
            // Capture and restore theme-authored transforms before each pooled use.
            // This keeps repeated layout/model changes from accumulating scale or offset.
            foreach (var noteGroup in _noteGroups)
            {
                if (!_authoredGroupScales.ContainsKey(noteGroup.Group))
                {
                    _authoredGroupScales.Add(noteGroup.Group, noteGroup.Group.transform.localScale);
                    _authoredGroupPositions.Add(noteGroup.Group, noteGroup.Group.transform.localPosition);
                }
                Geometry(noteGroup.Group).Reset(noteGroup.Group.transform);
                noteGroup.Group.transform.localScale = _authoredGroupScales[noteGroup.Group];
                noteGroup.Group.transform.localPosition = _authoredGroupPositions[noteGroup.Group];
                noteGroup.Group.SetActive(false);
            }

            // Find the correct note group, with hierarchical fallback for
            // cymbal variants (CymbalAccent/CymbalGhost → Cymbal → Normal)
            var initialType = NoteRef.EliteDescriptor is { ModelType: ThemeNoteType.DedicatedLaneKick }
                ? ThemeNoteType.DedicatedLaneKick : NoteRef.NoteType;
            _currentNoteGroup = FindNoteGroup(initialType,
                NoteRef.ForceStarPower ?? FakeTrackPlayer.ForceStarPowerNotes);

            if (!NoteRef.CenterNote)
            {
                // Set the position. If the game mode provides explicit X positions
                // (e.g. piano-key spacing for pro keys, 3-lane layout for six-fret),
                // use those; otherwise fall back to the uniform lane formula.
                var info = FakeTrackPlayer.CurrentGameModeInfo;
                float x;
                if (info.NoteXPositions is { Length: > 0 } positions
                    && NoteRef.Fret >= 0 && NoteRef.Fret < positions.Length)
                {
                    x = positions[NoteRef.Fret];
                }
                else
                {
                    int fretCount = info.LaneCount;
                    x = TrackPlayer.TRACK_WIDTH / fretCount * NoteRef.Fret
                        - TrackPlayer.TRACK_WIDTH / 2f - 1f / fretCount;
                }
                transform.localPosition = new Vector3(x, 0f, 0f);
            }
            else
            {
                // Set the position
                transform.localPosition = Vector3.zero;
            }

            _currentNoteGroup.SetActive(true);
            _currentNoteGroup.Initialize();

            // Open HOPO notes have EmissionAddition: 1 in the prefab which
            // washes the color to white. Reset it so the dedicated color shows.
            if (NoteRef.NoteType == ThemeNoteType.OpenHOPO)
            {
                _currentNoteGroup.ResetEmissionAddition();
            }

            // Force update position and other properties
            OnSettingChanged();
            Update();

            if (NoteRef != null) gameObject.SetActive(true);
        }

        private ThemeNoteType EliteModelType(ThemeNoteType type) => !FakeTrackPlayer.UseCymbalModels &&
            type is ThemeNoteType.Cymbal or ThemeNoteType.OpenHiHat or ThemeNoteType.ClosedHiHat
                ? ThemeNoteType.Normal : type;

        private NoteGroup FindNoteGroup(ThemeNoteType type, bool starPower)
        {
            if (starPower)
            {
                var starPowerPair = _noteGroups.Find(i => i.NoteType == type && i.StarPower);
                if (starPowerPair.Group != null) return starPowerPair.Group;
            }
            // Try the exact regular type first
            var pair = _noteGroups.Find(i => i.NoteType == type && !i.StarPower);
            if (pair.Group != null) return pair.Group;

            var ordinaryType = type switch
            {
                ThemeNoteType.OpenHiHat => ThemeNoteType.Cymbal,
                ThemeNoteType.OpenHiHatAccent => ThemeNoteType.CymbalAccent,
                ThemeNoteType.OpenHiHatGhost => ThemeNoteType.CymbalGhost,
                ThemeNoteType.ClosedHiHat => ThemeNoteType.Cymbal,
                ThemeNoteType.ClosedHiHatAccent => ThemeNoteType.CymbalAccent,
                ThemeNoteType.ClosedHiHatGhost => ThemeNoteType.CymbalGhost,
                _ => type
            };
            if (ordinaryType != type) return FindNoteGroup(ordinaryType, starPower);

            // Cymbal variants fall back to Cymbal before Normal
            if (type is ThemeNoteType.CymbalAccent or ThemeNoteType.CymbalGhost)
            {
                pair = _noteGroups.Find(i => i.NoteType == ThemeNoteType.Cymbal && !i.StarPower);
                if (pair.Group != null) return pair.Group;
            }

            // Final fallback: Normal
            return _noteGroups.Find(i => i.NoteType == ThemeNoteType.Normal && !i.StarPower).Group;
        }

        internal static void GetEliteLayoutRegion(EliteDrumsResolvedLayout layout, string source, bool cymbal,
            bool lefty, bool offset, bool paired, out float center, out float width)
        {
            int laneIndex = layout.SourceToLane[source];
            var lane = layout.Lanes[laneIndex];
            width = layout.GetLaneWidth(TrackPlayer.TRACK_WIDTH);
            center = layout.GetLaneX(laneIndex, TrackPlayer.TRACK_WIDTH, lefty);

            // Only a saved lane containing both a drum and cymbal gets separate preview
            // regions, matching gameplay's layout geometry when paired gems are offset.
            if (paired && lane.Kind == EliteDrumsLayoutSlotKind.Ordinary && lane.EffectiveIds.Count == 2)
            {
                YARG.Gameplay.Visuals.EliteDrumsRuntimeGeometry.GetPairRegion(center, width, cymbal,
                    lefty, offset, out center, out width);
            }
        }

        public void OnSettingChanged()
        {
            foreach (var group in _noteGroups) Geometry(group.Group);
            foreach (var companion in _flamCompanions.Values) companion.SetActive(false);
            foreach (var pair in _geometry) pair.Value.Reset(pair.Key.transform);
            var colorProfile = FakeTrackPlayer.ColorProfile;
            var highwayPreset = FakeTrackPlayer.HighwayPreset;

            // Update color
            var info = FakeTrackPlayer.CurrentGameModeInfo;
            var useStarPower = NoteRef.ForceStarPower ?? FakeTrackPlayer.ForceStarPowerNotes;
            bool dedicatedEliteKick = NoteRef.EliteDescriptor is { Role: EliteDrumsColorRole.Kick or EliteDrumsColorRole.DoubleKick } &&
                info.EliteLayout is { KickMode: not EliteDrumsLayoutKickMode.Bars };
            bool kickFlamAccent = NoteRef.EliteDescriptor is { DedicatedLaneSource: not null, ModelType: ThemeNoteType.Accent };
            var selectedType = dedicatedEliteKick ? ThemeNoteType.DedicatedLaneKick
                : kickFlamAccent ? ThemeNoteType.Accent : NoteRef.NoteType;
            if (NoteRef.EliteDescriptor != null) selectedType = EliteModelType(selectedType);
            var selectedGroup = FindNoteGroup(selectedType, useStarPower);
            if (selectedGroup != _currentNoteGroup)
            {
                _currentNoteGroup.SetActive(false);
                _currentNoteGroup = selectedGroup;
                _currentNoteGroup.SetActive(true);
                _currentNoteGroup.Initialize();
                if (NoteRef.NoteType == ThemeNoteType.OpenHOPO)
                {
                    _currentNoteGroup.ResetEmissionAddition();
                }
            }

            // Guitar lefty flip reverses the color order (Green<->Orange, Red<->Blue)
            // without moving notes: look up the mirrored fret's color, but keep the
            // note's own fret (and thus its lane position).
            FakeNoteData colorRef = NoteRef;
            if (FakeTrackPlayer.LeftyFlip
                && FakeTrackPlayer.SelectedGameMode == GameMode.FiveFretGuitar
                && !NoteRef.CenterNote)
            {
                colorRef = new FakeNoteData
                {
                    Time = NoteRef.Time,
                    Fret = 6 - NoteRef.Fret,
                    CenterNote = NoteRef.CenterNote,
                    NoteType = NoteRef.NoteType
                };
            }

            var color = NoteRef.EliteDescriptor is { Role: EliteDrumsColorRole.KickFlam }
                ? (useStarPower ? colorProfile.EliteDrums.GetNoteStarPowerColor(EliteDrumsColorRole.KickFlam)
                    : colorProfile.EliteDrums.GetNoteColor(EliteDrumsColorRole.KickFlam)).ToUnityColor()
                : useStarPower && info.NoteStarPowerColorProvider is not null
                    ? info.NoteStarPowerColorProvider(colorProfile, colorRef)
                    : info.NoteColorProvider(colorProfile, colorRef);

            // Open HOPO notes use a dedicated color (the prefab's
            // EmissionAddition: 1 washes to white by default)
            if (NoteRef.NoteType == ThemeNoteType.OpenHOPO &&
                FakeTrackPlayer.SelectedGameMode == GameMode.FiveFretGuitar)
            {
                color = (useStarPower
                    ? colorProfile.FiveFretGuitar.OpenHopoNoteStarPower
                    : colorProfile.FiveFretGuitar.OpenHopoNote).ToUnityColor();
            }

            // Miss is the highest-priority preview override.
            if (NoteRef.ForceMiss)
            {
                color = (FakeTrackPlayer.SelectedGameMode switch
                {
                    GameMode.EliteDrums => colorProfile.EliteDrums.Miss,
                    GameMode.FiveFretGuitar => colorProfile.FiveFretGuitar.Miss,
                    GameMode.FourLaneDrums  => colorProfile.FourLaneDrums.Miss,
                    GameMode.FiveLaneDrums  => colorProfile.FiveLaneDrums.Miss,
                    GameMode.ProKeys        => FakeTrackPlayer.UseFiveLaneKeys
                        ? colorProfile.FiveFretGuitar.Miss
                        : colorProfile.ProKeys.Miss,
                    _ => colorProfile.FiveFretGuitar.Miss,
                }).ToUnityColor();
            }

            // Override dark-strip emission for tap and ghost notes from the
            // color profile. These note types have a strip material with
            // EmissionMultiplier 0 in the prefab; the user can boost it.
            float stripEmission = GetStripEmission(
                FakeTrackPlayer.SelectedGameMode, NoteRef.NoteType, colorProfile);
            if (stripEmission >= 0f)
            {
                _currentNoteGroup.OverrideZeroEmission(stripEmission);
            }

            _currentNoteGroup.SetColorWithEmission(color, color);

            // Six-fret note models share materials with the barre models, so
            // standard up/down notes need their secondary color set to the same
            // color as the primary. A barre combines the black and white fret of
            // a lane pair: primary black, secondary white. Lefty flip swaps rows.
            if (FakeTrackPlayer.SelectedGameMode == GameMode.SixFretGuitar)
            {
                bool isBarre = NoteRef.NoteType is ThemeNoteType.SixFretBarre
                    or ThemeNoteType.SixFretBarreTap or ThemeNoteType.SixFretBarreHOPO;
                bool isUp = NoteRef.NoteType is ThemeNoteType.SixFretUp
                    or ThemeNoteType.SixFretUpHOPO or ThemeNoteType.SixFretUpTap;
                bool isDown = NoteRef.NoteType is ThemeNoteType.SixFretDown
                    or ThemeNoteType.SixFretDownHOPO or ThemeNoteType.SixFretDownTap;

                if (isUp || isDown || isBarre)
                {
                    if (FakeTrackPlayer.LeftyFlip)
                    {
                        (isUp, isDown) = (isDown, isUp);
                    }

                    bool isBlack = isUp || isBarre;
                    var primary = (useStarPower
                        ? isBlack
                            ? colorProfile.SixFretGuitar.BlackNoteStarPower
                            : colorProfile.SixFretGuitar.WhiteNoteStarPower
                        : isBlack
                            ? colorProfile.SixFretGuitar.BlackNote
                            : colorProfile.SixFretGuitar.WhiteNote).ToUnityColor();
                    var secondary = primary;
                    if (isBarre)
                    {
                        secondary = (useStarPower
                            ? isBlack
                                ? colorProfile.SixFretGuitar.WhiteNoteStarPower
                                : colorProfile.SixFretGuitar.BlackNoteStarPower
                            : isBlack
                                ? colorProfile.SixFretGuitar.WhiteNote
                                : colorProfile.SixFretGuitar.BlackNote).ToUnityColor();
                    }

                    if (NoteRef.ForceMiss)
                    {
                        primary = color;
                    }

                    _currentNoteGroup.SetColorWithEmission(primary, primary);
                    _currentNoteGroup.SetSecondaryColor(secondary, secondary);
                }
            }

            // Set metal color
            var metalColor = (FakeTrackPlayer.SelectedGameMode switch
            {
                GameMode.EliteDrums => colorProfile.EliteDrums.GetMetalColor(useStarPower),
                GameMode.FiveFretGuitar => colorProfile.FiveFretGuitar.GetMetalColor(useStarPower),
                GameMode.SixFretGuitar => colorProfile.SixFretGuitar.GetMetalColor(useStarPower),
                GameMode.FourLaneDrums  => colorProfile.FourLaneDrums.GetMetalColor(useStarPower),
                GameMode.FiveLaneDrums  => colorProfile.FiveLaneDrums.GetMetalColor(useStarPower),
                GameMode.ProKeys        => FakeTrackPlayer.UseFiveLaneKeys
                    ? colorProfile.FiveFretGuitar.GetMetalColor(useStarPower)
                    : colorProfile.ProKeys.GetMetalColor(useStarPower),
                _ => colorProfile.FiveFretGuitar.GetMetalColor(false),
            }).ToUnityColor();
            _currentNoteGroup.SetMetalColor(metalColor);

            // Update height
            float width = NoteRef.EliteDescriptor?.Width ?? 1f;
            if (NoteRef.EliteDescriptor is { Role: EliteDrumsColorRole.Stomp or EliteDrumsColorRole.Splash } &&
                info.EliteLayout != null)
                width = 3f / info.LaneCount;
            transform.localScale = new Vector3(width, highwayPreset.NoteHeight, 1f);
            if (NoteRef.EliteDescriptor is { } elite)
            {
                var authoredScale = _authoredGroupScales[_currentNoteGroup];
                _currentNoteGroup.transform.localScale = authoredScale;
                _currentNoteGroup.transform.localPosition = _authoredGroupPositions[_currentNoteGroup];
                float? fittedWidth = null;
                float x = elite.IsBar
                    ? (elite.Role is EliteDrumsColorRole.Stomp or EliteDrumsColorRole.Splash) &&
                      info.EliteLayout != null
                        ? Mathf.Clamp(info.EliteLayout.GetSourceX("hi-hat", TrackPlayer.TRACK_WIDTH,
                            FakeTrackPlayer.LeftyFlip), -TrackPlayer.TRACK_WIDTH / 2f + 1.5f * TrackPlayer.TRACK_WIDTH / info.LaneCount,
                            TrackPlayer.TRACK_WIDTH / 2f - 1.5f * TrackPlayer.TRACK_WIDTH / info.LaneCount)
                        : info.EliteLayout is { KickMode: EliteDrumsLayoutKickMode.Split } barLayout
                            ? (elite.Role == EliteDrumsColorRole.DoubleKick
                                ? barLayout.GetSourceX("kick2", TrackPlayer.TRACK_WIDTH, FakeTrackPlayer.LeftyFlip)
                                : barLayout.GetSourceX("kick1", TrackPlayer.TRACK_WIDTH, FakeTrackPlayer.LeftyFlip))
                            : 0f
                    : TrackPlayer.TRACK_WIDTH / info.LaneCount * elite.Fret
                    - TrackPlayer.TRACK_WIDTH / 2f - 1f / info.LaneCount;
                if ((dedicatedEliteKick || elite.DedicatedLaneSource != null) && info.EliteLayout is { } kickLayout)
                {
                    string source = elite.DedicatedLaneSource ?? (kickLayout.KickMode == EliteDrumsLayoutKickMode.Combined
                        ? "kick" : elite.Role == EliteDrumsColorRole.DoubleKick ? "kick2" : "kick1");
                    int lane = kickLayout.SourceToLane[source];
                    x = kickLayout.GetLaneX(lane, TrackPlayer.TRACK_WIDTH, FakeTrackPlayer.LeftyFlip);
                    float laneWidth = kickLayout.GetLaneWidth(TrackPlayer.TRACK_WIDTH);
                    transform.localScale = transform.localScale.WithX(1f);
                    fittedWidth = laneWidth;
                }
                else if (elite.Role == EliteDrumsColorRole.Wildcard)
                {
                    x = 0f;
                    fittedWidth = TrackPlayer.TRACK_WIDTH;
                }
                if (info.EliteLayout != null && !elite.IsBar && info.LaneCount > 0)
                {
                    string source = elite.Role switch
                    {
                        EliteDrumsColorRole.Snare or EliteDrumsColorRole.HandFlam => "snare",
                        EliteDrumsColorRole.HatIndifferent or EliteDrumsColorRole.HatOpen or EliteDrumsColorRole.HatClosed => "hi-hat",
                        EliteDrumsColorRole.Tom1 => "tom1",
                        EliteDrumsColorRole.Tom2 => "tom2",
                        EliteDrumsColorRole.Tom3 => "tom3",
                        EliteDrumsColorRole.LeftCrash => "left-crash",
                        EliteDrumsColorRole.Ride => "ride",
                        EliteDrumsColorRole.RightCrash => "right-crash",
                        _ => null
                    };
                    if (source != null)
                    {
                        // Mirror the lane first, like the gameplay highway ordering, then
                        // Obtain the resolved lane before the measured pair fit below.
                        bool cymbal = elite.ModelType is ThemeNoteType.Cymbal or ThemeNoteType.CymbalAccent
                            or ThemeNoteType.CymbalGhost or ThemeNoteType.OpenHiHat or ThemeNoteType.OpenHiHatAccent
                            or ThemeNoteType.OpenHiHatGhost or ThemeNoteType.ClosedHiHat or ThemeNoteType.ClosedHiHatAccent
                            or ThemeNoteType.ClosedHiHatGhost;
                        GetEliteLayoutRegion(info.EliteLayout, source, cymbal, FakeTrackPlayer.LeftyFlip,
                            false, false,
                            out x, out float pairWidth);
                        // Match live gems: resize the authored model from its five-lane baseline,
                        // while the wrapper retains only the descriptor's model width.
                        transform.localScale = transform.localScale.WithX(elite.Width);
                        float factor = EliteDrumsRuntimeGeometry.GetAuthoredLaneScale(pairWidth,
                            TrackPlayer.TRACK_WIDTH / 5f);
                        _currentNoteGroup.transform.localScale = new Vector3(
                            authoredScale.x * factor, authoredScale.y, authoredScale.z);
                    }
                }
                else if (FakeTrackPlayer.LeftyFlip && !elite.IsBar)
                {
                    x = -x;
                }
                float offset = dedicatedEliteKick ? 0f : elite.Offset;
                if (elite.Width == 2f / 3f && info.EliteLayout != null)
                    offset *= info.EliteLayout.GetLaneWidth(TrackPlayer.TRACK_WIDTH) /
                        (TrackPlayer.TRACK_WIDTH / 5f);
                if ((elite.Role is EliteDrumsColorRole.Stomp or EliteDrumsColorRole.Splash) &&
                    info.EliteLayout != null)
                    offset = 0f;
                transform.localPosition = transform.localPosition.WithX(x + offset);
                // Fit only after the wrapper reaches its final X; moving it afterward
                // would move the renderer bounds away from their fitted center.
                if (fittedWidth is { } targetModelWidth)
                {
                    float parentScale = Mathf.Abs(transform.parent.lossyScale.x);
                    float targetCenter = transform.parent.TransformPoint(new Vector3(x, 0f, 0f)).x;
                    FitCurrentModelWidth(targetModelWidth * parentScale, targetCenter);
                }
                if (info.EliteLayout != null &&
                    elite.Role is EliteDrumsColorRole.Stomp or EliteDrumsColorRole.Splash)
                {
                    var renderedTransform = _currentNoteGroup.transform;
                    if (info.LaneCount > 0)
                    {
                        float targetWidth = TrackPlayer.TRACK_WIDTH * 3f / info.LaneCount *
                            Mathf.Abs(transform.parent.lossyScale.x);
                        float targetCenter = transform.parent.TransformPoint(new Vector3(x, 0f, 0f)).x;
                        FitCurrentModelWidth(targetWidth, targetCenter);
                    }
                    double tickDuration = 1d / (120d * 480d / 60d);
                    renderedTransform.localPosition = renderedTransform.localPosition.WithZ(
                        EliteDrumsRuntimeGeometry.GetVisualLeadOffset((float) FakeTrackPlayer.NOTE_SPEED, tickDuration));
                }
                if (info.EliteLayout != null && (NoteRef.EliteHandFlam || NoteRef.ElitePairPartner != null))
                {
                    transform.localScale = transform.localScale.WithX(1f);
                    float center = transform.localPosition.x;
                    float laneWidth = info.EliteLayout.GetLaneWidth(TrackPlayer.TRACK_WIDTH);
                    var ownGeometry = Geometry(_currentNoteGroup);
                    if (NoteRef.EliteHandFlam)
                    {
                        if (!_flamCompanions.TryGetValue(_currentNoteGroup, out var companion))
                        {
                            ownGeometry.Reset(_currentNoteGroup.transform);
                            companion = Instantiate(_currentNoteGroup, transform);
                            _flamCompanions.Add(_currentNoteGroup, companion);
                            Geometry(companion);
                        }
                        EliteDrumsRuntimeGeometry.FitPair(ownGeometry, _currentNoteGroup.transform,
                            Geometry(companion), companion.transform, info.LaneCount, center, laneWidth,
                            center, !NoteRef.EliteFlatFlam);
                        companion.SetActive(true);
                        companion.Initialize();
                        companion.SetColorWithEmission(color, color);
                        companion.SetMetalColor(metalColor);
                    }
                    else if (NoteRef.ElitePairPartner.EliteDescriptor is { } partner)
                    {
                        bool partnerStar = NoteRef.ElitePairPartner.ForceStarPower ?? FakeTrackPlayer.ForceStarPowerNotes;
                        var other = FindNoteGroup(EliteModelType(partner.ModelType), partnerStar);
                        bool cymbal = elite.Role is EliteDrumsColorRole.HatIndifferent or EliteDrumsColorRole.HatOpen
                            or EliteDrumsColorRole.HatClosed or EliteDrumsColorRole.LeftCrash
                            or EliteDrumsColorRole.Ride or EliteDrumsColorRole.RightCrash;
                        bool right = cymbal != FakeTrackPlayer.LeftyFlip;
                        EliteDrumsRuntimeGeometry.FitPair(Geometry(right ? other : _currentNoteGroup),
                            right ? null : _currentNoteGroup.transform,
                            Geometry(right ? _currentNoteGroup : other), right ? _currentNoteGroup.transform : null,
                            info.LaneCount, center, laneWidth, center, false);
                    }
                }
            }
        }

        private void FitCurrentModelWidth(float targetWorldWidth, float targetWorldCenter)
        {
            var renderers = _currentNoteGroup.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;
            float left = float.PositiveInfinity;
            float right = float.NegativeInfinity;
            foreach (var renderer in renderers)
            {
                left = Mathf.Min(left, renderer.bounds.min.x);
                right = Mathf.Max(right, renderer.bounds.max.x);
            }
            float currentWidth = right - left;
            float parentScale = Mathf.Abs(_currentNoteGroup.transform.parent.lossyScale.x);
            if (currentWidth <= 0f || parentScale <= 0f) return;

            float factor = targetWorldWidth / currentWidth;
            var groupTransform = _currentNoteGroup.transform;
            var scale = groupTransform.localScale;
            groupTransform.localScale = new Vector3(scale.x * factor, scale.y, scale.z);
            float currentCenter = (left + right) / 2f;
            float scaledCenter = groupTransform.position.x + (currentCenter - groupTransform.position.x) * factor;
            var position = groupTransform.localPosition;
            groupTransform.localPosition = new Vector3(position.x +
                (targetWorldCenter - scaledCenter) / parentScale, position.y, position.z);
        }

        protected void Update()
        {
            if (NoteRef != null && FakeTrackPlayer != null) RefreshPosition();
        }

        public void RefreshPosition()
        {
            float z =
                TrackPlayer.STRIKE_LINE_POS                            // Shift origin to the strike line
                + (float) (NoteRef.Time - FakeTrackPlayer.PreviewTime) // Get time of note relative to now
                * FakeTrackPlayer.NOTE_SPEED;                          // Adjust speed (units/s)

            var cacheTransform = transform;
            cacheTransform.localPosition = cacheTransform.localPosition.WithZ(z);

            if (z < -4f)
            {
                ParentPool.Return(this);
            }
        }

        internal static float GetStripEmission(GameMode gameMode, ThemeNoteType noteType,
            ColorProfile colorProfile)
        {
            return noteType switch
            {
                ThemeNoteType.Tap => colorProfile.FiveFretGuitar.TapStripEmission / 100f,
                ThemeNoteType.Ghost or ThemeNoteType.CymbalGhost => gameMode switch
                {
                    GameMode.FourLaneDrums => colorProfile.FourLaneDrums.GhostStripEmission / 100f,
                    GameMode.FiveLaneDrums => colorProfile.FiveLaneDrums.GhostStripEmission / 100f,
                    _ => -1f,
                },
                _ => -1f,
            };
        }

        public void DisableIntoPool()
        {
            foreach (var companion in _flamCompanions.Values) companion.SetActive(false);
            foreach (var pair in _geometry) pair.Value.Reset(pair.Key.transform);
            foreach (var pair in _noteGroups)
            {
                pair.Group.SetActive(false);
            }
            foreach (var pair in _materialDefaults)
            {
                pair.Key.CopyPropertiesFromMaterial(pair.Value);
            }
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            _currentNoteGroup = null;
            NoteRef = null;
            FakeTrackPlayer = null;
            gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            foreach (var pair in _materialDefaults)
            {
                Destroy(pair.Key);
                Destroy(pair.Value);
            }
        }

        public static GameObject CreateFakeNoteFromTheme(ThemePreset themePreset, VisualStyle style)
        {
            var themeContainer = ThemeManager.Instance.GetThemeContainer(themePreset, style);
            var component = themeContainer.GetThemeComponent();
            var regular = component.GetNoteModelsForVisualStyle(style, false);
            var stars = component.GetNoteModelsForVisualStyle(style, true);
            if (style == VisualStyle.EliteDrums)
                (regular, stars) = ThemeNoteModelFallbacks.ResolveEliteModels(regular, stars);
            var prefab = CreateFakeNoteFromModels(regular, stars);
            if (style == VisualStyle.EliteDrums)
                NormalizeElitePreviewBars(prefab.GetComponent<FakeNote>());
            return prefab;
        }

        private static void NormalizeElitePreviewBars(FakeNote fake)
        {
            // Descriptor widths assume a full-highway baseline. Scale the
            // wrapper, keeping all author-edited model transforms intact.
            foreach (var pair in fake._noteGroups)
            {
                if (pair.NoteType is not (ThemeNoteType.Kick or ThemeNoteType.DedicatedLaneKick or ThemeNoteType.Wildcard))
                    continue;
                var renderers = pair.Group.GetComponentsInChildren<MeshRenderer>(true);
                if (renderers.Length == 0) continue;
                var bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                if (bounds.size.x <= 0.00001f) continue;
                var scale = pair.Group.transform.localScale;
                scale.x *= TrackPlayer.TRACK_WIDTH / bounds.size.x;
                pair.Group.transform.localScale = scale;
            }
        }

        public static GameObject CreateFakeNoteFromModels(
            IReadOnlyDictionary<ThemeNoteType, GameObject> models,
            IReadOnlyDictionary<ThemeNoteType, GameObject> starPowerModels = null)
        {
            (models, starPowerModels) = ThemeNoteModelFallbacks.ResolveHiHatModels(models, starPowerModels);
            // Create GameObject
            var notePrefab = new GameObject("Note Prefab");
            notePrefab.transform.localPosition = Vector3.zero;
            var fakeNote = notePrefab.AddComponent<FakeNote>();

            // Create note groups
            fakeNote._noteGroups = new List<NoteTypePair>();
            foreach (var (type, gameObject) in models)
            {
                fakeNote._noteGroups.Add(new NoteTypePair
                {
                    NoteType = type,
                    Group = NoteGroup.CreateNoteGroupFromTheme(notePrefab.transform, gameObject)
                });
            }

            if (starPowerModels != null)
            {
                foreach (var (type, model) in starPowerModels)
                {
                    fakeNote._noteGroups.Add(new NoteTypePair
                    {
                        NoteType = type,
                        StarPower = true,
                        Group = NoteGroup.CreateNoteGroupFromTheme(notePrefab.transform, model)
                    });
                }
            }

            foreach (var pair in fakeNote._noteGroups)
            {
                pair.Group.SetActive(false);
            }

            // Set layer
            fakeNote.transform.SetLayerRecursive(LayerMask.NameToLayer("Settings Preview"));

            return notePrefab;
        }
    }
}
