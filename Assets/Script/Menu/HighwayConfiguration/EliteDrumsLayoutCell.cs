using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core.Game;
using YARG.Menu.Navigation;

namespace YARG.Menu.HighwayConfiguration
{
    public sealed class EliteDrumsLayoutCell : NavigatableBehaviour
    {
        [SerializeField] private TMP_Text _label;
        [SerializeField] private Button _button;
        [SerializeField] private Outline _outline;
        [SerializeField] private Image _fill;
        [SerializeField] private Image _fretPill;
        [SerializeField] private GameObject _focusVisual;
        private EliteDrumsLayoutMenu _menu;
        private bool _selected;

        protected override void OnSelectionChanged(bool selected)
        {
            _selected = selected;
            if (_focusVisual != null) _focusVisual.SetActive(selected);
        }
        public int Column { get; private set; }
        public bool Drum { get; private set; }

        public void Initialize(EliteDrumsLayoutMenu menu, int column, bool drum)
        {
            _menu = menu;
            Column = column;
            Drum = drum;
            _button.onClick.AddListener(() =>
            {
                SetSelected(true, SelectionOrigin.Mouse);
                Confirm();
            });
        }

        public override void Confirm()
        {
            if (_button != null && _button.interactable)
                _menu.OpenCell(Column, Drum);
        }

        public void Render(EliteDrumsLayoutSlot slot, ColorProfile previewColor, bool readOnly, bool runtimeSupported)
        {
            string id = slot.Kind switch
            {
                EliteDrumsLayoutSlotKind.HiHat => "hi-hat",
                EliteDrumsLayoutSlotKind.Kick => Drum ? "kick" : "not-applicable",
                EliteDrumsLayoutSlotKind.Kick1 => Drum ? "kick1" : "not-applicable",
                EliteDrumsLayoutSlotKind.Kick2 => Drum ? "kick2" : "not-applicable",
                _ => (Drum ? slot.Drum : slot.Cymbal) ?? "empty"
            };
            bool assigned = id != "empty" && id != "not-applicable";
            _label.text = FormatLabel(id, EliteDrumsLayoutMenu.Label(
                slot.Kind == EliteDrumsLayoutSlotKind.HiHat && Drum ? "hi-hat-pedal" : id));
            _button.interactable = !readOnly && id != "not-applicable";
            var colors = previewColor?.EliteDrums;
            var noteColor = NoteColor(colors, slot, id);
            _outline.effectColor = noteColor.HasValue ? ToUnityColor(noteColor.Value) : new Color(0.32f, 0.36f, 0.4f);
            if (_fill != null)
            {
                var dark = new Color(0.055f, 0.07f, 0.09f, 1f);
                var fill = runtimeSupported && assigned && noteColor.HasValue
                    ? Color.Lerp(dark, ToUnityColor(noteColor.Value), 0.25f)
                    : dark;
                fill.a = 1f;
                _fill.color = fill;
            }
            if (_fretPill != null)
                _fretPill.color = colors == null ? new Color(0.32f, 0.36f, 0.4f) : ToUnityColor(colors.GetSlotFretColor(Column));
            if (_focusVisual != null)
                _focusVisual.SetActive(_selected);
        }

        private static string FormatLabel(string id, string value) => id switch
        {
            "left-crash" or "right-crash" or "tom1" or "tom2" or "tom3" or "kick" or "kick1" or "kick2" =>
                YARG.Localization.Localize.Key("Menu.EliteLayout.Cell." + id),
            _ => value
        };

        private System.Drawing.Color? NoteColor(ColorProfile.EliteDrumsColors colors, EliteDrumsLayoutSlot slot, string id)
        {
            if (colors == null || id is "empty" or "not-applicable") return null;
            if (slot.Kind == EliteDrumsLayoutSlotKind.HiHat)
                return colors.GetNoteColor(EliteDrumsColorRole.HatIndifferent);
            if (slot.Kind == EliteDrumsLayoutSlotKind.Kick)
                return colors.GetNoteColor(EliteDrumsColorRole.Kick);
            if (slot.Kind == EliteDrumsLayoutSlotKind.Kick1)
                return colors.GetNoteColor(EliteDrumsColorRole.Kick);
            if (slot.Kind == EliteDrumsLayoutSlotKind.Kick2)
                return colors.GetNoteColor(EliteDrumsColorRole.DoubleKick);
            if (Drum && id == "stomp") return colors.GetNoteColor(EliteDrumsColorRole.Stomp);
            if (Drum && id == "kick") return colors.GetNoteColor(EliteDrumsColorRole.Kick);
            if (Drum && id == "kick1") return colors.GetNoteColor(EliteDrumsColorRole.Kick);
            if (Drum && id == "kick2") return colors.GetNoteColor(EliteDrumsColorRole.DoubleKick);
            var role = id switch
            {
                "snare" => EliteDrumsColorRole.Snare,
                "hi-hat" => EliteDrumsColorRole.HatIndifferent,
                "left-crash" => EliteDrumsColorRole.LeftCrash,
                "ride" => EliteDrumsColorRole.Ride,
                "right-crash" => EliteDrumsColorRole.RightCrash,
                "tom1" => EliteDrumsColorRole.Tom1,
                "tom2" => EliteDrumsColorRole.Tom2,
                "tom3" => EliteDrumsColorRole.Tom3,
                "stomp" => EliteDrumsColorRole.Stomp,
                "kick" => EliteDrumsColorRole.Kick,
                "kick1" => EliteDrumsColorRole.Kick,
                "kick2" => EliteDrumsColorRole.DoubleKick,
                _ => (EliteDrumsColorRole?)null
            };
            return role.HasValue ? colors.GetNoteColor(role.Value) : null;
        }

        private static Color ToUnityColor(System.Drawing.Color color) => new Color32(color.R, color.G, color.B, color.A);
    }
}
