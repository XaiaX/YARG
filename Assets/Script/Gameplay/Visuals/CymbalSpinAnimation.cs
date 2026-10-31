using UnityEngine;
using YARG.Gameplay.Player;

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

        private GameManager _gameManager;
        private EliteDrumsNoteElement _noteElement;
        private EliteDrumsPlayer _player;

        public Transform Rotor => _rotor;
        public Bounds SweptLocalBounds => _sweptBounds;

        private void OnEnable()
        {
            _noteElement = GetComponentInParent<EliteDrumsNoteElement>();
            _player = GetComponentInParent<EliteDrumsPlayer>(true);
            if (_gameManager == null) _gameManager = FindAnyObjectByType<GameManager>();
            // The serialized rotor rest is identity even when a pool/template was
            // cloned from an active source whose rotor was already turning.
            RestorePose();
            LateUpdate();
        }

        private void LateUpdate()
        {
            if (_noteElement == null || _noteElement.NoteRef == null || _gameManager == null ||
                !_gameManager.Started || _gameManager.Chart?.SyncTrack == null)
            {
                RestorePose();
                return;
            }
            ApplyBeatPhase(HiHatBeatMotion.EvaluateBeatPhase(_gameManager.VisualTime, _gameManager.Chart.SyncTrack));
        }

        public void ApplyBeatPhase(double beat)
        {
            bool leftyFlip = _player != null && _player.Player?.Profile?.LeftyFlip == true;
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