// pattern: Imperative Shell

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.AddressableAssets;
using UnityEngine.UI;
using YARG.Core;
using YARG.Core.Game;
using YARG.Core.Extensions;
using YARG.Helpers.Extensions;
using YARG.Localization;
using YARG.Menu.Navigation;

namespace YARG.Menu.Maestro
{
    /// <summary>
    /// Compact, stable-ID keyed overview row for one staged Maestro player.
    /// The row has no profile mutation responsibilities; selection is reported to the page.
    /// </summary>
    public sealed class MaestroPlayerRow : NavigatableBehaviour, IPointerClickHandler
    {
        private static readonly Dictionary<string, Sprite> IconCache = new();

        [SerializeField] private TMP_Text _name;
        [SerializeField] private Image _gameModeIcon;
        [SerializeField] private TMP_Text _setup;
        [SerializeField] private TMP_Text _modifiers;

        private bool _wasSelectedOnPointerDown;
        private Color _nameColor;
        private Color _setupColor;
        private Color _modifiersColor;
        private Color _gameModeIconColor;
        private bool _contentColorsCaptured;

        public Guid ProfileId { get; private set; }
        public event Action<Guid> Confirmed;

        public void Initialize(MaestroStagedPlayer player)
        {
            ProfileId = player.ProfileId;
            Refresh(player, false);
        }

        public void Refresh(MaestroStagedPlayer player, bool selected, string tierLabel = null,
            bool partAvailable = true)
        {
            if (player.ProfileId != ProfileId)
                return;

            if (_name != null)
                _name.text = player.IsBot ? $"* {player.Name}" : player.Name;
            SetGameModeIcon(player.GameMode);
            if (_setup != null)
            {
                if (player.SittingOut)
                {
                    const string sitOut = "Sitting Out";
                    _setup.text = $"<color=#FFB636>{sitOut}</color>";
                    if (_modifiers != null)
                        _modifiers.text = partAvailable ? "—" : "No Part Available";
                    SetSelected(selected, SelectionOrigin.Programmatically);
                    return;
                }

                // For Party Vocals, show "Solo"/"Harmony" to match the dropdown
                // labels instead of the raw instrument names "Vocals"/"Harmony".
                string instrumentLabel =
                    player.GameMode == GameMode.PartyVocals &&
                    player.Instrument is Instrument.Vocals or Instrument.Harmony
                        ? (player.Instrument == Instrument.Vocals ? "Solo" : "Harmony")
                        : player.Instrument.ToLocalizedName();
                string line1 = $"{instrumentLabel} · " +
                    player.Difficulty.ToLocalizedName();

                var lines = new List<string> { line1 };
                if (!string.IsNullOrEmpty(tierLabel))
                    lines.Add($"<size=14>{tierLabel}</size>");

                string trackDelta = GetTrackDeltaLabel(player);
                if (!string.IsNullOrEmpty(trackDelta))
                    lines.Add($"<size=14>{trackDelta}</size>");

                _setup.text = string.Join("\n", lines);
            }
            if (_modifiers != null)
            {
                var activeAdjustments = EnumExtensions<Modifier>.Values
                    .Where(modifier => modifier != Modifier.None &&
                        (player.Modifiers & modifier) != 0 &&
                        !MaestroSelectionRules.IsAccessibilityModifier(modifier))
                    .Select(modifier => modifier.ToLocalizedName())
                    .ToList();

                foreach (var modifier in EnumExtensions<Modifier>.Values)
                {
                    if (modifier == Modifier.None || modifier == Modifier.RangeCompress ||
                        !MaestroSelectionRules.IsAccessibilityModifier(modifier) ||
                        (player.Modifiers & modifier) == 0)
                        continue;

                    activeAdjustments.Add(modifier.ToLocalizedName());
                }

                if (player.LeftyFlip && MaestroSelectionRules.SupportsLeftyFlip(player.GameMode))
                {
                    activeAdjustments.Add(Localize.Key("Menu.DifficultySelect", "LeftyFlip"));
                }

                if (MaestroSelectionRules.HasNoRangeShifts(player))
                {
                    activeAdjustments.Add(Localize.Key("Menu.DifficultySelect", "NoRangeShifts"));
                }

                if (player.GameMode == GameMode.ProKeys)
                {
                    string openLane = player.OpenLaneDisplayType switch
                    {
                        OpenLaneDisplayType.Always =>
                            Localize.Key("Menu.DifficultySelect", "OpenLaneAlways"),
                        OpenLaneDisplayType.IfChartContainsOpens =>
                            Localize.Key("Menu.DifficultySelect", "OpenLaneWhenCharted"),
                        _ => null,
                    };
                    if (openLane != null)
                        activeAdjustments.Add(openLane);
                }

                _modifiers.text = activeAdjustments.Count == 0
                    ? "No modifiers"
                    : string.Join(", ", activeAdjustments);
            }
            SetSelected(selected, SelectionOrigin.Programmatically);
        }

        /// <summary>
        /// Builds a compact "Speed +1.0  Length -0.5" delta label when either value
        /// differs from its default. Returns null when both are at default.
        /// </summary>
        private static string GetTrackDeltaLabel(MaestroStagedPlayer player)
        {
            float speedDelta = Mathf.Round(
                (player.NoteSpeed - MaestroDefaults.NoteSpeed) * 10f) / 10f;
            float lengthDelta = Mathf.Round(
                (player.HighwayLength - MaestroDefaults.HighwayLength) * 10f) / 10f;

            var parts = new List<string>(2);
            if (speedDelta != 0f)
                parts.Add("Speed " + speedDelta.ToString("+0.0;-0.0", CultureInfo.CurrentCulture));
            if (lengthDelta != 0f)
                parts.Add("Length " + lengthDelta.ToString("+0.0;-0.0", CultureInfo.CurrentCulture));

            return parts.Count == 0 ? null : string.Join("  ", parts);
        }

        private void SetGameModeIcon(GameMode gameMode)
        {
            if (_gameModeIcon == null)
                return;

            string resourceName = gameMode.ToResourceName();
            if (string.IsNullOrEmpty(resourceName))
            {
                _gameModeIcon.sprite = null;
                _gameModeIcon.enabled = false;
                return;
            }

            string assetKey = $"InstrumentIcons[{resourceName}]";
            if (!IconCache.TryGetValue(assetKey, out var icon))
            {
                icon = Addressables.LoadAssetAsync<Sprite>(assetKey).WaitForCompletion();
                IconCache[assetKey] = icon;
            }

            _gameModeIcon.sprite = icon;
            _gameModeIcon.enabled = icon != null;
        }

        public void SetSelected(bool selected)
        {
            SetSelected(selected, SelectionOrigin.Programmatically);
        }

        public void SetEditorDimmed(bool dimmed)
        {
            CaptureContentColors();
            float alpha = dimmed ? 0.2f : 1f;
            SetGraphicAlpha(_name, _nameColor, alpha);
            SetGraphicAlpha(_setup, _setupColor, alpha);
            SetGraphicAlpha(_modifiers, _modifiersColor, alpha);
            SetGraphicAlpha(_gameModeIcon, _gameModeIconColor, alpha);
        }

        private void CaptureContentColors()
        {
            if (_contentColorsCaptured)
                return;

            _nameColor = _name != null ? _name.color : Color.white;
            _setupColor = _setup != null ? _setup.color : Color.white;
            _modifiersColor = _modifiers != null ? _modifiers.color : Color.white;
            _gameModeIconColor = _gameModeIcon != null ? _gameModeIcon.color : Color.white;
            _contentColorsCaptured = true;
        }

        private static void SetGraphicAlpha(Graphic graphic, Color baseColor, float alpha)
        {
            if (graphic == null)
                return;

            var color = baseColor;
            color.a = baseColor.a * alpha;
            graphic.color = color;
        }

        public override void Confirm()
        {
            Confirmed?.Invoke(ProfileId);
        }

        public override void OnPointerDown(PointerEventData eventData)
        {
            _wasSelectedOnPointerDown = Selected;
            base.OnPointerDown(eventData);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            // The first click selects a different profile and leaves the right-side
            // editor unfocused. Clicking the already-selected row acts as mouse confirm.
            if (_wasSelectedOnPointerDown)
                Confirm();
        }
    }
}
