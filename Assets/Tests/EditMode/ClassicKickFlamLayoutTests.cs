// pattern: Functional Core
using System;
using System.Reflection;
using NUnit.Framework;

namespace YARG.Tests.EditMode
{
    public sealed class ClassicKickFlamLayoutTests
    {
        private static (float Left, float Right) Endpoints(int lanes, int first, int second, bool five = false, bool lefty = false)
        {
            var type = Type.GetType("YARG.Gameplay.Visuals.ClassicKickFlamLayout, Assembly-CSharp");
            Assert.That(type, Is.Not.Null, "Classic kick flams need layout-aware exposed inner endpoints.");
            var method = type.GetMethod("GetInnerColumns", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            return ((float, float))method.Invoke(null, new object[] { lanes, first, second, five, lefty });
        }

        [TestCase(4, 1, 2, .5f, 3.5f)]
        [TestCase(4, 2, 3, .5f, 1.5f)]
        [TestCase(4, 1, 3, .5f, 2.5f)]
        [TestCase(5, 1, 3, .5f, 4.5f)]
        [TestCase(7, 0, 6, 2.5f, 3.5f)]
        public void TwoHandChordsExposeTheSketchEndpoints(int lanes, int first, int second, float left, float right)
        {
            var actual = Endpoints(lanes, first, second);
            Assert.That(actual.Left, Is.EqualTo(left));
            Assert.That(actual.Right, Is.EqualTo(right));
        }

        [TestCase(4, -1, -1)]
        [TestCase(4, 1, -1)]
        [TestCase(7, 3, -1)]
        public void ZeroOrOneHandUsesTheOrdinaryOneAndAHalfColumnBars(int lanes, int first, int second)
        {
            var actual = Endpoints(lanes, first, second);
            Assert.That(actual.Left, Is.EqualTo(1.5f));
            Assert.That(actual.Right, Is.EqualTo(lanes - 1.5f));
        }

        [Test]
        public void FiveLaneRetainsFixedBarsWithTwoHands()
        {
            var actual = Endpoints(5, 1, 2, true);
            Assert.That(actual.Left, Is.EqualTo(1.5f));
            Assert.That(actual.Right, Is.EqualTo(3.5f));
        }

        [Test]
        public void ResolvedLayoutsAreMirrorSymmetricAndKeepInnerCapsInEmptyColumns()
        {
            for (int lanes = 4; lanes <= 9; lanes++)
            for (int first = 0; first < lanes; first++)
            for (int second = first + 1; second < lanes; second++)
            {
                var normal = Endpoints(lanes, first, second);
                var mirrored = Endpoints(lanes, lanes - 1 - first, lanes - 1 - second, false, true);
                Assert.That(mirrored.Left, Is.EqualTo(lanes - normal.Right));
                Assert.That(mirrored.Right, Is.EqualTo(lanes - normal.Left));
                int leftColumn = (int)normal.Left;
                int rightColumn = (int)normal.Right;
                Assert.That(leftColumn, Is.Not.EqualTo(first).And.Not.EqualTo(second));
                Assert.That(rightColumn, Is.Not.EqualTo(first).And.Not.EqualTo(second));
                Assert.That(normal.Left, Is.LessThan(normal.Right));
                Assert.That(normal.Left, Is.GreaterThan(0));
                Assert.That(normal.Right, Is.LessThan(lanes));
            }
        }
    }
}
