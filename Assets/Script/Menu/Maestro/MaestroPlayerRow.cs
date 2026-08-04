using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using YARG.Core.Game;
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
        [SerializeField] private TMP_Text _name;
        [SerializeField] private TMP_Text _status;
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
                _name.text = player.Name;
            if (_status != null)
            {
                string state = player.SittingOut ? "Sitting out" : player.IsBot ? "Bot" :
                    player.IsMissingInput ? "Missing input" : "Ready";
                _status.text = state;
            }
            if (_setup != null)
                _setup.text = $"{player.GameMode} · {player.Instrument} · {player.Difficulty}";
            if (_modifiers != null)
            {
                string modifierText = player.Modifiers == Modifier.None
                    ? "No modifiers"
                    : player.Modifiers.ToLocalizedName();
                _modifiers.text = modifierText;
            }
            SetSelected(selected, SelectionOrigin.Programmatically);
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
