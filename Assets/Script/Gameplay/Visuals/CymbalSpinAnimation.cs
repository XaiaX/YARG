using UnityEngine;

// pattern: Imperative Shell
namespace YARG.Gameplay.Visuals
{
    /// <summary>
    /// Spins a canonical rotor between the authored deformation and its inverse.
    /// The hierarchy handles normals and preserves the model's apparent footprint.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CymbalSpinAnimation : MonoBehaviour
    {
        [SerializeField] private Transform _rotor;
        [Min(.1f)]
        [Tooltip("Quarter-note beats for one full revolution.")]
        [SerializeField] private float _beatsPerRevolution = CymbalSpinMotion.DEFAULT_BEATS_PER_REVOLUTION;
        [Tooltip("Full-spin envelope in this assembly's local space, authored once for fitting.")]
        [SerializeField] private Bounds _sweptBounds;

        // Set while assembling the note template. Unity remaps this reference to
        // each pooled clone, including its inactive SP and split-flam models.
        [SerializeField, HideInInspector] private EliteDrumsNoteElement _noteElement;

        internal void BindOwner(EliteDrumsNoteElement owner) => _noteElement = owner;

        public Transform Rotor => _rotor;
        public Bounds SweptLocalBounds => _sweptBounds;

        private void OnEnable()
        {
            // The serialized rotor rest is identity even when a pool/template was
            // cloned from an active source whose rotor was already turning.
            RestorePose();
            LateUpdate();
        }

        private void LateUpdate()
        {
            if (_rotor == null) return;
            _rotor.localRotation = _noteElement != null
                ? _noteElement.GetCymbalRotation(_beatsPerRevolution)
                : Quaternion.identity;
        }

        // Explicit phase sampling is also used by the authoring previews.
        public void ApplyBeatPhase(double beat)
        {
            bool leftyFlip = _noteElement != null && _noteElement.CymbalLeftyFlip;
            if (_rotor != null)
                _rotor.localRotation = Quaternion.AngleAxis(
                    (leftyFlip ? 1f : -1f) * CymbalSpinMotion.EvaluateAngle(beat, _beatsPerRevolution), Vector3.forward);
        }

        internal static Quaternion RestRotationOf(Transform child)
        {
            var animation = child.GetComponentInParent<CymbalSpinAnimation>(true);
            return animation != null && animation._rotor == child ? Quaternion.identity : child.localRotation;
        }

        private void RestorePose()
        {
            if (_rotor != null) _rotor.localRotation = Quaternion.identity;
        }

        private void OnDisable() => RestorePose();
    }
}