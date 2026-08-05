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
        [SerializeField] private Image _instrumentIcon;
        [SerializeField] private TMP_Text _setup;
        [SerializeField] private TMP_Text _modifiers;

        public Guid ProfileId { get; private set; }
        public event Action<Guid> Clicked;

        public void Initialize(MaestroStagedPlayer player)
        {
            ProfileId = player.ProfileId;
            Refresh(player, false);
        }

        public void Refresh(MaestroStagedPlayer player, bool selected)
        {
            if (player.ProfileId != ProfileId)
                return;

            if (_name != null)
                _name.text = player.IsBot ? $"* {player.Name}" : player.Name;
            SetInstrumentIcon(player.Instrument);
            if (_setup != null)
                _setup.text = $"{player.GameMode} · {player.Instrument} · {player.Difficulty}";
            if (_modifiers != null)
            {
                string modifierText = player.Modifiers == Modifier.None
                    ? "No modifiers"
                    : string.Join(", ", EnumExtensions<Modifier>.Values
                        .Where(modifier => modifier != Modifier.None &&
                            (player.Modifiers & modifier) != 0)
                        .Select(modifier => modifier.ToLocalizedName()));
                _modifiers.text = modifierText;
            }
            SetSelected(selected, SelectionOrigin.Programmatically);
        }

        private void SetInstrumentIcon(Instrument instrument)
        {
            if (_instrumentIcon == null)
                return;

            string resourceName = instrument.ToResourceName();
            if (string.IsNullOrEmpty(resourceName))
            {
                _instrumentIcon.sprite = null;
                _instrumentIcon.enabled = false;
                return;
            }

            string assetKey = $"InstrumentIcons[{resourceName}]";
            if (!IconCache.TryGetValue(assetKey, out var icon))
            {
                icon = Addressables.LoadAssetAsync<Sprite>(assetKey).WaitForCompletion();
                IconCache[assetKey] = icon;
            }

            _instrumentIcon.sprite = icon;
            _instrumentIcon.enabled = icon != null;
        }

        public void SetSelected(bool selected)
        {
            SetSelected(selected, SelectionOrigin.Programmatically);
        }

        public override void Confirm()
        {
            Clicked?.Invoke(ProfileId);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            Clicked?.Invoke(ProfileId);
        }
    }
}
