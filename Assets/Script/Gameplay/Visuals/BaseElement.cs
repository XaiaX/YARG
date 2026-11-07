// pattern: Imperative Shell
using YARG.Core.Diagnostics;

namespace YARG.Gameplay.Visuals
{
    public abstract class BaseElement : GameplayBehaviour, IPoolable
    {
        public Pool ParentPool { get; set; }

        /// <summary>
        /// The time at which the element resides.
        /// </summary>
        public abstract double ElementTime { get; }
        /// <summary>
        /// How many move units the element should be kept for past the default remove point.
        /// Should be positive.
        /// </summary>
        protected virtual float RemovePointOffset => 0f;

        protected bool Initialized { get; private set; }
        private bool _hasBeenEnabled;

        protected override void GameplayAwake()
        {
            // Hide everything at the start
            HideElement();
        }

        public void EnableFromPool()
        {
            using var timing = TargetedPerformance.MeasureWithContext(TargetedOperation.ElementEnable,
                TargetedPerformance.Enabled ? GetInstanceID() : 0);
            if (!_hasBeenEnabled) TargetedPerformance.Current?.RecordFirstElementEnable();
            _hasBeenEnabled = true;
            using (TargetedPerformance.Measure(TargetedOperation.ElementSetActive))
                gameObject.SetActive(true);

            using (TargetedPerformance.Measure(TargetedOperation.ElementInitialize))
                InitializeElement();
            Initialized = true;

            // Force update the position once just in case to prevent flickering
            using (TargetedPerformance.Measure(TargetedOperation.ElementFirstVisualUpdate))
                UpdateVisualElement();
        }

        protected abstract void InitializeElement();
        protected abstract void UpdateElement();
        protected abstract void HideElement();

        protected abstract bool UpdateElementPosition();

        protected void LateUpdate()
        {
            UpdateVisualElement();
        }

        private void UpdateVisualElement()
        {
            // Skip if not initialized
            if (!Initialized) return;

            if (!UpdateElementPosition())
            {
                return;
            }

            UpdateElement();
        }

        public void DisableIntoPool()
        {
            HideElement();

            Initialized = false;
            gameObject.SetActive(false);
        }
    }
}
