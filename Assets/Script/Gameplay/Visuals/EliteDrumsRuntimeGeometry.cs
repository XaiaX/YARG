// pattern: Imperative Shell
using UnityEngine;
using YARG.Core.Chart;
using YARG.Core.Game;
using YARG.Gameplay.Player;
using YARG.Settings;
using System.Collections.Generic;
using System.Linq;

namespace YARG.Gameplay.Visuals
{
    /// <summary>Bounds for a native Elite gem within its resolved highway column.</summary>
    internal static class EliteDrumsRuntimeGeometry
    {
        internal const float FLAM_ANGLE = 25f;
        internal static int ModelMeasurements, PairComputations, MeshFingerprints;
        internal static double MeasurementMilliseconds, PairMilliseconds, SignatureMilliseconds;
        internal static double LastPreparationMilliseconds;

        internal static void ClearSessionCache()
        {
            EliteDrumsGeometryCache.Clear();
            ModelMeasurements = PairComputations = MeshFingerprints = 0;
            MeasurementMilliseconds = PairMilliseconds = SignatureMilliseconds = 0d;
        }

        // A managed value target lets the existing fitting math produce a complete
        // pair without temporary GameObjects or retaining either live transform.
        internal sealed class ModelPose
        {
            internal Vector3 localPosition, localScale;
            internal Quaternion localRotation;
            internal ModelPose(ModelGeometry geometry)
            {
                localPosition = geometry.Position;
                localScale = geometry.Scale;
                localRotation = geometry.Rotation;
            }
            internal void Apply(Transform model)
            {
                if (model == null) return;
                model.localPosition = localPosition;
                model.localScale = localScale;
                model.localRotation = localRotation;
            }
        }

        internal sealed class PairPlacement
        {
            internal readonly Pose Left, Right;
            internal PairPlacement(ModelPose left, ModelPose right) { Left = new Pose(left); Right = new Pose(right); }
        }

        internal readonly struct Pose
        {
            private readonly Vector3 _position, _scale;
            private readonly Quaternion _rotation;
            internal Pose(ModelPose builder)
            {
                _position = builder.localPosition;
                _scale = builder.localScale;
                _rotation = builder.localRotation;
            }
            internal void Apply(Transform model, float elementCenter)
            {
                if (model == null) return;
                model.localPosition = _position - new Vector3(elementCenter, 0f, 0f);
                model.localScale = _scale;
                model.localRotation = _rotation;
            }
        }

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
            internal readonly long Identity;
            internal Dictionary<EliteDrumsGeometryCache.PairKey, PairPlacement> PreparedPlacements;
            internal readonly Vector3 Scale;
            internal readonly Vector3 Position;
            internal readonly Quaternion Rotation;
            private readonly Vector3[] _corners;
            private readonly Vector3[] _basePlane;
            internal long ArrayBytes => 12L * (_corners.Length + _basePlane.Length);
            internal ModelGeometry(Transform model) : this(EliteDrumsGeometryCache.GetModel(model)) { }

            internal ModelGeometry(ModelGeometry source)
            {
                Identity = source.Identity;
                Scale = source.Scale;
                Position = source.Position;
                Rotation = source.Rotation;
                _corners = source._corners;
                _basePlane = source._basePlane;
                PreparedPlacements = source.PreparedPlacements;
            }

            internal ModelGeometry(Transform model, long identity)
            {
                Identity = identity;
                Scale = model.localScale;
                Position = model.localPosition;
                Rotation = model.localRotation;
                var filters = model.GetComponentsInChildren<MeshFilter>(true);
                var corners = new System.Collections.Generic.List<Vector3>(filters.Length * 8);
                foreach (var filter in filters)
                {
                    if (filter.sharedMesh == null) continue;
                    var bounds = filter.sharedMesh.bounds;
                    var matrix = EliteDrumsGeometryCache.RelativeMatrix(model, filter.transform);
                    for (int i = 0; i < 8; i++)
                    {
                        var p = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                            (i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f,
                            (i & 4) == 0 ? -1f : 1f));
                        corners.Add(matrix.MultiplyPoint3x4(p));
                    }
                }
                _corners = corners.ToArray();
                // The base child itself is tilted in the assembled model. Slicing
                // assembled Y selects only an edge, not the base contact surface.
                var vertices = new List<Vector3>();
                foreach (var filter in filters)
                {
                    var mesh = filter.sharedMesh;
                    if (mesh == null || !mesh.isReadable) continue;
                    if (filter.transform.name == "Upper Cymbal" ||
                        filter.transform.parent != null && filter.transform.parent.name == "Upper Cymbal") continue;
                    var matrix = EliteDrumsGeometryCache.RelativeMatrix(model, filter.transform);
                    var points = mesh.vertices;
                    if (mesh.name == "Cymbal Base" || filter.transform.name == "Base")
                    {
                        foreach (var point in points) vertices.Add(matrix.MultiplyPoint3x4(point));
                    }
                    else if (mesh.name.Contains("Cymbal") && mesh.subMeshCount > 2)
                    {
                        // Legacy cymbals export Note_Base as submesh 2. Do not
                        // include the colored top, even when it is lower in Y.
                        foreach (int index in mesh.GetTriangles(2))
                        {
                            var point = matrix.MultiplyPoint3x4(points[index]);
                            vertices.Add(point);
                        }
                    }
                }
                _basePlane = new HashSet<Vector3>(vertices).ToArray();
            }

            internal Vector3[] ParallelPoints(float factor, float ordinaryFactor, float angle, bool contact,
                out Vector3 position)
            {
                var scale = new Vector3(Scale.x * factor, Scale.y, Scale.z);
                var rotation = Quaternion.AngleAxis(angle, Vector3.up) * Rotation;
                var ordinary = Matrix4x4.TRS(Position, Rotation, new Vector3(Scale.x * ordinaryFactor, Scale.y, Scale.z));
                var rotated = Matrix4x4.TRS(Position, rotation, scale);
                var before = _corners.Select(ordinary.MultiplyPoint3x4).ToArray();
                var after = _corners.Select(rotated.MultiplyPoint3x4).ToArray();
                Vector3 Center(Vector3[] points) => points.Length == 0 ? Position :
                    (points.Aggregate(Vector3.Min) + points.Aggregate(Vector3.Max)) * 0.5f;
                // Each member turns about its own visible center, not the pair's
                // origin. Its ordinary visible Y/Z never changes with pair fitting.
                var correction = Center(before) - Center(after);
                position = Position + correction;
                var matrix = Matrix4x4.TRS(position, rotation, scale);
                return (contact && _basePlane.Length > 0 ? _basePlane : _corners)
                    .Select(matrix.MultiplyPoint3x4).ToArray();
            }

            internal void ResetPose(ModelPose model)
            {
                model.localPosition = Position;
                model.localScale = Scale;
                model.localRotation = Rotation;
            }

            internal void Reset(Transform model)
            {
                model.localPosition = Position;
                model.localScale = Scale;
                model.localRotation = Rotation;
            }

            internal void Extents(float factor, float angle, out float min, out float max, bool alternateLayout = false)
            {
                min = float.PositiveInfinity;
                max = float.NegativeInfinity;
                var matrix = Matrix4x4.Rotate(Quaternion.AngleAxis(alternateLayout ? -angle : angle,
                    alternateLayout ? Vector3.forward : Vector3.up)) *
                    Matrix4x4.TRS(Position, Rotation, new Vector3(Scale.x * factor, Scale.y, Scale.z));
                foreach (var corner in _corners)
                {
                    float x = matrix.MultiplyPoint3x4(corner).x;
                    min = Mathf.Min(min, x);
                    max = Mathf.Max(max, x);
                }
                if (_corners.Length == 0) min = max = 0f;
            }

            internal void ContactExtents(float factor, float angle, bool bucket, Vector3 direction,
                bool basePlane, out float min, out float max)
            {
                var points = basePlane && _basePlane.Length > 0 ? _basePlane : _corners;
                var matrix = Matrix4x4.Rotate(Quaternion.AngleAxis(bucket ? -angle : angle,
                    bucket ? Vector3.forward : Vector3.up)) *
                    Matrix4x4.TRS(Position, Rotation, new Vector3(Scale.x * factor, Scale.y, Scale.z));
                min = float.PositiveInfinity;
                max = float.NegativeInfinity;
                foreach (var point in points)
                {
                    float projection = Vector3.Dot(matrix.MultiplyPoint3x4(point), direction);
                    min = Mathf.Min(min, projection);
                    max = Mathf.Max(max, projection);
                }
                if (points.Length == 0) min = max = 0f;
            }

            internal Vector3 ContactPoint(float factor, float angle, bool bucket, Vector3 direction, bool cymbal, bool maximum)
            {
                var points = cymbal && _basePlane.Length > 0 ? _basePlane : _corners;
                var matrix = Matrix4x4.Rotate(Quaternion.AngleAxis(bucket ? -angle : angle,
                    bucket ? Vector3.forward : Vector3.up)) *
                    Matrix4x4.TRS(Position, Rotation, new Vector3(Scale.x * factor, Scale.y, Scale.z));
                float extreme = maximum ? float.NegativeInfinity : float.PositiveInfinity;
                var contact = Vector3.zero;
                int count = 0;
                foreach (var point in points)
                {
                    var transformed = matrix.MultiplyPoint3x4(point);
                    float projection = Vector3.Dot(transformed, direction);
                    if (maximum ? projection > extreme + 0.000001f : projection < extreme - 0.000001f)
                    {
                        extreme = projection;
                        contact = transformed;
                        count = 1;
                    }
                    else if (Mathf.Abs(projection - extreme) <= 0.000001f)
                    {
                        contact += transformed;
                        count++;
                    }
                }
                return count > 0 ? contact / count : Vector3.zero;
            }

            internal void Apply(Transform model, float factor, float angle, float origin, bool alternateLayout = false)
            {
                var pose = new ModelPose(this);
                ApplyPose(pose, factor, angle, origin, alternateLayout);
                pose.Apply(model);
            }

            internal void ApplyPose(ModelPose model, float factor, float angle, float origin, bool alternateLayout = false)
            {
                // Forward-axis tilt lowers the inner edges into a shallow bowl.
                var rotation = Quaternion.AngleAxis(alternateLayout ? -angle : angle,
                    alternateLayout ? Vector3.forward : Vector3.up);
                model.localScale = new Vector3(Scale.x * factor, Scale.y, Scale.z);
                model.localRotation = rotation * Rotation;
                model.localPosition = rotation * Position + new Vector3(origin, 0f, 0f);
            }
        }

        private static List<Vector2> Hull(IEnumerable<Vector2> source)
        {
            var points = source.Distinct().OrderBy(p => p.x).ThenBy(p => p.y).ToArray();
            float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            var hull = new List<Vector2>();
            foreach (var point in points)
            {
                while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], point) <= 0f) hull.RemoveAt(hull.Count - 1);
                hull.Add(point);
            }
            int lower = hull.Count;
            for (int i = points.Length - 2; i >= 0; i--)
            {
                while (hull.Count > lower && Cross(hull[hull.Count - 2], hull[hull.Count - 1], points[i]) <= 0f) hull.RemoveAt(hull.Count - 1);
                hull.Add(points[i]);
            }
            if (hull.Count > 1) hull.RemoveAt(hull.Count - 1);
            return hull;
        }

        internal static float GetLateralContact(Vector3[] left, Vector3[] right)
        {
            var a = Hull(left.Select(p => new Vector2(p.x, p.z)));
            var b = Hull(right.Select(p => new Vector2(p.x, p.z)));
            // Configuration obstacle A-B: a relative translation is penetrating
            // exactly while it is inside this convex polygon. Intersect Z=0,
            // rather than projecting on a normal or using horizontal AABBs.
            var obstacle = Hull(a.SelectMany(p => b.Select(q => p - q)));
            float contact = 0f;
            for (int i = 0; i < obstacle.Count; i++)
            {
                var p = obstacle[i];
                var q = obstacle[(i + 1) % obstacle.Count];
                if (Mathf.Abs(p.y) < 0.000001f) contact = Mathf.Max(contact, p.x);
                if ((p.y < 0f && q.y > 0f) || (p.y > 0f && q.y < 0f))
                    contact = Mathf.Max(contact, Mathf.Lerp(p.x, q.x, -p.y / (q.y - p.y)));
            }
            return contact;
        }

        internal static float GetParallelCymbalSeparation(float outline, float leftWidth, float rightWidth)
        {
            // Deliberate visual overlap rather than triangle-surface matching.
            // Preserve at least 90% of the outline separation for thin/custom rims.
            const float BASE_WIDTH_OVERLAP = 0.04f;
            const float MAX_SEPARATION_OVERLAP = 0.1f;
            float overlap = Mathf.Min(Mathf.Min(leftWidth, rightWidth) * BASE_WIDTH_OVERLAP,
                outline * MAX_SEPARATION_OVERLAP);
            return Mathf.Max(0f, outline - Mathf.Max(0f, overlap));
        }

        private static void FitParallel(ModelGeometry left, ModelPose leftModel, ModelGeometry right, ModelPose rightModel,
            int columns, float laneCenter, float laneWidth, float elementCenter, float factor, float angle, bool cymbal)
        {
            float ordinaryFactor = GetAuthoredLaneScale(laneWidth, TrackPlayer.TRACK_WIDTH / 5f);
            var lc = left.ParallelPoints(factor, ordinaryFactor, angle, cymbal, out var leftPosition);
            var rc = right.ParallelPoints(factor, ordinaryFactor, angle, cymbal, out var rightPosition);
            float contact = GetLateralContact(lc, rc);
            if (cymbal)
            {
                float leftWidth = lc.Length == 0 ? 0f : lc.Max(p => p.x) - lc.Min(p => p.x);
                float rightWidth = rc.Length == 0 ? 0f : rc.Max(p => p.x) - rc.Min(p => p.x);
                contact = GetParallelCymbalSeparation(contact, leftWidth, rightWidth);
            }
            var l = left.ParallelPoints(factor, ordinaryFactor, angle, false, out _);
            var r = right.ParallelPoints(factor, ordinaryFactor, angle, false, out _);
            float min = Mathf.Min(l.Length == 0 ? 0f : l.Min(p => p.x), (r.Length == 0 ? 0f : r.Min(p => p.x)) + contact);
            float max = Mathf.Max(l.Length == 0 ? 0f : l.Max(p => p.x), (r.Length == 0 ? 0f : r.Max(p => p.x)) + contact);
            var lp = leftPosition;
            var rp = rightPosition;
            float separation = contact;
            float halfPair = (max - min) * 0.5f;
            float outer = (TrackPlayer.TRACK_WIDTH - laneWidth) * 0.5f;
            float midpoint = columns > 1 && outer > 0f ? laneCenter / outer * (TrackPlayer.TRACK_WIDTH * 0.5f - halfPair) : 0f;
            float origin = midpoint - (min + max) * 0.5f - elementCenter;
            void Apply(ModelGeometry geometry, ModelPose model, Vector3 position, float x)
            {
                if (model == null) return;
                model.localScale = new Vector3(geometry.Scale.x * factor, geometry.Scale.y, geometry.Scale.z);
                model.localRotation = Quaternion.AngleAxis(angle, Vector3.up) * geometry.Rotation;
                model.localPosition = position + new Vector3(x, 0f, 0f);
            }
            Apply(left, leftModel, lp, origin);
            Apply(right, rightModel, rp, origin + separation);
        }

        internal static void FitPair(ModelGeometry left, Transform leftModel, ModelGeometry right,
            Transform rightModel, int columns, float laneCenter, float laneWidth, float elementCenter,
            bool regularFlam, EliteFlamLayout layout = EliteFlamLayout.V, bool cymbalModel = false,
            bool lefty = false)
        {
            if (!float.IsFinite(laneCenter) || !float.IsFinite(laneWidth) || laneWidth <= 0f ||
                !float.IsFinite(elementCenter))
            {
                if (leftModel != null) left.Reset(leftModel);
                if (rightModel != null) right.Reset(rightModel);
                return;
            }
            // Store highway-local poses, with the lane center in the key. The
            // note element offset is applied afterward; world positions are absent.
            var key = new EliteDrumsGeometryCache.PairKey(left.Identity, right.Identity, columns,
                laneCenter, laneWidth, regularFlam, layout, cymbalModel, lefty);
            PairPlacement placement;
            if (left.PreparedPlacements == null || !left.PreparedPlacements.TryGetValue(key, out placement))
            {
                if (!EliteDrumsGeometryCache.TryGetPair(key, out placement))
                {
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    var l = new ModelPose(left);
                    var r = new ModelPose(right);
                    FitPairUncached(left, l, right, r, columns, laneCenter, laneWidth, 0f,
                        regularFlam, layout, cymbalModel, lefty);
                    placement = new PairPlacement(l, r);
                    PairComputations++;
                    PairMilliseconds += clock.Elapsed.TotalMilliseconds;
                    EliteDrumsGeometryCache.AddPair(key, placement);
                }
                left.PreparedPlacements?.TryAdd(key, placement);
            }
            placement.Left.Apply(leftModel, elementCenter);
            placement.Right.Apply(rightModel, elementCenter);
        }

        private static void FitPairUncached(ModelGeometry left, ModelPose leftModel, ModelGeometry right,
            ModelPose rightModel, int columns, float laneCenter, float laneWidth, float elementCenter,
            bool regularFlam, EliteFlamLayout layout, bool cymbalModel, bool lefty)
        {
            if (!float.IsFinite(laneCenter) || !float.IsFinite(laneWidth) || laneWidth <= 0f ||
                !float.IsFinite(elementCenter))
            {
                if (leftModel != null) left.ResetPose(leftModel);
                if (rightModel != null) right.ResetPose(rightModel);
                return;
            }
            float factor = GetAuthoredLaneScale(laneWidth, TrackPlayer.TRACK_WIDTH / 5f) * GetPairWidthFactor(columns);
            bool bucket = regularFlam && layout == EliteFlamLayout.Bucket;
            bool parallel = regularFlam && layout == EliteFlamLayout.Parallel;
            float leftAngle = regularFlam ? FLAM_ANGLE : 0f;
            float rightAngle = -leftAngle;
            if (parallel)
            {
                FitParallel(left, leftModel, right, rightModel, columns, laneCenter, laneWidth, elementCenter,
                    factor, lefty ? -FLAM_ANGLE : FLAM_ANGLE, cymbalModel);
                return;
            }

            if (!parallel && !(regularFlam && cymbalModel))
            {
                // Preserve the original rectangular drum clearance and flat pairs.
                left.Extents(factor, leftAngle, out float lMin, out float lMax);
                right.Extents(factor, rightAngle, out float rMin, out float rMax);
                GetPairPlacement(lMin, lMax, rMin, rMax, laneCenter, laneWidth,
                    TrackPlayer.TRACK_WIDTH, columns, out float lx, out float rx);
                if (leftModel != null) left.ApplyPose(leftModel, factor, leftAngle, lx - elementCenter, bucket);
                if (rightModel != null) right.ApplyPose(rightModel, factor, rightAngle, rx - elementCenter, bucket);
                return;
            }

            // V/Bucket cymbals meet at their actual base features.
            Vector3 direction = Vector3.right;
            var leftContact = left.ContactPoint(factor, leftAngle, bucket, direction, cymbalModel, true);
            var rightContact = right.ContactPoint(factor, rightAngle, bucket, direction, cymbalModel, false);
            // Matching support planes alone can leave bases staggered and disjoint.
            // Align their actual facing features in Y/Z as well as X. The facing
            // supports still prevent penetration along the separating normal.
            Vector3 separation = leftContact - rightContact;
            left.Extents(factor, leftAngle, out float leftMin, out float leftMax, bucket);
            right.Extents(factor, rightAngle, out float rightMin, out float rightMax, bucket);
            float min = Mathf.Min(leftMin, rightMin + separation.x);
            float max = Mathf.Max(leftMax, rightMax + separation.x);
            float halfPair = (max - min) * 0.5f;
            float outerCenter = (TrackPlayer.TRACK_WIDTH - laneWidth) * 0.5f;
            float midpoint = columns > 1 && outerCenter > 0f
                ? laneCenter / outerCenter * (TrackPlayer.TRACK_WIDTH * 0.5f - halfPair) : 0f;
            float origin = midpoint - (min + max) * 0.5f - elementCenter;
            float longitudinalCenter = 0f;
            if (leftModel != null)
            {
                left.ApplyPose(leftModel, factor, leftAngle, origin, bucket);
                leftModel.localPosition += new Vector3(0f, -separation.y * 0.5f, longitudinalCenter - separation.z * 0.5f);
            }
            if (rightModel != null)
            {
                right.ApplyPose(rightModel, factor, rightAngle, origin + separation.x, bucket);
                rightModel.localPosition += new Vector3(0f, separation.y * 0.5f, longitudinalCenter + separation.z * 0.5f);
            }
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
        internal static int GetPedalRegion(int visualColumn, int laneCount, float highwayWidth,
            out float center, out float width)
        {
            center = width = 0f;
            if (laneCount <= 0 || !float.IsFinite(highwayWidth) || highwayWidth <= 0f) return 0;
            int column = Mathf.Clamp(visualColumn, 0, laneCount - 1);
            int first = Mathf.Max(0, column - 1);
            int last = Mathf.Min(laneCount - 1, column + 1);
            int sections = last - first + 1;
            float laneWidth = highwayWidth / laneCount;
            center = (first + last + 1) * 0.5f * laneWidth - highwayWidth * 0.5f;
            width = sections * laneWidth;
            return sections;
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
                return;
            }
            if (note.Pad == (int) EliteDrumNote.EliteDrumPad.HatPedal)
            {
                GetPedalRegion(position, layout.Lanes.Count, TrackPlayer.TRACK_WIDTH, out center, out width);
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
