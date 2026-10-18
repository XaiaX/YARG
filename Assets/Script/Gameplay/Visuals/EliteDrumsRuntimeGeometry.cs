using UnityEngine;
using YARG.Core.Chart;
using YARG.Core.Game;
using YARG.Gameplay.Player;

namespace YARG.Gameplay.Visuals
{
    /// <summary>Bounds for a native Elite gem within its resolved highway column.</summary>
    internal static class EliteDrumsRuntimeGeometry
    {
        internal const float FLAM_ANGLE = 25f;

        internal static float GetPairWidthFactor(int columns) => columns > 1 ? (columns - 1f) / columns : 0.5f;

        internal static void GetPairPlacement(float leftMin, float leftMax, float rightMin, float rightMax,
            float laneCenter, float laneWidth, float highwayWidth, int columns,
            out float leftOrigin, out float rightOrigin)
        {
            leftOrigin = rightOrigin = 0f;
            if (!float.IsFinite(leftMin) || !float.IsFinite(leftMax) || !float.IsFinite(rightMin) ||
                !float.IsFinite(rightMax) || !float.IsFinite(laneCenter) || !float.IsFinite(laneWidth) ||
                !float.IsFinite(highwayWidth) || laneWidth <= 0f || highwayWidth <= 0f) return;
            float halfPair = (leftMax - leftMin + rightMax - rightMin) * 0.5f;
            float halfHighway = highwayWidth * 0.5f;
            float outerCenter = halfHighway - laneWidth * 0.5f;
            float midpoint = columns > 1 && outerCenter > 0f
                ? laneCenter / outerCenter * (halfHighway - halfPair) : 0f;
            if (!float.IsFinite(midpoint) || !float.IsFinite(halfPair) ||
                leftMax < leftMin || rightMax < rightMin) return;
            float seam = midpoint - halfPair + leftMax - leftMin;
            leftOrigin = seam - leftMax;
            rightOrigin = seam - rightMin;
        }

        // Captured once per assembled model (including normal/SP variants). Mesh
        // corners are in the group's parent space, so authored pivots/rotations,
        // child highlights and caps are preserved without world AABB projection.
        internal sealed class ModelGeometry
        {
            internal readonly Vector3 Scale;
            internal readonly Vector3 Position;
            internal readonly Quaternion Rotation;
            private readonly Vector3[] _corners;

            internal ModelGeometry(Transform model)
            {
                Scale = model.localScale;
                Position = model.localPosition;
                Rotation = model.localRotation;
                var filters = model.GetComponentsInChildren<MeshFilter>(true);
                var corners = new System.Collections.Generic.List<Vector3>(filters.Length * 8);
                foreach (var filter in filters)
                {
                    if (filter.sharedMesh == null) continue;
                    var bounds = filter.sharedMesh.bounds;
                    var matrix = model.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                    for (int i = 0; i < 8; i++)
                    {
                        var p = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                            (i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f,
                            (i & 4) == 0 ? -1f : 1f));
                        corners.Add(matrix.MultiplyPoint3x4(p));
                    }
                }
                _corners = corners.ToArray();
            }

            internal void Reset(Transform model)
            {
                model.localPosition = Position;
                model.localScale = Scale;
                model.localRotation = Rotation;
            }

            internal void Extents(float factor, float angle, out float min, out float max)
            {
                min = float.PositiveInfinity;
                max = float.NegativeInfinity;
                var matrix = Matrix4x4.Rotate(Quaternion.AngleAxis(angle, Vector3.up)) *
                    Matrix4x4.TRS(Position, Rotation, new Vector3(Scale.x * factor, Scale.y, Scale.z));
                foreach (var corner in _corners)
                {
                    float x = matrix.MultiplyPoint3x4(corner).x;
                    min = Mathf.Min(min, x);
                    max = Mathf.Max(max, x);
                }
                if (_corners.Length == 0) min = max = 0f;
            }

            internal void Apply(Transform model, float factor, float angle, float origin)
            {
                var rotation = Quaternion.AngleAxis(angle, Vector3.up);
                model.localScale = new Vector3(Scale.x * factor, Scale.y, Scale.z);
                model.localRotation = rotation * Rotation;
                model.localPosition = rotation * Position + new Vector3(origin, 0f, 0f);
            }
        }

        internal static void FitPair(ModelGeometry left, Transform leftModel, ModelGeometry right,
            Transform rightModel, int columns, float laneCenter, float laneWidth, float elementCenter,
            bool regularFlam)
        {
            float factor = GetAuthoredLaneScale(laneWidth, TrackPlayer.TRACK_WIDTH / 5f) * GetPairWidthFactor(columns);
            // Positive left / negative right puts the seam toward the receptor
            // (-Z), a V rather than a roof when +Z points up the highway.
            float angle = regularFlam ? -FLAM_ANGLE : 0f;
            left.Extents(factor, -angle, out float leftMin, out float leftMax);
            right.Extents(factor, angle, out float rightMin, out float rightMax);
            GetPairPlacement(leftMin, leftMax, rightMin, rightMax, laneCenter, laneWidth,
                TrackPlayer.TRACK_WIDTH, columns, out float leftX, out float rightX);
            if (leftModel != null) left.Apply(leftModel, factor, -angle, leftX - elementCenter);
            if (rightModel != null) right.Apply(rightModel, factor, angle, rightX - elementCenter);
        }

        internal static EliteDrumNote GetCollisionPartner(EliteDrumsPlayer player, EliteDrumNote note)
        {
            if (!EliteDrumsNoteElement.IsHandFlamPad(note.Pad)) return null;
            var layout = player.Layout;
            if (!layout.SourceToLane.TryGetValue(EliteDrumsPlayer.GetLayoutSource(note.Pad), out int lane)) return null;
            if (layout.Lanes[lane].Kind != EliteDrumsLayoutSlotKind.Ordinary ||
                layout.Lanes[lane].EffectiveIds.Count != 2) return null;
            foreach (var member in note.ParentOrSelf.AllNotes)
            {
                if (member.Pad == note.Pad || member.IsInvisibleTerminator ||
                    EliteDrumsPlayer.IsCymbal(member.Pad) == EliteDrumsPlayer.IsCymbal(note.Pad)) continue;
                if (layout.SourceToLane.TryGetValue(EliteDrumsPlayer.GetLayoutSource(member.Pad), out int other) && lane == other)
                    return member;
            }
            return null;
        }

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
