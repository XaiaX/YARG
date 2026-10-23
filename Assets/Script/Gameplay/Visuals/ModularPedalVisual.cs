using System;
using System.Collections.Generic;
using UnityEngine;

namespace YARG.Gameplay.Visuals
{
    public sealed class ModularPedalVisual : MonoBehaviour
    {
        [SerializeField] private MeshFilter[] _sections;
        [SerializeField] private float _sectionWidth = 0.4f;
        [SerializeField] private Transform _leftEnd;
        [SerializeField] private Transform _rightEnd;

        private Mesh[] _sources;
        private Vector3 _leftEndRest;
        private Vector3 _rightEndRest;
        private readonly Dictionary<int, Mesh[]> _meshes = new();

        public void Configure(int count)
        {
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
            if (_sections == null || _sections.Length == 0 || _sectionWidth <= 0f)
                throw new InvalidOperationException("Pedal sections require meshes and a positive width.");
            if (_sources == null)
            {
                if (_leftEnd != null) _leftEndRest = _leftEnd.localPosition;
                if (_rightEnd != null) _rightEndRest = _rightEnd.localPosition;
                _sources = new Mesh[_sections.Length];
                for (int i = 0; i < _sections.Length; i++)
                {
                    var source = _sections[i].sharedMesh;
                    if (source == null || source.subMeshCount != 1)
                        throw new InvalidOperationException("Each pedal section must have one material submesh.");
                    _sources[i] = source;
                }
            }
            if (!_meshes.TryGetValue(count, out var meshes))
            {
                meshes = new Mesh[_sources.Length];
                for (int part = 0; part < _sources.Length; part++)
                {
                    var instances = new CombineInstance[count];
                    for (int lane = 0; lane < count; lane++)
                        instances[lane] = new CombineInstance
                        {
                            mesh = _sources[part],
                            transform = Matrix4x4.Translate(new Vector3(
                                (lane - (count - 1) / 2f) * _sectionWidth, 0, 0))
                        };
                    var mesh = new Mesh { name = $"{_sources[part].name} ({count} lanes)",
                        hideFlags = HideFlags.DontSave,
                        indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                    mesh.CombineMeshes(instances, true, true);
                    meshes[part] = mesh;
                }
                _meshes.Add(count, meshes);
            }
            for (int i = 0; i < _sections.Length; i++) _sections[i].sharedMesh = meshes[i];
            float endOffset = (count - 1) * _sectionWidth * 0.5f;
            if (_leftEnd != null) _leftEnd.localPosition = _leftEndRest + Vector3.left * endOffset;
            if (_rightEnd != null) _rightEnd.localPosition = _rightEndRest + Vector3.right * endOffset;
        }

        private void OnDestroy()
        {
            foreach (var meshes in _meshes.Values)
            foreach (var mesh in meshes)
            {
                if (Application.isPlaying) Destroy(mesh);
                else DestroyImmediate(mesh);
            }
        }
    }
}
