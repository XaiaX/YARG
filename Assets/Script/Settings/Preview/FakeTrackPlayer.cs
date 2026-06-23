using System;
using System.Collections.Generic;
using UnityEngine;
using YARG.Assets.Script.Helpers;
using YARG.Core;
using YARG.Core.Chart;
using YARG.Core.Engine.Keys;
using YARG.Core.Game;
using YARG.Gameplay;
using YARG.Gameplay.Player;
using YARG.Gameplay.Visuals;
using YARG.Helpers.Extensions;
using YARG.Menu.Settings;
using YARG.Settings.Customization;
using YARG.Settings.Metadata;
using YARG.Themes;
using Random = UnityEngine.Random;

namespace YARG.Settings.Preview
{
    public class FakeTrackPlayer : MonoBehaviour
    {
        public struct Info
        {
            public delegate ColorProfile.IFretColorProvider FretColorProviderFunc(ColorProfile c);
            public delegate Color NoteColorProviderFunc(ColorProfile c, FakeNoteData note);
            public delegate EnginePreset.HitWindowPreset HitWindowProviderFunc(EnginePreset e);
            public delegate FakeNoteData CreateFakeNoteFunc(double time);

            public bool UseKickFrets;
            public bool UseProKeys;
            public bool UseHighwayOverlay;

            public Dictionary<int, int> HighwayOrdering;
            public int LaneCount;
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

            public CreateFakeNoteFunc CreateFakeNote;
        }

        private static readonly Dictionary<GameMode, Info> _gameModeInfos = new()
        {
            {
                GameMode.FiveFretGuitar,
                new Info
                {
                    HighwayOrdering = FiveFretGuitarPlayer.DEFAULT_HIGHWAY_ORDERING,
                    LaneCount = 5,

                    FretColorProvider = (colorProfile) => colorProfile.FiveFretGuitar,
                    NoteColorProvider = (colorProfile, note) => colorProfile.FiveFretGuitar
                        .GetNoteColor(note.Fret)
                        .ToUnityColor(),
                    NoteStarPowerColorProvider = (colorProfile, note) => colorProfile.FiveFretGuitar
                        .GetNoteStarPowerColor(note.Fret)
                        .ToUnityColor(),

                    HitWindowProvider = (enginePreset) => enginePreset.FiveFretGuitar.HitWindow,

                    CreateFakeNote = (time) =>
                    {
                        // Here we use 0 as open as it's easier to visualize.
                        // We convert this into the correct value in the if below.
                        int fret = Random.Range(0, 6);

                        // Open notes have different models
                        if (fret == 0)
                        {
                            return new FakeNoteData
                            {
                                Time = time,

                                Fret = (int) FiveFretGuitarFret.Open,
                                CenterNote = true,
                                NoteType = ThemeNoteType.Open
                            };
                        }

                        // Otherwise, select a random note type
                        var noteType = Random.Range(0, 3) switch
                        {
                            0 => ThemeNoteType.Normal,
                            1 => ThemeNoteType.HOPO,
                            2 => ThemeNoteType.Tap,
                            _ => throw new Exception("Unreachable.")
                        };

                        return new FakeNoteData
                        {
                            Time = time,

                            Fret = fret,
                            CenterNote = false,
                            NoteType = noteType
                        };
                    }
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
                        int colorNote = (note.Fret, note.NoteType) switch
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
                        int colorNote = (note.Fret, note.NoteType) switch
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

                    CreateFakeNote = (time) =>
                    {
                        int fret = Random.Range(0, 5);
                        ThemeNoteType noteType;

                        // Kick notes have different models
                        if (fret == 0)
                        {
                            return new FakeNoteData
                            {
                                Time = time,

                                Fret = fret,
                                CenterNote = true,
                                NoteType = ThemeNoteType.Kick
                            };
                        }

                        // Red lane (snare): 100% drum, never cymbal. Other lanes: 25% drum, 75% cymbal.
                        // Within each: 75% base, 15% accent, 10% ghost. Lefty flip relocates the snare
                        // from red to green via ApplyLeftyToFret (see Update).
                        bool isCymbal = fret != 1 && Random.Range(0, 100) < 75;
                        int variant = Random.Range(0, 100);
                        if (isCymbal)
                        {
                            noteType = variant < 75 ? ThemeNoteType.Cymbal
                                : variant < 90 ? ThemeNoteType.CymbalAccent
                                : ThemeNoteType.CymbalGhost;
                        }
                        else
                        {
                            noteType = variant < 75 ? ThemeNoteType.Normal
                                : variant < 90 ? ThemeNoteType.Accent
                                : ThemeNoteType.Ghost;
                        }

                        return new FakeNoteData
                        {
                            Time = time,

                            Fret = fret,
                            CenterNote = false,
                            NoteType = noteType
                        };
                    }
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

                    CreateFakeNote = (time) =>
                    {
                        int fret = Random.Range(0, 6);

                        // Kick notes have different models
                        if (fret == 0)
                        {
                            return new FakeNoteData
                            {
                                Time = time,

                                Fret = fret,
                                CenterNote = true,
                                NoteType = ThemeNoteType.Kick
                            };
                        }

                        // Cymbal lanes (2, 4): cymbal variants. Other lanes: drum variants.
                        // Within each: 75% base, 15% accent, 10% ghost.
                        ThemeNoteType noteType;
                        int variant = Random.Range(0, 100);
                        if (fret is 2 or 4)
                        {
                            noteType = variant < 75 ? ThemeNoteType.Cymbal
                                : variant < 90 ? ThemeNoteType.CymbalAccent
                                : ThemeNoteType.CymbalGhost;
                        }
                        else
                        {
                            noteType = variant < 75 ? ThemeNoteType.Normal
                                : variant < 90 ? ThemeNoteType.Accent
                                : ThemeNoteType.Ghost;
                        }

                        return new FakeNoteData
                        {
                            Time = time,

                            Fret = fret,
                            CenterNote = false,
                            NoteType = noteType
                        };
                    }
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

                    CreateFakeNote = (time) =>
                    {
                        int fret = Random.Range(0, 17);

                        // Otherwise, select the correct note type
                        var noteType = ThemeNoteType.White;
                        if (ProKeysUtilities.IsBlackKey(fret % 12))
                        {
                            noteType = ThemeNoteType.Black;
                        }

                        return new FakeNoteData
                        {
                            Time = time,

                            Fret = fret,
                            CenterNote = true,
                            NoteType = noteType
                        };
                    }
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

        // Renderers for the pro-keys highway overlay quads (5 sections, one per
        // overlay color group). Stored for live-recoloring on setting change.
        private readonly List<SpriteRenderer> _proKeysOverlayRenderers = new();

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

        // When true (default), the keys (ProKeys) section previews as 5-lane keys
        // (guitar-style lanes + colors, no HOPO/Tap). When false, full pro keys.
        public bool UseFiveLaneKeys { get; set; } = true;

        public GameMode SelectedGameMode { get; set; } = GameMode.FiveFretGuitar;

        public double PreviewTime { get; private set; }
        private double _nextSpawnTime;

        public Info CurrentGameModeInfo { get; private set; }

        private void Start()
        {
            CurrentGameModeInfo = _gameModeInfos[SelectedGameMode];

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
                fiveLaneKeys.CreateFakeNote = time =>
                {
                    int fret = Random.Range(0, 6);

                    if (fret == 0)
                    {
                        return new FakeNoteData
                        {
                            Time = time,

                            Fret = (int) FiveFretGuitarFret.Open,
                            CenterNote = true,
                            NoteType = ThemeNoteType.Open
                        };
                    }

                    // 5-lane keys has no note variations (no HOPO/Tap)
                    return new FakeNoteData
                    {
                        Time = time,

                        Fret = fret,
                        CenterNote = false,
                        NoteType = ThemeNoteType.Normal
                    };
                };
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

                info.CreateFakeNote = time =>
                {
                    int key = Random.Range(0, 17); // keys 0-16 (LOW_C window)
                    var noteType = ProKeysUtilities.IsBlackKey(key % 12)
                        ? ThemeNoteType.Black
                        : ThemeNoteType.White;
                    return new FakeNoteData
                    {
                        Time = time,
                        Fret = key,
                        CenterNote = false,
                        NoteType = noteType
                    };
                };

                CurrentGameModeInfo = info;
            }
            var theme = ThemePreset.Default;

            // If we aren't using Pro Keys, then the passed instrument doesn't really matter; arbitrarily pass Five-Fret Guitar
            var style = VisualStyleHelpers.GetVisualStyle(SelectedGameMode, CurrentGameModeInfo.UseProKeys ? Instrument.ProKeys : Instrument.FiveFretGuitar);

            // Create frets and put them on the right layer
            if (!CurrentGameModeInfo.UseProKeys)
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
                    CreateProKeysOverlay(ColorProfile.Default.ProKeys);
                }
                else
                {
                    _fretArray.UseKickFrets = CurrentGameModeInfo.UseKickFrets;
                    _fretArray.Initialize(
                        CurrentGameModeInfo.HighwayOrdering,
                        CurrentGameModeInfo.LaneCount,
                        CurrentGameModeInfo.KickFretPrefab,
                        CurrentGameModeInfo.FretColorProvider(ColorProfile.Default),
                        theme,
                        style
                    );
                }
                _fretArray.transform.SetLayerRecursive(LayerMask.NameToLayer("Settings Preview"));
            }

            // Create the note prefab (this has to be specially done, because
            // TrackElements need references to the GameManager)
            var prefab = FakeNote.CreateFakeNoteFromTheme(theme,
                CurrentGameModeInfo.NoteVisualStyle ?? style);
            prefab.transform.parent = transform;
            prefab.SetActive(false);
            _notePool.SetPrefabAndReset(prefab);

            // Show hit window if enabled
            _hitWindow.gameObject.SetActive(SettingsManager.Settings.ShowHitWindow.Value || ForceShowHitWindow);
            _hitWindow.NoteSpeed = NOTE_SPEED;
            _trackMaterial.StarpowerMode = ForceStarPower;
            _trackMaterial.GrooveMode = ForceGroove;

            SettingsMenu.Instance.SettingChanged += OnSettingChanged;

            var highwayRenderer = _cameraPositioner.GetComponent<HighwayCameraRendering>();
            var camera = _cameraPositioner.GetComponent<Camera>();
            highwayRenderer.AddPlayerParams(transform.position, camera, 0, 0, 0, 0, false);

            // Force update it as well to make sure it's right before any settings are changed
            OnSettingChanged();
        }

        private void OnSettingChanged()
        {
            var cameraPreset = PresetsTab.GetLastSelectedPreset(CustomContentManager.CameraSettings);
            var colorProfile = PresetsTab.GetLastSelectedPreset(CustomContentManager.ColorProfiles);
            var enginePreset = PresetsTab.GetLastSelectedPreset(CustomContentManager.EnginePresets);
            var highwayPreset = PresetsTab.GetLastSelectedPreset(CustomContentManager.HighwayPresets);

            // Update camera presets
            _trackMaterial.Initialize(highwayPreset);
            _cameraPositioner.Initialize(cameraPreset);

            var camera = _cameraPositioner.GetComponent<Camera>();
            var highwayRenderer = camera.GetComponent<HighwayCameraRendering>();
            highwayRenderer.UpdateCurveFactor(cameraPreset.CurveFactor, 0);
            highwayRenderer.UpdateFadeParams(0, 3f, cameraPreset.FadeLength);
            highwayRenderer.UpdateCameraProjectionMatrices();

            // Update hit window
            _hitWindow.HitWindow = CurrentGameModeInfo.HitWindowProvider(enginePreset).Create();

            // Update all of the notes
            foreach (var note in _notePool.AllSpawned)
            {
                ((FakeNote)note).OnSettingChanged();
            }

            // Reverse the fret color order for guitar lefty flip. Frets use the default
            // color profile; reversing their assignment mirrors the layout in place
            // without moving frets or touching asymmetric theme graphics.
            if (SelectedGameMode == GameMode.FiveFretGuitar)
            {
                _fretArray.RecolorFrets(
                    CurrentGameModeInfo.FretColorProvider(ColorProfile.Default),
                    FretColorIndexForLefty);
            }
            else if (SelectedGameMode == GameMode.ProKeys && !UseFiveLaneKeys)
            {
                // Pro-keys: live-recolor the highway overlay sections with the
                // PRESET's overlay colors (not ColorProfile.Default), so editing
                // an overlay color live-updates without a rebuild.
                RecolorProKeysOverlay(colorProfile.ProKeys);
            }
        }

        private void SpawnNote(FakeNoteData note)
        {
            var noteObj = (FakeNote)_notePool.KeyedTakeWithoutEnabling(note);
            noteObj.NoteRef = note;
            noteObj.FakeTrackPlayer = this;
            noteObj.EnableFromPool();
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
            // Update the preview notes
            PreviewTime += Time.deltaTime;

            // Queue the notes
            if (_nextSpawnTime <= PreviewTime)
            {
                double spawnTime = PreviewTime + SpawnTimeOffset;
                _nextSpawnTime = PreviewTime + SPAWN_FREQ;

                var note = CurrentGameModeInfo.CreateFakeNote(spawnTime);
                ApplyLeftyToFret(note);
                SpawnNote(note);

                // For drums, sometimes spawn chords (multiple pads/cymbals + kick)
                if (SelectedGameMode is GameMode.FourLaneDrums or GameMode.FiveLaneDrums)
                {
                    if (note.CenterNote)
                    {
                        // Kick came first; add a pad/cymbal alongside it
                        var alongsideKick = CurrentGameModeInfo.CreateFakeNote(spawnTime);
                        ApplyLeftyToFret(alongsideKick);
                        SpawnNote(alongsideKick);
                    }
                    else
                    {
                        // Sometimes add a second pad/cymbal on a different lane
                        if (Random.Range(0, 3) == 0)
                        {
                            for (int i = 0; i < 3; i++)
                            {
                                var extra = CurrentGameModeInfo.CreateFakeNote(spawnTime);
                                ApplyLeftyToFret(extra);
                                if (!extra.CenterNote && extra.Fret != note.Fret)
                                {
                                    SpawnNote(extra);
                                    break;
                                }
                            }
                        }

                        // Sometimes add a kick alongside pads/cymbals
                        if (Random.Range(0, 3) == 0)
                        {
                            SpawnNote(new FakeNoteData
                            {
                                Time = spawnTime,
                                Fret = 0,
                                CenterNote = true,
                                NoteType = ThemeNoteType.Kick
                            });
                        }
                    }
                }

                // For 5-lane keys, sometimes spawn chords (up to 3 additional
                // notes). Scan lanes upward from the first note: 1/4 chance to
                // place, halving each time a note is placed (1/8, 1/16, ...).
                // No chords with open notes.
                if (SelectedGameMode == GameMode.ProKeys && UseFiveLaneKeys)
                {
                    if (!note.CenterNote) // no chords with open notes
                    {
                        int placed = 0;
                        int denominator = 4; // start at 1/4 probability
                        for (int pos = note.Fret + 1; pos <= 5 && placed < 3; pos++)
                        {
                            if (Random.Range(0, denominator) == 0)
                            {
                                SpawnNote(new FakeNoteData
                                {
                                    Time = spawnTime,
                                    Fret = pos,
                                    CenterNote = false,
                                    NoteType = ThemeNoteType.Normal
                                });
                                placed++;
                                denominator *= 2; // halve: 1/8 → 1/16 → 1/32...
                            }
                        }
                    }
                }

                // For pro keys, sometimes spawn chords (up to 4 notes). Scan
                // positions from the first note: descending probability (1/4,
                // halving after each placement) with skip-2/skip-1 spacing to
                // avoid z-fighting between adjacent white/black notes. Capped at
                // start+8 to avoid unnaturally wide chords.
                if (SelectedGameMode == GameMode.ProKeys && !UseFiveLaneKeys)
                {
                    int noteCount = 1;
                    int denominator = 4; // start at 1/4 probability
                    int pos = note.Fret + 2;
                    int maxPos = Math.Min(note.Fret + 8, 16);
                    while (noteCount < 4 && pos <= maxPos)
                    {
                        if (Random.Range(0, denominator) == 0)
                        {
                            SpawnNote(new FakeNoteData
                            {
                                Time = spawnTime,
                                Fret = pos,
                                CenterNote = false,
                                NoteType = ProKeysUtilities.IsBlackKey(pos % 12)
                                    ? ThemeNoteType.Black
                                    : ThemeNoteType.White
                            });
                            noteCount++;
                            denominator *= 2; // halve: 1/8 → 1/16 → ...
                            pos += 2; // skip 2 after placing
                        }
                        else
                        {
                            pos += 1; // skip 1 if not placing
                        }
                    }
                }
            }

            _trackMaterial.SetTrackScroll(PreviewTime, NOTE_SPEED);
        }

        private void OnDestroy()
        {
            SettingsMenu.Instance.SettingChanged -= OnSettingChanged;
        }

        // --- Pro-keys highway overlay ---

        // X centers for the 5 overlay groups (2 white keys each, left to right).
        // White key spacing = TRACK_WIDTH / 10 = 0.2, so each group is 0.4 wide.
        private static readonly float[] PRO_KEYS_OVERLAY_CENTERS = { -0.8f, -0.4f, 0f, 0.4f, 0.8f };
        private const float PRO_KEYS_OVERLAY_WIDTH   = 0.4f;
        private const float PRO_KEYS_OVERLAY_LENGTH   = 10f;   // covers the visible highway
        private const float PRO_KEYS_OVERLAY_Z_CENTER = 0.5f;  // midpoint of z=-4..5
        private const float PRO_KEYS_OVERLAY_ALPHA    = 0.05f;

        private void CreateProKeysOverlay(ColorProfile.ProKeysColors colors)
        {
            _proKeysOverlayRenderers.Clear();
            var layer = LayerMask.NameToLayer("Settings Preview");

            // Use a SpriteRenderer (not MeshRenderer) so the overlay always
            // renders transparent with ZWrite Off. A MeshRenderer with URP/Unlit
            // can't reliably disable ZWrite (the transparent shader variant isn't
            // compiled unless another material uses it), which occludes the highway.
            var sprite = Sprite.Create(Texture2D.whiteTexture,
                new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);

            for (int group = 0; group < 5; group++)
            {
                var overlay = new GameObject("ProKeysOverlay");
                overlay.transform.SetParent(transform, false);
                // Rotate to lie flat on the highway surface (facing up)
                overlay.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                overlay.transform.localPosition = new Vector3(
                    PRO_KEYS_OVERLAY_CENTERS[group], 0.01f, PRO_KEYS_OVERLAY_Z_CENTER);
                overlay.transform.localScale = new Vector3(
                    PRO_KEYS_OVERLAY_WIDTH, PRO_KEYS_OVERLAY_LENGTH, 1f);

                var sr = overlay.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                var oc = colors.GetOverlayColor(group).ToUnityColor();
                sr.color = new Color(oc.r, oc.g, oc.b, oc.a * PRO_KEYS_OVERLAY_ALPHA);

                overlay.transform.SetLayerRecursive(layer);
                _proKeysOverlayRenderers.Add(sr);
            }
        }

        private void RecolorProKeysOverlay(ColorProfile.ProKeysColors colors)
        {
            for (int i = 0; i < _proKeysOverlayRenderers.Count; i++)
            {
                var c = colors.GetOverlayColor(i).ToUnityColor();
                _proKeysOverlayRenderers[i].color = new Color(c.r, c.g, c.b, c.a * PRO_KEYS_OVERLAY_ALPHA);
            }
        }

        /// <summary>
        /// Computes the X positions for the 17 visible pro-keys (keys 0-16, the
        /// LOW_C window: 10 white + 7 black) using the same algorithm as
        /// <see cref="KeysArray"/>: white keys evenly spaced, black keys offset
        /// by half a spacing, with gaps at the E-F and B-C boundaries.
        /// </summary>
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
