// pattern: Imperative Shell
using System;

namespace YARG.Settings.Types
{
    public sealed class AntialiasingSetting : DropdownSetting<VenueAntiAliasingMethod>
    {
        private readonly bool _venue;

        public AntialiasingSetting(VenueAntiAliasingMethod value, bool venue,
            Action<VenueAntiAliasingMethod> onChange = null) : base(value, onChange)
        {
            _venue = venue;
            UpdateValues();
        }

        public override int CurrentIndex
        {
            get
            {
                int savedIndex = _possibleValues.IndexOf(Value);
                if (savedIndex >= 0)
                {
                    return savedIndex;
                }
                if (Value == VenueAntiAliasingMethod.TAA && !_venue)
                {
                    return _possibleValues.IndexOf(VenueAntiAliasingMethod.SMAA);
                }

                // Moving a settings file between machines must not erase the original preference.
                // Show the effective supported fallback until the player chooses another value.
                int requested = GraphicsManager.GetRequestedMsaaSamples(Value);
                int fallbackIndex = 0;
                int fallbackSamples = 1;
                for (int i = 0; i < _possibleValues.Count; i++)
                {
                    int samples = GraphicsManager.GetRequestedMsaaSamples(_possibleValues[i]);
                    if (samples <= requested && samples > fallbackSamples)
                    {
                        fallbackIndex = i;
                        fallbackSamples = samples;
                    }
                }
                return fallbackIndex;
            }
        }

        public override void UpdateValues()
        {
            _possibleValues.Clear();
            _possibleValues.Add(VenueAntiAliasingMethod.None);
            _possibleValues.Add(VenueAntiAliasingMethod.FXAA);
            _possibleValues.Add(VenueAntiAliasingMethod.SMAA);
            foreach (var method in new[]
            {
                VenueAntiAliasingMethod.MSAA2x,
                VenueAntiAliasingMethod.MSAA,
                VenueAntiAliasingMethod.MSAA8x,
            })
            {
                if (GraphicsManager.IsAntialiasingSupported(method, _venue))
                {
                    _possibleValues.Add(method);
                }
            }
            if (_venue)
            {
                _possibleValues.Add(VenueAntiAliasingMethod.TAA);
            }
        }
    }
}
