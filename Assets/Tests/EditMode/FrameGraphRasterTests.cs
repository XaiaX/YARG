// pattern: Imperative Shell
using System;
using NUnit.Framework;
using UnityEngine;

namespace YARG.Tests.EditMode
{
    public sealed class FrameGraphRasterTests
    {
        private static void Fill(Color32[] pixels, int height, double[] history, int count, int head)
        {
            var type = Type.GetType("YARG.Persistent.FrameGraphRaster, Assembly-CSharp");
            Assert.That(type, Is.Not.Null, "Graph bars should be batched into a reusable texture.");
            type.GetMethod("Fill").Invoke(null, new object[] { pixels, height, history, count, head, 50.0, 20.0 });
        }

        [Test]
        public void WrappedHistoryDrawsNewestAtRightWithBudgetColorsAndClippedHeight()
        {
            var pixels = new Color32[40];
            Fill(pixels, 10, new double[] { 25, 60, 10, 20 }, 4, 2);
            // Chronological order is 10, 20, 25, 60 ms after wrapping.
            Assert.That(pixels[0], Is.EqualTo(new Color32(51, 204, 128, 255)));
            Assert.That(pixels[2], Is.EqualTo(new Color32(255, 179, 51, 255)));
            Assert.That(pixels[3], Is.EqualTo(new Color32(255, 38, 89, 255)));
            Assert.That(pixels[4], Is.EqualTo(pixels[0]));
            Assert.That(pixels[8].a, Is.Zero);
            Assert.That(pixels[39].a, Is.EqualTo(255), "A long hitch must clip to the graph's top.");
        }

        [Test]
        public void ReusedBufferClearsOldBarsAndLeavesUnfilledHistoryTransparent()
        {
            var pixels = new Color32[40];
            Fill(pixels, 10, new double[] { 50, 50, 50, 50 }, 4, 0);
            Fill(pixels, 10, new double[] { 10, 0, 0, 0 }, 1, 1);
            int visible = 0;
            foreach (var pixel in pixels) if (pixel.a > 0) visible++;
            Assert.That(visible, Is.EqualTo(2));
            Assert.That(pixels[3].a, Is.EqualTo(255));
            Assert.That(pixels[7].a, Is.EqualTo(255));
        }
    }
}
