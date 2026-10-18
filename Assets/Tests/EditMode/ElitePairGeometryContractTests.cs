using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace YARG.Tests.EditMode
{
    public sealed class ElitePairGeometryContractTests
    {
        private const BindingFlags STATIC = BindingFlags.Static | BindingFlags.NonPublic;
        private const BindingFlags INSTANCE = BindingFlags.Instance | BindingFlags.NonPublic;
        private static Type Geometry => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("YARG.Gameplay.Visuals.EliteDrumsRuntimeGeometry"))
            .First(t => t != null);

        [TestCase(1)]
        [TestCase(5)]
        [TestCase(7)]
        [TestCase(10)]
        public void ElitePairGeometryContract(int columns)
        {
            var factor = Geometry.GetMethod("GetPairWidthFactor", STATIC);
            Assert.That((float) factor.Invoke(null, new object[] { columns }),
                Is.EqualTo(columns == 1 ? 0.5f : (columns - 1f) / columns));
            var placement = Geometry.GetMethod("GetPairPlacement", STATIC);
            float laneWidth = 2f / columns;
            foreach (bool lefty in new[] { false, true })
            for (int column = 0; column < columns; column++)
            {
                float center = ((column + 0.5f) * laneWidth - 1f) * (lefty ? -1f : 1f);
                // Deliberately unequal widths and asymmetric authored origins.
                object[] args = { -0.17f, 0.11f, -0.04f, 0.19f, center, laneWidth, 2f, columns, 0f, 0f };
                placement.Invoke(null, args);
                float l = (float) args[8];
                float r = (float) args[9];
                Assert.That(l + 0.11f, Is.EqualTo(r - 0.04f).Within(0.00001f), "Visible bounds share a seam.");
                float min = l - 0.17f;
                float max = r + 0.19f;
                float expectedMid = columns == 1 ? 0f : center / (1f - laneWidth / 2f) * (1f - 0.255f);
                Assert.That((min + max) / 2f, Is.EqualTo(expectedMid).Within(0.00001f));
                Assert.That(min, Is.GreaterThanOrEqualTo(-1.00001f));
                Assert.That(max, Is.LessThanOrEqualTo(1.00001f));
                if (columns > 1 && Mathf.Abs(center) > 1f - laneWidth)
                    Assert.That(center < 0 ? min : max, Is.EqualTo(center < 0 ? -1f : 1f).Within(0.00001f));
            }
        }

        private static object Layout(string name) => Enum.Parse(AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("YARG.Settings.EliteFlamLayout")).First(t => t != null), name);

        [TestCase(true)]
        [TestCase(false)]
        public void AlternateFlamLayoutChangesOnlyRegularFlamRotation(bool regular)
        {
            var root = new GameObject("Alternate flam pair test");
            var left = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            var right = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            left.SetParent(root.transform, false);
            right.SetParent(root.transform, false);
            left.localScale = right.localScale = new Vector3(0.3f, 0.07f, 0.2f);
            try
            {
                var type = Geometry.GetNestedType("ModelGeometry", BindingFlags.NonPublic);
                var l = Activator.CreateInstance(type, INSTANCE, null, new object[] { left }, null);
                var r = Activator.CreateInstance(type, INSTANCE, null, new object[] { right }, null);
                var fit = Geometry.GetMethod("FitPair", STATIC);
                object[] args = { l, left, r, right, 5, 0f, 0.5f, 0f, regular, Layout("V"), false, false };
                fit.Invoke(null, args);
                var leftPosition = left.localPosition;
                var rightPosition = right.localPosition;
                var scale = left.localScale;
                args[9] = Layout("Bucket");
                fit.Invoke(null, args);
                Assert.That(left.localPosition, Is.EqualTo(leftPosition));
                Assert.That(right.localPosition, Is.EqualTo(rightPosition));
                Assert.That(left.localScale, Is.EqualTo(scale));
                Assert.That(right.localScale, Is.EqualTo(scale));
                Assert.That(Quaternion.Angle(left.localRotation,
                    Quaternion.AngleAxis(regular ? -25f : 0f, Vector3.forward)), Is.LessThan(0.001f));
                Assert.That(Quaternion.Angle(right.localRotation,
                    Quaternion.AngleAxis(regular ? 25f : 0f, Vector3.forward)), Is.LessThan(0.001f));
                if (regular)
                {
                    Assert.That((left.localRotation * Vector3.right).y, Is.LessThan(0f), "Left inner edge slopes down.");
                    Assert.That((right.localRotation * Vector3.right).y, Is.GreaterThan(0f), "Right outer edge slopes up.");
                }
                args[9] = Layout("V");
                fit.Invoke(null, args);
                Assert.That(Quaternion.Angle(left.localRotation,
                    Quaternion.AngleAxis(regular ? 25f : 0f, Vector3.up)), Is.LessThan(0.001f), "Reuse restores original axis.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [TestCase(5)]
        [TestCase(7)]
        [TestCase(10)]
        public void AlternateCymbalFlamsMeetAtTiltedBounds(int columns)
        {
            var root = new GameObject("Tilted cymbal bounds test");
            var left = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            var right = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            left.SetParent(root.transform, false);
            right.SetParent(root.transform, false);
            left.name = right.name = "Base";
            left.localScale = right.localScale = new Vector3(0.3f, 0.04f, 0.2f);
            try
            {
                var type = Geometry.GetNestedType("ModelGeometry", BindingFlags.NonPublic);
                var l = Activator.CreateInstance(type, INSTANCE, null, new object[] { left }, null);
                var r = Activator.CreateInstance(type, INSTANCE, null, new object[] { right }, null);
                var fit = Geometry.GetMethod("FitPair", STATIC);
                float width = 2.5f / columns;
                foreach (float center in new[] { 0f, -1.25f + width / 2f, 1.25f - width / 2f })
                {
                    fit.Invoke(null, new object[] { l, left, r, right, columns, center, width, 0f, true, Layout("Bucket"), true, false });
                    float maxLeft = float.NegativeInfinity;
                    float minRight = float.PositiveInfinity;
                    for (int i = 0; i < 8; i++)
                    {
                        var corner = new Vector3((i & 1) == 0 ? -0.5f : 0.5f,
                            (i & 2) == 0 ? -0.5f : 0.5f, (i & 4) == 0 ? -0.5f : 0.5f);
                        maxLeft = Mathf.Max(maxLeft, left.localToWorldMatrix.MultiplyPoint3x4(corner).x);
                        minRight = Mathf.Min(minRight, right.localToWorldMatrix.MultiplyPoint3x4(corner).x);
                    }
                    Assert.That(maxLeft, Is.EqualTo(minRight).Within(0.00001f), "Tilted bounds meet without the inherited yaw gap.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [TestCase("V", false)]
        [TestCase("Bucket", false)]
        [TestCase("Parallel", false)]
        [TestCase("V", true)]
        [TestCase("Bucket", true)]
        [TestCase("Parallel", true)]
        public void SlopedCymbalBaseContactAndParallelReuse(string layout, bool lefty)
        {
            var root = new GameObject("Sloped cymbal pair");
            var mesh = new Mesh();
            // Upper points are deliberately wider and higher than the base.
            var vertices = new[] { new Vector3(-0.5f, 0f, -0.4f), new Vector3(0.5f, 0f, -0.4f),
                new Vector3(-0.5f, 0f, 0.4f), new Vector3(0.5f, 0f, 0.4f),
                new Vector3(-0.8f, 0.6f, -0.5f), new Vector3(0.8f, 0.9f, 0.5f) };
            mesh.vertices = vertices;
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3, 0, 4, 5 };
            mesh.RecalculateBounds();
            var left = new GameObject("Left").transform;
            var right = new GameObject("Right").transform;
            foreach (var model in new[] { left, right })
            {
                model.SetParent(root.transform, false);
                model.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                var basePart = new GameObject("Base");
                basePart.transform.SetParent(model, false);
                basePart.transform.localRotation = Quaternion.Euler(17f, 0f, 11f);
                basePart.transform.localPosition = new Vector3(0.03f, -0.02f, 0.07f);
                basePart.AddComponent<MeshFilter>().sharedMesh = mesh;
                model.localScale = new Vector3(0.3f, 0.07f, 0.2f);
            }
            try
            {
                var type = Geometry.GetNestedType("ModelGeometry", BindingFlags.NonPublic);
                var l = Activator.CreateInstance(type, INSTANCE, null, new object[] { left }, null);
                var r = Activator.CreateInstance(type, INSTANCE, null, new object[] { right }, null);
                var fit = Geometry.GetMethod("FitPair", STATIC);
                Vector3[] Envelope(Transform model, Matrix4x4 rootMatrix)
                {
                    return model.GetComponentsInChildren<MeshFilter>().SelectMany(filter =>
                    {
                        var bounds = filter.sharedMesh.bounds;
                        var child = model.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                        return Enumerable.Range(0, 8).Select(i => (rootMatrix * child).MultiplyPoint3x4(bounds.center +
                            Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1f : 1f,
                                (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f))));
                    }).ToArray();
                }
                foreach (int columns in new[] { 5, 7, 10 })
                foreach (float center in new[] { 0f, -1f + 1f / columns, 1f - 1f / columns })
                {
                    object[] args = { l, left, r, right, columns, center, 2f / columns, 0f,
                        true, Layout(layout), true, lefty };
                    fit.Invoke(null, args);
                    Vector3 direction = layout == "Parallel"
                        ? Quaternion.AngleAxis(lefty ? -25f : 25f, Vector3.up) * Vector3.forward : Vector3.right;
                    var leftPoints = vertices.Select(p => left.Find("Base").TransformPoint(p)).ToArray();
                    var rightPoints = vertices.Select(p => right.Find("Base").TransformPoint(p)).ToArray();
                    float leftMax = leftPoints.Max(p => Vector3.Dot(p, direction));
                    float rightMin = rightPoints.Min(p => Vector3.Dot(p, direction));
                    if (layout != "Parallel") Assert.That(leftMax, Is.EqualTo(rightMin).Within(0.00001f), "Full tilted base supports must not penetrate.");
                    // With this asymmetric sloped mesh each facing support is a
                    // unique vertex: direct Euclidean distance detects Y/Z gaps.
                    float distance = leftPoints.Min(a => rightPoints.Min(b => Vector3.Distance(a, b)));
                    if (layout != "Parallel") Assert.That(distance, Is.LessThan(0.00001f), $"Tilted child bases physically touch: {layout}, lefty={lefty}, N={columns}, distance={distance}");
                    if (layout != "Parallel") Assert.That(left.localPosition.x, Is.LessThan(right.localPosition.x), "Stable members must not cross.");
                    else
                    {
                        Assert.That(right.localPosition.x, Is.GreaterThan(left.localPosition.x));
                        Assert.That(right.localPosition.z, Is.EqualTo(left.localPosition.z).Within(0.00001f));
                        Assert.That(right.localPosition.y, Is.EqualTo(left.localPosition.y).Within(0.00001f));
                        Assert.That(Vector3.Distance(leftPoints[0], rightPoints[0]), Is.GreaterThan(0f), "Distinct fixed-depth members remain separated laterally.");
                    }
                    if (layout == "Parallel")
                    {
                        Assert.That(Quaternion.Angle(left.localRotation, right.localRotation), Is.LessThan(0.001f));
                        Assert.That(Quaternion.Angle(left.localRotation,
                            Quaternion.AngleAxis(lefty ? -25f : 25f, Vector3.up)), Is.LessThan(0.001f));
                        Assert.That(right.localPosition.z - left.localPosition.z, Is.Zero.Within(0.00001f));
                        var visible = Envelope(left, Matrix4x4.TRS(left.localPosition, left.localRotation, left.localScale))
                            .Concat(Envelope(right, Matrix4x4.TRS(right.localPosition, right.localRotation, right.localScale))).ToArray();
                        var ordinary = Envelope(left, Matrix4x4.Scale(new Vector3(0.3f * 5f / columns, 0.07f, 0.2f)));
                        Assert.That((visible.Min(p => p.z) + visible.Max(p => p.z)) * 0.5f,
                            Is.EqualTo((ordinary.Min(p => p.z) + ordinary.Max(p => p.z)) * 0.5f).Within(0.00001f),
                            "Parallel cymbal envelope uses ordinary gem longitudinal alignment.");
                    }
                    var position = left.localPosition;
                    fit.Invoke(null, args);
                    Assert.That(left.localPosition, Is.EqualTo(position), "Repeated fit must not accumulate offsets.");
                    Assert.That(left.localScale.y, Is.EqualTo(0.07f));
                    Assert.That(left.localScale.z, Is.EqualTo(0.2f));
                    args[8] = false;
                    fit.Invoke(null, args);
                    Assert.That(Quaternion.Angle(left.localRotation, Quaternion.identity), Is.LessThan(0.001f));
                    Assert.That(left.localPosition.z, Is.Zero.Within(0.00001f), "Flat reuse removes Parallel depth offset.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [TestCase(5, false)]
        [TestCase(5, true)]
        [TestCase(7, false)]
        [TestCase(7, true)]
        [TestCase(10, false)]
        [TestCase(10, true)]
        public void ParallelLongSidesTouchAndVisibleCenterMatchesOrdinary(int columns, bool lefty)
        {
            var root = new GameObject("Parallel longitudinal contract");
            var left = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            var right = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            var mesh = UnityEngine.Object.Instantiate(left.GetComponent<MeshFilter>().sharedMesh);
            var bounds = new Bounds(new Vector3(0.17f, 0.03f, -0.21f), new Vector3(1f, 0.2f, 0.45f));
            mesh.bounds = bounds;
            foreach (var model in new[] { left, right })
            {
                model.SetParent(root.transform, false);
                model.GetComponent<MeshFilter>().sharedMesh = mesh;
                model.localScale = new Vector3(0.3f, 0.07f, 0.2f);
                model.localPosition = new Vector3(0.03f, 0.02f, 0.13f);
                model.localRotation = Quaternion.Euler(4f, 8f, 3f);
            }
            try
            {
                var type = Geometry.GetNestedType("ModelGeometry", BindingFlags.NonPublic);
                var l = Activator.CreateInstance(type, INSTANCE, null, new object[] { left }, null);
                var r = Activator.CreateInstance(type, INSTANCE, null, new object[] { right }, null);
                var corners = Enumerable.Range(0, 8).Select(i => bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f))).ToArray();
                var ordinaryMatrix = Matrix4x4.TRS(left.localPosition, left.localRotation,
                    new Vector3(0.3f * 5f / columns, 0.07f, 0.2f));
                var ordinary = corners.Select(ordinaryMatrix.MultiplyPoint3x4).ToArray();
                Geometry.GetMethod("FitPair", STATIC).Invoke(null, new object[] { l, left, r, right,
                    columns, 0f, 2f / columns, 0f, true, Layout("Parallel"), false, lefty });
                var a = corners.Select(left.TransformPoint).ToArray();
                var b = corners.Select(right.TransformPoint).ToArray();
                var pair = a.Concat(b).ToArray();
                float centerZ = (pair.Min(p => p.z) + pair.Max(p => p.z)) * 0.5f;
                Assert.That(centerZ, Is.EqualTo((ordinary.Min(p => p.z) + ordinary.Max(p => p.z)) * 0.5f).Within(0.00001f),
                    "Independent visible envelope center, not transform pivot, matches ordinary gem.");
                float ordinaryZ = (ordinary.Min(p => p.z) + ordinary.Max(p => p.z)) * 0.5f;
                foreach (var member in new[] { a, b })
                    Assert.That((member.Min(p => p.z) + member.Max(p => p.z)) * 0.5f, Is.EqualTo(ordinaryZ).Within(0.00001f));
                Assert.That(right.localPosition.z, Is.EqualTo(left.localPosition.z).Within(0.00001f));
                Assert.That(right.localPosition.y, Is.EqualTo(left.localPosition.y).Within(0.00001f));
                // Independent separating-axis interval test on projected actual
                // box geometry: touching now, overlapping if squeezed further.
                Assert.That(ProjectedOverlap(a, b, 0f), Is.False);
                Assert.That(ProjectedOverlap(a, b, -0.0001f), Is.True);
                float width = pair.Max(p => p.x) - pair.Min(p => p.x);
                float singleWidth = a.Max(p => p.x) - a.Min(p => p.x);
                float conservativeWidth = singleWidth * 2f;
                Assert.That(width, Is.LessThan(conservativeWidth - 0.00001f),
                    "Independent same-transform reference: partially contacting lateral pair is narrower than two horizontal-AABB-separated gems.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ParallelLateralDistanceUsesPartialLongSideContact(bool lefty)
        {
            var root = new GameObject("Parallel lateral reference");
            var left = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            var right = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            foreach (var model in new[] { left, right })
            {
                model.SetParent(root.transform, false);
                model.localScale = new Vector3(0.4f, 0.03f, 0.08f);
                model.localPosition = new Vector3(0.02f, 0.01f, 0.12f);
            }
            try
            {
                var type = Geometry.GetNestedType("ModelGeometry", BindingFlags.NonPublic);
                var l = Activator.CreateInstance(type, INSTANCE, null, new object[] { left }, null);
                var r = Activator.CreateInstance(type, INSTANCE, null, new object[] { right }, null);
                Geometry.GetMethod("FitPair", STATIC).Invoke(null, new object[] { l, left, r, right, 5, 0f, 0.4f, 0f,
                    true, Layout("Parallel"), false, lefty });
                float angle = 25f * Mathf.Deg2Rad;
                float expected = Mathf.Min(0.4f * 0.8f / Mathf.Cos(angle), 0.08f / Mathf.Sin(angle));
                Assert.That(right.localPosition.x - left.localPosition.x, Is.EqualTo(expected).Within(0.00001f));
                Assert.That(right.localPosition.z, Is.EqualTo(0.12f).Within(0.00001f));
                Assert.That(left.localPosition.z, Is.EqualTo(0.12f).Within(0.00001f));
                Assert.That(expected * Mathf.Cos(angle), Is.LessThan(0.4f * 0.8f), "Long sides overlap partially, not end-to-end or fully aligned.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void ParallelCacheKeyInvalidatesGeometryInputsAndReusesTranslatedPlacement()
        {
            var root = new GameObject("Parallel cache key test");
            var left = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            var right = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            left.SetParent(root.transform, false);
            right.SetParent(root.transform, false);
            try
            {
                var type = Geometry.GetNestedType("ModelGeometry", BindingFlags.NonPublic);
                var l = Activator.CreateInstance(type, INSTANCE, null, new object[] { left }, null);
                var r = Activator.CreateInstance(type, INSTANCE, null, new object[] { right }, null);
                var fit = Geometry.GetMethod("FitPair", STATIC);
                var computations = type.GetField("ParallelLayoutComputations", INSTANCE);
                object[] args = { l, left, r, right, 5, 0.2f, 0.4f, 0.1f, true, Layout("Parallel"), false, false };

                fit.Invoke(null, args);
                Assert.That((int)computations.GetValue(l), Is.EqualTo(1), "Nonempty geometry is computed on the first fit.");
                fit.Invoke(null, args);
                Assert.That((int)computations.GetValue(l), Is.EqualTo(1), "An identical key reuses computed geometry.");

                args[11] = true;
                fit.Invoke(null, args);
                Assert.That((int)computations.GetValue(l), Is.EqualTo(2), "Changing handedness angle invalidates the cache.");
                args[4] = 7;
                fit.Invoke(null, args);
                Assert.That((int)computations.GetValue(l), Is.EqualTo(3), "A changed pair geometry factor invalidates the cache.");
                args[4] = 5;
                args[6] = 0.5f;
                fit.Invoke(null, args);
                Assert.That((int)computations.GetValue(l), Is.EqualTo(4), "A changed lane width invalidates the cache.");

                var replacement = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
                replacement.SetParent(root.transform, false);
                var replacementGeometry = Activator.CreateInstance(type, INSTANCE, null, new object[] { replacement }, null);
                args[2] = replacementGeometry;
                args[3] = replacement;
                fit.Invoke(null, args);
                Assert.That((int)computations.GetValue(l), Is.EqualTo(5), "Replacing the right partner invalidates the cache.");

                args[10] = true;
                fit.Invoke(null, args);
                Assert.That((int)computations.GetValue(l), Is.EqualTo(6), "Changing the cymbal geometry flag invalidates the cache.");

                Vector3 leftPosition = left.localPosition;
                Vector3 rightPosition = replacement.localPosition;
                args[5] = 0.45f;
                fit.Invoke(null, args);
                Assert.That((int)computations.GetValue(l), Is.EqualTo(6), "Lane center changes placement but not cached geometry.");
                Assert.That(left.localPosition.x - leftPosition.x, Is.EqualTo(replacement.localPosition.x - rightPosition.x).Within(0.00001f));
                Assert.That(left.localPosition.z, Is.EqualTo(replacement.localPosition.z).Within(0.00001f), "Translated pair keeps equal depth.");

                leftPosition = left.localPosition;
                rightPosition = replacement.localPosition;
                args[7] = 0.3f;
                fit.Invoke(null, args);
                Assert.That((int)computations.GetValue(l), Is.EqualTo(6), "Element center changes placement but not cached geometry.");
                Assert.That(left.localPosition.x - leftPosition.x, Is.EqualTo(-0.2f).Within(0.00001f));
                Assert.That(replacement.localPosition.x - rightPosition.x, Is.EqualTo(-0.2f).Within(0.00001f));
                Assert.That(left.localPosition.z, Is.EqualTo(replacement.localPosition.z).Within(0.00001f), "Translated pair keeps equal depth.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void ParallelEmptyGeometryAndInvalidInputsAreSafe()
        {
            var root = new GameObject("Empty Parallel geometry");
            var left = new GameObject("Left").transform;
            var right = new GameObject("Right").transform;
            left.SetParent(root.transform, false);
            right.SetParent(root.transform, false);
            try
            {
                var type = Geometry.GetNestedType("ModelGeometry", BindingFlags.NonPublic);
                var l = Activator.CreateInstance(type, INSTANCE, null, new object[] { left }, null);
                var r = Activator.CreateInstance(type, INSTANCE, null, new object[] { right }, null);
                var fit = Geometry.GetMethod("FitPair", STATIC);
                object[] args = { l, left, r, right, 5, 0f, 0.4f, 0f, true, Layout("Parallel"), true, false };
                Assert.DoesNotThrow(() => fit.Invoke(null, args));
                Assert.DoesNotThrow(() => fit.Invoke(null, args)); // Cached empty configuration.
                Assert.That((int)type.GetField("ParallelLayoutComputations", INSTANCE).GetValue(l), Is.EqualTo(1));
                args[11] = true;
                fit.Invoke(null, args);
                Assert.That((int)type.GetField("ParallelLayoutComputations", INSTANCE).GetValue(l), Is.EqualTo(2), "Handed angle invalidates layout cache.");
                foreach (int index in new[] { 5, 6, 7 })
                foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                {
                    args[5] = 0f; args[6] = 0.4f; args[7] = 0f;
                    args[index] = invalid;
                    fit.Invoke(null, args);
                    Assert.That(left.localPosition, Is.EqualTo(Vector3.zero));
                    Assert.That(right.localPosition, Is.EqualTo(Vector3.zero));
                    Assert.That(left.localScale, Is.EqualTo(Vector3.one));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void SurfaceLateralContactMatchesYZNotProjectedXZ()
        {
            // Both triangles occupy the same YZ triangle. Their X values vary
            // with Y; maximum difference is at Y=0, independent of XZ hulls.
            var a = new[] { new Vector3(2f, 0f, 0f), new Vector3(1f, 1f, 0f), new Vector3(2f, 0f, 1f) };
            var b = new[] { new Vector3(0f, 0f, 0f), new Vector3(0.5f, 1f, 0f), new Vector3(0f, 0f, 1f) };
            float separation = (float)Geometry.GetMethod("GetSurfaceLateralContact", STATIC).Invoke(null, new object[] { a, b });
            Assert.That(separation, Is.EqualTo(2f).Within(0.00001f));
            Assert.That(float.IsNaN((float)Geometry.GetMethod("GetSurfaceLateralContact", STATIC)
                .Invoke(null, new object[] { a, b.Select(p => p + Vector3.up * 3f).ToArray() })), Is.True,
                "Disjoint YZ projections cannot contact through X translation alone.");
        }

        [Test]
        public void SurfaceLateralContactIncludesCollinearYZEndpoints()
        {
            var left = new[] { new Vector3(3f, 0f, 0f), new Vector3(3f, 0f, 1f), new Vector3(3f, 0f, 2f) };
            var right = new[] { new Vector3(0f, 0f, -1f), new Vector3(1f, 0f, 1f), new Vector3(2f, 0f, 3f) };
            float contact = (float)Geometry.GetMethod("GetSurfaceLateralContact", STATIC)
                .Invoke(null, new object[] { left, right });
            Assert.That(contact, Is.EqualTo(2.5f).Within(0.00001f),
                "Projected collinear intervals overlap at a left endpoint inside a right edge.");
        }

        [Test]
        public void ParallelCoincidentThinSurfacesNeverCollapseToSamePosition()
        {
            var root = new GameObject("Thin base pair");
            var mesh = new Mesh { name = "Cymbal Base" };
            mesh.vertices = new[] { new Vector3(-1f, 0f, -1f), new Vector3(1f, 0f, -1f),
                new Vector3(-1f, 0f, 1f), new Vector3(1f, 0f, 1f) };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.RecalculateBounds();
            var left = new GameObject("Left").transform;
            var right = new GameObject("Right").transform;
            foreach (var model in new[] { left, right })
            {
                model.SetParent(root.transform, false);
                model.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                model.localScale = new Vector3(0.2f, 0.1f, 0.1f);
                model.localRotation = Quaternion.Euler(17f, 0f, 11f);
            }
            try
            {
                var type = Geometry.GetNestedType("ModelGeometry", BindingFlags.NonPublic);
                var l = Activator.CreateInstance(type, INSTANCE, null, new object[] { left }, null);
                var r = Activator.CreateInstance(type, INSTANCE, null, new object[] { right }, null);
                Geometry.GetMethod("FitPair", STATIC).Invoke(null, new object[] { l, left, r, right, 5, 0f, 0.4f, 0f,
                    true, Layout("Parallel"), true, true });
                Assert.That(right.localPosition.x, Is.GreaterThan(left.localPosition.x), "Identical thin meshes must not be stacked.");
                Assert.That(right.localPosition.z, Is.EqualTo(left.localPosition.z).Within(0.00001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        private static bool ProjectedOverlap(Vector3[] left, Vector3[] right, float rightShift)
        {
            var a = left.Select(p => new Vector2(p.x, p.z)).ToArray();
            var b = right.Select(p => new Vector2(p.x + rightShift, p.z)).ToArray();
            foreach (var points in new[] { a, b })
            for (int i = 0; i < points.Length; i++)
            for (int j = i + 1; j < points.Length; j++)
            {
                var edge = points[j] - points[i];
                if (edge.sqrMagnitude < 1e-12f) continue;
                var axis = new Vector2(-edge.y, edge.x).normalized;
                if (a.Max(p => Vector2.Dot(p, axis)) <= b.Min(p => Vector2.Dot(p, axis)) + 0.000001f ||
                    b.Max(p => Vector2.Dot(p, axis)) <= a.Min(p => Vector2.Dot(p, axis)) + 0.000001f) return false;
            }
            return true;
        }

        [TestCase(true, 1)]
        [TestCase(false, 0)]
        public void LegacyAlternateLayoutMigrationIsNarrow(bool alternate, int expected)
        {
            var migration = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("YARG.Settings.SettingsMigration")).First(t => t != null);
            var jsonType = migration.GetMethod("Migrate", STATIC).GetParameters()[0].ParameterType;
            var parse = jsonType.GetMethod("Parse", new[] { typeof(string) });
            var item = jsonType.GetProperty("Item", new[] { typeof(string) });
            var json = parse.Invoke(null, new object[] { "{\"SettingsSchemaVersion\":1,\"AlternateEliteFlamLayout\":" + alternate.ToString().ToLowerInvariant() + "}" });
            object[] args = { json, false };
            migration.GetMethod("Migrate", STATIC).Invoke(null, args);
            Assert.That(item.GetValue(json, new object[] { "EliteFlamLayout" }).ToString(), Is.EqualTo(expected.ToString()));
            Assert.That(item.GetValue(json, new object[] { "AlternateEliteFlamLayout" }), Is.Null);
            args[0] = parse.Invoke(null, new object[] { "{\"SettingsSchemaVersion\":1,\"EliteFlamLayout\":2,\"AlternateEliteFlamLayout\":true}" });
            migration.GetMethod("Migrate", STATIC).Invoke(null, args);
            Assert.That(item.GetValue(args[0], new object[] { "EliteFlamLayout" }).ToString(), Is.EqualTo("2"), "Explicit new selection wins.");
        }

        [Test]
        public void ElitePairGeometryRejectsEveryNonfiniteInput()
        {
            var placement = Geometry.GetMethod("GetPairPlacement", STATIC);
            for (int i = 0; i < 7; i++)
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                object[] args = { -0.1f, 0.1f, -0.2f, 0.2f, 0f, 0.4f, 2f, 5, 99f, 99f };
                args[i] = invalid;
                placement.Invoke(null, args);
                Assert.That(args[8], Is.EqualTo(0f));
                Assert.That(args[9], Is.EqualTo(0f));
            }
        }

        [Test]
        public void ElitePairGeometryPreservesAuthoredTransformAndUsesOppositeHighwayNormalRotations()
        {
            var root = new GameObject("Pair authored transform test");
            var model = GameObject.CreatePrimitive(PrimitiveType.Cube);
            model.transform.SetParent(root.transform, false);
            var scale = new Vector3(0.3f, 0.07f, 0.2f);
            var position = new Vector3(0.03f, 0.02f, -0.01f);
            var rotation = Quaternion.Euler(4f, 9f, 3f);
            model.transform.localScale = scale;
            model.transform.localPosition = position;
            model.transform.localRotation = rotation;
            try
            {
                var type = Geometry.GetNestedType("ModelGeometry", BindingFlags.NonPublic);
                var geometry = Activator.CreateInstance(type, INSTANCE, null, new object[] { model.transform }, null);
                foreach (float angle in new[] { -25f, 0f, 25f })
                {
                    type.GetMethod("Apply", INSTANCE).Invoke(geometry, new object[] { model.transform, 0.8f, angle, 0f, false });
                    Assert.That(model.transform.localScale.y, Is.EqualTo(scale.y));
                    Assert.That(model.transform.localScale.z, Is.EqualTo(scale.z));
                    Assert.That(model.transform.localScale.x, Is.EqualTo(scale.x * 0.8f));
                    Assert.That(Quaternion.Angle(model.transform.localRotation,
                        Quaternion.AngleAxis(angle, Vector3.up) * rotation), Is.LessThan(0.001f));
                    var direction = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.right;
                    if (angle == 0f) Assert.That(direction.z, Is.Zero);
                    else Assert.That(Mathf.Sign(direction.z), Is.EqualTo(-Mathf.Sign(angle)));
                    type.GetMethod("Reset", INSTANCE).Invoke(geometry, new object[] { model.transform });
                    Assert.That(model.transform.localPosition, Is.EqualTo(position));
                    Assert.That(model.transform.localScale, Is.EqualTo(scale));
                    Assert.That(Quaternion.Angle(model.transform.localRotation, rotation), Is.LessThan(0.001f));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
