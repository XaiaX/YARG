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

            public Dictionary<int, int> HighwayOrdering;
            public int LaneCount;
            #nullable enable
            public GameObject? FretPrefab;
            public GameObject? KickFretPrefab;
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

        public bool ForceShowHitWindow { get; set; }
        public bool ForceGroove { get; set; }
        public bool ForceStarPower { get; set; }
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
            var theme = ThemePreset.Default;

            // If we aren't using Pro Keys, then the passed instrument doesn't really matter; arbitrarily pass Five-Fret Guitar
            var style = VisualStyleHelpers.GetVisualStyle(SelectedGameMode, CurrentGameModeInfo.UseProKeys ? Instrument.ProKeys : Instrument.FiveFretGuitar);

            // Create frets and put then on the right layer
            if (!CurrentGameModeInfo.UseProKeys)
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
                _fretArray.transform.SetLayerRecursive(LayerMask.NameToLayer("Settings Preview"));
            }

            // Create the note prefab (this has to be specially done, because
            // TrackElements need references to the GameManager)
            var prefab = FakeNote.CreateFakeNoteFromTheme(theme, style);
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
            }

            _trackMaterial.SetTrackScroll(PreviewTime, NOTE_SPEED);
        }

        private void OnDestroy()
        {
            SettingsMenu.Instance.SettingChanged -= OnSettingChanged;
        }
    }
}
