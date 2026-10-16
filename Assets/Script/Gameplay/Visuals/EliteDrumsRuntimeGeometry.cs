using UnityEngine;
using YARG.Core.Chart;
using YARG.Core.Game;
using YARG.Gameplay.Player;

namespace YARG.Gameplay.Visuals
{
    /// <summary>Bounds for a native Elite gem within its resolved highway column.</summary>
    internal static class EliteDrumsRuntimeGeometry
    {
        // Authored Elite note models are sized for the five-column highway. Apply
        // one proportional scale to preserve their theme-authored relative sizes.
        internal static float GetAuthoredLaneScale(float renderedWidth, float fiveLaneWidth) =>
            fiveLaneWidth > 0f ? renderedWidth / fiveLaneWidth : 1f;

        // Stomp and Splash artwork leads by one chart tick visually; note timing,
        // engine state, and sustain endpoints continue to use the authored note.
        internal static float GetVisualLeadOffset(float visualSpeed, double tickDuration) =>
            -(float) (visualSpeed * tickDuration);

        internal static double GetNextTickDuration(YARG.Core.Chart.SyncTrack syncTrack, uint tick)
        {
            if (syncTrack == null || tick == uint.MaxValue) return 0d;
            double duration = syncTrack.TickToTime(tick + 1) - syncTrack.TickToTime(tick);
            return duration > 0d ? duration : 0d;
        }
        internal static void GetRegion(EliteDrumsPlayer player, EliteDrumNote note, bool offset,
            out float center, out float width)
        {
            var layout = player.Layout;
            width = layout.GetLaneWidth(TrackPlayer.TRACK_WIDTH);
            int position = player.GetVisualPosition(note);
            center = (position + 0.5f) * width - TrackPlayer.TRACK_WIDTH / 2f;
            if (note.Pad == (int) EliteDrumNote.EliteDrumPad.Wildcard)
            {
                center = 0;
                width = TrackPlayer.TRACK_WIDTH;
                return;
            }
            if (note.Pad != (int) EliteDrumNote.EliteDrumPad.HatPedal &&
                EliteDrumsPlayer.IsFootPad(note.Pad) && player.UsesKickBars)
            {
                center = 0;
                width = TrackPlayer.TRACK_WIDTH;
                if (player.UsesHistoricalPedalBars && EliteDrumsNoteElement.TryGetPairedPedalKickLayout(note,
                    player.Player.Profile.LeftyFlip, out float pairedCenter, out float pairedWidth))
                {
                    center = pairedCenter;
                    width = pairedWidth;
                }
                return;
            }
            if (note.Pad == (int) EliteDrumNote.EliteDrumPad.HatPedal)
            {
                center = Mathf.Clamp(center, -TrackPlayer.TRACK_WIDTH / 2f + 1.5f * width,
                    TrackPlayer.TRACK_WIDTH / 2f - 1.5f * width);
                width *= 3f;
                return;
            }

            bool cymbal = EliteDrumsPlayer.IsCymbal(note.Pad);
            string source = EliteDrumsPlayer.GetLayoutSource(note.Pad);
            if (!layout.SourceToLane.TryGetValue(source, out int laneIndex)) return;
            var lane = layout.Lanes[laneIndex];
            center = layout.GetLaneX(laneIndex, TrackPlayer.TRACK_WIDTH, player.Player.Profile.LeftyFlip);
            if (lane.Kind != EliteDrumsLayoutSlotKind.Ordinary || lane.EffectiveIds.Count != 2) return;
            bool paired = false;
            foreach (var member in note.ParentOrSelf.AllNotes)
            {
                if (member.Pad == note.Pad || member.IsInvisibleTerminator ||
                    EliteDrumsPlayer.IsCymbal(member.Pad) == cymbal) continue;
                if (layout.SourceToLane.TryGetValue(EliteDrumsPlayer.GetLayoutSource(member.Pad),
                    out int memberLane) && memberLane == laneIndex)
                {
                    paired = true;
                    break;
                }
            }
            if (!paired) return;
            GetPairRegion(center, width, cymbal, player.Player.Profile.LeftyFlip, offset, out center, out width);
        }

        public static void GetPairRegion(float laneCenter, float columnWidth, bool cymbal, bool lefty,
            bool offset, out float center, out float width)
        {
            float mirror = lefty ? -1f : 1f;
            float leftBound = Mathf.Max(-TrackPlayer.TRACK_WIDTH / 2f, laneCenter - columnWidth / 2f);
            float rightBound = Mathf.Min(TrackPlayer.TRACK_WIDTH / 2f, laneCenter + columnWidth / 2f);
            width = offset ? Mathf.Min(laneCenter - leftBound, rightBound - laneCenter) : columnWidth;
            center = offset ? laneCenter + (cymbal ? 1f : -1f) * mirror * width / 2f : laneCenter;
        }
    }
}
