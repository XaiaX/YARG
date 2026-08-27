using UnityEngine;
using YARG.Core.Logging;

namespace YARG.Venue
{
    // WARNING: Changing this could break themes or venues!
    //
    // This script is used a lot in theme creation.
    // Changing the serialized fields in this file will result in older themes
    // not working properly. Only change if you need to.

    public class NeonLightManager : MonoBehaviour
    {
        private static readonly int _emissionMultiplier = Shader.PropertyToID("_Emission_Multiplier");
        private static readonly int _emissionSecondaryColor = Shader.PropertyToID("_Emission_Secondary_Color");
        private static readonly int _emissionColor = Shader.PropertyToID("_EmissionColor");

        [SerializeField]
        private Material[] _neonMaterials;

		[System.Serializable]
		public struct NeonFullColor {
			public Material Material;
			public VenueLightLocation Location;
			public VenueSpotLightLocation SpotLocation;
			[System.NonSerialized]
			public Color InitialColor;
		}

		[SerializeField]
		private NeonFullColor[] _neonMaterialsFullColor;

        private LightManager _lightManager;
        private float[] _diagnosticFloatValues;
        private Color[] _diagnosticColorValues;
        private bool[] _diagnosticFloatValuesInitialized;
        private bool[] _diagnosticColorValuesInitialized;

        private void Start()
        {
            _lightManager = FindFirstObjectByType<LightManager>();

			for (int i = 0; i < _neonMaterialsFullColor.Length; i++) {
				_neonMaterialsFullColor[i].InitialColor = (_neonMaterialsFullColor[i].Material.GetColor(_emissionColor));
			}

            if (PerformanceDiagnostics.Enabled)
            {
                int cacheLength = _neonMaterials.Length + _neonMaterialsFullColor.Length;
                _diagnosticFloatValues = new float[cacheLength];
                _diagnosticColorValues = new Color[cacheLength];
                _diagnosticFloatValuesInitialized = new bool[cacheLength];
                _diagnosticColorValuesInitialized = new bool[cacheLength];
            }
        }

        private void RecordFloatWrite(int cacheIndex, float value)
        {
            if (!PerformanceDiagnostics.Enabled)
            {
                return;
            }

            bool unchanged = _diagnosticFloatValuesInitialized[cacheIndex] &&
                             Mathf.Approximately(_diagnosticFloatValues[cacheIndex], value);
            _diagnosticFloatValues[cacheIndex] = value;
            _diagnosticFloatValuesInitialized[cacheIndex] = true;
            PerformanceDiagnostics.NeonPropertyWrite(unchanged);
        }

        private void RecordColorWrite(int cacheIndex, Color value)
        {
            if (!PerformanceDiagnostics.Enabled)
            {
                return;
            }

            bool unchanged = _diagnosticColorValuesInitialized[cacheIndex] &&
                             _diagnosticColorValues[cacheIndex] == value;
            _diagnosticColorValues[cacheIndex] = value;
            _diagnosticColorValuesInitialized[cacheIndex] = true;
            PerformanceDiagnostics.NeonPropertyWrite(unchanged);
        }

        private void Update()
        {
            using var diagnostics = PerformanceDiagnostics.Scope(PerformanceDiagnostics.NeonUpdateMarker);
            PerformanceDiagnostics.NeonMaterialCount(_neonMaterials.Length + _neonMaterialsFullColor.Length);

            // Update all of the materials
            for (int i = 0; i < _neonMaterials.Length; i++)
            {
                var material = _neonMaterials[i];
				var lightState = _lightManager.GenericLightState;
                RecordFloatWrite(i, lightState.Intensity);
				material.SetFloat(_emissionMultiplier, lightState.Intensity);

                if (lightState.Color == null)
                {
                    RecordColorWrite(i, Color.white);
					material.SetColor(_emissionSecondaryColor, Color.white);
                }
                else
                {
                    RecordColorWrite(i, lightState.Color.Value);
					material.SetColor(_emissionSecondaryColor, lightState.Color.Value);
                }
            }

            for (int i = 0; i < _neonMaterialsFullColor.Length; i++)
            {
				var neon = _neonMaterialsFullColor[i];

				switch ((neon.Location, neon.SpotLocation))
				{
					case (VenueLightLocation.Generic, VenueSpotLightLocation.None):
						var lightState = _lightManager.GenericLightState;
                        RecordColorWrite(i + _neonMaterials.Length, lightState.Color ?? neon.InitialColor);
						neon.Material.SetColor(_emissionColor, lightState.Color ?? neon.InitialColor);
                        RecordFloatWrite(i + _neonMaterials.Length, lightState.Intensity);
						neon.Material.SetFloat(_emissionMultiplier, lightState.Intensity);
					break;

					case (VenueLightLocation.Left, VenueSpotLightLocation.None):
						var lightStateLeft = _lightManager.LeftLightState;
						RecordColorWrite(i + _neonMaterials.Length, lightStateLeft.Color ?? neon.InitialColor);
						neon.Material.SetColor(_emissionColor, lightStateLeft.Color ?? neon.InitialColor);
						RecordFloatWrite(i + _neonMaterials.Length, lightStateLeft.Intensity);
						neon.Material.SetFloat(_emissionMultiplier, lightStateLeft.Intensity);
					break;

					case (VenueLightLocation.Right, VenueSpotLightLocation.None):
						var lightStateRight = _lightManager.RightLightState;
						RecordColorWrite(i + _neonMaterials.Length, lightStateRight.Color ?? neon.InitialColor);
						neon.Material.SetColor(_emissionColor, lightStateRight.Color ?? neon.InitialColor);
						RecordFloatWrite(i + _neonMaterials.Length, lightStateRight.Intensity);
						neon.Material.SetFloat(_emissionMultiplier, lightStateRight.Intensity);
					break;

					case (VenueLightLocation.Front, VenueSpotLightLocation.None):
						var lightStateFront = _lightManager.FrontLightState;
						RecordColorWrite(i + _neonMaterials.Length, lightStateFront.Color ?? neon.InitialColor);
						neon.Material.SetColor(_emissionColor, lightStateFront.Color ?? neon.InitialColor);
						RecordFloatWrite(i + _neonMaterials.Length, lightStateFront.Intensity);
						neon.Material.SetFloat(_emissionMultiplier, lightStateFront.Intensity);
					break;

					case (VenueLightLocation.Back, VenueSpotLightLocation.None):
						var lightStateBack = _lightManager.BackLightState;
						RecordColorWrite(i + _neonMaterials.Length, lightStateBack.Color ?? neon.InitialColor);
						neon.Material.SetColor(_emissionColor, lightStateBack.Color ?? neon.InitialColor);
						RecordFloatWrite(i + _neonMaterials.Length, lightStateBack.Intensity);
						neon.Material.SetFloat(_emissionMultiplier, lightStateBack.Intensity);
					break;

					case (VenueLightLocation.Center, VenueSpotLightLocation.None):
						var lightStateCenter = _lightManager.CenterLightState;
						RecordColorWrite(i + _neonMaterials.Length, lightStateCenter.Color ?? neon.InitialColor);
						neon.Material.SetColor(_emissionColor, lightStateCenter.Color ?? neon.InitialColor);
						RecordFloatWrite(i + _neonMaterials.Length, lightStateCenter.Intensity);
						neon.Material.SetFloat(_emissionMultiplier, lightStateCenter.Intensity);
					break;

					case (VenueLightLocation.Crowd, VenueSpotLightLocation.None):
						var lightStateCrowd = _lightManager.CrowdLightState;
						RecordColorWrite(i + _neonMaterials.Length, lightStateCrowd.Color ?? neon.InitialColor);
						neon.Material.SetColor(_emissionColor, lightStateCrowd.Color ?? neon.InitialColor);
						RecordFloatWrite(i + _neonMaterials.Length, lightStateCrowd.Intensity);
						neon.Material.SetFloat(_emissionMultiplier, lightStateCrowd.Intensity);
					break;

					case (_, VenueSpotLightLocation.Bass):
						var BassIntensity = neon.Material.GetFloat(_emissionMultiplier);
						var lightStateBass = _lightManager.GetSpotlightStateFor(VenueSpotLightLocation.Bass);
						float Bass = Mathf.Lerp(BassIntensity, lightStateBass ? 1f : 0f, Time.deltaTime * 10f);
                        RecordFloatWrite(i + _neonMaterials.Length, Bass);
						neon.Material.SetFloat(_emissionMultiplier, Bass);
					break;

					case (_, VenueSpotLightLocation.Drums):
						var DrumsIntensity = neon.Material.GetFloat(_emissionMultiplier);
						var lightStateDrums = _lightManager.GetSpotlightStateFor(VenueSpotLightLocation.Drums);
						float Drums = Mathf.Lerp(DrumsIntensity, lightStateDrums ? 1f : 0f, Time.deltaTime * 10f);
                        RecordFloatWrite(i + _neonMaterials.Length, Drums);
						neon.Material.SetFloat(_emissionMultiplier, Drums);
					break;

					case (_, VenueSpotLightLocation.Guitar):
						var GuitarIntensity = neon.Material.GetFloat(_emissionMultiplier);
						var lightStateGuitar = _lightManager.GetSpotlightStateFor(VenueSpotLightLocation.Guitar);
						float Guitar = Mathf.Lerp(GuitarIntensity, lightStateGuitar ? 1f : 0f, Time.deltaTime * 10f);
                        RecordFloatWrite(i + _neonMaterials.Length, Guitar);
						neon.Material.SetFloat(_emissionMultiplier, Guitar);
					break;

					case (_, VenueSpotLightLocation.Vocals):
						var VocalsIntensity = neon.Material.GetFloat(_emissionMultiplier);
						var lightStateVocals = _lightManager.GetSpotlightStateFor(VenueSpotLightLocation.Vocals);
						float Vocals = Mathf.Lerp(VocalsIntensity, lightStateVocals ? 1f : 0f, Time.deltaTime * 10f);
                        RecordFloatWrite(i + _neonMaterials.Length, Vocals);
						neon.Material.SetFloat(_emissionMultiplier, Vocals);
					break;

					default:
						YargLogger.LogDebug("Unknown location for neon light");
					break;
				}
            }
        }
    }
}
