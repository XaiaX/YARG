// pattern: Imperative Shell
using System.Runtime.CompilerServices;
using UnityEngine;
using YARG.Gameplay.Player;

namespace YARG.Gameplay.Visuals
{
    public abstract class TrackElement<TPlayer> : BaseElement
        where TPlayer : TrackPlayer
    {
        protected const float REMOVE_POINT = -4f;

        protected TPlayer Player { get; private set; }

        /// <summary>
        /// Whether or not the player has lefty flip on.
        /// </summary>
        protected bool LeftyFlip => Player.Player.Profile.LeftyFlip;

        /// <summary>
        /// The lefty flip position multiplier. <c>1</c> if lefty flip is off, <c>-1</c> if it is on.
        /// This is not automatically accounted for.
        /// </summary>
        protected float LeftyFlipMultiplier => LeftyFlip ? -1f : 1f;

        protected override GameManager ResolveGameManager()
        {
            Player = GetComponentInParent<TPlayer>();
            // Pools are owned by an initialized player. A scene-wide search per fresh
            // note scales with the whole scene and defers that cost until first use.
            // Keep the existing fallback for unusual startup order or standalone elements.
            return Player != null && Player.GameplayContext != null
                ? Player.GameplayContext
                : base.ResolveGameManager();
        }

        protected float GetZPositionAtTime(double time)
        {
            // Calibration is not taken into consideration here, as that is instead handled in more
            // critical areas such as the game manager and players

            return TrackPlayer.STRIKE_LINE_POS                          // Shift origin to the strike line
                + (float) (time - GameManager.VisualTime) // Get time of note relative to now
                * Player.NoteSpeed;                                  // Adjust speed (units/s)
        }

        protected override bool UpdateElementPosition()
        {
            // Calibration is not taken into consideration here, as that is instead handled in more
            // critical areas such as the game manager and players
            float z =
                TrackPlayer.STRIKE_LINE_POS                      // Shift origin to the strike line
                + (float) (ElementTime - GameManager.VisualTime) // Get time of note relative to now
                * Player.NoteSpeed;                              // Adjust speed (units/s)

            var cacheTransform = transform;
            cacheTransform.localPosition = cacheTransform.localPosition.WithZ(z);

            if (z < REMOVE_POINT - RemovePointOffset)
            {
                ParentPool.Return(this);
                return false;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected static float GetElementX(float index, int subdivisions)
        {
            return TrackPlayer.TRACK_WIDTH / subdivisions * (index + 1) - TrackPlayer.TRACK_WIDTH / 2f - 1f / subdivisions;
        }
    }
}
