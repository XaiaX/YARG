using System.Collections.Generic;
using UnityEngine;
using YARG.Core.Diagnostics;
using YARG.Core.Game;
using YARG.Helpers.Extensions;
using YARG.Themes;
using Color = System.Drawing.Color;

namespace YARG.Gameplay.Visuals
{
    public class Fret : MonoBehaviour, IThemeBindable<ThemeFret>
    {
        private static readonly int _fade          = Shader.PropertyToID("Fade");
        private static readonly int _secondaryFade = Shader.PropertyToID("SecondaryFade");
        private static readonly int _emissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly int _secondaryColor = Shader.PropertyToID("_SecondaryColor");
        private static readonly int _secondaryEmissionColor = Shader.PropertyToID("_SecondaryEmissionColor");

        private static readonly int _hit       = Animator.StringToHash("Hit");
        private static readonly int _cymbalHit = Animator.StringToHash("CymbalHit");
        private static readonly int _miss      = Animator.StringToHash("Miss");
        private static readonly int _openMiss  = Animator.StringToHash("OpenMiss");
        private static readonly int _pressed   = Animator.StringToHash("Pressed");
        private static readonly int _sustain   = Animator.StringToHash("Sustain");
        private static readonly int _secondaryPressed = Animator.StringToHash("SecondaryPressed");

        // If we want info to be copied over when we copy the prefab,
        // we must make them SerializeFields.
        [field: SerializeField]
        [field: HideInInspector]
        public ThemeFret ThemeBind { get; set; }

        private readonly List<Material> _topMaterials   = new();
        private readonly List<Material> _innerMaterials = new();

        // Secondary half materials (for 6-fret dual-half frets)
        private readonly List<Material> _secondaryTopMaterials   = new();
        private readonly List<Material> _secondaryInnerMaterials = new();

        private AnimatorOverrideController _ownedSustainController;
        private static readonly int _kitCymbal = Shader.PropertyToID("_EliteFretCymbal");
        private static readonly int _kitX = Shader.PropertyToID("_EliteFretAxisX");
        private static readonly int _kitZ = Shader.PropertyToID("_EliteFretAxisZ");
        private static readonly int _kitExpansion = Shader.PropertyToID("_EliteFretExpansion");
        private readonly List<Material> _splitMaterials = new();
        private float _lastSplit;
        private UnityEngine.Color _originalCymbal;
        private float _cymbalDirection;

        private void SetCymbalTint(UnityEngine.Color value)
        {
            if (_splitMaterials.Count == 0) return;
            var tint = QualitySettings.activeColorSpace == ColorSpace.Linear ? value.linear : value;
            foreach (var mat in _splitMaterials)
                mat.SetVector(_kitCymbal, new Vector4(tint.r, tint.g, tint.b, _cymbalDirection));
        }

        internal void SetKitColors(UnityEngine.Color drum, UnityEngine.Color cymbal, bool lefty, bool paired, EliteFretGradient gradient)
        {
            _originalUnityTopColor = _originalUnityInnerColor = drum;
            _originalEmissionColor = drum * 11.5f;
            _splitMaterials.Clear();
            _lastSplit = 0;
            // Project mesh-local positions into fret space. Submeshes can have their
            // own transforms and texture atlases, including on the pressed surface.
            var bounds = new Bounds();
            bool first = true;
            foreach (var surface in ThemeBind.ColorSurfaces)
            {
                var mesh = surface.Mesh.GetComponent<MeshFilter>().sharedMesh;
                var box = mesh.GetSubMesh(surface.MaterialIndex).bounds;
                var matrix = transform.worldToLocalMatrix * surface.Mesh.localToWorldMatrix;
                for (int corner = 0; corner < 8; corner++)
                {
                    var point = matrix.MultiplyPoint3x4(new Vector3(
                        (corner & 1) == 0 ? box.min.x : box.max.x,
                        (corner & 2) == 0 ? box.min.y : box.max.y,
                        (corner & 4) == 0 ? box.min.z : box.max.z));
                    if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                    else bounds.Encapsulate(point);
                }
            }
            _originalCymbal = cymbal;
            // The sign mirrors the pattern. Its magnitude selects the mask in EliteFretBlend.
            int mask = gradient switch { EliteFretGradient.Vertical => 2, EliteFretGradient.Diagonal => 3, _ => 1 };
            _cymbalDirection = lefty ? -mask : mask;
            foreach (var surface in ThemeBind.ColorSurfaces)
            {
                var mat = surface.Mesh.materials[surface.MaterialIndex];
                mat.color = drum;
                if (mat.HasProperty(_emissionColor)) mat.SetColor(_emissionColor, _originalEmissionColor);
                if (!mat.HasProperty(_kitCymbal)) continue;
                if (!paired)
                {
                    mat.SetVector(_kitCymbal, Vector4.zero);
                    continue;
                }
                var matrix = transform.worldToLocalMatrix * surface.Mesh.localToWorldMatrix;
                var x = matrix.GetRow(0);
                var z = matrix.GetRow(2);
                x.w -= bounds.center.x;
                z.w -= bounds.center.z;
                mat.SetVector(_kitX, x / Mathf.Max(.0001f, bounds.size.x));
                mat.SetVector(_kitZ, z / Mathf.Max(.0001f, bounds.size.z));
                mat.SetFloat(_kitExpansion, 0);
                _splitMaterials.Add(mat);
            }
            SetCymbalTint(cymbal);
        }

        internal void SetColorSplit(float amount)
        {
            if (amount == _lastSplit) return;
            _lastSplit = amount;
            foreach (var material in _splitMaterials) material.SetFloat(_kitExpansion, amount);
        }

        private bool _hasPressedParam;
        private bool _hasSustainParam;
        private bool _hasOpenMissTrigger;
        private bool _hasSecondaryPressedParam;

        // These need to be saved since the colors can now change during play
        // They are saved as Unity colors to avoid having to repeatedly convert
        // when transitioning between active and inactive states
        private UnityEngine.Color _originalUnityTopColor;
        private UnityEngine.Color _originalUnityInnerColor;
        private UnityEngine.Color _originalEmissionColor;

        // Secondary half original colors
        private UnityEngine.Color _secondaryOriginalUnityTopColor;
        private UnityEngine.Color _secondaryOriginalUnityInnerColor;
        private UnityEngine.Color _secondaryOriginalEmissionColor;

        // TODO: Consider making this customizable or perhaps just a desaturated and dimmed version of the base color
        private UnityEngine.Color _inactiveColor = new(0.321f, 0.321f, 0.321f, 1.0f);

        private bool _active             = true;
        private bool _colorChangeEnabled = false;
        private bool _fadeDirection      = true;
        // True is pulsing, false is fading
        private bool  _pulseOrFade  = true;
        private float _fadeDuration = 0.25f;
        private float _fadeStartTime;
        private float _fadeAmount = 0.0f;

        public enum AnimType
        {
            CorrectNormal,
            CorrectHard,
            CorrectSoft,
            TooHard,
            TooSoft,
        }

        private void OnDestroy()
        {
            if (_ownedSustainController != null) Destroy(_ownedSustainController);
            var released = new HashSet<Material>();
            Release(_topMaterials);
            Release(_innerMaterials);
            Release(_secondaryTopMaterials);
            Release(_secondaryInnerMaterials);
            return;

            void Release(IEnumerable<Material> materials)
            {
                foreach (var material in materials)
                {
                    if (material == null || !released.Add(material)) continue;
                    if (Application.isPlaying) Destroy(material);
                    else DestroyImmediate(material);
                }
            }
        }

        public void Initialize(Color top, Color inner, Color particles, Color openParticles)
        {
            // Clear so Initialize is safe to re-call for live fret recoloring (e.g. the
            // settings preview's lefty flip); these lists are empty on the first call.
            _topMaterials.Clear();
            _innerMaterials.Clear();

            _originalUnityTopColor = top.ToUnityColor();
            _originalUnityInnerColor = inner.ToUnityColor();
            _originalEmissionColor = top.ToUnityColor() * 11.5f;

            // Set the top material color
            foreach (var material in ThemeBind.GetColoredMaterials())
            {
                material.color = _originalUnityTopColor;
                material.SetColor(_emissionColor, _originalEmissionColor);
                _topMaterials.Add(material);
            }

            // Set the inner material color
            foreach (var material in ThemeBind.GetInnerColoredMaterials())
            {
                material.color = _originalUnityInnerColor;
                _innerMaterials.Add(material);
            }

            // Stop any in-progress color fade/pulse so an immediate recolor
            // (e.g. a preview color profile switch) is not animating from the
            // old color afterwards.
            _colorChangeEnabled = false;
            _fadeAmount = 0f;

            // Set the particle colors
            ThemeBind.HitEffect.SetColor(particles.ToUnityColor());
            ThemeBind.OpenHitEffect.SetColor(openParticles.ToUnityColor());
            ThemeBind.SustainEffect.SetColor(particles.ToUnityColor());
            ThemeBind.PressedEffect.SetColor(particles.ToUnityColor());

            // See if certain parameters exist
            _hasPressedParam = ThemeBind.Animator.HasParameter(_pressed);
            _hasSustainParam = ThemeBind.Animator.HasParameter(_sustain);
            _hasOpenMissTrigger = ThemeBind.Animator.HasParameter(_openMiss);
        }

        // Initialize dual-half fret (primary + secondary)
        public void Initialize(Color top, Color inner, Color particles, Color openParticles,
            Color secondaryTop, Color secondaryInner, Color secondaryParticles)
        {
            // Primary half (existing logic)
            Initialize(top, inner, particles, openParticles);

            // Secondary half
            _secondaryTopMaterials.Clear();
            _secondaryInnerMaterials.Clear();
            _secondaryOriginalUnityTopColor = secondaryTop.ToUnityColor();
            _secondaryOriginalUnityInnerColor = secondaryInner.ToUnityColor();
            _secondaryOriginalEmissionColor = secondaryTop.ToUnityColor() * 11.5f;

            // Set secondary top materials
            foreach (var material in ThemeBind.GetSecondaryColoredMaterials())
            {
                material.color = _secondaryOriginalUnityTopColor;
                material.SetColor(_emissionColor, _secondaryOriginalEmissionColor);
                _secondaryTopMaterials.Add(material);
            }

            // Set secondary inner materials
            foreach (var material in ThemeBind.GetSecondaryInnerColoredMaterials())
            {
                material.SetColor(_secondaryColor, _secondaryOriginalUnityInnerColor);
                _secondaryInnerMaterials.Add(material);
            }

            // Set secondary particle colors
            if (ThemeBind.SecondaryPressedEffect != null)
            {
                ThemeBind.SecondaryPressedEffect.SetColor(secondaryParticles.ToUnityColor());
            }

            // Check for secondary pressed param
            _hasSecondaryPressedParam = ThemeBind.Animator.HasParameter(_secondaryPressed);
        }

        public void Update()
        {
            UpdateColor();
        }

        public void SetPressed(bool pressed)
        {
            SetPressed(pressed, pressed ? 1f : 0f);
        }

        public void SetPressedDrum(bool pressed, AnimType animType)
        {
            // Set the inner portion of the fret
            SetPressed(pressed, pressed ? GetFretInnerBrightnessMultiplier(animType) : 0f);
        }

        public void WhitenFretColor()
        {
            SetCymbalTint(UnityEngine.Color.white);
            foreach (var material in _topMaterials)
            {
                material.color = UnityEngine.Color.white;
                material.SetColor(_emissionColor, UnityEngine.Color.white);
            }

            foreach (var material in _innerMaterials)
            {
                material.color = UnityEngine.Color.white;
                material.SetColor(_emissionColor, UnityEngine.Color.white);
            }

            foreach (var material in _secondaryTopMaterials)
            {
                material.color = UnityEngine.Color.white;
                material.SetColor(_secondaryEmissionColor, UnityEngine.Color.white);
            }

            foreach (var material in _secondaryInnerMaterials)
            {
                material.color = UnityEngine.Color.white;
                material.SetColor(_secondaryEmissionColor, UnityEngine.Color.white);
            }

            foreach (var light in ThemeBind.HitEffect.EffectLights)
            {
                light.IsBrightened = true;
            }

            foreach (var particle in ThemeBind.HitEffect.EffectParticles)
            {
                particle.BrightenColor();
            }
        }

        public void RestoreFretColor()
        {
            SetCymbalTint(_originalCymbal);
            foreach (var material in _topMaterials)
            {
                material.color = _originalUnityTopColor;
                material.SetColor(_emissionColor, _originalEmissionColor);
            }

            foreach (var material in _innerMaterials)
            {
                material.color = _originalUnityInnerColor;
                material.SetColor(_emissionColor, _originalEmissionColor);
            }

            foreach (var material in _secondaryTopMaterials)
            {
                material.color = _secondaryOriginalUnityTopColor;
                material.SetColor(_secondaryEmissionColor, _secondaryOriginalEmissionColor);
            }

            foreach (var material in _secondaryInnerMaterials)
            {
                material.color = _secondaryOriginalUnityInnerColor;
                material.SetColor(_secondaryEmissionColor, _secondaryOriginalEmissionColor);
            }

            foreach (var light in ThemeBind.HitEffect.EffectLights)
            {
                light.IsBrightened = false;
            }

            foreach (var particle in ThemeBind.HitEffect.EffectParticles)
            {
                particle.RestoreColor();
            }
        }

        private float GetFretInnerBrightnessMultiplier(AnimType animType)
        {
            // Normal and hard should both be 1f
            return AnimType.CorrectSoft == animType ? 0f : 1f;
        }

        public void SetPressed(bool pressed, float value)
        {
            foreach (var material in _innerMaterials)
            {
                material.SetFloat(_fade, value);
            }

            if (_hasPressedParam)
            {
                ThemeBind.Animator.SetBool(_pressed, pressed);
            }

            if (pressed)
            {
                ThemeBind.PressedEffect.Play();
            }
            else
            {
                ThemeBind.PressedEffect.Stop();
            }
        }

        // Secondary half press (white fret half)
        public void SetPressedSecondary(bool pressed, float value)
        {
            foreach (var material in _secondaryInnerMaterials)
            {
                material.SetFloat(_secondaryFade, value);
            }

            if (_hasSecondaryPressedParam)
            {
                ThemeBind.Animator.SetBool(_secondaryPressed, pressed);
            }

            if (pressed && ThemeBind.SecondaryPressedEffect != null)
            {
                ThemeBind.SecondaryPressedEffect.Play();
            }
            else if (ThemeBind.SecondaryPressedEffect != null)
            {
                ThemeBind.SecondaryPressedEffect.Stop();
            }
        }

        public void PlayHitAnimation()
        {
            using var timing = TargetedPerformance.Measure(TargetedOperation.FretHitAnimator);
            ThemeBind.Animator.SetTrigger(_hit);
        }

        public void PlayCymbalHitAnimation()
        {
            using var timing = TargetedPerformance.Measure(TargetedOperation.FretHitAnimator);
            ThemeBind.Animator.SetTrigger(_cymbalHit);
        }

        public void PlayHitParticles()
        {
            using var timing = TargetedPerformance.Measure(TargetedOperation.FretHitEffect);
            ThemeBind.HitEffect.Play();
        }

        public void PlayHitParticles(System.Drawing.Color color)
        {
            using (TargetedPerformance.Measure(TargetedOperation.FretHitColor))
                ThemeBind.HitEffect.SetColor(color.ToUnityColor());
            PlayHitParticles();
        }

        public void PlayFullWidthHitParticles()
        {
            ThemeBind.OpenHitEffect.Play();
        }

        public void PlayMissAnimation()
        {
            ThemeBind.Animator.SetTrigger(_miss);
        }

        public void PlayMissParticles()
        {
            ThemeBind.MissEffect.Play();
        }

        public void PlayMissParticles(System.Drawing.Color color)
        {
            ThemeBind.MissEffect.SetColor(color.ToUnityColor());
            ThemeBind.MissEffect.Play();
        }

        public void PlayOpenMissAnimation()
        {
            if (_hasOpenMissTrigger)
            {
                ThemeBind.Animator.SetTrigger(_openMiss);
            }
            else
            {
                PlayMissAnimation();
            }
        }

        public void PlayOpenMissParticles()
        {
            ThemeBind.OpenMissEffect.Play();
        }

        /// <summary>Native hat sustain uses the underlying guitar motion, retaining drum hit overrides.</summary>
        public void UseBaseSustainAnimation()
        {
            if (_ownedSustainController != null ||
                ThemeBind.Animator.runtimeAnimatorController is not AnimatorOverrideController source ||
                source.name != "FretControllerDrums") return;
            var clips = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            source.GetOverrides(clips);
            bool changed = false;
            for (int i = 0; i < clips.Count; i++)
            {
                if (clips[i].Key.name != "FretSustained" || clips[i].Value == clips[i].Key) continue;
                clips[i] = new KeyValuePair<AnimationClip, AnimationClip>(clips[i].Key, clips[i].Key);
                changed = true;
            }
            if (!changed) return;
            _ownedSustainController = new AnimatorOverrideController(source.runtimeAnimatorController);
            _ownedSustainController.ApplyOverrides(clips);
            ThemeBind.Animator.runtimeAnimatorController = _ownedSustainController;
        }

        public void SetSustained(bool sustained)
        {
            if (sustained)
            {
                ThemeBind.SustainEffect.Play();
            }
            else
            {
                ThemeBind.SustainEffect.Stop();
            }

            if (_hasSustainParam)
            {
                ThemeBind.Animator.SetBool(_sustain, sustained);
            }
        }

        public void DimColor(bool fade = true)
        {
            if (!fade) SetCymbalTint(_inactiveColor);
            _active = false;
            _fadeDirection = false;
            _colorChangeEnabled = true;
            _fadeAmount = 0.0f;

            if (fade)
            {
                FadeColor(_fadeDuration, false, false);
            }
            else
            {
                foreach (var material in _topMaterials)
                {
                    material.color = _inactiveColor;
                }

                foreach (var material in _innerMaterials)
                {
                    material.color = _inactiveColor;
                }
            }
        }

        public void ResetColor(bool fade = false)
        {
            if (!fade) SetCymbalTint(_originalCymbal);
            _active = true;
            _fadeDirection = true;
            _colorChangeEnabled = false;
            _fadeAmount = 0.0f;

            if (fade)
            {
                FadeColor(_fadeDuration, false, true);
            }
            else
            {
                foreach (var material in _topMaterials)
                {
                    material.color = _originalUnityTopColor;
                }

                foreach (var material in _innerMaterials)
                {
                    material.color = _originalUnityInnerColor;
                }
            }
        }

        public void SetBreMode(bool breMode)
        {
            ThemeBind.SetBreMode(breMode);
        }

        /// <summary>
        /// Fades or pulses the fret color between the normal color and inactive color
        ///
        /// Note that the pulse happens only once per call, it does not continue indefinitely.
        /// </summary>
        /// <param name="duration">Length of time transition will take</param>
        /// <param name="pulse">Whether to pulse or to fade once</param>
        /// <param name="fadeDirection">True fades in, false fades out (default true)</param>
        public void FadeColor(float duration, bool pulse, bool fadeDirection = true)
        {
            if (_active && pulse)
            {
                // Can't pulse if the fret is already active
                return;
            }

            _pulseOrFade = pulse;
            _fadeDuration = duration;
            _fadeDirection = fadeDirection;
            _fadeStartTime = Time.time;
            _colorChangeEnabled = true;
            _fadeAmount = 0.0f;
        }

        // TODO: Investigate whether we should be using a MaterialPropertyBlock to set the color
        //  instead of setting the color directly so that draw call batching is possible
        //  (I think it doesn't actually matter much since there's only 10 of these materials active at a time,
        //   but every bit helps, I guess?)
        public void UpdateColor()
        {
            if (!_colorChangeEnabled)
            {
                return;
            }

            var rateAdjustment = 1; //_pulseOrFade ? 2 : 1;

            _fadeAmount += Time.deltaTime / (_fadeDuration / rateAdjustment);
            var fadeIntensity = _pulseOrFade ? _fadeAmount : ((Mathf.Cos(Mathf.PI * _fadeAmount) / 2) * -1) + 1;

            SetCymbalTint(UnityEngine.Color.Lerp(_inactiveColor, _originalCymbal,
                _fadeDirection ? fadeIntensity : 1f - fadeIntensity));
            if (_fadeDirection)
            {
                // Fading in
                foreach (var material in _topMaterials)
                {
                    material.color = UnityEngine.Color.Lerp(_inactiveColor, _originalUnityTopColor, fadeIntensity);
                }

                foreach (var material in _innerMaterials)
                {
                    material.color = UnityEngine.Color.Lerp(_inactiveColor, _originalUnityTopColor, fadeIntensity);
                }
            }
            else
            {
                // Fading out
                foreach (var material in _topMaterials)
                {
                    material.color = UnityEngine.Color.Lerp(_originalUnityTopColor, _inactiveColor, fadeIntensity);
                }

                foreach (var material in _innerMaterials)
                {
                    material.color = UnityEngine.Color.Lerp(_originalUnityTopColor, _inactiveColor, fadeIntensity);
                }
            }

            if (Time.time - _fadeStartTime >= _fadeDuration)
            {
                _colorChangeEnabled = false;
                _fadeAmount = 0.0f;
                SetCymbalTint(_fadeDirection ? _originalCymbal : _inactiveColor);

                if (_fadeDirection)
                {
                    // Fading in
                    foreach (var material in _topMaterials)
                    {
                        material.color = _originalUnityTopColor;
                    }

                    foreach (var material in _innerMaterials)
                    {
                        // Why was this _originalUnityTopColor?
                        material.color = _originalUnityInnerColor;
                    }
                }
                else
                {
                    // Fading out
                    foreach (var material in _topMaterials)
                    {
                        material.color = _inactiveColor;
                    }

                    foreach (var material in _innerMaterials)
                    {
                        material.color = _inactiveColor;
                    }
                }
            }
        }
    }
}