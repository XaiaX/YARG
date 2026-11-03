// pattern: Imperative Shell
using UnityEngine;

namespace YARG.Gameplay.Visuals
{
    /// <summary>Preserves the authored housings while fitting each split kick beam to its exposed endpoint.</summary>
    public sealed class ClassicKickFlamVisual : MonoBehaviour
    {
        // Left outer housing, inner housing, beam; then the same three on the right.
        [SerializeField] private MeshFilter[] _parts;
        private Part[] _authored;

        private readonly struct Part
        {
            internal readonly MeshFilter Mesh;
            internal readonly Vector3 Position, Scale;
            internal readonly float Width, Center, Minimum, Maximum;
            internal Part(MeshFilter mesh, Transform root)
            {
                Mesh = mesh;
                Position = mesh.transform.localPosition;
                Scale = mesh.transform.localScale;
                var bounds = BoundsIn(root, mesh);
                Width = bounds.size.x;
                Center = bounds.center.x;
                Minimum = bounds.min.x;
                Maximum = bounds.max.x;
            }
        }

        public void SetAuthoredParts(MeshFilter[] parts)
        {
            _parts = parts;
            _authored = null;
        }

        public bool Configure(Transform highwaySpace, float highwayWidth, float leftInner, float rightInner)
        {
            if (highwaySpace == null || !float.IsFinite(highwayWidth) || highwayWidth <= 0f ||
                !float.IsFinite(leftInner) || !float.IsFinite(rightInner) || _parts == null || _parts.Length != 6)
                return false;
            if (_authored == null)
            {
                var authored = new Part[6];
                for (int i = 0; i < authored.Length; i++)
                {
                    if (_parts[i] == null || _parts[i].sharedMesh == null) return false;
                    authored[i] = new Part(_parts[i], transform);
                    if (authored[i].Width <= 0f) return false;
                }
                _authored = authored;
            }
            ResetParts();
            float LocalX(float value) => transform.InverseTransformPoint(
                highwaySpace.TransformPoint(new Vector3(value, 0f, 0f))).x;
            float left = LocalX(-highwayWidth * .5f), right = LocalX(highwayWidth * .5f);
            float leftCap = LocalX(leftInner), rightCap = LocalX(rightInner);
            float leftOuter = left + _authored[0].Width * .5f;
            float rightOuter = right - _authored[3].Width * .5f;
            // Preserve the authored beam/socket overlap at each housing, instead
            // of opening a seam when the visible endpoints move to empty columns.
            float leftBeamMin = leftOuter + _authored[2].Minimum - _authored[0].Center;
            float leftBeamMax = leftCap + _authored[2].Maximum - _authored[1].Center;
            float rightBeamMin = rightCap + _authored[5].Minimum - _authored[4].Center;
            float rightBeamMax = rightOuter + _authored[5].Maximum - _authored[3].Center;
            if (left >= right || leftCap >= rightCap || leftBeamMax <= leftBeamMin || rightBeamMax <= rightBeamMin)
                return false;
            Fit(0, leftOuter, 1f);
            Fit(1, leftCap, 1f);
            Fit(2, (leftBeamMin + leftBeamMax) * .5f, (leftBeamMax - leftBeamMin) / _authored[2].Width);
            Fit(3, rightOuter, 1f);
            Fit(4, rightCap, 1f);
            Fit(5, (rightBeamMin + rightBeamMax) * .5f, (rightBeamMax - rightBeamMin) / _authored[5].Width);
            return true;
        }

        private void ResetParts()
        {
            foreach (var part in _authored)
            {
                part.Mesh.transform.localPosition = part.Position;
                part.Mesh.transform.localScale = part.Scale;
            }
        }

        private void Fit(int index, float center, float widthFactor)
        {
            var part = _authored[index];
            var target = part.Mesh.transform;
            target.localScale = new Vector3(part.Scale.x * widthFactor, part.Scale.y, part.Scale.z);
            float offset = center - BoundsIn(transform, part.Mesh).center.x;
            target.localPosition += target.parent.InverseTransformVector(transform.TransformVector(new Vector3(offset, 0f, 0f)));
        }

        private static Bounds BoundsIn(Transform root, MeshFilter mesh)
        {
            var bounds = mesh.sharedMesh.bounds;
            var matrix = root.worldToLocalMatrix * mesh.transform.localToWorldMatrix;
            var result = new Bounds(matrix.MultiplyPoint3x4(bounds.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
                result.Encapsulate(matrix.MultiplyPoint3x4(bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f))));
            return result;
        }
    }
}
