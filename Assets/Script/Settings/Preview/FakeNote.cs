using System;
using System.Collections.Generic;
using UnityEngine;
using YARG.Core;
using YARG.Gameplay;
using YARG.Gameplay.Player;
using YARG.Gameplay.Visuals;
using YARG.Helpers.Extensions;
using YARG.Settings.Customization;
using YARG.Settings.Metadata;
using YARG.Themes;

namespace YARG.Settings.Preview
{
    public class FakeNote : MonoBehaviour, IPoolable
    {
        [Serializable]
        public struct NoteTypePair
        {
            public ThemeNoteType NoteType;
            public NoteGroup Group;
        }

        public Pool ParentPool { get; set; }

        public FakeNoteData NoteRef { get; set; }
        public FakeTrackPlayer FakeTrackPlayer { get; set; }

        private NoteGroup _currentNoteGroup;

        // We can't use a dictionary here (Unity L)
        [SerializeField]
        private List<NoteTypePair> _noteGroups;

        private readonly List<Material> _materials = new();

        public void EnableFromPool()
        {
            // Disable all note groups
            foreach (var noteGroup in _noteGroups)
            {
                noteGroup.Group.SetActive(false);
            }

            // Find the correct note group
            var pair = _noteGroups.Find(i => i.NoteType == NoteRef.NoteType);
            _currentNoteGroup = pair.Group != null ? pair.Group
                : _noteGroups.Find(i => i.NoteType == ThemeNoteType.Normal).Group;

            if (!NoteRef.CenterNote)
            {
                // Set the position. If the game mode provides explicit X positions
                // (e.g. piano-key spacing for pro keys), use those; otherwise fall
                // back to the uniform lane formula.
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

            // Get all materials
            _materials.Clear();
            var meshRenderers = GetComponentsInChildren<MeshRenderer>(true);
            foreach (var meshRenderer in meshRenderers)
            {
                foreach (var material in meshRenderer.materials)
                {
                    _materials.Add(material);
                }
            }

            // Force update position and other properties
            OnSettingChanged();
            Update();

            gameObject.SetActive(true);
        }

        public void OnSettingChanged()
        {
            var cameraPreset = PresetsTab.GetLastSelectedPreset(CustomContentManager.CameraSettings);
            var colorProfile = PresetsTab.GetLastSelectedPreset(CustomContentManager.ColorProfiles);
            var highwayPreset = PresetsTab.GetLastSelectedPreset(CustomContentManager.HighwayPresets);

            // Update color
            var info = FakeTrackPlayer.CurrentGameModeInfo;
            var useStarPower = FakeTrackPlayer.ForceStarPowerNotes;

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

            var color = useStarPower && info.NoteStarPowerColorProvider is not null
                ? info.NoteStarPowerColorProvider(colorProfile, colorRef)
                : info.NoteColorProvider(colorProfile, colorRef);
            _currentNoteGroup.SetColorWithEmission(color, color);

            // Set metal color
            var metalColor = (FakeTrackPlayer.SelectedGameMode switch
            {
                GameMode.FiveFretGuitar => colorProfile.FiveFretGuitar.GetMetalColor(useStarPower),
                GameMode.FourLaneDrums  => colorProfile.FourLaneDrums.GetMetalColor(useStarPower),
                GameMode.FiveLaneDrums  => colorProfile.FiveLaneDrums.GetMetalColor(useStarPower),
                GameMode.ProKeys        => FakeTrackPlayer.UseFiveLaneKeys
                    ? colorProfile.FiveFretGuitar.GetMetalColor(useStarPower)
                    : colorProfile.ProKeys.GetMetalColor(useStarPower),
                _ => colorProfile.FiveFretGuitar.GetMetalColor(false),
            }).ToUnityColor();
            _currentNoteGroup.SetMetalColor(metalColor);

            // Update height
            transform.localScale = new Vector3(1f, highwayPreset.NoteHeight, 1f);
        }

        protected void Update()
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

        public void DisableIntoPool()
        {
            gameObject.SetActive(false);
        }

        public static GameObject CreateFakeNoteFromTheme(ThemePreset themePreset, VisualStyle style)
        {
            // Create GameObject
            var notePrefab = new GameObject("Note Prefab");
            notePrefab.transform.localPosition = Vector3.zero;
            var fakeNote = notePrefab.AddComponent<FakeNote>();

            // Get models
            var themeContainer = ThemeManager.Instance.GetThemeContainer(themePreset, style);
            var models = themeContainer.GetThemeComponent().GetNoteModelsForVisualStyle(style, false);

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

            // Set layer
            fakeNote.transform.SetLayerRecursive(LayerMask.NameToLayer("Settings Preview"));

            return notePrefab;
        }
    }
}
