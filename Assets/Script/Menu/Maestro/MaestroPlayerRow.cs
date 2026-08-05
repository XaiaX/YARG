// pattern: Imperative Shell

using System;
using System.Collections.Generic;
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
        private CanvasGroup _rowCanvasGroup;

        public Guid ProfileId { get; private set; }
        public event Action<Guid> Confirmed;

        public void Initialize(MaestroStagedPlayer player)
        {
            ProfileId = player.ProfileId;
            Refresh(player, false);
        }

        public void Refresh(MaestroStagedPlayer player, bool selected, string tierLabel = null)
        {
            if (player.ProfileId != ProfileId)
                return;

            if (_name != null)
                _name.text = player.IsBot ? $"* {player.Name}" : player.Name;
            SetGameModeIcon(player.GameMode);
            if (_setup != null)
            {
                string line1 = $"{player.Instrument.ToLocalizedName()} · " +
                    player.Difficulty.ToLocalizedName();
                _setup.text = string.IsNullOrEmpty(tierLabel)
                    ? line1
                    : $"{line1}\n<size=14>{tierLabel}</size>";
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
            EnsureRowCanvasGroup();
            if (_rowCanvasGroup != null)
                _rowCanvasGroup.alpha = dimmed ? 0.5f : 1f;
        }

        private void EnsureRowCanvasGroup()
        {
            if (_rowCanvasGroup == null)
                _rowCanvasGroup = GetComponent<CanvasGroup>() ??
                    gameObject.AddComponent<CanvasGroup>();
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
