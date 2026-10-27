// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using UnityEngine;
using YARG.Settings;
using ModelGeometry = YARG.Gameplay.Visuals.EliteDrumsRuntimeGeometry.ModelGeometry;
using PairPlacement = YARG.Gameplay.Visuals.EliteDrumsRuntimeGeometry.PairPlacement;

namespace YARG.Gameplay.Visuals
{
    /// <summary>
    /// Main-thread session lookup cache. Entries contain managed values only. Live
    /// templates retain their prepared data independently, so FIFO eviction cannot
    /// make a currently playing song fall back to fitting on a note spawn.
    /// </summary>
    internal static class EliteDrumsGeometryCache
    {
        private const int MODEL_CAPACITY = 512;
        private const int PAIR_CAPACITY = 8192;
        private const long MODEL_BYTE_CAPACITY = 32L * 1024 * 1024;
        private static long _modelBytes;
        internal static long RetainedGeometryBytes => _modelBytes;
        internal static long SignatureKeyBytes => 2L * Models.Keys.Sum(key => key.Length);
        private static readonly Dictionary<string, ModelGeometry> Models = new();
        private static readonly Queue<string> ModelOrder = new();
        private static readonly Dictionary<PairKey, PairPlacement> Pairs = new();
        private static readonly Queue<PairKey> PairOrder = new();
        private static long _nextIdentity;
        // Loaded note meshes are immutable at runtime. Cache their content once
        // by managed identity without keeping their Unity wrappers alive. A new
        // Mesh after theme reload gets a fresh fingerprint, regardless of IDs.
        private static ConditionalWeakTable<Mesh, MeshFingerprint> _meshFingerprints = new();

        private sealed class MeshFingerprint
        {
            internal readonly string Value;
            internal MeshFingerprint(string value) => Value = value;
        }

        /// <summary>
        /// Call after deliberately editing a loaded note mesh, before rebuilding
        /// its prepared source. Existing live bindings remain immutable snapshots;
        /// editing meshes during active gameplay requires rebuilding/restarting.
        /// </summary>
        internal static void InvalidateMeshFingerprint(Mesh mesh)
        {
            if (mesh != null) _meshFingerprints.Remove(mesh);
        }

        internal readonly struct PairKey : IEquatable<PairKey>
        {
            private readonly (long, long, int, float, float, bool, EliteFlamLayout, bool, bool) _value;
            internal PairKey(long left, long right, int columns, float center, float width,
                bool regular, EliteFlamLayout layout, bool cymbal, bool lefty) =>
                _value = (left, right, columns, center, width, regular, layout, cymbal, lefty);
            public bool Equals(PairKey other) => _value.Equals(other._value);
            public override bool Equals(object other) => other is PairKey key && Equals(key);
            public override int GetHashCode() => _value.GetHashCode();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession() => EliteDrumsRuntimeGeometry.ClearSessionCache();

        internal static void Clear()
        {
            Models.Clear();
            _modelBytes = 0;
            ModelOrder.Clear();
            Pairs.Clear();
            PairOrder.Clear();
            _meshFingerprints = new ConditionalWeakTable<Mesh, MeshFingerprint>();
            // Do not recycle identities: live template bindings may outlive a clear.
        }

        internal static bool TryGetPair(PairKey key, out PairPlacement placement) => Pairs.TryGetValue(key, out placement);
        internal static void AddPair(PairKey key, PairPlacement placement)
        {
            if (Pairs.ContainsKey(key)) return;
            if (Pairs.Count == PAIR_CAPACITY) Pairs.Remove(PairOrder.Dequeue());
            Pairs.Add(key, placement);
            PairOrder.Enqueue(key);
        }

        internal static ModelGeometry GetModel(Transform model)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            string signature = Signature(model);
            EliteDrumsRuntimeGeometry.SignatureMilliseconds += clock.Elapsed.TotalMilliseconds;
            if (Models.TryGetValue(signature, out var geometry)) return geometry;
            clock.Restart();
            geometry = new ModelGeometry(model, ++_nextIdentity);
            EliteDrumsRuntimeGeometry.MeasurementMilliseconds += clock.Elapsed.TotalMilliseconds;
            EliteDrumsRuntimeGeometry.ModelMeasurements++;
            // A huge custom model remains owned by the current template without
            // making session retention exceed the measured-array byte budget.
            long bytes = geometry.ArrayBytes;
            if (bytes > MODEL_BYTE_CAPACITY) return geometry;
            while (Models.Count > 0 && (Models.Count == MODEL_CAPACITY || _modelBytes + bytes > MODEL_BYTE_CAPACITY))
            {
                string oldest = ModelOrder.Dequeue();
                _modelBytes -= Models[oldest].ArrayBytes;
                Models.Remove(oldest);
            }
            Models.Add(signature, geometry);
            _modelBytes += bytes;
            ModelOrder.Enqueue(signature);
            return geometry;
        }

        internal static Matrix4x4 RelativeMatrix(Transform root, Transform child)
        {
            var matrix = Matrix4x4.identity;
            while (child != root)
            {
                matrix = Matrix4x4.TRS(child.localPosition, child.localRotation, child.localScale) * matrix;
                child = child.parent;
            }
            return matrix;
        }

        private static void Write(BinaryWriter writer, Vector3 value)
        {
            writer.Write(value.x); writer.Write(value.y); writer.Write(value.z);
        }

        private static string MeshSignature(Mesh mesh)
        {
            if (_meshFingerprints.TryGetValue(mesh, out var cached)) return cached.Value;
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(mesh.name);
            Write(writer, mesh.bounds.center);
            Write(writer, mesh.bounds.extents);
            writer.Write(mesh.isReadable);
            if (mesh.isReadable)
            {
                var points = mesh.vertices;
                writer.Write(points.Length);
                foreach (var point in points) Write(writer, point);
                var triangles = mesh.triangles;
                writer.Write(triangles.Length);
                foreach (int triangle in triangles) writer.Write(triangle);
                writer.Write(mesh.subMeshCount);
                if (mesh.subMeshCount > 2)
                {
                    var baseTriangles = mesh.GetTriangles(2);
                    writer.Write(baseTriangles.Length);
                    foreach (int triangle in baseTriangles) writer.Write(triangle);
                }
            }
            // Hash actual geometry content, never asset IDs or names alone. The
            // compact SHA-256 digest avoids retaining raw mesh copies in keys.
            string signature = Fingerprint(stream);
            _meshFingerprints.Add(mesh, new MeshFingerprint(signature));
            EliteDrumsRuntimeGeometry.MeshFingerprints++;
            return signature;
        }

        private static string Fingerprint(MemoryStream stream)
        {
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(stream.GetBuffer(), 0, (int)stream.Length));
        }

        private static string Signature(Transform model)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            Write(writer, model.localPosition);
            Write(writer, model.localScale);
            var rotation = model.localRotation;
            writer.Write(rotation.x); writer.Write(rotation.y); writer.Write(rotation.z); writer.Write(rotation.w);
            var filters = model.GetComponentsInChildren<MeshFilter>(true);
            writer.Write(filters.Length);
            foreach (var filter in filters)
            {
                writer.Write(filter.transform.name == "Base");
                writer.Write(filter.transform.name == "Upper Cymbal");
                writer.Write(filter.transform.parent != null && filter.transform.parent.name == "Upper Cymbal");
                var matrix = RelativeMatrix(model, filter.transform);
                for (int i = 0; i < 16; i++) writer.Write(matrix[i]);
                writer.Write(filter.sharedMesh != null);
                if (filter.sharedMesh != null) writer.Write(MeshSignature(filter.sharedMesh));
            }
            return Fingerprint(stream);
        }
    }
}
