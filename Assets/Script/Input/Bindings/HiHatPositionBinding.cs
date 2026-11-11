// pattern: Imperative Shell
using System;
using Minis;
using UnityEngine.InputSystem;
using YARG.Core.Input;
using YARG.Input.Serialization;

namespace YARG.Input
{
    internal sealed class HiHatPositionBinding : ButtonBinding
    {
        internal const float MIN_CLOSED_THRESHOLD = .01f;
        private const float DEFAULT_CLOSED_THRESHOLD = .8f;

        public HiHatPositionBinding() : base("EliteDrums.HiHatPosition", (int) EliteDrumsAction.EliteHiHatPosition)
        {
        }

        public override bool IsControlCompatible(InputControl control, out InputControl<float> typedControl)
        {
            typedControl = control as MidiValueControl;
            return typedControl != null;
        }

        protected override SingleButtonBinding CreateBinding(ActuationSettings settings, InputControl<float> control) =>
            new PedalControl(control) { PressPoint = DEFAULT_CLOSED_THRESHOLD };

        protected override SingleButtonBinding DeserializeControl(InputControl<float> control,
            SerializedInputControl serialized) => new PedalControl(control, serialized);

        private sealed class PedalControl : SingleButtonBinding
        {
            public PedalControl(InputControl<float> control) : base(control)
            {
            }

            public PedalControl(InputControl<float> control, SerializedInputControl serialized) : base(control, serialized)
            {
                // Loading inversion can read the device's default zero before any CC
                // has arrived. Start open so resolution alone cannot emit a press.
                ResetState();
            }

            public override float PressPoint
            {
                get => base.PressPoint;
                set => base.PressPoint = float.IsFinite(value)
                    ? Math.Clamp(value, MIN_CLOSED_THRESHOLD, 1f)
                    : DEFAULT_CLOSED_THRESHOLD;
            }

            protected override float CalculateState(float rawValue) => Inverted ? 1f - rawValue : rawValue;
        }
    }
}
