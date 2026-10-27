using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YARG.Assets.Script.Helpers;
using YARG.Core;
using YARG.Core.Chart;
using YARG.Core.Engine.Keys;
using YARG.Core.Game;
using YARG.Gameplay;
using YARG.Gameplay.Player;
using YARG.Gameplay.Visuals;
using YARG.Helpers;
using YARG.Helpers.Extensions;
using YARG.Menu.Settings;
using YARG.Settings.Customization;
using YARG.Settings.Metadata;
using YARG.Themes;
using Random = UnityEngine.Random;

// pattern: Mixed (needs refactoring)

namespace YARG.Settings.Preview
{
    public class FakeTrackPlayer : MonoBehaviour
    {
        public struct Info
        {
            public delegate ColorProfile.IFretColorProvider FretColorProviderFunc(ColorProfile c);
            public delegate Color NoteColorProviderFunc(ColorProfile c, FakeNoteData note);
            public delegate EnginePreset.HitWindowPreset HitWindowProviderFunc(EnginePreset e);

            public bool UseKickFrets;
            public bool UseProKeys;
            public bool UseHighwayOverlay;

            public Dictionary<int, int> HighwayOrdering;
            public int LaneCount;
            public EliteDrumsResolvedLayout EliteLayout;
            // When set, notes are positioned at these X coordinates by fret index
            // instead of the uniform lane formula. Used for piano-key spacing.
            public float[] NoteXPositions;
            #nullable enable
            public GameObject? FretPrefab;
            public GameObject? KickFretPrefab;
            // When set, overrides the visual style used for note models (independent
            // of the fret-array style). Used by the compressed pro-keys keyboard,
            // which needs guitar fret bars (FiveLaneKeys) but pro-keys note shapes
            // (White/Black models from the ProKeys style).
            public VisualStyle? NoteVisualStyle;
            #nullable restore

            public FretColorProviderFunc FretColorProvider;
            public NoteColorProviderFunc NoteColorProvider;
            public NoteColorProviderFunc NoteStarPowerColorProvider;

            public HitWindowProviderFunc HitWindowProvider;

            public IFakeNoteGenerator Generator;
        }

        /// <summary>
        /// Accent and ghost are dynamics modifiers, so all cymbal variants
        /// (CymbalAccent, CymbalGhost) use the cymbal color — matching real
        /// gameplay, where the note model (not its color) encodes accent/ghost.
        /// </summary>
        private static ThemeNoteType FourLaneDrumsColorType(ThemeNoteType type) =>
            type is ThemeNoteType.CymbalAccent or ThemeNoteType.CymbalGhost
                ? ThemeNoteType.Cymbal
                : type;

        private static readonly Dictionary<GameMode, Info> _gameModeInfos = new()
        {
            {
                GameMode.EliteDrums,
                new Info
                {
                    HighwayOrdering = new Dictionary<int, int> { {1, 0}, {2, 1}, {3, 2}, {4, 3}, {5, 4} },
                    LaneCount = 5,
                    NoteVisualStyle = VisualStyle.EliteDrums,
                    FretColorProvider = c => c.EliteDrums,
                    NoteColorProvider = (c, note) => c.EliteDrums.GetNoteColor(note.EliteDescriptor.Value.Role).ToUnityColor(),
                    NoteStarPowerColorProvider = (c, note) => c.EliteDrums.GetNoteStarPowerColor(note.EliteDescriptor.Value.Role).ToUnityColor(),
                    HitWindowProvider = e => e.Drums.HitWindow,
                    Generator = null
                }
            },
            {
                GameMode.FiveFretGuitar,
                new Info
                {
                    HighwayOrdering = GryboHighwayHelpers.DEFAULT_HIGHWAY_ORDERING,
                    LaneCount = 5,

                    FretColorProvider = (colorProfile) => colorProfile.FiveFretGuitar,
                    NoteColorProvider = (colorProfile, note) => colorProfile.FiveFretGuitar
                        .GetNoteColor(note.Fret)
                        .ToUnityColor(),
                    NoteStarPowerColorProvider = (colorProfile, note) => colorProfile.FiveFretGuitar
                        .GetNoteStarPowerColor(note.Fret)
                        .ToUnityColor(),

                    HitWindowProvider = (enginePreset) => enginePreset.FiveFretGuitar.HitWindow,
                    Generator = new FiveFretGuitarFakeNoteGenerator()
                }
            },
            {
                GameMode.SixFretGuitar,
                new Info
                {
                    HighwayOrdering = SixFretGuitarPlayer.DEFAULT_LANE_POSITIONS,
                    LaneCount = 3,

                    // 6 frets map onto 3 visual lanes (fret pair per lane), so the
                    // uniform lane formula doesn't apply — use explicit positions.
                    NoteXPositions = ComputeSixFretNotePositions(),

                    FretColorProvider = (colorProfile) => colorProfile.SixFretGuitar,
                    NoteColorProvider = (colorProfile, note) => colorProfile.SixFretGuitar
                        .GetNoteColor(note.Fret)
                        .ToUnityColor(),
                    NoteStarPowerColorProvider = (colorProfile, note) => colorProfile.SixFretGuitar
                        .GetNoteStarPowerColor(note.Fret)
                        .ToUnityColor(),

                    HitWindowProvider = (enginePreset) => enginePreset.SixFretGuitar.HitWindow,
                    Generator = new SixFretGuitarFakeNoteGenerator()
                }
            },
            {
                GameMode.FourLaneDrums,
                new Info
                {
                    UseKickFrets = true,

                    HighwayOrdering = DrumsPlayer.DEFAULT_FOUR_LANE_HIGHWAY_ORDERING,
                    LaneCount = 4,

                    FretColorProvider = (colorProfile) => colorProfile.FourLaneDrums,
                    NoteColorProvider = (colorProfile, note) =>
                    {
                        int colorNote = (note.Fret, FourLaneDrumsColorType(note.NoteType)) switch
                        {
                            ((int) ColorProfile.FourLaneDrumsFret.Kick, _)                          => (int) ColorProfile.FourLaneDrumsFret.Kick,
                            ((int) ColorProfile.FourLaneDrumsFret.RedDrum, ThemeNoteType.Cymbal)    => (int) ColorProfile.FourLaneDrumsFret.RedCymbal,
                            ((int) ColorProfile.FourLaneDrumsFret.RedDrum, _)                       => (int) ColorProfile.FourLaneDrumsFret.RedDrum,
                            ((int) ColorProfile.FourLaneDrumsFret.YellowDrum, ThemeNoteType.Cymbal) => (int) ColorProfile.FourLaneDrumsFret.YellowCymbal,
                            ((int) ColorProfile.FourLaneDrumsFret.YellowDrum, _)                    => (int) ColorProfile.FourLaneDrumsFret.YellowDrum,
                            ((int) ColorProfile.FourLaneDrumsFret.BlueDrum, ThemeNoteType.Cymbal)   => (int) ColorProfile.FourLaneDrumsFret.BlueCymbal,
                            ((int) ColorProfile.FourLaneDrumsFret.BlueDrum, _)                      => (int) ColorProfile.FourLaneDrumsFret.BlueDrum,
                            ((int) ColorProfile.FourLaneDrumsFret.GreenDrum, ThemeNoteType.Cymbal)  => (int) ColorProfile.FourLaneDrumsFret.GreenCymbal,
                            ((int) ColorProfile.FourLaneDrumsFret.GreenDrum, _)                     => (int) ColorProfile.FourLaneDrumsFret.GreenDrum,
                            _                                                    => throw new Exception("Unreachable.")
                        };

                        return colorProfile.FourLaneDrums
                            .GetNoteColor(colorNote)
                            .ToUnityColor();
                    },
                    NoteStarPowerColorProvider = (colorProfile, note) =>
                    {
                        int colorNote = (note.Fret, FourLaneDrumsColorType(note.NoteType)) switch
                        {
                            ((int) ColorProfile.FourLaneDrumsFret.Kick, _)                          => (int) ColorProfile.FourLaneDrumsFret.Kick,
                            ((int) ColorProfile.FourLaneDrumsFret.RedDrum, ThemeNoteType.Cymbal)    => (int) ColorProfile.FourLaneDrumsFret.RedCymbal,
                            ((int) ColorProfile.FourLaneDrumsFret.RedDrum, _)                       => (int) ColorProfile.FourLaneDrumsFret.RedDrum,
                            ((int) ColorProfile.FourLaneDrumsFret.YellowDrum, ThemeNoteType.Cymbal) => (int) ColorProfile.FourLaneDrumsFret.YellowCymbal,
                            ((int) ColorProfile.FourLaneDrumsFret.YellowDrum, _)                    => (int) ColorProfile.FourLaneDrumsFret.YellowDrum,
                            ((int) ColorProfile.FourLaneDrumsFret.BlueDrum, ThemeNoteType.Cymbal)   => (int) ColorProfile.FourLaneDrumsFret.BlueCymbal,
                            ((int) ColorProfile.FourLaneDrumsFret.BlueDrum, _)                      => (int) ColorProfile.FourLaneDrumsFret.BlueDrum,
                            ((int) ColorProfile.FourLaneDrumsFret.GreenDrum, ThemeNoteType.Cymbal)  => (int) ColorProfile.FourLaneDrumsFret.GreenCymbal,
                            ((int) ColorProfile.FourLaneDrumsFret.GreenDrum, _)                     => (int) ColorProfile.FourLaneDrumsFret.GreenDrum,
                            _                                                    => throw new Exception("Unreachable.")
                        };

                        return colorProfile.FourLaneDrums
                            .GetNoteStarPowerColor(colorNote)
                            .ToUnityColor();
                    },

                    HitWindowProvider = (enginePreset) => enginePreset.Drums.HitWindow,
                    Generator = new DrumFakeNoteGenerator(fiveLane: false)
                }
            },
            {
                GameMode.FiveLaneDrums,
                new Info
                {
                    UseKickFrets = true,

                    FretColorProvider = (colorProfile) => colorProfile.FiveLaneDrums,
                    NoteColorProvider = (colorProfile, note) => colorProfile.FiveLaneDrums
                        .GetNoteColor(note.Fret)
                        .ToUnityColor(),
                    NoteStarPowerColorProvider = (colorProfile, note) => colorProfile.FiveLaneDrums
                        .GetNoteStarPowerColor(note.Fret)
                        .ToUnityColor(),

                    HighwayOrdering = DrumsPlayer.DEFAULT_FIVE_LANE_HIGHWAY_ORDERING,
                    LaneCount = 5,

                    HitWindowProvider = (enginePreset) => enginePreset.Drums.HitWindow,
                    Generator = new DrumFakeNoteGenerator(fiveLane: true)
                }
            },
            {
                GameMode.ProKeys,
                new Info
                {
                    UseProKeys = true,

                    FretColorProvider = null,
                    NoteColorProvider = (colorProfile, note) => (ProKeysUtilities.IsWhiteKey(note.Fret % 12)
                        ? colorProfile.ProKeys.WhiteNote
                        : colorProfile.ProKeys.BlackNote).ToUnityColor(),
                    NoteStarPowerColorProvider = (colorProfile, note) => (ProKeysUtilities.IsWhiteKey(note.Fret % 12)
                        ? colorProfile.ProKeys.WhiteNoteStarPower
                        : colorProfile.ProKeys.BlackNoteStarPower).ToUnityColor(),

                    HitWindowProvider = (enginePreset) => enginePreset.ProKeys.HitWindow,
                    Generator = new ProKeysFakeNoteGenerator()
                }
            }
        };

        public const float NOTE_SPEED = 6f;
        private const double SPAWN_FREQ = 0.2;

        private double SpawnTimeOffset => (TrackPlayer.NOTE_SPAWN_OFFSET + -TrackPlayer.STRIKE_LINE_POS) / NOTE_SPEED;

        [SerializeField]
        private CameraPositioner _cameraPositioner;
        [SerializeField]
        private TrackMaterial _trackMaterial;
        [SerializeField]
        private FretArray _fretArray;
        [SerializeField]
        private KeyedPool _notePool;
        [SerializeField]
        private FakeHitWindowDisplay _hitWindow;
        [SerializeField]
        private Texture2D _proKeysEdgeTexture;

        // Renderers for the pro-keys highway overlay quads. Stored for live
        // recoloring on setting change.
        private readonly List<ProKeysOverlayRenderer> _proKeysOverlayRenderers = new();

        public bool ForceShowHitWindow { get; set; }

        private bool _forceGroove;
        public bool ForceGroove
        {
            get => _forceGroove;
            set
            {
                _forceGroove = value;
                if (_trackMaterial != null)
                {
                    _trackMaterial.GrooveMode = value;
                }
            }
        }

        private bool _forceStarPower;
        public bool ForceStarPower
        {
            get => _forceStarPower;
            set
            {
                _forceStarPower = value;
                if (_trackMaterial != null)
                {
                    _trackMaterial.StarpowerMode = value;
                }
            }
        }

        public bool ForceStarPowerNotes { get; set; }
        public bool LeftyFlip { get; set; }
        public bool UseCymbalModels { get; set; } = true;
        // Simultaneous resolved drum/cymbal collision pairs are always separated.

        // Deferred until five-lane keys has a dedicated color-profile subsection.
        public bool UseFiveLaneKeys { get; set; }

        public GameMode SelectedGameMode { get; set; } = GameMode.FiveFretGuitar;

        public double PreviewTime { get; private set; }
        private double _nextSpawnTime;

        // Lane spotlight: while > 0, spawns come from a single lane (set via
        // SpotlightLane) instead of the random generator, so the user can see
        // the note type whose color they're editing. Chord extras are
        // suppressed for the duration.
        public const int SPOTLIGHT_NOTE_COUNT = 16;

        private int _spotlightRemaining;
        private int _spotlightFret;
        private bool _spotlightCenterNote;
        private bool _spotlightCymbal;
        private bool _spotlightStarPower;

        private int _spotlightTypeRemaining;
        private ThemeNoteType _spotlightNoteType;
        private bool? _spotlightTypeStarPower;

        private int _spotlightMissRemaining;

        private int _spotlightStarPowerRemaining;

        public Info CurrentGameModeInfo { get; private set; }

        private sealed class UnityFakeNoteRandom : IFakeNoteRandom
        {
            public int Range(int minInclusive, int maxExclusive) => Random.Range(minInclusive, maxExclusive);
        }

        private readonly IFakeNoteRandom _random = new UnityFakeNoteRandom();

        public CameraPreset CameraPreset { get; private set; }
        public ColorProfile ColorProfile { get; private set; }
        public EliteDrumsLayoutPreset EliteLayoutOverride { get; set; }
        /// <summary>Allows structurally valid, merge-free incomplete layouts for editor previews only.</summary>
        public bool AllowIncompleteEliteLayoutPreview { get; set; }
        public ColorProfile ColorProfileOverride { get; set; }
        public HighwayPreset HighwayPreset { get; private set; }
        private bool _initialized;
        private SettingsMenu _settingsMenu;
        private GameObject _notePrefab;

        private void Start()
        {
            if (_initialized) return;
            Initialize(ThemePreset.Default, _notePool,
                (theme, style) => FakeNote.CreateFakeNoteFromTheme(theme, style),
                PresetsTab.GetLastSelectedPreset(CustomContentManager.CameraSettings),
                ColorProfileOverride ?? PresetsTab.GetLastSelectedPreset(CustomContentManager.ColorProfiles),
                PresetsTab.GetLastSelectedPreset(CustomContentManager.EnginePresets),
                PresetsTab.GetLastSelectedPreset(CustomContentManager.HighwayPresets),
                SettingsManager.Settings?.ShowHitWindow.Value ?? false);
            if (ColorProfileOverride == null)
            {
                _settingsMenu = SettingsMenu.Instance;
                if (_settingsMenu != null) _settingsMenu.SettingChanged += OnSettingChanged;
            }
        }

        /// <summary>
        /// Initializes the preview using explicit content and settings dependencies.
        /// The optional fret initializer allows content loaders to supply their own fret models.
        /// </summary>
        public void Initialize(ThemePreset theme, KeyedPool notePool,
            Func<ThemePreset, VisualStyle, GameObject> createNotePrefab,
            CameraPreset cameraPreset, ColorProfile colorProfile, EnginePreset enginePreset,
            HighwayPreset highwayPreset, bool showHitWindow,
            Action<Info, ThemePreset, VisualStyle> initializeFrets = null)
        {
            if (_initialized) throw new InvalidOperationException("Preview is already initialized.");
            if (notePool == null) throw new ArgumentNullException(nameof(notePool));
            _notePool = notePool;
            CameraPreset = cameraPreset;
            ColorProfile = colorProfile;
            HighwayPreset = highwayPreset;
            CurrentGameModeInfo = _gameModeInfos[SelectedGameMode];
            if (SelectedGameMode == GameMode.EliteDrums)
            {
                var elite = CurrentGameModeInfo;
                var layoutPreset = EliteLayoutOverride ?? EliteDrumsLayoutPreset.Compact;
                if (AllowIncompleteEliteLayoutPreview)
                {
                    elite.EliteLayout = EliteDrumsLayoutResolver.TryPreviewResolve(layoutPreset, out var previewLayout)
                        ? previewLayout
                        : null;
                }
                else
                {
                    elite.EliteLayout = EliteDrumsLayoutResolver.Resolve(layoutPreset);
                }
                elite.Generator = new EliteDrumsFakeNoteGenerator(
                    elite.EliteLayout?.KickMode ?? EliteDrumsLayoutKickMode.Bars);
                elite.LaneCount = elite.EliteLayout?.Lanes.Count ?? 0;
                elite.HighwayOrdering = elite.EliteLayout?.Lanes.ToDictionary(lane => lane.Index + 1, lane => lane.Index)
                    ?? new Dictionary<int, int>();
                CurrentGameModeInfo = elite;
            }

            // 5-lane keys shares the guitar color section and lane models in-game
            // (FiveLaneKeysPlayer / FiveLaneKeysNoteElement read ColorProfile.FiveFretGuitar),
            // so reuse the FiveFretGuitar info but with normal-only notes (no HOPO/Tap),
            // the keys hit window, and the fret-array rendering path (not the pro-keys array).
            if (SelectedGameMode == GameMode.ProKeys && UseFiveLaneKeys)
            {
                // Seed from the FiveFretGuitar info: 5-lane keys shares guitar's lane
                // layout AND color providers in-game (it reads ColorProfile.FiveFretGuitar),
                // so we must start from guitar's info, not the ProKeys one (whose
                // FretColorProvider is null and whose note colors read the ProKeys section).
                var fiveLaneKeys = _gameModeInfos[GameMode.FiveFretGuitar];
                fiveLaneKeys.UseProKeys = false;
                fiveLaneKeys.HitWindowProvider = enginePreset => enginePreset.ProKeys.HitWindow;
                fiveLaneKeys.Generator = new FiveLaneKeysFakeNoteGenerator();
                CurrentGameModeInfo = fiveLaneKeys;
            }
            else if (SelectedGameMode == GameMode.ProKeys)
            {
                // Pro-keys piano-keyboard preview: 10 white keys + 7 black keys
                // (the LOW_C window, keys 0-16) spread across the highway, with
                // overlay colors drawn on the highway surface (not as fret bars).
                // Uses the real pro-keys note models (White/Black piano-key shapes).
                var info = _gameModeInfos[GameMode.ProKeys];
                info.UseProKeys = false;
                info.UseHighwayOverlay = true;
                info.NoteVisualStyle = VisualStyle.ProKeys;

                // Piano-key note positions: 17 keys at non-uniform spacing, matching
                // the in-game KeysArray layout (white keys evenly spaced, black keys
                // offset between them, gaps at E-F and B-C boundaries).
                info.NoteXPositions = ComputeProKeysNotePositions();

                // Note colors and white/black determination use ProKeysUtilities,
                // same as the original single-line preview — white/black depends on
                // the key's position in the chromatic scale, not a random assignment.
                info.NoteColorProvider = (c, note) => (ProKeysUtilities.IsWhiteKey(note.Fret % 12)
                    ? c.ProKeys.WhiteNote
                    : c.ProKeys.BlackNote).ToUnityColor();
                info.NoteStarPowerColorProvider = (c, note) => (ProKeysUtilities.IsWhiteKey(note.Fret % 12)
                    ? c.ProKeys.WhiteNoteStarPower
                    : c.ProKeys.BlackNoteStarPower).ToUnityColor();

                info.HitWindowProvider = enginePreset => enginePreset.ProKeys.HitWindow;

                CurrentGameModeInfo = info;
            }
            // If we aren't using Pro Keys, then the passed instrument doesn't really matter; arbitrarily pass Five-Fret Guitar
            var style = SelectedGameMode == GameMode.EliteDrums ? VisualStyle.FiveLaneDrums
                : VisualStyleHelpers.GetVisualStyle(SelectedGameMode, CurrentGameModeInfo.UseProKeys ? Instrument.ProKeys : Instrument.FiveFretGuitar);

            // Create frets and put them on the right layer
            if (initializeFrets != null)
            {
                initializeFrets(CurrentGameModeInfo, theme, style);
            }
            else if (!CurrentGameModeInfo.UseProKeys)
            {
                if (CurrentGameModeInfo.UseHighwayOverlay)
                {
                    // Pro-keys: initialize the fret array with an EMPTY ordering
                    // (no visible frets) to trigger the same rendering setup that
                    // other game modes rely on, then draw the overlay on top.
                    _fretArray.Initialize(
                        new Dictionary<int, int>(),
                        1, null, null,
                        theme, style);
                    CreateProKeysOverlay(colorProfile.ProKeys);
                }
                else if (CurrentGameModeInfo.EliteLayout != null && CurrentGameModeInfo.LaneCount > 0)
                {
                    var resolved = CurrentGameModeInfo.EliteLayout;
                    var ordering = new Dictionary<int, HighwayOrderingInfo>();
                    int key = 0;
                    foreach (var source in resolved.SourceToLane)
                    {
                        int savedColumn = resolved.Lanes[source.Value].SavedColumn;
                        int encoded = ColorProfile.EliteDrumsFretColorAdapter.EncodeSlot(savedColumn);
                        ordering.Add(key++, new HighwayOrderingInfo(source.Value, encoded));
                    }
                    _fretArray.UseKickFrets = resolved.KickMode == EliteDrumsLayoutKickMode.Bars;
                    _fretArray.Initialize(ordering, resolved.Lanes.Count, CurrentGameModeInfo.KickFretPrefab,
                        CreateEliteDrumsFretColorProvider(resolved, colorProfile.EliteDrums), theme, style);
                }
                else
                {
                    _fretArray.UseKickFrets = CurrentGameModeInfo.UseKickFrets;
                    _fretArray.Initialize(
                        CurrentGameModeInfo.HighwayOrdering,
                        CurrentGameModeInfo.LaneCount,
                        CurrentGameModeInfo.KickFretPrefab,
                        CurrentGameModeInfo.FretColorProvider(colorProfile),
                        theme,
                        style,
                        SelectedGameMode == GameMode.SixFretGuitar
                    );
                }
                _fretArray.transform.SetLayerRecursive(LayerMask.NameToLayer("Settings Preview"));
            }

            // Create the note prefab (this has to be specially done, because
            // TrackElements need references to the GameManager)
            var prefab = createNotePrefab(theme, CurrentGameModeInfo.NoteVisualStyle ?? style);
            _notePrefab = prefab;
            prefab.transform.parent = transform;
            prefab.SetActive(false);
            _notePool.SetPrefabAndReset(prefab);

            // Show hit window if enabled
            if (_hitWindow != null)
            {
                _hitWindow.gameObject.SetActive(showHitWindow || ForceShowHitWindow);
                _hitWindow.NoteSpeed = NOTE_SPEED;
            }
            if (_trackMaterial != null)
            {
                _trackMaterial.StarpowerMode = ForceStarPower;
                _trackMaterial.GrooveMode = ForceGroove;
            }
            if (_cameraPositioner != null)
            {
                var highwayRenderer = _cameraPositioner.GetComponent<HighwayCameraRendering>();
                var camera = _cameraPositioner.GetComponent<Camera>();
                highwayRenderer.AddPlayerParams(transform.position, camera, 0, 0, 0, 0, false);
            }

            _initialized = true;
            ApplySettings(cameraPreset, colorProfile, enginePreset, highwayPreset);
        }

        public void RefreshColors(ColorProfile colorProfile)
        {
            if (colorProfile == null) throw new ArgumentNullException(nameof(colorProfile));
            ColorProfileOverride = colorProfile;
            ColorProfile = colorProfile;
            if (!_initialized) return;

            foreach (var pooled in _notePool.AllSpawned)
                ((FakeNote)pooled).OnSettingChanged();

            if (_fretArray != null && CurrentGameModeInfo.EliteLayout != null
                && SelectedGameMode == GameMode.EliteDrums)
            {
                _fretArray.RecolorFrets(
                    CreateEliteDrumsFretColorProvider(CurrentGameModeInfo.EliteLayout, colorProfile.EliteDrums),
                    index => index);
            }
            else if (_fretArray != null && CurrentGameModeInfo.FretColorProvider != null
                && !(SelectedGameMode == GameMode.ProKeys && !UseFiveLaneKeys))
            {
                var fretColors = CurrentGameModeInfo.FretColorProvider(colorProfile);
                Func<int, int> remap = SelectedGameMode == GameMode.FiveFretGuitar
                    || (SelectedGameMode == GameMode.ProKeys && UseFiveLaneKeys)
                    ? FretColorIndexForLefty : index => index;
                _fretArray.RecolorFrets(fretColors, remap);
            }

            if (SelectedGameMode == GameMode.ProKeys && !UseFiveLaneKeys)
                RecolorProKeysOverlay(colorProfile.ProKeys);
        }

        private void OnSettingChanged()
        {
            var cameraPreset = PresetsTab.GetLastSelectedPreset(CustomContentManager.CameraSettings);
            var colorProfile = PresetsTab.GetLastSelectedPreset(CustomContentManager.ColorProfiles);
            var enginePreset = PresetsTab.GetLastSelectedPreset(CustomContentManager.EnginePresets);
            var highwayPreset = PresetsTab.GetLastSelectedPreset(CustomContentManager.HighwayPresets);

            ApplySettings(cameraPreset, colorProfile, enginePreset, highwayPreset);
        }

        private static ColorProfile.IFretColorProvider CreateEliteDrumsFretColorProvider(
            EliteDrumsResolvedLayout layout, ColorProfile.EliteDrumsColors colors)
        {
            var slotRoles = new Dictionary<int, EliteDrumsColorRole>();
            foreach (var source in layout.SourceToLane)
            {
                int encodedSlot = ColorProfile.EliteDrumsFretColorAdapter.EncodeSlot(
                    layout.Lanes[source.Value].SavedColumn);
                if (!slotRoles.ContainsKey(encodedSlot))
                {
                    slotRoles.Add(encodedSlot, source.Key switch
                    {
                        "snare" => EliteDrumsColorRole.Snare,
                        "hi-hat" => EliteDrumsColorRole.HatIndifferent,
                        "tom1" => EliteDrumsColorRole.Tom1,
                        "tom2" => EliteDrumsColorRole.Tom2,
                        "tom3" => EliteDrumsColorRole.Tom3,
                        "left-crash" => EliteDrumsColorRole.LeftCrash,
                        "ride" => EliteDrumsColorRole.Ride,
                        "right-crash" => EliteDrumsColorRole.RightCrash,
                        "kick1" => EliteDrumsColorRole.Kick,
                        "kick2" => EliteDrumsColorRole.DoubleKick,
                        "kick" => EliteDrumsColorRole.Kick,
                        _ => EliteDrumsColorRole.Kick
                    });
                }
            }

            return new ColorProfile.EliteDrumsFretColorAdapter(colors, slotRoles);
        }

        public void ApplySettings(CameraPreset cameraPreset, ColorProfile colorProfile,
            EnginePreset enginePreset, HighwayPreset highwayPreset)
        {
            CameraPreset = cameraPreset;
            ColorProfile = colorProfile;
            HighwayPreset = highwayPreset;
            // Settings previews also run in isolated UI/test contexts before settings load.
            RefreshEliteOffsetPairs();

            // Update camera presets
            if (_trackMaterial != null) _trackMaterial.Initialize(highwayPreset);
            if (_cameraPositioner != null)
            {
                _cameraPositioner.Initialize(cameraPreset);
                var camera = _cameraPositioner.GetComponent<Camera>();
                var highwayRenderer = camera.GetComponent<HighwayCameraRendering>();
                highwayRenderer.UpdateCurveFactor(cameraPreset.CurveFactor, 0);
                highwayRenderer.UpdateFadeParams(0, 3f, cameraPreset.FadeLength);
                highwayRenderer.UpdateCameraProjectionMatrices();
            }

            // Update hit window
            if (_hitWindow != null)
                _hitWindow.HitWindow = CurrentGameModeInfo.HitWindowProvider(enginePreset).Create();

            // Update all of the notes
            foreach (var note in _notePool.AllSpawned)
            {
                ((FakeNote)note).OnSettingChanged();
            }

            if (_fretArray != null && (CurrentGameModeInfo.FretColorProvider != null
                    || (SelectedGameMode == GameMode.EliteDrums && CurrentGameModeInfo.EliteLayout != null))
                && !(SelectedGameMode == GameMode.ProKeys && !UseFiveLaneKeys))
            {
                ColorProfile.IFretColorProvider fretColors;
                Func<int, int> colorIndexRemap;
                if (SelectedGameMode == GameMode.EliteDrums && CurrentGameModeInfo.EliteLayout != null)
                {
                    // Elite receptor indices encode zero-based saved slots; do not recolor
                    // through the legacy role/index provider after initial construction.
                    fretColors = CreateEliteDrumsFretColorProvider(CurrentGameModeInfo.EliteLayout,
                        colorProfile.EliteDrums);
                    colorIndexRemap = index => index;
                }
                else
                {
                    // Compressed Pro Keys resolves to Five-Fret Guitar's fret provider;
                    // use the resolved configuration rather than the selected enum alone.
                    fretColors = CurrentGameModeInfo.FretColorProvider(colorProfile);
                    var usesFiveFretColors = SelectedGameMode == GameMode.FiveFretGuitar
                        || (SelectedGameMode == GameMode.ProKeys && UseFiveLaneKeys);
                    colorIndexRemap = usesFiveFretColors
                        ? FretColorIndexForLefty
                        : (Func<int, int>)(index => index);
                }
                _fretArray.RecolorFrets(fretColors, colorIndexRemap);
            }

            if (SelectedGameMode == GameMode.ProKeys && !UseFiveLaneKeys)
            {
                // Piano-style Pro Keys has editable overlay colors, not physical fret bars.
                RecolorProKeysOverlay(colorProfile.ProKeys);
            }
        }

        /// <summary>
        /// Clears all active spotlight modes so a new one starts fresh.
        /// </summary>
        private ElitePreviewDescriptor? _eliteSpotlight;
        private bool _eliteSpotlightStarPower;

        /// <summary>Shows a specific Elite color role with independently selectable model geometry.</summary>
        public void SpotlightEliteRole(EliteDrumsColorRole role, bool starPower,
            ElitePreviewDescriptor? descriptor = null)
        {
            var appearance = descriptor ?? EliteDrumsFakeNoteGenerator.DescribeForKickLayout(role,
                ThemeNoteType.Normal, CurrentGameModeInfo.EliteLayout?.KickMode ?? EliteDrumsLayoutKickMode.Bars);
            if (AllowIncompleteEliteLayoutPreview
                && !IsEliteNoteRepresented(EliteDrumsFakeNoteGenerator.CreateRoleNote(0d, appearance),
                    CurrentGameModeInfo.EliteLayout))
                return;
            ClearSpotlights();
            _eliteSpotlight = new ElitePreviewDescriptor(role, appearance.Fret, appearance.ModelType,
                appearance.IsBar, appearance.Width, appearance.Offset, appearance.DedicatedLaneSource);
            _eliteSpotlightStarPower = starPower;
            _spotlightRemaining = SPOTLIGHT_NOTE_COUNT;
        }

        private void ClearSpotlights()
        {
            _eliteSpotlight = null;
            _spotlightRemaining = 0;
            _spotlightTypeRemaining = 0;
            _spotlightMissRemaining = 0;
            _spotlightStarPowerRemaining = 0;
        }

        /// <summary>
        /// Makes the next <see cref="SPOTLIGHT_NOTE_COUNT"/> spawned notes all come
        /// from the given lane. <paramref name="fret"/> uses the same indices as the
        /// mode's generator (guitar: 1-5 + FiveFretGuitarFret.Open; drums: 0 = kick,
        /// then lanes left to right). <paramref name="cymbal"/> selects the cymbal
        /// note models on drums; <paramref name="starPower"/> overrides the star
        /// power notes toggle in both directions, so the spotlighted notes render
        /// with star power colors when editing a star power field and regular
        /// colors otherwise, regardless of the toggle.
        /// </summary>
        public void SpotlightLane(int fret, bool centerNote, bool cymbal, bool starPower)
        {
            ClearSpotlights();
            _spotlightFret = fret;
            _spotlightCenterNote = centerNote;
            _spotlightCymbal = cymbal;
            _spotlightStarPower = starPower;
            _spotlightRemaining = SPOTLIGHT_NOTE_COUNT;
        }

        /// <summary>
        /// Makes the next <see cref="SPOTLIGHT_NOTE_COUNT"/> notes all use the
        /// specified note type (e.g. all taps, all ghosts), preserving random
        /// lane variation. Used when editing strip emission settings so the user
        /// can see the effect on the relevant note type. A null starPower value
        /// follows the preview's Star Power Notes toggle.
        /// </summary>
        public void SpotlightNoteType(ThemeNoteType noteType, bool? starPower = null)
        {
            ClearSpotlights();
            _spotlightNoteType = noteType;
            _spotlightTypeStarPower = starPower;
            _spotlightTypeRemaining = SPOTLIGHT_NOTE_COUNT;
        }

        /// <summary>
        /// Makes the next preview notes use only the requested Pro Keys color
        /// class, preserving the normal or Star Power color selection.
        /// </summary>
        public void SpotlightProKeysNoteType(bool black, bool starPower)
        {
            if (SelectedGameMode != GameMode.ProKeys || UseFiveLaneKeys)
            {
                return;
            }

            SpotlightNoteType(black ? ThemeNoteType.Black : ThemeNoteType.White, starPower);
        }

        /// <summary>
        /// Makes the next <see cref="SPOTLIGHT_NOTE_COUNT"/> notes render with
        /// the Miss color, preserving their normal note type and lane. Used when
        /// editing the Miss color field.
        /// </summary>
        public void SpotlightMiss()
        {
            ClearSpotlights();
            _spotlightMissRemaining = SPOTLIGHT_NOTE_COUNT;
        }

        /// <summary>
        /// Makes the next <see cref="SPOTLIGHT_NOTE_COUNT"/> notes render with
        /// star power colors, preserving their normal note type and lane. Used
        /// when editing the MetalStarPower color field.
        /// </summary>
        public void SpotlightStarPower()
        {
            ClearSpotlights();
            _spotlightStarPowerRemaining = SPOTLIGHT_NOTE_COUNT;
        }

        private FakeNoteData CreateSpotlightNote(double time)
        {
            if (_eliteSpotlight.HasValue)
            {
                var eliteNote = EliteDrumsFakeNoteGenerator.CreateRoleNote(time, _eliteSpotlight.Value);
                eliteNote.ForceStarPower = _eliteSpotlightStarPower;
                return eliteNote;
            }
            var note = CurrentGameModeInfo.Generator.CreateSpotlightNote(time,
                new FakeNoteSpotlight(_spotlightFret, _spotlightCenterNote, _spotlightCymbal,
                    LeftyFlip), _random);
            note.ForceStarPower = _spotlightStarPower;
            return note;
        }

        private void SpawnNote(FakeNoteData note)
        {
            var noteObj = (FakeNote)_notePool.KeyedTakeWithoutEnabling(note);
            if (noteObj == null) return;
            noteObj.NoteRef = note;
            noteObj.FakeTrackPlayer = this;
            noteObj.EnableFromPool();
        }

        private static string EliteSource(EliteDrumsColorRole role) => role switch
        {
            EliteDrumsColorRole.Snare or EliteDrumsColorRole.HandFlam => "snare",
            // Kick bars have no dedicated lane; dedicated kick descriptors carry their own source.
            EliteDrumsColorRole.Kick or EliteDrumsColorRole.DoubleKick or EliteDrumsColorRole.KickFlam => null,
            EliteDrumsColorRole.HatIndifferent or EliteDrumsColorRole.HatOpen or EliteDrumsColorRole.HatClosed
                or EliteDrumsColorRole.Stomp or EliteDrumsColorRole.Splash => "hi-hat",
            EliteDrumsColorRole.Tom1 => "tom1",
            EliteDrumsColorRole.Tom2 => "tom2",
            EliteDrumsColorRole.Tom3 => "tom3",
            EliteDrumsColorRole.LeftCrash => "left-crash",
            EliteDrumsColorRole.Ride => "ride",
            EliteDrumsColorRole.RightCrash => "right-crash",
            _ => null
        };

        private static bool IsCymbalModel(ThemeNoteType type) => type is ThemeNoteType.Cymbal or ThemeNoteType.CymbalAccent
            or ThemeNoteType.CymbalGhost or ThemeNoteType.OpenHiHat or ThemeNoteType.OpenHiHatAccent
            or ThemeNoteType.OpenHiHatGhost or ThemeNoteType.ClosedHiHat or ThemeNoteType.ClosedHiHatAccent
            or ThemeNoteType.ClosedHiHatGhost;

        private static bool IsEliteNoteRepresented(FakeNoteData note, EliteDrumsResolvedLayout layout)
        {
            if (note?.EliteDescriptor is not { } descriptor || layout == null)
                return false;
            string source = descriptor.DedicatedLaneSource ?? EliteSource(descriptor.Role);
            return source == null || layout.SourceToLane.ContainsKey(source);
        }

        private static void RefreshEliteOffsetPairFlags(IReadOnlyList<FakeNoteData> notes, bool enabled,
            EliteDrumsResolvedLayout layout)
        {
            foreach (var note in notes)
            {
                note.OffsetElitePair = false;
                note.ElitePairPartner = null;
            }
            if (layout != null)
            {
                MarkEliteOffsetPairs(notes, layout);
            }
        }

        private static void MarkEliteOffsetPairs(IReadOnlyList<FakeNoteData> notes,
            EliteDrumsResolvedLayout layout)
        {
            if (layout == null) return;
            for (int i = 0; i < notes.Count; i++)
            {
                var first = notes[i];
                if (first.EliteDescriptor is not { IsBar: false } firstDescriptor) continue;
                string firstSource = EliteSource(firstDescriptor.Role);
                if (firstSource == null || !layout.SourceToLane.TryGetValue(firstSource, out int lane))
                    continue;

                for (int j = i + 1; j < notes.Count; j++)
                {
                    var second = notes[j];
                    if (second.Time != first.Time || second.EliteDescriptor is not { IsBar: false } secondDescriptor)
                        continue;
                    bool firstCymbal = IsCymbalModel(firstDescriptor.ModelType);
                    bool secondCymbal = IsCymbalModel(secondDescriptor.ModelType);
                    if (firstCymbal == secondCymbal) continue;
                    string secondSource = EliteSource(secondDescriptor.Role);
                    if (secondSource == null || !layout.SourceToLane.TryGetValue(secondSource, out int secondLane)
                        || lane != secondLane) continue;
                    first.OffsetElitePair = true;
                    second.OffsetElitePair = true;
                    first.ElitePairPartner = second;
                    second.ElitePairPartner = first;
                }
            }
        }

        private void RefreshEliteOffsetPairs()
        {
            if (_notePool == null) return;
            var notes = _notePool.AllSpawned.Cast<FakeNote>().Where(note => note.NoteRef != null).ToArray();
            var data = notes.Select(note => note.NoteRef).ToArray();
            RefreshEliteOffsetPairFlags(data, true, CurrentGameModeInfo.EliteLayout);
            foreach (var note in notes) note.OnSettingChanged();
        }

        private void ApplyLeftyToFret(FakeNoteData note)
        {
            // 4-lane drums: lefty flip relocates the snare from red to green. The
            // generator treats red (fret 1) as the snare; relabel red<->green so green
            // becomes the all-drum snare lane and red becomes cymbal-capable. Yellow,
            // blue, and the kick are unaffected. Other game modes are unaffected.
            if (!LeftyFlip || SelectedGameMode != GameMode.FourLaneDrums || note.CenterNote)
            {
                return;
            }

            note.Fret = note.Fret switch
            {
                1 => 4,
                4 => 1,
                _ => note.Fret
            };
        }

        // Mirrors the 5-fret color order for guitar lefty flip:
        // Green(1)<->Orange(5), Red(2)<->Blue(4), Yellow(3) center.
        private int FretColorIndexForLefty(int noteType) => LeftyFlip ? 6 - noteType : noteType;

        private void Update()
        {
            Advance(Time.deltaTime);
        }

        public void Advance(double deltaTime)
        {
            if (!_initialized) return;
            if (deltaTime < 0 || double.IsNaN(deltaTime) || double.IsInfinity(deltaTime))
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            // Update the preview notes
            PreviewTime += deltaTime;

            // A structurally valid empty draft is an intentional blank preview.
            if (SelectedGameMode == GameMode.EliteDrums && CurrentGameModeInfo.LaneCount == 0)
            {
                if (_trackMaterial != null) _trackMaterial.SetTrackScroll(PreviewTime, NOTE_SPEED);
                return;
            }

            // Queue the notes
            if (_nextSpawnTime <= PreviewTime &&
                (CurrentGameModeInfo.Generator is not EliteDrumsFakeNoteGenerator eliteGenerator
                    || eliteGenerator.HasNext || _spotlightRemaining > 0 || _spotlightTypeRemaining > 0))
            {
                double spawnTime = PreviewTime + SpawnTimeOffset;
                _nextSpawnTime = PreviewTime + SPAWN_FREQ;

                bool spotlightType = _spotlightTypeRemaining > 0;
                bool spotlight = _spotlightRemaining > 0;
                bool spotlightMiss = _spotlightMissRemaining > 0;
                FakeNoteData note;
                if (spotlightType)
                {
                    _spotlightTypeRemaining--;
                    note = CurrentGameModeInfo.Generator.CreateTypeSpotlightNote(
                        spawnTime, _spotlightNoteType, _random);
                    ApplyLeftyToFret(note);

                    // Apply SP override (false = force non-SP even if toggle is on)
                    note.ForceStarPower = _spotlightTypeStarPower;
                }
                else if (spotlight)
                {
                    _spotlightRemaining--;
                    note = CreateSpotlightNote(spawnTime);
                }
                else
                {
                    note = CurrentGameModeInfo.Generator.CreateNote(spawnTime, _random);
                }

                if (SelectedGameMode == GameMode.EliteDrums
                    && !IsEliteNoteRepresented(note, CurrentGameModeInfo.EliteLayout))
                {
                    _nextSpawnTime = PreviewTime;
                    return;
                }

                if (spotlightMiss)
                {
                    _spotlightMissRemaining--;
                    note.ForceMiss = true;
                }

                if (_spotlightStarPowerRemaining > 0)
                {
                    _spotlightStarPowerRemaining--;
                    note.ForceStarPower = true;
                }

                // Generate chord extras before lefty so fret comparisons
                // are in the same (non-transformed) space.
                var notesToSpawn = new List<FakeNoteData>();
                if (!spotlight && !spotlightType)
                {
                    foreach (var chordNote in CurrentGameModeInfo.Generator.CreateChordNotes(
                        spawnTime, note, _random))
                    {
                        if (SelectedGameMode == GameMode.EliteDrums
                            && !IsEliteNoteRepresented(chordNote, CurrentGameModeInfo.EliteLayout))
                            continue;
                        ApplyLeftyToFret(chordNote);
                        notesToSpawn.Add(chordNote);
                    }
                }

                // A filtered-out pedal companion must not leave an isolated half bar.
                if (note.EliteDescriptor is { IsBar: true } kickDescriptor &&
                    kickDescriptor.Role is EliteDrumsColorRole.Kick or EliteDrumsColorRole.DoubleKick &&
                    !notesToSpawn.Exists(extra => extra.EliteDescriptor is { } descriptor &&
                        descriptor.Role is EliteDrumsColorRole.Stomp or EliteDrumsColorRole.Splash))
                {
                    note.EliteDescriptor = new ElitePreviewDescriptor(kickDescriptor.Role, kickDescriptor.Fret,
                        kickDescriptor.ModelType, true, 1f, 0f, kickDescriptor.DedicatedLaneSource);
                }
                ApplyLeftyToFret(note);
                notesToSpawn.Add(note);
                MarkEliteOffsetPairs(notesToSpawn, CurrentGameModeInfo.EliteLayout);
                foreach (var noteToSpawn in notesToSpawn) SpawnNote(noteToSpawn);
            }

            var spawned = new List<IPoolable>(_notePool.AllSpawned);
            foreach (var pooled in spawned)
            {
                ((FakeNote)pooled).RefreshPosition();
            }
            if (_trackMaterial != null) _trackMaterial.SetTrackScroll(PreviewTime, NOTE_SPEED);
        }

        private void OnDestroy()
        {
            if (_settingsMenu != null) _settingsMenu.SettingChanged -= OnSettingChanged;
            if (_notePool != null) _notePool.ReturnAllObjects();
            if (_notePrefab != null) Destroy(_notePrefab);
        }

        // --- Pro-keys highway overlay ---

        private const float PRO_KEYS_OVERLAY_LENGTH   = 10f;
        private const float PRO_KEYS_OVERLAY_Z_CENTER = 0.5f;
        private const float PRO_KEYS_OVERLAY_ALPHA    = 0.05f;
        private const float PRO_KEYS_LANE_GAP         = 0.015f;

        private readonly struct ProKeysOverlayRenderer
        {
            public ProKeysOverlayRenderer(SpriteRenderer renderer, int colorGroup)
            {
                Renderer = renderer;
                ColorGroup = colorGroup;
            }

            public SpriteRenderer Renderer { get; }
            public int ColorGroup { get; }
        }

        private void CreateProKeysOverlay(ColorProfile.ProKeysColors colors)
        {
            _proKeysOverlayRenderers.Clear();
            var layerMask = LayerMask.NameToLayer("Settings Preview");

            // Use SpriteRenderers so the transparent overlays do not occlude the
            // highway. The gameplay overlay uses the same per-key geometry and
            // edge texture, but its material also depends on gameplay fade state.
            var whiteSprite = CreateOverlaySprite(Texture2D.whiteTexture);
            var edgeSprite = _proKeysEdgeTexture != null
                ? CreateOverlaySprite(_proKeysEdgeTexture)
                : null;

            foreach (var overlayLayer in ProKeysPreviewLayout.CreateOverlayLayers(
                TrackPlayer.TRACK_WIDTH, PRO_KEYS_LANE_GAP))
            {
                if (overlayLayer.IsEdge && edgeSprite == null)
                {
                    continue;
                }

                var overlay = new GameObject(overlayLayer.IsEdge
                    ? "ProKeysOverlayEdge"
                    : "ProKeysOverlayFill");
                overlay.transform.SetParent(transform, false);
                overlay.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                overlay.transform.localPosition = new Vector3(
                    overlayLayer.Band.Center, 0.01f, PRO_KEYS_OVERLAY_Z_CENTER);

                var sr = overlay.AddComponent<SpriteRenderer>();
                sr.sprite = overlayLayer.IsEdge ? edgeSprite : whiteSprite;
                sr.flipX = overlayLayer.FlipX;
                sr.sortingOrder = overlayLayer.IsEdge ? 1 : 0;
                var spriteSize = sr.sprite.bounds.size;
                overlay.transform.localScale = new Vector3(
                    overlayLayer.Band.Width / spriteSize.x,
                    PRO_KEYS_OVERLAY_LENGTH / spriteSize.y,
                    1f);

                var oc = colors.GetOverlayColor(overlayLayer.Band.Group).ToUnityColor();
                sr.color = new Color(oc.r, oc.g, oc.b, oc.a * PRO_KEYS_OVERLAY_ALPHA);

                overlay.transform.SetLayerRecursive(layerMask);
                _proKeysOverlayRenderers.Add(new ProKeysOverlayRenderer(
                    sr,
                    overlayLayer.Band.Group));
            }
        }

        private static Sprite CreateOverlaySprite(Texture2D texture)
        {
            return Sprite.Create(
                texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                texture.width);
        }

        private void RecolorProKeysOverlay(ColorProfile.ProKeysColors colors)
        {
            for (int i = 0; i < _proKeysOverlayRenderers.Count; i++)
            {
                var c = colors.GetOverlayColor(_proKeysOverlayRenderers[i].ColorGroup).ToUnityColor();
                _proKeysOverlayRenderers[i].Renderer.color = new Color(
                    c.r,
                    c.g,
                    c.b,
                    c.a * PRO_KEYS_OVERLAY_ALPHA);
            }
        }

        /// <summary>
        /// Computes the X positions for the 17 visible pro-keys (keys 0-16, the
        /// LOW_C window: 10 white + 7 black) using the same algorithm as
        /// <see cref="KeysArray"/>: white keys evenly spaced, black keys offset
        /// by half a spacing, with gaps at the E-F and B-C boundaries.
        /// </summary>
        /// <summary>
        /// Computes the X positions for the six-fret guitar preview: the six frets
        /// collapse onto three visual lanes (black+white pair per lane), so frets
        /// 1 and 4 share lane 0, 2 and 5 share lane 1, and 3 and 6 share lane 2.
        /// </summary>
        private static float[] ComputeSixFretNotePositions()
        {
            const int FRET_COUNT = 6;

            var positions = new float[FRET_COUNT + 1]; // indexed by fret value (1-6)
            for (int fret = 1; fret <= FRET_COUNT; fret++)
            {
                int laneIndex = (fret - 1) % 3;
                positions[fret] = TrackPlayer.TRACK_WIDTH / 3f * laneIndex
                    - TrackPlayer.TRACK_WIDTH / 2f + 1f / 3f;
            }

            return positions;
        }

        private static float[] ComputeProKeysNotePositions()
        {
            const int KEY_COUNT = 17; // keys 0-16

            float spacing = TrackPlayer.TRACK_WIDTH / ProKeysPlayer.WHITE_KEY_VISIBLE_COUNT;
            float whiteOffset = -TrackPlayer.TRACK_WIDTH / 2f + spacing / 2f;
            float blackOffset = whiteOffset + spacing / 2f;

            var positions = new float[KEY_COUNT];
            int whitePos = 0;
            int blackPos = 0;

            for (int i = 0; i < KEY_COUNT; i++)
            {
                int noteIndex = i % 12;
                if (ProKeysUtilities.IsBlackKey(noteIndex))
                {
                    positions[i] = blackPos * spacing + blackOffset;
                    blackPos++;
                    if (ProKeysUtilities.IsGapOnNextBlackKey(noteIndex))
                    {
                        blackPos++; // skip the gap (E-F or B-C boundary)
                    }
                }
                else
                {
                    positions[i] = whitePos * spacing + whiteOffset;
                    whitePos++;
                }
            }

            return positions;
        }
    }
}
