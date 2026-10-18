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
                    type.GetMethod("Apply", INSTANCE).Invoke(geometry, new object[] { model.transform, 0.8f, angle, 0f });
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
