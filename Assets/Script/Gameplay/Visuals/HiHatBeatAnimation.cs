using UnityEngine;
using UnityEngine.Serialization;

// pattern: Imperative Shell
namespace YARG.Gameplay.Visuals
{
    [DisallowMultipleComponent]
    public sealed class HiHatBeatAnimation : MonoBehaviour
    {
        public enum MotionMode
        {
            LateralSway,
            ForwardBackRock
        }

        [SerializeField] private MotionMode _mode = MotionMode.ForwardBackRock;
        [FormerlySerializedAs("_upperPosition")]
        [SerializeField] private Vector3 _restPosition = new(0f, 0.0022f, -0.0024f);
        [SerializeField] private Quaternion _restRotation = Quaternion.Euler(-85f, -180f, 0f);
        [Tooltip("Cone tip in mesh-local coordinates, measured in the Editor.")]
        [SerializeField] private Vector3 _pivotLocal;
        [Range(0f, 10f)]
        [SerializeField] private float _swayDegrees = 2f;

        [Min(0.1f)]
        [Tooltip("The last N quarter-note beats smoothly reduce rocking to the neutral pose.")]
        [SerializeField] private float _fadeBeats = 2f;
        [SerializeField] private Vector3 _backPosition = new(0f, 0.00220106752f, -0.00300104637f);
        [SerializeField] private Quaternion _backRotation = new(3.03643048e-08f, 0.719343722f, 0.694654346f, -3.14435162e-08f);
        [SerializeField] private Vector3 _forwardPosition = new(0f, 0.00222244998f, -0.00112135429f);
        [SerializeField] private Quaternion _forwardRotation = new(2.76856529e-08f, 0.773845971f, 0.633373916f, -3.38258843e-08f);

        private GameManager _gameManager;
        private EliteDrumsNoteElement _noteElement;

        private void OnEnable()
        {
            _noteElement = GetComponentInParent<EliteDrumsNoteElement>();
            if (_gameManager == null) _gameManager = FindAnyObjectByType<GameManager>();
            LateUpdate();
        }

        private void LateUpdate()
        {
            // Serialized rest values survive pool/Star Power clones made while the source is
            // already swaying. Unbound prefab and settings preview models retain the open pose.
            if (_noteElement == null || _noteElement.NoteRef == null || _gameManager == null ||
                !_gameManager.Started || _gameManager.Chart?.SyncTrack == null)
            {
                RestorePose();
                return;
            }

            var sync = _gameManager.Chart.SyncTrack;
            double beatPhase = HiHatBeatMotion.EvaluateBeatPhase(_gameManager.VisualTime, sync);
            double arrivalPhase = HiHatBeatMotion.EvaluateBeatPhase(_noteElement.NoteRef.Time, sync);
            Vector3 position;
            Quaternion rotation;
            if (_mode == MotionMode.LateralSway)
            {
                HiHatBeatMotion.EvaluatePose(beatPhase - arrivalPhase, _swayDegrees, _restPosition, _restRotation,
                    transform.localScale, _pivotLocal, out position, out rotation);
            }
            else
            {
                HiHatBeatMotion.EvaluateRockPose(beatPhase, arrivalPhase - beatPhase, _fadeBeats,
                    _restPosition, _restRotation, _backPosition, _backRotation,
                    _forwardPosition, _forwardRotation, out position, out rotation);
            }
            transform.localPosition = position;
            transform.localRotation = rotation;
        }

        private void RestorePose()
        {
            transform.localPosition = _restPosition;
            transform.localRotation = _restRotation;
        }

        private void OnDisable()
        {
            RestorePose();
        }
    }
}
