using System;
using TMPro;
using UnityEngine;

namespace YARG.Menu.HighwayConfiguration
{
    /// <summary>Localized labels and shared geometry for the three layout transform buttons.</summary>
    public static class EliteDrumsTransformButtonLayout
    {
        public const float HORIZONTAL_PADDING = 32f;
        public const float DEFAULT_FONT_SIZE = 24f;
        public const float MIN_FONT_SIZE = 18f;
        public const float DEFAULT_GAP = 12f;

        // Decorate the localized values, without changing their case or TMP font style.
        public static string ShiftLeftLabel(string localized) => "\u2190 " + localized;
        public static string ReverseLabel(string localized) => "\u21C4  " + localized + "  \u21C6";
        public static string ShiftRightLabel(string localized) => localized + " \u2192";

        /// <summary>
        /// Measures all three labels (including hidden buttons), uses the largest preferred width
        /// plus 32 units for every wrapper, and centers the row inside the available section.
        /// Wrappers must be direct children of section; left/right are normalized section bounds.
        /// Reapply after localization, font, or section-width changes. Existing vertical anchors,
        /// positions, heights, casing and font style are preserved. No shared font asset is mutated.
        /// Returns false without moving wrappers if even the readable floor cannot fit the row.
        /// </summary>
        public static bool Apply(RectTransform[] wrappers, TMP_Text[] texts, RectTransform section,
            float left = 0f, float right = 1f, float gap = DEFAULT_GAP,
            float fontSize = DEFAULT_FONT_SIZE, float minimumFontSize = MIN_FONT_SIZE)
        {
            if (wrappers == null || wrappers.Length != 3)
                throw new ArgumentException("Exactly three transform wrappers are required.", nameof(wrappers));
            if (texts == null || texts.Length != 3)
                throw new ArgumentException("Exactly three transform labels are required.", nameof(texts));
            if (section == null) throw new ArgumentNullException(nameof(section));
            if (!IsFinite(left) || !IsFinite(right) || left < 0f || right > 1f || right <= left ||
                !IsFinite(gap) || gap < 0f || !IsFinite(fontSize) || !IsFinite(minimumFontSize) ||
                minimumFontSize < MIN_FONT_SIZE || fontSize < minimumFontSize)
                throw new ArgumentOutOfRangeException(nameof(left), "Invalid section bounds, gap, or font size.");
            for (int i = 0; i < 3; i++)
            {
                if (wrappers[i] == null || texts[i] == null || wrappers[i].parent != section)
                    throw new ArgumentException("Each wrapper and text must exist, and wrappers must be section children.");
                for (int j = 0; j < i; j++)
                    if (wrappers[i] == wrappers[j] || texts[i] == texts[j])
                        throw new ArgumentException("Transform wrappers and texts must be distinct.");
            }
            float available = section.rect.width * (right - left);
            if (!IsFinite(available) || available <= gap * 2f) return false;
            float maxWidth = (available - gap * 2f) / 3f;
            float width = Measure(texts, fontSize);
            if (width > maxWidth)
            {
                width = Measure(texts, minimumFontSize);
                if (width > maxWidth)
                {
                    // Extremely narrow canvases retain equal widths and readable text by wrapping.
                    foreach (var text in texts)
                    {
                        text.textWrappingMode = TextWrappingModes.Normal;
                        if (text.GetPreferredValues(text.text, Mathf.Max(1f, maxWidth - HORIZONTAL_PADDING),
                            Mathf.Infinity).y > wrappers[0].rect.height - 4f) return false;
                    }
                    width = maxWidth;
                }
                else
                {
                    // Use TMP measurements rather than assuming perfectly linear scaling of rich text.
                    float low = minimumFontSize;
                    float high = fontSize;
                    for (int i = 0; i < 12; i++)
                    {
                        float candidate = (low + high) * 0.5f;
                        if (Measure(texts, candidate) <= maxWidth) low = candidate;
                        else high = candidate;
                    }
                    width = Measure(texts, low);
                }
            }
            float rowWidth = width * 3f + gap * 2f;
            float start = left * section.rect.width + (available - rowWidth) * 0.5f;
            for (int i = 0; i < 3; i++)
            {
                RectTransform wrapper = wrappers[i];
                float center = (start + width * 0.5f + i * (width + gap)) / section.rect.width;
                wrapper.anchorMin = new Vector2(center, wrapper.anchorMin.y);
                wrapper.anchorMax = new Vector2(center, wrapper.anchorMax.y);
                wrapper.pivot = new Vector2(0.5f, wrapper.pivot.y);
                wrapper.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                wrapper.anchoredPosition = new Vector2(0f, wrapper.anchoredPosition.y);
            }
            return true;
        }

        private static float Measure(TMP_Text[] texts, float fontSize)
        {
            float maximum = 0f;
            foreach (TMP_Text text in texts)
            {
                text.enableAutoSizing = false;
                text.fontSize = fontSize;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                maximum = Mathf.Max(maximum, text.GetPreferredValues(text.text).x);
            }
            return maximum + HORIZONTAL_PADDING;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
