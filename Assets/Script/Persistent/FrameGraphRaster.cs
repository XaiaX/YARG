// pattern: Functional Core
using System;
using UnityEngine;

namespace YARG.Persistent
{
    /// <summary>Fills a reusable pixel buffer in texture order (bottom row first).</summary>
    public static class FrameGraphRaster
    {
        public static void Fill(Color32[] pixels, int height, double[] history, int count, int head,
            double topMs, double budgetMs)
        {
            if (history == null || history.Length == 0 || height <= 0 || pixels == null ||
                pixels.Length != (long) history.Length * height || topMs <= 0 ||
                double.IsNaN(topMs) || double.IsInfinity(topMs))
                throw new ArgumentException("invalid graph dimensions or scale");

            int width = history.Length;
            count = Math.Clamp(count, 0, width);
            head = (head % width + width) % width;
            Array.Clear(pixels, 0, pixels.Length);
            for (int i = 0; i < count; i++)
            {
                double ms = history[(head - 1 - i + width) % width];
                if (double.IsNaN(ms) || ms <= 0) continue;
                int barHeight = (int) Math.Ceiling(Math.Min(height, ms / topMs * height));
                Color32 color = budgetMs > 0 && ms > budgetMs * 1.5 ? new Color32(255, 38, 89, 255) :
                    budgetMs > 0 && ms > budgetMs * 1.05 ? new Color32(255, 179, 51, 255) :
                    new Color32(51, 204, 128, 255);
                int x = width - 1 - i;
                for (int y = 0; y < barHeight; y++) pixels[y * width + x] = color;
            }
        }
    }
}
