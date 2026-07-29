using System;

namespace YARG.Settings.Metadata
{
    public sealed class ButtonRowMetadata : AbstractMetadata
    {
        public override string[] UnlocalizedSearchNames { get; }

        public string[] Buttons { get; private set; }

        /// <summary>
        /// Optional predicate used for runtime-only actions. The settings tab omits this
        /// button row when the predicate is false.
        /// </summary>
        public Func<bool> VisibilityPredicate { get; }

        public ButtonRowMetadata(string button, bool isAdvanced = false, Func<bool> visibilityPredicate = null)
            : base(isAdvanced)
        {
            UnlocalizedSearchNames = new[] { $"Button.{button}" };
            Buttons = new[] { button };
            VisibilityPredicate = visibilityPredicate;
        }

        public ButtonRowMetadata(string button, Func<bool> visibilityPredicate)
            : this(button, false, visibilityPredicate)
        {
        }

        public ButtonRowMetadata(bool isAdvanced, params string[] buttons)
            : base(isAdvanced)
        {
            UnlocalizedSearchNames = new string[buttons.Length];
            for (int i = 0; i < buttons.Length; i++)
            {
                UnlocalizedSearchNames[i] = $"Button.{buttons[i]}";
            }

            Buttons = buttons;
        }

        public ButtonRowMetadata(params string[] buttons)
            : this(false, buttons)
        {
        }
    }
}