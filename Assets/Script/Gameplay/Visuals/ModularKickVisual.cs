// pattern: Imperative Shell
using System;
using UnityEngine;

namespace YARG.Gameplay.Visuals
{
    [RequireComponent(typeof(MeshFilter))]
    public sealed class ModularKickVisual : MonoBehaviour
    {
        [SerializeField] private MeshFilter _beam;
        [SerializeField] private MeshFilter _mirroredBeam;
        [SerializeField, Tooltip("Baked bar meshes ordered by visible fret count, starting at one.")]
        private Mesh[] _meshesByLaneCount;

        public void Configure(int count)
        {
            if (count < 1 || count > _meshesByLaneCount.Length)
                throw new ArgumentOutOfRangeException(nameof(count));
            var mesh = _meshesByLaneCount[count - 1];
            _beam.sharedMesh = mesh;
            if (_mirroredBeam != null) _mirroredBeam.sharedMesh = mesh;
        }
    }
}
