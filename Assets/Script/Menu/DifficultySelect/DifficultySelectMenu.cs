using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using YARG.Core;
using YARG.Core.Extensions;
using YARG.Core.Game;
using YARG.Core.Input;
using YARG.Core.Song;
using YARG.Core.Utility;
using YARG.Helpers.Extensions;
using YARG.Integration.Maestro;
using YARG.Localization;
using YARG.Menu.Navigation;
using YARG.Menu.Persistent;
using YARG.Menu.Filters;
using YARG.Menu.MusicLibrary;
using YARG.Menu.Maestro;
using YARG.Player;
using YARG.Settings;
using YARG.Song;

// pattern: Mixed (needs refactoring)

namespace YARG.Menu.DifficultySelect
{
    public class DifficultySelectMenu : MonoBehaviour
    {
        /// <summary>
        /// The saved song speed value
        /// </summary>
        private static float _songSpeed = 1f;

        private enum State
        {
            Main,
            Instrument,
            Difficulty,
            Adjustments,
            Modifiers,
            OpenLane,
            Accessibility,
            Harmony,
            PartyVocalsBotMicCount,
            PartyVocalsChartChoice
        }

        // Modifiers relocated from the Modifiers menu to the Accessibility menu.
        // RangeCompress is folded into the "No Range Shifts" toggle there.
        private const Modifier ACCESSIBILITY_MODIFIERS =
            Modifier.OpensToGreens | Modifier.NoKicks | Modifier.UnpitchedOnly | Modifier.RangeCompress;

        // Backdrop circle marking the selected instrument's ring — translucent
        // black (a blue tint blended into the row's blue selection highlight).
        private static readonly Color SELECTED_INSTRUMENT_COLOR = new Color(0f, 0f, 0f, 0.5f);

        // Non-selected instrument icons in the main menu's ring row are dimmed
        // so the selected one stands out.
        private static readonly Color UNSELECTED_INSTRUMENT_ICON_COLOR = new Color(1f, 1f, 1f, 0.33f);

        // Done buttons get the Ready treatment in the menu's pale blue instead
        // of green: tinted text that darkens while the row is highlighted (the
        // dark blue derives from #2ED9FF with the same per-channel darkening
        // the green pair uses).
        private static readonly Color DONE_TEXT_COLOR = new Color32(0x2E, 0xD9, 0xFF, 0xFF);
        private static readonly Color DONE_SELECTED_TEXT_COLOR = new Color32(0x01, 0x22, 0x27, 0xFF);

        [SerializeField]
        private TextMeshProUGUI _subHeader;
        [SerializeField]
        private Transform _container;
        [SerializeField]
        private NavigationGroup _navGroup;
        [SerializeField]
        private TextMeshProUGUI _text;
        [SerializeField]
        private DifficultyRing _difficultyRing;
        [SerializeField]
        private TMP_InputField _speedInput;
        [SerializeField]
        private TextMeshProUGUI _loadingPhrase;
        [SerializeField]
        private TextMeshProUGUI _warningMessage;
        [SerializeField]
        private GameObject _warningMessageContainer;

        [Space]
        [SerializeField]
        private TextMeshProUGUI _songTitleText;
        [SerializeField]
        private TextMeshProUGUI _artistText;
        [SerializeField]
        private Image _sourceIcon;

        [Space]
        [SerializeField]
        private DifficultyItem _difficultyItemPrefab;
        [SerializeField]
        private DifficultyItem _difficultyGreenPrefab;
        [SerializeField]
        private DifficultyItem _difficultyRedPrefab;
        [SerializeField]
        private DifficultyItem _difficultyItemSmallRedPrefab;
        [SerializeField]
        private ModifierItem _modifierItemPrefab;

        private int _playerIndex;
        private int _vocalModifierSelectIndex = -1;
        private Guid _vocalModifierPrimaryProfileId;

        private State _lastMenuState;
        private State _menuState;

        private readonly List<Instrument> _possibleInstruments  = new();
        private readonly List<Difficulty> _possibleDifficulties = new();
        private readonly List<Modifier>   _possibleModifiers    = new();

        [NonSerialized]
        private Modifier _excusableModifiers;

        private int _maxHarmonyIndex = 3;

        private readonly List<ModifierItem> _modifierItems = new();
        private readonly List<Modifier> _itemModifiers = new();

        private List<SongEntry> _songList;

        private YargPlayer CurrentPlayer => PlayerContainer.Players[_playerIndex];

        private ScrollRect _scrollRect;
        private Scrollbar _scrollbar;

        private void OnEnable()
        {
            string subHeaderKey = GlobalVariables.State.IsPractice ? "Practice" : "Quickplay";
            _subHeader.text = Localize.Key("Menu.Main.Options", subHeaderKey);

            // Maestro Back returns to the completed-player boundary instead of starting a
            // fresh song setup session. The page has already popped its own scheme before
            // reactivating this menu, so this menu owns the restored Difficulty Select scheme.
            var pageSession = MaestroSetupSession.Active;
            bool returningFromMaestro = pageSession?.ReturningToDifficultySelect == true;

            // Set navigation scheme
            Navigator.Instance.PushScheme(new NavigationScheme(new()
            {
                NavigationScheme.Entry.NavigateUp,
                NavigationScheme.Entry.NavigateDown,
                NavigationScheme.Entry.NavigateSelect,
                new NavigationScheme.Entry(MenuAction.Red, "Menu.Common.Back", () =>
                {
                    if (_menuState == State.Main)
                    {
                        if (_playerIndex == 0)
                        {
                            MenuManager.Instance.PopMenu();
                        }
                        else
                        {
                            ChangePlayer(-1);
                        }
                    }
                    else
                    {
                        // Nested menus back out one level at a time:
                        // OpenLane -> Modifiers -> Adjustments -> Main.
                        _menuState = _menuState switch
                        {
                            State.OpenLane => State.Modifiers,
                            State.Modifiers or State.Accessibility => State.Adjustments,
                            _ => State.Main,
                        };
                        UpdateForPlayer();
                    }
                })
            }, false));

            _speedInput.text = $"{Mathf.RoundToInt(_songSpeed * 100f)}%";
            _songTitleText.text = GlobalVariables.State.CurrentSong.Name;
            _artistText.text = GlobalVariables.State.CurrentSong.Artist;

            if (GlobalVariables.State.PlayingAShow)
            {
                _songList = GlobalVariables.State.ShowSongs;
            }
            else
            {
                _songList = new List<SongEntry> { GlobalVariables.State.CurrentSong };
            }

            if (!returningFromMaestro && SettingsManager.Settings.MaestroEnable.Value &&
                SettingsManager.Settings.MaestroGoDirectlyToSummary.Value)
            {
                OpenMaestroSummaryDirectly();
                return;
            }

            if (returningFromMaestro)
            {
                _playerIndex = Mathf.Clamp(pageSession.CompletedPlayerBoundary - 1, 0,
                    Mathf.Max(0, PlayerContainer.Players.Count - 1));
                _vocalModifierPrimaryProfileId = pageSession.VocalPrimaryProfileId;
                _vocalModifierSelectIndex = -1;
                if (_vocalModifierPrimaryProfileId != default)
                {
                    for (int i = 0; i < PlayerContainer.Players.Count; i++)
                    {
                        if (PlayerContainer.Players[i].Profile.Id == _vocalModifierPrimaryProfileId)
                        {
                            _vocalModifierSelectIndex = i;
                            break;
                        }
                    }
                }

                // Rebuild the current player's view without advancing to the next player.
                ChangePlayer(0);
                pageSession.ClearReturningToDifficultySelect();
            }
            else
            {
                // Starting a fresh selection session: discard any session-scoped modifiers
                // imposed by a previous song (see ApplySessionModifiers) so each player's
                // own saved selection is what shows and is edited here.
                foreach (var player in PlayerContainer.Players)
                {
                    player.Profile.RestoreSavedModifiers();
                }

                // ChangePlayer(0) will update for the current player
                _playerIndex = 0;
                _vocalModifierSelectIndex = -1;
                _vocalModifierPrimaryProfileId = default;
                ChangePlayer(0);
            }

            _loadingPhrase.text = RichTextUtils.StripRichTextTags(
                GlobalVariables.State.CurrentSong.LoadingPhrase, RichTextTags.BadTags);

            _sourceIcon.sprite = SongSources.SourceToIcon(GlobalVariables.State.CurrentSong.Source);
            _sourceIcon.gameObject.SetActive(_sourceIcon.sprite != null);

            // The header ring is only a template for the per-row rings now (the
            // header shows just the game-mode sprite and name); keep it inactive.
            if (_difficultyRing != null)
            {
                _difficultyRing.gameObject.SetActive(false);
            }

            _scrollRect = GetComponentInChildren<ScrollRect>();
            _scrollbar = GetComponentInChildren<Scrollbar>();
            _navGroup.SelectionChanged += UpdateForSelectionChanged;
        }

        private void OpenMaestroSummaryDirectly()
        {
            foreach (var player in PlayerContainer.Players)
            {
                player.Profile.RestoreSavedModifiers();
            }

            Guid vocalPrimaryProfileId = PlayerContainer.Players
                .FirstOrDefault(player => player.Profile.GameMode is GameMode.Vocals or GameMode.PartyVocals)
                ?.Profile.Id ?? default;
            MaestroSetupSession.Begin(PlayerContainer.Players, _songList,
                PlayerContainer.Players.Count, vocalPrimaryProfileId);
            MenuManager.Instance.PushMenu(MenuManager.Menu.MaestroSetup);
        }

        private void UpdateForSelectionChanged(NavigatableBehaviour navigatableBehaviour,
            SelectionOrigin selectionOrigin)
        {
            // Live-preview the ring for the highlighted instrument in the instrument
            // sub-menu, so the player sees the tier before committing a selection.
            if (_menuState == State.Instrument)
            {
                int? selIndex = _navGroup.SelectedIndex;
                if (selIndex is { } si && si >= 0 && si < _possibleInstruments.Count)
                {
                    SetDifficultyRingForInstrument(_possibleInstruments[si]);
                }
            }

            RefreshScrollbar();
        }

        private void UpdateScrollbarForSelection()
        {
            if (!_scrollbar)
            {
                return;
            }

            int? index = _navGroup.SelectedIndex;
            if (index is { } i)
            {
                int count = _navGroup.Count;
                float highScrollBound = _scrollbar.size + (1 - _scrollbar.size) * _scrollbar.value;
                float lowScrollBound = (1 - _scrollbar.size) * _scrollbar.value;
                float indexHighBound = 1 - (1 / (float) count) * i;
                float indexLowBound = 1 - (1 / (float) count) * (i + 1);
                if (highScrollBound < indexHighBound)
                {
                    _scrollbar.value = (indexHighBound - _scrollbar.size) / (1 - _scrollbar.size);
                }
                else if (lowScrollBound > indexLowBound)
                {
                    _scrollbar.value = indexLowBound / (1 - _scrollbar.size);
                }
            }
        }

        private void UpdateForPlayer()
        {
            // Set player text
            var profile = CurrentPlayer.Profile;
            _text.text = profile.Name;

            UpdateDifficultyRing();

            // Reset content
            _navGroup.ClearNavigatables();
            _container.DestroyChildren();
            StatsManager.Instance.UpdateActivePlayers();

            // Create the menu
            switch (_menuState)
            {
                case State.Main:
                    CreateMainMenu();
                    break;
                case State.Instrument:
                    CreateInstrumentMenu();
                    break;
                case State.Difficulty:
                    CreateDifficultyMenu();
                    break;
                case State.Adjustments:
                    CreateAdjustmentsMenu();
                    break;
                case State.Modifiers:
                    CreateModifierMenu();
                    break;
                case State.OpenLane:
                    CreateOpenLaneMenu();
                    break;
                case State.Accessibility:
                    CreateAccessibilityMenu();
                    break;
                case State.Harmony:
                    CreateHarmonyMenu();
                    break;
                case State.PartyVocalsBotMicCount:
                    CreatePartyVocalsBotMicCountMenu();
                    break;
                case State.PartyVocalsChartChoice:
                    CreatePartyVocalsChartChoiceMenu();
                    break;
            }

            _lastMenuState = _menuState;
        }

        // Refresh the header ring to the song's charter-rated tier for the current
        // instrument. Mirrors ScoreCard.SetCardContents (ScoreCard.cs:158-163): same
        // data source (CurrentSong[instrument]), driven from UpdateForPlayer so it
        // tracks instrument changes (e.g. guitar<->bass can have different tiers).
        private void UpdateDifficultyRing()
        {
            SetDifficultyRingForInstrument(CurrentPlayer.Profile.CurrentInstrument);
        }

        // Get the charter-rated tier values for an instrument. Harmony reads from
        // HarmonyVocals, which is empty on solo-only songs (no harmony chart) —
        // fall back to the lead vocals tier so the ring still shows meaningful
        // data instead of the dimmed state.
        private static PartValues GetTierValues(SongEntry song, Instrument instrument)
        {
            var tierValues = song[instrument];

            if (instrument is Instrument.Harmony or Instrument.PartyVocals && !tierValues.IsActive())
            {
                tierValues = song[Instrument.Vocals];
            }

            return tierValues;
        }

        private void RefreshScrollbar()
        {
            if (_scrollRect == null)
            {
                UpdateScrollbarForSelection();
                return;
            }

            Canvas.ForceUpdateCanvases();
            _scrollRect.Rebuild(CanvasUpdate.PostLayout);

            if (_scrollRect.ScrollableHeight() <= 0f)
            {
                _scrollRect.verticalNormalizedPosition = 1f;
                return;
            }

            UpdateScrollbarForSelection();
        }

        // Set the ring to show the tier for a specific instrument. Used both for the
        // committed selection (via UpdateDifficultyRing) and for live preview while
        // navigating the instrument sub-menu (via UpdateForSelectionChanged).
        private void SetDifficultyRingForInstrument(Instrument instrument)
        {
            if (_difficultyRing == null) return;

            var song = GlobalVariables.State.CurrentSong;
            var tierValues = song[instrument];

            // Harmony and PartyVocals read from HarmonyVocals, which is empty on
            // solo-only songs (no harmony chart). Fall back to the lead vocals tier
            // so the ring still shows meaningful data instead of the dimmed state.
            if (instrument is Instrument.Harmony or Instrument.PartyVocals
                && !tierValues.IsActive())
            {
                tierValues = song[Instrument.Vocals];
            }

            _difficultyRing.SetInfo(
                GetInstrumentRingAsset(instrument, song.VocalsCount),
                instrument,
                tierValues);
        }

        // Get the charter-rated tier for an instrument, mirroring the fallback
        // used by SetDifficultyRingForInstrument (Harmony/PartyVocals fall back
        // to lead vocals on solo-only songs).
        private static sbyte GetInstrumentTier(SongEntry song, Instrument instrument)
        {
            var tierValues = song[instrument];

            if (instrument is Instrument.Harmony or Instrument.PartyVocals
                && !tierValues.IsActive())
            {
                tierValues = song[Instrument.Vocals];
            }

            return tierValues.Intensity;
        }

        // Build a visual tier indicator using ●/○/◇/◉ with TMP color tags:
        //   Tier 0: 5 empty dots (○)
        //   Tier N (1-5): N filled (● bright) + (5-N) empty (○)
        //   Tier 6: 5 burning red dots (●)
        //   Tier 7-10: burning dots, replacing one ● with ◉ fisheye per tier
        //   Tier 11+: clamped to tier 10 (all 5 fisheye)
        //   Unknown (-1): alternating empty dots/diamonds (○◇○◇○)
        private static string GetTierDisplay(sbyte tier)
        {
            if (tier < 0)
            {
                return "<size=80%><color=#888888>Unknown</color></size>";
            }

            var sb = new StringBuilder();

            if (tier >= 6)
            {
                // Tier 6: 5 red ●. Each tier above replaces one ● with ◉ fisheye.
                // Tier 10 = all 5 ◉. Clamp above that.
                int fisheye = System.Math.Min(tier - 6, 5); // tier 6→0, 7→1, ... 11+→5
                sb.Append("<color=#F32B37>");
                for (int i = 0; i < 5; i++)
                {
                    sb.Append(i < fisheye ? '\u25C9' : '\u25CF'); // ◉ vs ●
                }
                sb.Append("</color>");
            }
            else
            {
                int filled = tier;
                int empty = 5 - filled;

                sb.Append("<color=#DDDDDD>");
                for (int i = 0; i < filled; i++) sb.Append('\u25CF'); // ●
                sb.Append("</color>");

                for (int i = 0; i < empty; i++) sb.Append('\u25CB'); // ○
            }

            return sb.ToString();
        }

        // Resolve the bare Addressable icon name for the ring. Handles the 22-fret
        // pro-instrument gap (ToResourceName returns null for ProGuitar_22Fret /
        // ProBass_22Fret, InstrumentExtensions.cs:95) and selects the part-count mic
        // icon for harmony/party-vocals based on the song's vocal part count.
        private static string GetInstrumentRingAsset(Instrument instrument, int vocalPartCount)
            => instrument switch
        {
            Instrument.ProGuitar_22Fret => "realGuitar",
            Instrument.ProBass_22Fret   => "realBass",
            Instrument.Harmony or Instrument.PartyVocals => vocalPartCount switch
            {
                >= 3 => "harmVocals",
                2    => "twoVocals",
                _    => "vocals",
            },
            _ => instrument.ToResourceName(),
        };

        private void CreateMainMenu()
        {
            var player = CurrentPlayer;

            if (player.IsMissingMicrophone)
            {
                ShowWarning(Localize.Key("Menu.DifficultySelect.WarningVocalistNoMicrophone"));
            }
            else if (player.IsMissingInputDevice)
            {
                ShowWarning(Localize.Key("Menu.DifficultySelect.WarningPlayerNoInputDevice"));
            }
            else
            {
                ShowWarning(null);
            }

            // Only show all these options if there are instruments available
            if (_possibleInstruments.Count > 0)
            {
                // Ready button
                CreateItem(LocalizeHeader("Ready"), _lastMenuState == State.Main, _difficultyGreenPrefab, () =>
                {
                    // If the player just selected vocal modifiers, don't show them again
                    if ((player.Profile.GameMode == GameMode.Vocals
                        || player.Profile.GameMode == GameMode.PartyVocals) &&
                        _vocalModifierSelectIndex == -1)
                    {
                        _vocalModifierSelectIndex = _playerIndex;
                        _vocalModifierPrimaryProfileId = player.Profile.Id;
                    }

                    ChangePlayer(1);
                });

                DifficultyItem instrumentItem;

                // Party Vocals' only instrument is Party Vocals, so the meaningful choice
                // under "Instrument" is the Solo-vs-Harmony vocal chart. Repurpose the row to
                // open the chart picker and show the resolved chart. When the song offers only
                // one vocal chart there's nothing to pick, so the row is shown dimmed and
                // non-interactable (visible feedback instead of a silent no-op).
                if (player.Profile.GameMode == GameMode.PartyVocals)
                {
                    var song = GlobalVariables.State.CurrentSong;
                    bool hasHarm = song.HasInstrument(Instrument.Harmony);
                    bool hasSolo = song.HasInstrument(Instrument.Vocals);

                    // All Party Vocals players share one VocalTrack, so a later player's
                    // chart must match the first player's or it won't render. Lock every
                    // player after the first to that choice: copy the preference (so the
                    // shared track and replay record the chart actually played) and dim
                    // the row, exactly like a single-chart song. The first Party Vocals
                    // player still chooses freely.
                    var lockedPreference = GetLockedPartyVocalsPreference();
                    if (lockedPreference is { } locked)
                    {
                        player.Profile.PartyVocalsChartPreference = locked;
                    }

                    bool realChoice = hasHarm && hasSolo && lockedPreference is null;

                    // Resolved chart for display: what ResolveMultitrack will pick.
                    bool willSingSolo =
                        player.Profile.PartyVocalsChartPreference == PartyVocalsChartPreference.Solo
                            ? hasSolo
                            : !hasHarm;
                    string chartLabel = willSingSolo ? "Solo" : "Harmony";

                    instrumentItem = CreateItem(LocalizeHeader("Instrument"), chartLabel,
                        _lastMenuState == State.PartyVocalsChartChoice, () =>
                    {
                        _menuState = State.PartyVocalsChartChoice;
                        UpdateForPlayer();
                    },
                    interactable: realChoice);
                }
                else
                {
                    string instrumentLabel = player.Profile.CurrentInstrument.ToLocalizedName();
                    instrumentItem = CreateItem(LocalizeHeader("Instrument"),
                        instrumentLabel,
                        _lastMenuState == State.Instrument, () =>
                    {
                        _menuState = State.Instrument;
                        UpdateForPlayer();
                    });
                }

                // Show every available instrument's tier wheel on its own row
                // within the item (localized instrument names can be long, so a
                // ring column beside the text doesn't reliably fit). Party Vocals
                // gets one wheel for Solo and one for Harmony when both charts are
                // available, matching the separate chart choices below.
                if (_difficultyRing != null && _possibleInstruments.Count > 0)
                {
                    const float ringSize = 40f;

                    var song = GlobalVariables.State.CurrentSong;
                    bool hasSolo = song.HasInstrument(Instrument.Vocals);
                    bool hasHarmony = song.HasInstrument(Instrument.Harmony);
                    bool showPartyVocalsCharts = instrumentItem != null
                        && player.Profile.GameMode == GameMode.PartyVocals
                        && hasSolo && hasHarmony;
                    int ringCount = showPartyVocalsCharts ? 2 : _possibleInstruments.Count;
                    var rings = instrumentItem.AttachRingRow(_difficultyRing, ringCount, ringSize);

                    bool selectedSolo = player.Profile.PartyVocalsChartPreference == PartyVocalsChartPreference.Solo
                        ? hasSolo
                        : !hasHarmony;
                    Instrument selectedPartyVocalsChart = selectedSolo
                        ? Instrument.Vocals
                        : Instrument.Harmony;

                    for (int i = 0; i < ringCount; i++)
                    {
                        Instrument ringInstrument = showPartyVocalsCharts
                            ? i == 0 ? Instrument.Vocals : Instrument.Harmony
                            : _possibleInstruments[i];
                        rings[i].SetInfo(
                            GetInstrumentRingAsset(ringInstrument, song.VocalsCount),
                            ringInstrument,
                            GetTierValues(song, ringInstrument));

                        bool selected = showPartyVocalsCharts
                            ? ringInstrument == selectedPartyVocalsChart
                            : ringInstrument == player.Profile.CurrentInstrument;
                        if (selected)
                        {
                            // Extra size is in the ring's native units; scale it
                            // so the circle extends four *screen* pixels per
                            // side, giving the ring a prominent rim.
                            rings[i].ShowSelectionBackdrop(SELECTED_INSTRUMENT_COLOR,
                                extraSize: 8f * 65f / ringSize);
                        }
                        else
                        {
                            rings[i].SetIconColor(UNSELECTED_INSTRUMENT_ICON_COLOR);
                            // Dim the filled segments too, so the selected
                            // wheel reads brightest.
                            rings[i].SetRingOpacity(0.6f);
                        }
                    }
                }

                CreateItem(LocalizeHeader("Difficulty"),
                    player.Profile.CurrentDifficulty.ToLocalizedName(),
                    _lastMenuState == State.Difficulty, () =>
                {
                    _menuState = State.Difficulty;
                    UpdateForPlayer();
                });

                // Harmony-locked players pick which HARM line they want. Free Vocals bots
                // no longer need this picker: on multi-HARM songs they auto-distribute one
                // synthetic vocalist per part (Party Vocals bot mode); on Solo-only songs
                // there's only one line. The HARM index isn't used by Free Vocals at all
                // anymore for bot configuration.
                if (player.Profile.CurrentInstrument is Instrument.Harmony)
                {
                    string harmonyDisplayText = $"HARM{player.Profile.HarmonyIndex + 1}";

                    CreateItem(LocalizeHeader("Harmony"),
                        harmonyDisplayText,
                        _lastMenuState == State.Harmony, () =>
                    {
                        _menuState = State.Harmony;
                        UpdateForPlayer();
                    });
                }

                // Free Vocals bots: expose a mic-count override for testing edge
                // cases (e.g. 3 mics vs 2 parts, 1 mic vs 3 semi-overlapping parts).
                // Auto = one bot mic per HARM part in the chart (default).
                if (player.Profile.IsFreeVocals && player.Profile.IsBot)
                {
                    byte botMicOverride = player.Profile.PartyVocalsMicCountOverride;
                    string botMicLabel = botMicOverride == 0
                        ? "Auto"
                        : botMicOverride.ToString();

                    CreateItem("Bot Mics",
                        botMicLabel,
                        _lastMenuState == State.PartyVocalsBotMicCount, () =>
                    {
                        _menuState = State.PartyVocalsBotMicCount;
                        UpdateForPlayer();
                    });
                }

                var adjustmentsItem = CreateItem(LocalizeHeader("Adjustments"),
                    BuildAdjustmentsSummary(player.Profile, out int optionCount),
                    _lastMenuState is State.Adjustments or State.Modifiers or State.Accessibility, () =>
                {
                    _menuState = State.Adjustments;
                    UpdateForPlayer();
                });

                // With a single active option (or none) the summary fits at
                // the normal body size; longer lists drop to the header size
                // to keep the row compact.
                if (optionCount >= 2)
                {
                    adjustmentsItem.UseSmallBodyText();
                }

                // (Party Vocals' Solo/Harmony chart choice now lives on the Instrument row above.)

                // Vocal modifiers must be uniform across all vocal players, so only the
                // first vocal player to claim selection can edit them. Later vocal players
                // see the inherited selection, dimmed and non-interactable — the same
                // pattern as the Party Vocals instrument/chart row.
                bool isVocalMode = player.Profile.GameMode == GameMode.Vocals
                    || player.Profile.GameMode == GameMode.PartyVocals;
                bool modifiersLocked = isVocalMode
                    && _vocalModifierSelectIndex != -1
                    && _vocalModifierSelectIndex != _playerIndex;

                // Show the effective modifiers: the primary player's selection when locked,
                // otherwise this player's own.
                var modifiersSource = modifiersLocked
                    ? PlayerContainer.Players[_vocalModifierSelectIndex].Profile
                    : player.Profile;

                // Create modifiers body text
                string modifierText = "";
                if ((modifiersSource.CurrentModifiers & ~_excusableModifiers) == Modifier.None)
                {
                    // If there are no modifiers (ignoring the excusable ones), then just say "none"
                    modifierText = Modifier.None.ToLocalizedName();
                }
                else
                {
                    // Combine all modifiers
                    foreach (var modifier in _possibleModifiers)
                    {
                        if (!modifiersSource.IsModifierActive(modifier)) continue;

                        modifierText += modifier.ToLocalizedName() + "\n";
                    }

                    modifierText = modifierText.Trim();
                }

                // Lefty flip is a separate profile flag, not a Modifier enum value,
                // so append it to the summary manually.
                if (modifiersSource.LeftyFlip)
                {
                    modifierText = modifierText == Modifier.None.ToLocalizedName()
                        ? "Lefty Flip"
                        : modifierText + "\nLefty Flip";
                }

            }

            // Only show if there is more than one play, only if there is instruments available
            if (_possibleInstruments.Count <= 0 || PlayerContainer.Players.Count != 1)
            {
                // Sit out button
                CreateItem(LocalizeHeader("SitOut"), _possibleInstruments.Count <= 0, _difficultyItemSmallRedPrefab, () =>
                {
                    // If the user went back to sit out, and the vocal modifiers were selected,
                    // deselect them.
                    if (_vocalModifierSelectIndex == _playerIndex)
                    {
                        _vocalModifierSelectIndex = -1;
                    }

                    player.SittingOut = true;
                    ChangePlayer(1);
                });

                // Disconnect button
                CreateItem(LocalizeHeader("Disconnect"), _possibleInstruments.Count <= 0, _difficultyItemSmallRedPrefab, () =>
                {
                    // If the user disconnected, and the vocal modifiers were selected,
                    // deselect them.
                    if (_vocalModifierSelectIndex == _playerIndex)
                    {
                        _vocalModifierSelectIndex = -1;
                    }

                    PlayerContainer.DisposePlayer(player);

                    // Since we're removing one player from the active players list, don't increment the player index.
                    ChangePlayer(0);
                });
            }
        }

        private void ShowWarning(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                _warningMessageContainer.SetActive(false);
                _warningMessage.text = "";
            }
            else
            {
                _warningMessageContainer.SetActive(true);
                _warningMessage.text = message;
            }
        }

        // "5 - Nightmare"-style tier text for the instrument menu, using the
        // filters menu's localized tier names. "No Part" can't come up here —
        // the menu only lists playable instruments.
        private static string GetTierLabel(PartValues values)
        {
            if (!values.IsActive() || values.Intensity < 0)
            {
                return "? - " + Localize.Key("Menu.Filters.Intensities.Unknown");
            }

            // The label clamps at the top tier; the number stays exact.
            string text = $"{values.Intensity} - {FiltersMenu.GetIntensityLabelByIndex(values.Intensity)}";

            // Top-tier colors (matching the web export's tier palette): orange
            // at 5, the ring segments' red at 6+.
            return values.Intensity switch
            {
                >= 6 => $"<color=#FB443F>{text}</color>",
                5    => $"<color=#FF8400>{text}</color>",
                _    => text,
            };
        }

        private static string GetPartyVocalsChartLabel(SongEntry song, Instrument instrument)
        {
            string chartName = instrument == Instrument.Vocals ? "Solo" : "Harmony";
            return chartName
                + $"\n<size=18><color=#FFFFFF80>{GetTierLabel(song[instrument])}</color></size>";
        }

        private void CreateInstrumentMenu()
        {
            var song = GlobalVariables.State.CurrentSong;

            foreach (var instrument in _possibleInstruments)
            {
                bool selected = CurrentPlayer.Profile.CurrentInstrument == instrument;

                // Instrument name with its charted tier on a smaller, dimmed
                // second line (mirroring the header text style).
                string label = instrument.ToLocalizedName()
                    + $"\n<size=18><color=#FFFFFF80>{GetTierLabel(GetTierValues(song, instrument))}</color></size>";

                CreateItem(label, selected, () =>
                {
                    var preferredInstrument = CurrentPlayer.Profile.PreferredInstrument;
                    CurrentPlayer.Profile.CurrentInstrument = instrument;

                    // Re-resolve after an instrument switch in case the raw harmony index is out
                    // of range for this song (ChangePlayer's check can be masked by the
                    // HarmonyIndex getter returning 0 when not on Harmony).
                    CurrentPlayer.Profile.ResolveHarmonyIndex(_maxHarmonyIndex);

                    // What we are doing here is resetting preferred instrument only if the current preferred instrument
                    // was an option for this chart. This ensures that preferred instrument does not change when the
                    // player is forced to use a different instrument.
                    if (instrument != preferredInstrument && _possibleInstruments.Contains(preferredInstrument))
                    {
                        CurrentPlayer.Profile.PreferredInstrument = instrument;
                    }

                    FiltersMenu.ResetIntensityFiltersForProfile(CurrentPlayer.Profile);
                    UpdatePossibleDifficulties();
                    UpdatePossibleModifiers();

                    _menuState = State.Main;
                    UpdateForPlayer();
                });
            }

        }

        private void CreateDifficultyMenu()
        {
            foreach (var difficulty in _possibleDifficulties)
            {
                bool selected = CurrentPlayer.Profile.CurrentDifficulty == difficulty;
                CreateItem(difficulty.ToLocalizedName(), selected, () =>
                {
                    CurrentPlayer.Profile.CurrentDifficulty
                        = CurrentPlayer.Profile.DifficultyFallback
                        = difficulty;

                    _menuState = State.Main;
                    UpdateForPlayer();
                });
            }
        }

        // Intermediate menu grouping the Modifiers and Accessibility sub-menus,
        // each previewing its active options in its body text.
        private void CreateAdjustmentsMenu()
        {
            var profile = CurrentPlayer.Profile;

            CreateItem(LocalizeHeader("Modifiers"),
                BuildModifierSummary(profile),
                _lastMenuState != State.Accessibility, () =>
            {
                _menuState = State.Modifiers;
                UpdateForPlayer();
            });

            if (HasAccessibilityOptions(profile))
            {
                CreateItem(LocalizeHeader("Accessibility"),
                    BuildAccessibilitySummary(profile),
                    _lastMenuState == State.Accessibility, () =>
                {
                    _menuState = State.Accessibility;
                    UpdateForPlayer();
                });
            }

            // Create done button
            CreateDoneItem(() =>
            {
                _menuState = State.Main;
                UpdateForPlayer();
            });
        }

        private void CreateDoneItem(UnityAction action)
        {
            CreateItem(LocalizeHeader("Done"), false, action)
                .SetAccentColors(DONE_TEXT_COLOR, DONE_SELECTED_TEXT_COLOR);
        }

        private void CreateModifierMenu()
        {
            var profile = CurrentPlayer.Profile;

            _modifierItems.Clear();
            _itemModifiers.Clear();

            bool isVocalMode = profile.GameMode is GameMode.Vocals or GameMode.PartyVocals;

            if (isVocalMode)
            {
                CreateModifierHeader(Localize.Key("Menu.DifficultySelect", "DisablePitch"));

                AddModifierToggle(profile, Modifier.UnpitchedOnly,  "Harmony 1");
                AddModifierToggle(profile, Modifier.UnpitchedHarm2, "Harmony 2");
                AddModifierToggle(profile, Modifier.UnpitchedHarm3, "Harmony 3");

                CreateModifierHeader(Localize.Key("Menu.DifficultySelect", "OtherModifiers"));

                AddModifierToggle(profile, Modifier.NoVocalPercussion, "Percussion");
                AddModifierToggle(profile, Modifier.ManualVocalStarPower, "Sing to Deploy");
            }
            else
            {
                // Accessibility-relocated modifiers live in the Accessibility menu.
                foreach (var modifier in _possibleModifiers)
                {
                    if ((modifier & ACCESSIBILITY_MODIFIERS) != 0) continue;
                    AddModifierToggle(profile, modifier);
                }

                // Five-lane keys: the three-state OpenLaneDisplayType gets its own sub-menu.
                if (profile.GameMode == GameMode.ProKeys)
                {
                    CreateItem(LocalizeHeader("DedicatedOpenLane"),
                        profile.OpenLaneDisplayType.ToLocalizedName(),
                        _lastMenuState == State.OpenLane, () =>
                    {
                        _menuState = State.OpenLane;
                        UpdateForPlayer();
                    });
                }
            }

            // Create done button (back to the Adjustments menu these nest under).
            CreateDoneItem(() =>
            {
                _menuState = State.Adjustments;
                UpdateForPlayer();
            });
        }

        private void CreateModifierHeader(string text)
        {
            var go = new GameObject("ModifierSectionHeader", typeof(RectTransform));
            go.transform.SetParent(_container, false);

            var tmp = go.AddComponent<TextMeshProUGUI>();

            // Match the font used by the modifier toggle rows, but slightly smaller.
            var refText = _modifierItemPrefab.GetComponentInChildren<TextMeshProUGUI>();
            if (refText != null)
            {
                tmp.font = refText.font;
                tmp.fontSize = refText.fontSize - 4;
            }
            else
            {
                tmp.font = TMP_Settings.defaultFontAsset;
                tmp.fontSize = 16;
            }

            tmp.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
            tmp.color = new Color(0.55f, 0.55f, 0.6f);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.text = text;

            var fitter = go.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private void AddModifierToggle(YargProfile profile, Modifier modifier, string labelOverride = null)
        {
            // Skip modifiers that aren't applicable to this game mode (defensive).
            if (!_possibleModifiers.Contains(modifier)) return;

            string label = labelOverride ?? modifier.ToLocalizedName();

            var btn = Instantiate(_modifierItemPrefab, _container);
            btn.Initialize(label, profile.IsModifierActive(modifier), active =>
            {
                if (active)
                {
                    profile.AddSingleModifier(modifier);
                }
                else
                {
                    profile.RemoveModifiers(modifier);
                }

                UpdateModifierMenu();
            });

            _navGroup.AddNavigatable(btn);
            _modifierItems.Add(btn);
            _itemModifiers.Add(modifier);
        }

        private static readonly OpenLaneDisplayType[] OPEN_LANE_OPTIONS =
        {
            OpenLaneDisplayType.Never,
            OpenLaneDisplayType.Always,
            OpenLaneDisplayType.IfChartContainsOpens,
        };

        private void CreateOpenLaneMenu()
        {
            var profile = CurrentPlayer.Profile;

            // Title row (same string as the Modifiers-menu row that leads here)
            // so the menu identifies itself; not part of the nav group.
            Instantiate(_difficultyItemPrefab, _container)
                .InitializeAsTitle(LocalizeHeader("DedicatedOpenLane"));

            foreach (var displayType in OPEN_LANE_OPTIONS)
            {
                var capture = displayType;
                bool selected = profile.OpenLaneDisplayType == displayType;
                CreateItem(displayType.ToLocalizedName(), selected, () =>
                {
                    profile.OpenLaneDisplayType = capture;

                    _menuState = State.Modifiers;
                    UpdateForPlayer();
                });
            }
        }

        private void CreateAccessibilityMenu()
        {
            var profile = CurrentPlayer.Profile;

            _modifierItems.Clear();
            _itemModifiers.Clear();

            if (SupportsLeftyFlip(profile.GameMode))
            {
                // Takes effect at track build time since this menu precedes gameplay.
                AddProfileToggle(LocalizeHeader("LeftyFlip"), profile.LeftyFlip,
                    on => profile.LeftyFlip = on);
            }

            if (SupportsRangeShifts(profile.GameMode))
            {
                // One positive "No Range Shifts" switch backed by both range
                // mechanisms: the profile's RangeEnabled display setting (the
                // profile editor's Range Disable toggle) and, where the game mode
                // supports it, the RangeCompress chart modifier. Shown active if
                // either says shifts are off, so it tracks the profile editor.
                bool compressPossible = _possibleModifiers.Contains(Modifier.RangeCompress);
                bool noRangeShifts = !profile.RangeEnabled
                    || (compressPossible && profile.IsModifierActive(Modifier.RangeCompress));

                AddProfileToggle(LocalizeHeader("NoRangeShifts"), noRangeShifts, on =>
                {
                    profile.RangeEnabled = !on;

                    if (compressPossible)
                    {
                        if (on)
                        {
                            profile.AddSingleModifier(Modifier.RangeCompress);
                        }
                        else
                        {
                            profile.RemoveModifiers(Modifier.RangeCompress);
                        }
                    }
                });
            }

            foreach (var modifier in _possibleModifiers)
            {
                if ((modifier & ACCESSIBILITY_MODIFIERS) == 0) continue;
                if (modifier == Modifier.RangeCompress) continue; // folded in above

                AddModifierToggle(profile, modifier);
            }

            // Create done button (back to the Adjustments menu these nest under)
            CreateDoneItem(() =>
            {
                _menuState = State.Adjustments;
                UpdateForPlayer();
            });

            _navGroup.SelectFirst();
        }

        // A ModifierItem toggle bound to arbitrary state (profile flags, display
        // types) rather than a Modifier enum value.
        private ModifierItem AddProfileToggle(string label, bool active, Action<bool> onChanged)
        {
            var btn = Instantiate(_modifierItemPrefab, _container);
            btn.Initialize(label, active, onChanged);
            _navGroup.AddNavigatable(btn);
            return btn;
        }

        private void AddModifierToggle(YargProfile profile, Modifier modifier)
        {
            var btn = AddProfileToggle(modifier.ToLocalizedName(),
                profile.IsModifierActive(modifier), active =>
            {
                // Enable/disable the modifier
                if (active)
                {
                    profile.AddSingleModifier(modifier);
                }
                else
                {
                    profile.RemoveModifiers(modifier);
                }

                UpdateModifierMenu();
            });

            _modifierItems.Add(btn);
            _itemModifiers.Add(modifier);
        }

        private static bool SupportsLeftyFlip(GameMode mode)
            => mode is GameMode.FiveFretGuitar or GameMode.SixFretGuitar
                or GameMode.FourLaneDrums or GameMode.FiveLaneDrums or GameMode.EliteDrums;

        private static bool SupportsRangeShifts(GameMode mode)
            => mode is GameMode.FiveFretGuitar or GameMode.ProKeys;

        private bool HasAccessibilityOptions(YargProfile profile)
            => SupportsLeftyFlip(profile.GameMode)
                || SupportsRangeShifts(profile.GameMode)
                || _possibleModifiers.Any(m => (m & ACCESSIBILITY_MODIFIERS) != 0);

        // Summary body for the main menu's Adjustments row: the active options
        // from both nested menus (Modifiers and Accessibility) combined.
        // optionCount lets the caller pick a font size for the list.
        private string BuildAdjustmentsSummary(YargProfile profile, out int optionCount)
        {
            string none = Modifier.None.ToLocalizedName();
            string text = "";

            string modifierSummary = BuildModifierSummary(profile);
            if (modifierSummary != none)
            {
                text += modifierSummary + "\n";
            }

            if (HasAccessibilityOptions(profile))
            {
                string accessibilitySummary = BuildAccessibilitySummary(profile);
                if (accessibilitySummary != none)
                {
                    text += accessibilitySummary + "\n";
                }
            }

            text = text.Trim();

            if (text.Length == 0)
            {
                optionCount = 0;
                return none;
            }

            optionCount = text.Count(c => c == '\n') + 1;
            return text;
        }

        // Summary body for the Adjustments menu's Modifiers row. Accessibility-relocated
        // modifiers are summarized on their own row; the keys open-lane toggles
        // live in the Modifiers menu but are backed by OpenLaneDisplayType rather
        // than a Modifier flag, so they're listed here explicitly.
        private string BuildModifierSummary(YargProfile profile)
        {
            string text = "";

            if ((profile.CurrentModifiers & ~_excusableModifiers & ~ACCESSIBILITY_MODIFIERS) != Modifier.None)
            {
                foreach (var modifier in _possibleModifiers)
                {
                    if ((modifier & ACCESSIBILITY_MODIFIERS) != 0) continue;
                    if (!profile.IsModifierActive(modifier)) continue;

                    text += modifier.ToLocalizedName() + "\n";
                }
            }

            // The bare display-type names wouldn't be meaningful in a summary;
            // Never counts as "off" and isn't listed.
            if (profile.GameMode == GameMode.ProKeys)
            {
                switch (profile.OpenLaneDisplayType)
                {
                    case OpenLaneDisplayType.Always:
                        text += LocalizeHeader("OpenLaneAlways") + "\n";
                        break;
                    case OpenLaneDisplayType.IfChartContainsOpens:
                        text += LocalizeHeader("OpenLaneWhenCharted") + "\n";
                        break;
                }
            }

            text = text.Trim();
            return text.Length == 0 ? Modifier.None.ToLocalizedName() : text;
        }

        private string BuildAccessibilitySummary(YargProfile profile)
        {
            string text = "";

            if (SupportsLeftyFlip(profile.GameMode) && profile.LeftyFlip)
            {
                text += LocalizeHeader("LeftyFlip") + "\n";
            }

            if (SupportsRangeShifts(profile.GameMode)
                && (!profile.RangeEnabled || profile.IsModifierActive(Modifier.RangeCompress)))
            {
                text += LocalizeHeader("NoRangeShifts") + "\n";
            }

            foreach (var modifier in _possibleModifiers)
            {
                if ((modifier & ACCESSIBILITY_MODIFIERS) == 0) continue;
                if (modifier == Modifier.RangeCompress) continue; // covered above
                if (!profile.IsModifierActive(modifier)) continue;

                text += modifier.ToLocalizedName() + "\n";
            }

            text = text.Trim();
            return text.Length == 0 ? Modifier.None.ToLocalizedName() : text;
        }

        private void CreateHarmonyMenu()
        {
            var profile = CurrentPlayer.Profile;

            for (int i = 1; i <= _maxHarmonyIndex; i++)
            {
                int capture = i;
                bool harmonySelected = profile.HarmonyIndex == (i - 1);
                CreateItem($"HARM{i}", harmonySelected, () =>
                {
                    profile.CurrentInstrument = Instrument.Harmony;
                    profile.HarmonyIndex = (byte) (capture - 1);

                    _menuState = State.Main;
                    UpdateForPlayer();
                });
            }
        }

        private void CreatePartyVocalsBotMicCountMenu()
        {
            var profile = CurrentPlayer.Profile;
            byte current = profile.PartyVocalsMicCountOverride;

            CreateItem("Auto", current == 0, () =>
            {
                profile.PartyVocalsMicCountOverride = 0;
                _menuState = State.Main;
                UpdateForPlayer();
            });

            for (int i = 1; i <= 7; i++)
            {
                byte capture = (byte) i;
                CreateItem(capture.ToString(), current == capture, () =>
                {
                    profile.PartyVocalsMicCountOverride = capture;
                    _menuState = State.Main;
                    UpdateForPlayer();
                });
            }
        }

        private void CreatePartyVocalsChartChoiceMenu()
        {
            var profile = CurrentPlayer.Profile;
            var current = profile.PartyVocalsChartPreference;

            var song = GlobalVariables.State.CurrentSong;

            CreateItem(GetPartyVocalsChartLabel(song, Instrument.Harmony),
                current == PartyVocalsChartPreference.Harmony, () =>
            {
                profile.PartyVocalsChartPreference = PartyVocalsChartPreference.Harmony;
                _menuState = State.Main;
                UpdateForPlayer();
            });

            CreateItem(GetPartyVocalsChartLabel(song, Instrument.Vocals),
                current == PartyVocalsChartPreference.Solo, () =>
            {
                profile.PartyVocalsChartPreference = PartyVocalsChartPreference.Solo;
                _menuState = State.Main;
                UpdateForPlayer();
            });
        }

        private void UpdateModifierMenu()
        {
            var profile = CurrentPlayer.Profile;

            for (int i = 0; i < _modifierItems.Count; i++)
            {
                var item = _modifierItems[i];
                var modifier = _itemModifiers[i];

                item.Active = profile.IsModifierActive(modifier);
            }
        }

        private void UpdatePossibleModifiers()
        {
            var profile = CurrentPlayer.Profile;

            // Get the possible modifiers (split the enum into multiple) and
            // make sure current modifiers are valid, and remove the invalid ones
            _possibleModifiers.Clear();
            var (possible, excusable) = profile.GameMode.PossibleModifiers(profile.CurrentInstrument);
            _excusableModifiers = excusable;

            foreach (var modifier in EnumExtensions<Modifier>.Values)
            {
                // Skip if the modifier is not a possible one
                if ((possible & modifier) == 0)
                {
                    // Also try to clear it if it isn't considered excusable yet the player somehow has it
                    if (((excusable & modifier) == 0) && profile.IsModifierActive(modifier))
                    {
                        profile.RemoveModifiers(modifier);
                    }

                    continue;
                }

                _possibleModifiers.Add(modifier);

                if (profile.IsModifierActive(modifier) && !_possibleModifiers.Contains(modifier))
                {
                    profile.RemoveModifiers(modifier);
                }
            }

        }

        private void ApplyVocalSessionModifiers()
        {
            if (_vocalModifierSelectIndex < 0 ||
                _vocalModifierSelectIndex >= PlayerContainer.Players.Count)
            {
                return;
            }

            var primaryPlayer = PlayerContainer.Players[_vocalModifierSelectIndex];
            foreach (var player in PlayerContainer.Players)
            {
                if (player.SittingOut || player == primaryPlayer)
                    continue;

                if (player.Profile.GameMode is GameMode.Vocals or GameMode.PartyVocals)
                    player.Profile.ApplySessionModifiers(primaryPlayer.Profile);
            }
        }

        private void ChangePlayer(int add)
        {
            _playerIndex += add;
            _menuState = State.Main;

            // When the user(s) have selected all of their difficulties, move on
            if (_playerIndex >= PlayerContainer.Players.Count)
            {
                // If everyone is sitting out, show a warning and boot back to music library
                if (PlayerContainer.Players.All(i => i.SittingOut))
                {
                    MenuManager.Instance.PopMenu();

                    DialogManager.Instance.ShowMessage("Nobody's Playing!",
                        "You tried to play a song with every player sitting out.");

                    return;
                }

                // The existing Difficulty Select boundary still owns song speed. Setup
                // values are captured here and finalized by Maestro Continue, so page
                // edits and remote drafts remain non-mutating until that explicit action.
                string speedText = _speedInput.text.TrimEnd('%').Trim();
                if (!float.TryParse(speedText, NumberStyles.Number, CultureInfo.CurrentCulture,
                        out float speedPercent))
                {
                    speedPercent = 100f;
                }

                float speed = Mathf.Clamp(speedPercent / 100f, 0.1f, 50.0f);
                _songSpeed = speed;
                GlobalVariables.State.SongSpeed = speed;

                var songs = GlobalVariables.State.PlayingAShow
                    ? GlobalVariables.State.ShowSongs
                    : new List<SongEntry> { GlobalVariables.State.CurrentSong };
                var vocalId = _vocalModifierPrimaryProfileId;
                if (vocalId == default && _vocalModifierSelectIndex >= 0 &&
                    _vocalModifierSelectIndex < PlayerContainer.Players.Count)
                {
                    vocalId = PlayerContainer.Players[_vocalModifierSelectIndex].Profile.Id;
                }

                if (SettingsManager.Settings.MaestroEnable.Value)
                {
                    MaestroSetupSession.Begin(PlayerContainer.Players, songs, _playerIndex, vocalId);
                    MenuManager.Instance.PushMenu(MenuManager.Menu.MaestroSetup);
                    return;
                }

                ApplyVocalSessionModifiers();
                GlobalVariables.Instance.LoadScene(SceneIndex.Gameplay);
                return;
            }

            var profile = CurrentPlayer.Profile;
            var song = GlobalVariables.State.CurrentSong;

            // Get the possible instruments for this show and player
            // TODO: We should probably allow players to select instruments that are not in
            //  all songs and have them sit out songs that don't have that instrument
            // TODO: We should also let Ekit users choose an option that switches them between
            // each song's native drum format
            _possibleInstruments.Clear();
            var allowedInstruments = profile.GameMode.PossibleInstrumentsForSong(GlobalVariables.State.CurrentSong);

            foreach (var instrument in allowedInstruments)
            {
                bool invalidInstrument = false;
                foreach (var showSong in _songList)
                {
                    if (!HasPlayableInstrument(showSong, instrument))
                    {
                        invalidInstrument = true;
                        break;
                    }
                }

                if (!invalidInstrument)
                {
                    _possibleInstruments.Add(instrument);
                }
            }

            // If the player's preferred instrument is available, set CurrentInstrument to that
            if (_possibleInstruments.Contains(profile.PreferredInstrument))
            {
                profile.CurrentInstrument = profile.PreferredInstrument;
            }

            // Set the instrument to a valid one
            if (!_possibleInstruments.Contains(profile.CurrentInstrument) && _possibleInstruments.Count > 0)
            {
                profile.CurrentInstrument = _possibleInstruments[0];
            }

            // Get the possible harmonies for this show
            _maxHarmonyIndex = song.VocalsCount;
            foreach (var showsong in GlobalVariables.State.ShowSongs)
            {
                _maxHarmonyIndex = Mathf.Min(_maxHarmonyIndex, showsong.VocalsCount);
            }

            // Resolve the effective harmony index for this song from the player's last
            // explicit selection (clamped to the available parts). Uses ResolveHarmonyIndex
            // so the raw backing field is checked regardless of CurrentInstrument
            // (HarmonyIndex getter returns 0 when not on Harmony, which would mask an
            // out-of-range value from a direct comparison), and so a song with fewer
            // parts doesn't permanently erase the selection — like DifficultyFallback
            // preserves Expert+ across songs that lack it.
            profile.ResolveHarmonyIndex(_maxHarmonyIndex);

            UpdatePossibleModifiers();

            // Don't sit out by default
            CurrentPlayer.SittingOut = false;

            // Update the possible difficulties as well
            UpdatePossibleDifficulties();

            UpdateForPlayer();
        }

        private void UpdatePossibleDifficulties()
        {
            _possibleDifficulties.Clear();

            var profile = CurrentPlayer.Profile;

            // Get the possible difficulties for the player's instrument in the song
            foreach (var difficulty in EnumExtensions<Difficulty>.Values)
            {
                bool invalidDifficulty = false;
                foreach (var showsong in _songList)
                {
                    if (!HasPlayableDifficulty(showsong, profile.CurrentInstrument, difficulty))
                    {
                        invalidDifficulty = true;
                        break;
                    }
                }

                if (!invalidDifficulty)
                {
                    _possibleDifficulties.Add(difficulty);
                }
            }

            // TODO: Handle difficulty fallback better in play a show mode

            var diff = (int) profile.DifficultyFallback;
            while (diff >= (int) Difficulty.Beginner && !_possibleDifficulties.Contains((Difficulty) diff))
            {
                --diff;
            }

            if (diff < (int) Difficulty.Beginner)
            {
                diff = (int) profile.DifficultyFallback;
                while (diff < (int) Difficulty.ExpertPlus)
                {
                    ++diff;
                    if (_possibleDifficulties.Contains((Difficulty) diff))
                    {
                        break;
                    }
                }
            }
            profile.CurrentDifficulty = (Difficulty) diff;
        }

        private void OnDisable()
        {
            Navigator.Instance.PopScheme();
        }

        private DifficultyItem CreateItem(string header, string body, bool selected, DifficultyItem difficultyItem, UnityAction a, bool interactable = true)
        {
            var btn = Instantiate(difficultyItem, _container);

            if (header is null)
            {
                btn.Initialize(body, a);
            }
            else
            {
                btn.Initialize(header, body, a);
            }

            btn.SetInteractable(interactable);

            // Non-interactable items (e.g. a forced single-chart choice) are shown dimmed and
            // kept out of the nav group so they can't be focused or activated.
            if (!interactable)
            {
                return btn;
            }

            _navGroup.AddNavigatable(btn.Button);

            if (selected)
            {
                _navGroup.SelectLast();
            }

            return btn;
        }

        private DifficultyItem CreateItem(string body, bool selected, DifficultyItem difficultyItem, UnityAction a)
        {
            return CreateItem(null, body, selected, difficultyItem, a);
        }

        private DifficultyItem CreateItem(string header, string body, bool selected, UnityAction a, bool interactable = true)
        {
            return CreateItem(header, body, selected, _difficultyItemPrefab, a, interactable);
        }

        private DifficultyItem CreateItem(string body, bool selected, UnityAction a)
        {
            return CreateItem(null, body, selected, a);
        }

        private string LocalizeHeader(string key)
        {
            return Localize.Key("Menu.DifficultySelect", key);
        }

        // Profile icon shown in the player header. Party Vocals uses the multi-mic
        // "harmVocals" icon when the song has a harmony chart, else the solo "vocals" icon.
        // (A mic-count icon would need numbered sprites wired into the TMP sprite asset —
        // not worth it for a prototype. Both names below already resolve.) All other game
        // modes use their normal instrument sprite.
        private static string GetProfileIconSprite(YargPlayer player)
        {
            var profile = player.Profile;
            if (profile.GameMode != GameMode.PartyVocals)
            {
                return profile.GameMode.ToResourceName();
            }

            return GlobalVariables.State.CurrentSong.HasInstrument(Instrument.Harmony)
                ? "harmVocals"
                : "vocals";
        }

        private bool HasPlayableInstrument(SongEntry entry, in Instrument instrument)
        {
            // Party Vocals is playable when the song has any vocals chart (solo or harmony).
            if (instrument == Instrument.PartyVocals)
            {
                return entry.HasInstrument(Instrument.Vocals) || entry.HasInstrument(Instrument.Harmony);
            }

            // For vocals, all players *must* select the same gamemode (solo/harmony)
            if (instrument is Instrument.Vocals or Instrument.Harmony)
            {
                if (!entry.HasInstrument(instrument))
                {
                    return false;
                }

                // Loop through all of the players up to the current one
                // to see what has already been selected. Skip sitting-out players —
                // their CurrentInstrument shouldn't lock later players into a vocals mode.
                for (int i = 0; i < _playerIndex; i++)
                {
                    var player = PlayerContainer.Players[i];
                    if (player.SittingOut) continue;
                    var playerInstrument = player.Profile.CurrentInstrument;
                    if (playerInstrument is Instrument.Vocals or Instrument.Harmony)
                    {
                        return playerInstrument == instrument;
                    }
                }
            }

            return entry.HasInstrument(instrument) || instrument switch
            {
                // Allow 5 -> 4-lane conversions to be played on 4-lane
                Instrument.FourLaneDrums or
                Instrument.ProDrums      => entry.HasInstrument(Instrument.FiveLaneDrums),
                // Allow 4 -> 5-lane conversions to be played on 5-lane
                Instrument.FiveLaneDrums => entry.HasInstrument(Instrument.ProDrums),
                _ => false
            };
        }

        /// <summary>
        /// Returns the Solo-vs-Harmony chart preference locked in by the first
        /// non-sitting-out Party Vocals player ahead of the current one, or null when
        /// the current player IS that first player (and so chooses freely).
        /// All Party Vocals players share a single <see cref="VocalTrack"/> that is
        /// initialized from the first vocal player (see GameManager.Loading.cs), so a
        /// later player picking a different chart wouldn't render — it must match the
        /// first player's choice. Mirrors the same-gamemode constraint for the old
        /// Vocals/Harmony path in <see cref="HasPlayableInstrument"/>.
        /// </summary>
        private PartyVocalsChartPreference? GetLockedPartyVocalsPreference()
        {
            for (int i = 0; i < _playerIndex; i++)
            {
                var player = PlayerContainer.Players[i];
                if (player.SittingOut) continue;
                if (player.Profile.GameMode == GameMode.PartyVocals)
                {
                    return player.Profile.PartyVocalsChartPreference;
                }
            }

            return null;
        }

        private bool HasPlayableDifficulty(SongEntry entry, in Instrument instrument, in Difficulty difficulty)
        {
            // For vocals, insert special difficulties
            if (instrument is Instrument.Vocals or Instrument.Harmony or Instrument.PartyVocals)
            {
                return difficulty is not Difficulty.ExpertPlus;
            }

            // For PK, disallow beginner
            if (instrument is Instrument.ProKeys && difficulty is Difficulty.Beginner)
            {
                return false;
            }

            // Otherwise, we can do this
            return entry[instrument][difficulty] || instrument switch
            {
                // Allow 5 -> 4-lane conversions to be played on 4-lane
                Instrument.FourLaneDrums or
                Instrument.ProDrums      => entry[Instrument.FiveLaneDrums][difficulty],
                // Allow 4 -> 5-lane conversions to be played on 5-lane
                Instrument.FiveLaneDrums => entry[Instrument.ProDrums][difficulty],
                _ => false
            };
        }

        public void SongSpeedEndEdit(string text)
        {
            if (!float.TryParse(text.TrimEnd('%'), NumberStyles.Number, null, out var speed))
            {
                speed = 100;
            }

            int intSpeed = (int) Math.Clamp(speed, 10, 5000);

            _speedInput.SetTextWithoutNotify($"{intSpeed}%");
        }
    }
}
