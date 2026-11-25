// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using YARG.Core.Chart;

namespace YARG.Tests.EditMode
{
    public sealed class CymbalAnimationSetupTests
    {
        private static Type Production(string name) => Type.GetType(name + ", Assembly-CSharp", true);
        private static Type Spin => Production("YARG.Gameplay.Visuals.CymbalSpinAnimation");
        private const string THEME = "Assets/Prefabs/Gameplay/Visual/Themes/RectangularTheme.prefab";

        [Test]
        public void CurrentThemeHasAccentSpinButNoObsoleteRockingComponents()
        {
            var theme = AssetDatabase.LoadAssetAtPath<GameObject>(THEME);
            var scripts = theme.GetComponentsInChildren<MonoBehaviour>(true);
            Assert.That(scripts.All(s => s != null), Is.True, "Removing rocking must not leave missing scripts.");
            Assert.That(scripts.Count(s => s.GetType().Name == "HiHatBeatAnimation"), Is.Zero);
            Assert.That(theme.GetComponentsInChildren(Spin, true).Length, Is.EqualTo(6));
        }

        [TestCase("ClosedHiHatAccent")]
        [TestCase("CymbalAccent")]
        [TestCase("OpenHiHatAccent")]
        public void AccentBaseUsesFivePetalsAndAnIndependentEightBeatRotor(string name)
        {
            var model = UnityEngine.Object.Instantiate(EliteModel(name).gameObject);
            try
            {
                var spins = model.GetComponentsInChildren(Spin, true)
                    .OrderBy(spin => new SerializedObject(spin).FindProperty("_beatsPerRevolution").floatValue).ToArray();
                Assert.That(spins.Length, Is.EqualTo(2));
                var crown = (Transform) Spin.GetProperty("Rotor").GetValue(spins[0]);
                var lower = (Transform) Spin.GetProperty("Rotor").GetValue(spins[1]);
                Spin.GetMethod("ApplyBeatPhase").Invoke(spins[0], new object[] { 1d });
                Spin.GetMethod("ApplyBeatPhase").Invoke(spins[1], new object[] { 1d });
                Assert.That(Quaternion.Angle(crown.localRotation, Quaternion.AngleAxis(-90f, Vector3.forward)), Is.LessThan(.001f));
                Assert.That(Quaternion.Angle(lower.localRotation, Quaternion.AngleAxis(-45f, Vector3.forward)), Is.LessThan(.001f));
                var filters = lower.GetComponentsInChildren<MeshFilter>(true);
                Assert.That(filters.Single(filter => filter.name == "Thorns").sharedMesh.triangles.Length / 3, Is.EqualTo(150));
                Assert.That(new SerializedObject(spins[1]).FindProperty("_contactSurface").objectReferenceValue,
                    Is.SameAs(filters.Single(filter => filter.name == "Base")));
            }
            finally { UnityEngine.Object.DestroyImmediate(model); }
        }

        [TestCase("Kick", 704, 1088)]
        [TestCase("KickAccent", 960, 1536)]
        [TestCase("KickGhost", 704, 1088)]
        public void EliteKickBarUsesTheBakedMeshForTheSelectedFretCount(string name, int compact, int full)
        {
            var noteType = Production("YARG.Themes.ThemeNote");
            var instance = UnityEngine.Object.Instantiate(EliteModel(name).gameObject);
            try
            {
                var modularType = Type.GetType("YARG.Gameplay.Visuals.ModularKickVisual, Assembly-CSharp");
                Assert.That(modularType, Is.Not.Null, "The bar needs an authored mesh for each fret count.");
                var modular = instance.GetComponentInChildren(modularType, true);
                Assert.That(modular, Is.Not.Null);
                var filter = modular.GetComponent<MeshFilter>();
                foreach (var (count, triangles) in new[] { (5, compact), (8, full), (5, compact) })
                {
                    modularType.GetMethod("Configure").Invoke(modular, new object[] { count });
                    Assert.That(AssetDatabase.Contains(filter.sharedMesh), Is.True);
                    Assert.That(filter.sharedMesh.triangles.Length / 3, Is.EqualTo(triangles));
                    Assert.That(filter.sharedMesh.bounds.size.x, Is.EqualTo(2f).Within(.0001f));
                }
                var note = instance.GetComponent(noteType);
                var serialized = new SerializedObject(note);
                var color = serialized.FindProperty("_coloredMaterials").GetArrayElementAtIndex(0);
                var metal = serialized.FindProperty("_coloredMetalMaterials").GetArrayElementAtIndex(0);
                Assert.That(color.FindPropertyRelative("Mesh").objectReferenceValue, Is.SameAs(filter.GetComponent<MeshRenderer>()));
                Assert.That(metal.FindPropertyRelative("Mesh").objectReferenceValue, Is.SameAs(filter.GetComponent<MeshRenderer>()));
                Assert.That(color.FindPropertyRelative("MaterialIndex").intValue, Is.Not.EqualTo(metal.FindPropertyRelative("MaterialIndex").intValue));
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }

        [TestCase("Kick")]
        [TestCase("KickAccent")]
        [TestCase("KickGhost")]
        public void KickEmittersHaveValidNormalsOnAllVisibleFaces(string name)
        {
            var mesh = EliteModel(name).GetComponentsInChildren<MeshFilter>(true).Single().sharedMesh;
            var points = mesh.vertices;
            var normals = mesh.normals;
            var metal = mesh.GetTriangles(1);
            for (int i = 0; i < metal.Length; i += 3)
            {
                int a = metal[i], b = metal[i + 1], c = metal[i + 2];
                if (Mathf.Abs(points[a].x) < .95f) continue;
                var area = Vector3.Cross(points[b] - points[a], points[c] - points[a]);
                Assert.That(area.sqrMagnitude, Is.GreaterThan(0f));
                Assert.That(normals[a].sqrMagnitude, Is.GreaterThan(.9f));
                Assert.That(normals[b].sqrMagnitude, Is.GreaterThan(.9f));
                Assert.That(normals[c].sqrMagnitude, Is.GreaterThan(.9f));
            }
        }

        [TestCase("Kick", .0285f)]
        [TestCase("KickGhost", .02565f)]
        public void ChromeKickRingsUseTheNarrowerCollar(string name, float width)
        {
            var mesh = EliteModel(name).GetComponentsInChildren<MeshFilter>(true).Single().sharedMesh;
            var points = mesh.vertices;
            var collar = mesh.GetTriangles(1).Select(index => points[index])
                .Where(point => Mathf.Abs(point.x + .6f) < .05f).ToArray();
            Assert.That(collar, Is.Not.Empty);
            Assert.That(collar.Max(point => point.x) - collar.Min(point => point.x), Is.EqualTo(width).Within(.0001f));
        }

        [Test]
        public void AccentKickCollarsAreWiderAndTaller()
        {
            var mesh = EliteModel("KickAccent").GetComponentsInChildren<MeshFilter>(true).Single().sharedMesh;
            var points = mesh.vertices;
            var collar = mesh.GetTriangles(2).Select(index => points[index])
                .Where(point => Mathf.Abs(point.x + .6f) < .05f).ToArray();
            Assert.That(collar, Is.Not.Empty);
            Assert.That(collar.Max(point => point.x) - collar.Min(point => point.x), Is.EqualTo(.045315f).Within(.0001f));
            Assert.That(collar.Max(point => point.y) - collar.Min(point => point.y), Is.EqualTo(.0860548f).Within(.0001f));
        }

        [Test]
        public void GhostKickGlowCoversACentered45DegreeArc()
        {
            var mesh = EliteModel("KickGhost").GetComponentsInChildren<MeshFilter>(true).Single().sharedMesh;
            var points = mesh.vertices;
            var dark = mesh.GetTriangles(2).Select(index => points[index]).ToArray();
            var bright = mesh.GetTriangles(0).Select(index => points[index]).ToArray();
            Assert.That(dark.Min(point => point.x), Is.EqualTo(bright.Min(point => point.x)).Within(.0001f));
            Assert.That(dark.Max(point => point.x), Is.EqualTo(bright.Max(point => point.x)).Within(.0001f));
            Assert.That(dark.Max(point => point.y), Is.EqualTo(bright.Min(point => point.y)).Within(.0001f));
            float bottom = dark.Min(point => point.y);
            float top = bright.Max(point => point.y);
            float seam = bright.Min(point => point.y);
            float near = dark.Min(point => point.z);
            float far = dark.Max(point => point.z);
            var seamAngles = dark.Where(point => Mathf.Abs(point.y - seam) < .00001f)
                .Select(point => Mathf.Atan2((point.z - (near + far) * .5f) / (far - near),
                    (point.y - (top + bottom) * .5f) / (top - bottom)) * Mathf.Rad2Deg).ToArray();
            Assert.That(seamAngles.Min(), Is.EqualTo(-22.5f).Within(.01f));
            Assert.That(seamAngles.Max(), Is.EqualTo(22.5f).Within(.01f));
            Assert.That(dark.Min(point => point.z), Is.LessThan(bright.Min(point => point.z)));
            Assert.That(dark.Max(point => point.z), Is.GreaterThan(bright.Max(point => point.z)));
        }

        [TestCase("Kick", 1)]
        [TestCase("KickGhost", 1)]
        [TestCase("KickAccent", 2)]
        [TestCase("KickFlam", 1)]
        [TestCase("KickFlamGhost", 1)]
        [TestCase("KickFlamAccent", 2)]
        public void DeformedKickPlacesRigidCollarsAtImpactAndRestoresBakedMeshes(string name, int collarSlot)
        {
            var note = UnityEngine.Object.Instantiate(EliteModel(name).gameObject);
            try
            {
                var modular = note.GetComponentInChildren(Production("YARG.Gameplay.Visuals.ModularKickVisual"), true);
                var place = modular.GetType().GetMethod("ConfigureAtBoundaries", BindingFlags.NonPublic | BindingFlags.Instance);
                var filters = note.GetComponentsInChildren<MeshFilter>(true);
                void Configure(float[] edges) => place.Invoke(modular, new object[] { 5, edges });
                float X(MeshFilter filter, Vector3 point) => note.transform.InverseTransformPoint(filter.transform.TransformPoint(point)).x;
                float[] CollarXs(MeshFilter filter)
                {
                    var mesh = filter.sharedMesh;
                    var vertices = mesh.vertices;
                    return mesh.GetTriangles(collarSlot).Select(i => vertices[i]).Where(v => Mathf.Abs(v.x) < .95f)
                        .Select(v => X(filter, v)).ToArray();
                }
                Configure(null);
                var originals = filters.Select(filter => filter.sharedMesh).ToArray();
                var spans = filters.Select(filter =>
                {
                    var xs = filter.sharedMesh.vertices.Select(v => X(filter, v)).ToArray();
                    return (min: xs.Min(), max: xs.Max());
                }).ToArray();
                float left = spans.Min(s => s.min), right = spans.Max(s => s.max);
                var originalCollars = CollarXs(filters[0]);
                float first = originalCollars.Min();
                float originalWidth = originalCollars.Where(x => x < first + .09f).Max() - first;
                var edges = new[] { 0f, .10f, .22f, .50f, .75f, 1f };
                Mesh[] deformed = null;
                foreach (var layout in new[] { edges, edges.Reverse().Select(edge => 1f - edge).ToArray() })
                {
                    Configure(layout);
                    for (int i = 0; i < filters.Length; i++)
                    {
                        var xs = CollarXs(filters[i]);
                        var expected = layout.Skip(1).Take(4).Select(edge => Mathf.Lerp(left, right, edge))
                            .Where(edge => edge > spans[i].min && edge < spans[i].max).ToArray();
                        Assert.That(xs.All(x => expected.Any(seam => Mathf.Abs(x - seam) < .045f)), Is.True);
                        foreach (var seam in expected)
                        {
                            var ring = xs.Where(x => Mathf.Abs(x - seam) < .045f).ToArray();
                            Assert.That(ring, Is.Not.Empty);
                            Assert.That((ring.Min() + ring.Max()) * .5f, Is.EqualTo(seam).Within(.0002f));
                            Assert.That(ring.Max() - ring.Min(), Is.EqualTo(originalWidth).Within(.0002f));
                        }
                        var all = filters[i].sharedMesh.vertices.Select(v => X(filters[i], v)).ToArray();
                        Assert.That(all.Min(), Is.EqualTo(spans[i].min).Within(.0002f));
                        Assert.That(all.Max(), Is.EqualTo(spans[i].max).Within(.0002f));
                    }
                    var current = filters.Select(filter => filter.sharedMesh).ToArray();
                    if (deformed != null) Assert.That(current, Is.EqualTo(deformed));
                    deformed = current;
                }
                if (filters.Length == 2)
                {
                    var ordered = spans.OrderBy(span => span.min).ToArray();
                    float gapStart = Mathf.InverseLerp(left, right, ordered[0].max);
                    float gapEnd = Mathf.InverseLerp(left, right, ordered[1].min);
                    Configure(new[] { 0f, .1f, gapStart - .0001f, gapEnd + .0001f, .9f, 1f });
                    for (int i = 0; i < filters.Length; i++)
                    {
                        var xs = filters[i].sharedMesh.vertices.Select(point => X(filters[i], point)).ToArray();
                        Assert.That(xs.Min(), Is.EqualTo(spans[i].min).Within(.0002f));
                        Assert.That(xs.Max(), Is.EqualTo(spans[i].max).Within(.0002f),
                            "A collar entering the end of a split beam must leave the gap clear.");
                    }
                }
                Configure(null);
                Assert.That(filters.Select(filter => filter.sharedMesh), Is.EqualTo(originals));
            }
            finally { UnityEngine.Object.DestroyImmediate(note); }
        }

        [TestCase("KickFlam", 1)]
        [TestCase("KickFlamGhost", 1)]
        [TestCase("KickFlamAccent", 2)]
        public void SplitKickFlamCollarsStayCenteredOnTheFretSeams(string name, int collarSlot)
        {
            var note = UnityEngine.Object.Instantiate(EliteModel(name).gameObject);
            try
            {
                var modularType = Production("YARG.Gameplay.Visuals.ModularKickVisual");
                var modular = note.GetComponentsInChildren(modularType, true).Single();
                var filters = note.GetComponentsInChildren<MeshFilter>(true);
                foreach (int count in Enumerable.Range(1, 10).Append(5))
                {
                    modularType.GetMethod("Configure").Invoke(modular, new object[] { count });
                    float InNote(MeshFilter filter, Vector3 point) =>
                        note.transform.InverseTransformPoint(filter.transform.TransformPoint(point)).x;
                    var beamSpans = filters.Select(filter =>
                    {
                        var xs = filter.sharedMesh.vertices.Select(point => InNote(filter, point)).ToArray();
                        return (Left: xs.Min(), Right: xs.Max());
                    }).ToArray();
                    float left = beamSpans.Min(span => span.Left);
                    float right = beamSpans.Max(span => span.Right);
                    var seams = Enumerable.Range(1, count - 1)
                        .Select(column => Mathf.Lerp(left, right, column / (float)count)).ToArray();
                    for (int i = 0; i < filters.Length; i++)
                    {
                        var span = beamSpans[i];
                        var expected = seams.Where(seam => span.Left < seam && seam < span.Right).ToArray();
                        var mesh = filters[i].sharedMesh;
                        var points = mesh.vertices;
                        var collars = mesh.GetTriangles(collarSlot).Select(index => points[index])
                            .Where(point => Mathf.Abs(point.x) < .95f)
                            .Select(point => InNote(filters[i], point)).ToArray();
                        foreach (var seam in expected)
                        {
                            var ring = collars.Where(x => Mathf.Abs(x - seam) < .025f).ToArray();
                            Assert.That(ring, Is.Not.Empty, $"No ring at {seam} on a {count}-fret highway.");
                            Assert.That((ring.Min() + ring.Max()) * .5f, Is.EqualTo(seam).Within(.0002f));
                        }
                        Assert.That(collars.All(x => expected.Any(seam => Mathf.Abs(x - seam) < .025f)));
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(note); }
        }

        [TestCase("KickFlamAccent")]
        [TestCase("DedicatedLaneKickFlamAccent")]
        public void EachEliteAccentFlamBeamHasTheDarkEdgedGlowingCollars(string name)
        {
            var note = EliteModel(name);
            foreach (var binding in new[] { "_coloredMaterials", "_coloredKick2Materials" })
            {
                var entries = new SerializedObject(note).FindProperty(binding);
                Assert.That(entries.arraySize, Is.EqualTo(1));
                var renderer = (MeshRenderer)entries.GetArrayElementAtIndex(0)
                    .FindPropertyRelative("Mesh").objectReferenceValue;
                var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                int glow = Array.FindIndex(renderer.sharedMaterials, material => material.name == "EliteAccentHOPOGlow");
                int edge = Array.FindIndex(renderer.sharedMaterials, material => material.name == "EliteHiHatDark");
                Assert.That(glow, Is.GreaterThanOrEqualTo(0));
                Assert.That(edge, Is.GreaterThanOrEqualTo(0));
                Assert.That(mesh.GetIndexCount(glow), Is.GreaterThan(0));
                Assert.That(mesh.GetIndexCount(edge), Is.GreaterThan(0));
            }
        }

        private static Component EliteModel(string name)
        {
            var noteType = Production("YARG.Themes.ThemeNote");
            var theme = AssetDatabase.LoadAssetAtPath<GameObject>(THEME);
            return theme.transform.Find("Elite Lane Notes").GetComponentsInChildren(noteType, true)
                .First(note => noteType.GetProperty("NoteType").GetValue(note).ToString() == name);
        }

        [Test]
        public void ThemeSetupBindsNormalStarAndSplitModelsAndPoolClonesRemapTheirOwner()
        {
            var root = new GameObject("inactive note template");
            root.SetActive(false);
            GameObject clone = null;
            try
            {
                var noteType = Production("YARG.Gameplay.Visuals.EliteDrumsNoteElement");
                var note = root.AddComponent(noteType);
                var theme = AssetDatabase.LoadAssetAtPath<GameObject>(THEME)
                    .GetComponent(Production("YARG.Themes.ThemeComponent"));
                var style = Enum.Parse(Production("YARG.Themes.VisualStyle"), "EliteDrums");
                object Models(bool star) => theme.GetType().GetMethod("GetNoteModelsForVisualStyle")
                    .Invoke(theme, new[] { style, (object)star });
                noteType.GetMethod("SetThemeModels").Invoke(note, new[] { Models(false), Models(true) });
                void Check(GameObject model)
                {
                    var owner = model.GetComponent(noteType);
                    var groupType = Production("YARG.Gameplay.Visuals.NoteGroup");
                    var groups = model.GetComponentsInChildren(groupType, true);
                    Assert.That(groups.Length, Is.GreaterThan(0));
                    foreach (var group in groups)
                        Assert.That(group.gameObject.activeSelf, Is.False,
                            "Unused model branches must stay inactive when a pooled note first wakes.");
                    var spins = model.GetComponentsInChildren(Spin, true);
                    Assert.That(spins.Length, Is.GreaterThanOrEqualTo(15), "Normal, SP and both split halves need binding.");
                    foreach (var spin in spins)
                    {
                        var binding = new SerializedObject(spin).FindProperty("_noteElement");
                        Assert.That(binding, Is.Not.Null, "Owner must survive Unity prefab/pool cloning.");
                        Assert.That(binding.objectReferenceValue, Is.SameAs(owner));
                    }
                }
                Check(root);
                clone = UnityEngine.Object.Instantiate(root);
                Check(clone);
            }
            finally
            {
                if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void SharedClockHandlesTempoSeekPausePeriodLeftyAndSongReplacement()
        {
            var clockType = Type.GetType("YARG.Gameplay.Visuals.CymbalSpinClock, Assembly-CSharp");
            Assert.That(clockType, Is.Not.Null);
            var clock = Activator.CreateInstance(clockType);
            var sync = new SyncTrack(480u,
                new List<TempoChange> { new(60, 0, 0), new(120, 2, 960) },
                new List<TimeSignatureChange>(), new List<Beatline>());
            Quaternion At(double time, float period = 4f, bool lefty = false) =>
                (Quaternion)clockType.GetMethod("Evaluate").Invoke(clock, new object[] { sync, time, period, lefty });
            void Check(double time, float angle) => Assert.That(Quaternion.Angle(At(time),
                Quaternion.AngleAxis(angle, Vector3.forward)), Is.LessThan(.001f));
            Check(1d, -90f);
            Check(2.25d, -225f);
            Check(2.25d, -225f); // Pause, no integrated delta-time drift.
            Check(-.5d, -315f); // Rewind into preroll.
            Assert.That(Quaternion.Angle(At(1, 8), Quaternion.AngleAxis(-45, Vector3.forward)), Is.LessThan(.001f));
            Assert.That(Quaternion.Angle(At(1, 4, true), Quaternion.AngleAxis(90, Vector3.forward)), Is.LessThan(.001f));
            sync = new SyncTrack(480u, new List<TempoChange> { new(120, 0, 0) },
                new List<TimeSignatureChange>(), new List<Beatline>());
            Check(1d, -180f); // Same timestamp, different chart.
        }
    }
}
