// pattern: Imperative Shell
using UnityEngine;
using YARG.Gameplay.Visuals;

namespace YARG.Gameplay
{
    public partial class GameManager
    {
        // Song-owned: no static cache can retain a destroyed gameplay scene.
        private readonly CymbalSpinClock _cymbalSpinClock = new();

        internal Quaternion GetCymbalRotation(float beatsPerRevolution, bool leftyFlip) =>
            _cymbalSpinClock.Evaluate(Chart.SyncTrack, VisualTime, beatsPerRevolution, leftyFlip);
    }
}