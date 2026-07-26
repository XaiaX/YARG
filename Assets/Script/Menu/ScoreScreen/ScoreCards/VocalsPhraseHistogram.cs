using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.UI;
using YARG.Core.Engine.Vocals;
using YARG.Core.Engine.Vocals.Engines;
using YARG.Gameplay.Vocals;
using YARG.Localization;

namespace YARG.Menu.ScoreScreen
{
    public static class VocalsPhraseHistogram
    {
        // Matches the offset histogram's footprint exactly (see OFFSET_HISTOGRAM_* in ScoreCard).
        private const float GRAPH_HEIGHT = 132f;
        private const float HORIZONTAL_MARGIN = 54f;
        // Floor so a fully-missed (0%) phrase still shows a clearly visible stub, kept just under the
        // Messy cutoff line so it still reads as "below Messy".
        private const float BAR_MIN_HEIGHT = 9f;
        private const float BAR_ALPHA = 0.875f;
        private const float BAR_DIM_TINT = 0.82f;       // multiplier for even Awesome bars (applied as RawImage color tint)
        private const float BAR_GRADIENT_BOTTOM = 0.55f; // fraction of top color at bar bottom (gray bar vertical gradient)
        // Re-introduce a half-gap between bars when the phrase count is low enough to benefit.
        private const int BAR_GAP_THRESHOLD = 25;
        private const float BAR_HALF_GAP_PX = 1.5f;
        private const float BAR_EDGE_PAD = BAR_HALF_GAP_PX * 2f; // fixed outer inset, always present

        private const float TALLY_ROW_HEIGHT = 22f;
        private const float TALLY_SPACING = 2f;
        private const float TALLY_SIDE_PADDING = HORIZONTAL_MARGIN; // line table edges up with the bars
        private const float TALLY_COUNT_WIDTH = 56f;
        private const float AWESOME_TIER_SPACING = 16f;     // gap between Awesome-row tier entries
        private const float AWESOME_ICON_DIM = 0.35f;       // dimmed (impossible-part) Awesome icon — RGB multiplier (darker)
        private const float DIVIDER_THICKNESS = 2f;
        private const float SECTION_SPACING = 14f;
        private const float BAR_BASE_Y = 1f;

        // Match the score card's existing section-header and stat-label text styling (see
        // ScoreCard.prefab) so the summary blends in rather than standing out.
        private const float TEXT_SIZE = 20f;
        private static readonly Color HeaderColor = new(0.8509804f, 0.8509804f, 1f);          // section header lavender
        private static readonly Color CoolGrayColor = new(0.48235294f, 0.49803922f, 0.60392157f); // #7b7f9a (tier labels)
        private static readonly Color MutedColor    = new(0.49019608f, 0.49019608f, 0.6392157f);  // #7D7DA3 (zero counts)
        private static readonly Color GoldColor      = new(1f,        0.83921569f, 0.25882353f); // #FFD642 Awesome bar top
        private static readonly Color UtOrangeColor  = new(1f,        0.51764706f, 0.07450980f); // #FF8413 Awesome bar bottom
        private static readonly Color BarDefaultColor = new(0.47843137f, 0.47843137f, 0.47843137f); // #7a7a7a Gray (Light #4) — dim bars
        private static readonly Color PartyMissMarkerColor = new(0.30f, 0.30f, 0.30f); // #4d4d4d — Miss-phrase buffer (darker than the axis)
        private static readonly Color BarBrightColor  = new(0.62745098f, 0.62745098f, 0.62745098f); // #a0a0a0 Gray (Light #3.5) — bright bars
        private static readonly Color BarCapColor      = new(0.9607843f,  0.9607843f,  0.9607843f,  1f); // #F5F5F5 White Smoke
        // Tier cutoff line colors (branding palette).
        private static readonly Color LineColorMessy  = new(0.93725490f, 0.20392157f, 0.21960784f); // #ef3438 Imperial Red
        private static readonly Color LineColorOkay   = new(0.72156863f, 0.72156863f, 0.72156863f); // #b8b8b8 Silver (Light #3)
        private static readonly Color LineColorGood   = new(0.27058824f, 0.84705882f, 0.99607843f); // #45d8fe Vivid Sky Blue
        private static readonly Color LineColorStrong = new(0.16862745f, 0.88235294f, 0.55294118f); // #2be18d Emerald

        // The brand fonts used by the card: Red Hat Display (headers), Barlow (body). Resolved
        // lazily from already-loaded assets so we don't need serialized prefab references.
        private const string HEADER_FONT_NAME = "RedHatDisplay-ExtraBold";
        private const string LABEL_FONT_NAME = "Barlow-Medium";
        private static TMP_FontAsset _headerFont;
        private static TMP_FontAsset _labelFont;
        // One 1×N vertical gradient texture per tier region; created once and reused across score screens.
        private const int GRADIENT_TEX_WIDTH = 256;
        private const float REGION_FILL_ALPHA = 0.125f;
        private const float REGION_FADE_MIN = 0.375f;
        private static readonly Texture2D[] _tierGradientTextures = new Texture2D[5];

        private static Texture2D _awesomeBarGradient;
        private static Texture2D _grayBarGradient;

        // Party Vocals: cached multi-segment awesome bar gradients (one per tier).
        // Bottom is always UT Orange → Gold (the existing awesome ramp); the top transitions through
        // Harmony line colors from VocalTrack.Colors (indices 0=Cyan/HARM1, 1=Orange/HARM2,
        // 2=Yellow/HARM3). These are the same colors used for the tallies and bar gradient tops.
        private static readonly Color Harm1Cyan    = new(0f, 0.800f, 1f);
        private static readonly Color Harm2Orange  = new(1f, 0.522f, 0f);
        private static readonly Color Harm3Yellow  = new(1f, 0.859f, 0f);

        public static void Build(RectTransform parent, IReadOnlyList<float> percents,
            Func<Transform, string, TextAlignmentOptions, TextMeshProUGUI> labelFactory, Color accentColor,
            int percussionHits, int percussionTotal, IReadOnlyList<PhraseGrade> phraseGrades = null,
            IReadOnlyList<IReadOnlyList<PartyPartResult>> partyPartResults = null, double awesomeThreshold = 0,
            int harmonyPartIndex = 0)
        {
            if (parent == null || percents == null || percents.Count == 0)
            {
                return;
            }

            // Legacy Solo/traditional Harmony captures only aggregate phrase percentages. Synthesize
            // the one selected HARM result at the display boundary so those summaries use the same
            // Party renderer as the per-part path, without changing scoring or gameplay capture.
            bool partyMode = phraseGrades != null && phraseGrades.Count > 0;
            bool synthesizedLegacySummary = !partyMode;
            if (synthesizedLegacySummary)
            {
                int partIndex = Mathf.Clamp(harmonyPartIndex, 0, 2);
                var displayGrades = new List<PhraseGrade>(percents.Count);
                var displayPartResults = new List<IReadOnlyList<PartyPartResult>>(percents.Count);
                for (int i = 0; i < percents.Count; i++)
                {
                    double normalized = Mathf.Clamp01(percents[i]);
                    double meter = normalized * awesomeThreshold;
                    displayGrades.Add(awesomeThreshold > 0 && meter >= awesomeThreshold
                        ? PhraseGrade.Awesome
                        : PhraseGrade.Miss);
                    // Legacy summaries are one-band displays. Keep the selected HARM index separate
                    // as a color choice rather than using it as the segment index; otherwise HARM2/3
                    // would create empty lower bands just to reach their color.
                    displayPartResults.Add(new[] { new PartyPartResult(0, meter) });
                }

                phraseGrades = displayGrades;
                partyPartResults = displayPartResults;
            }

            // Force a layout pass so the bars' world positions are final before we snap 1px elements
            // (caps, dividers) to screen pixels.
            if (parent.root is RectTransform layoutRoot)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(layoutRoot);
            }

            EnsureFonts();

            var rootRect = CreateLayoutColumn("Vocals Phrase Summary", parent, SECTION_SPACING);
            // Sit at the top of the advanced container (above the now-empty offset-histogram slot),
            // so the header lines up with "PERFORMANCE" in the basic view.
            rootRect.SetAsFirstSibling();

            // Section header — centered, uppercase, subdued, matching "PERFORMANCE" above it.
            var header = labelFactory(rootRect, "Header", TextAlignmentOptions.Top);
            header.text = Localize.Key("Menu.ScoreScreen.PhraseSummaryHeader");
            StyleText(header, _headerFont, HeaderColor, TextAlignmentOptions.Top);
            AddLayoutElement(header.rectTransform, 50f);

            // Highest harmony part the chart offers (duet = 2, trio = 3). Used to dim impossible
            // tier icons in the Awesome tally row (e.g. a duet can never hit a triple-awesome).
            int maxHarmonyParts = 1;
            if (partyPartResults != null)
            {
                foreach (var phrase in partyPartResults)
                {
                    if (phrase == null) continue;
                    foreach (var pr in phrase)
                    {
                        int partCount = pr.PartIndex + 1;
                        if (partCount > maxHarmonyParts) maxHarmonyParts = partCount;
                    }
                }
            }

            BuildGraph(rootRect, percents, phraseGrades, partyPartResults, awesomeThreshold, maxHarmonyParts,
                harmonyPartIndex, synthesizedLegacySummary);
            BuildTally(rootRect, percents, phraseGrades, maxHarmonyParts, labelFactory, accentColor, percussionHits, percussionTotal);
        }

        private static void BuildGraph(RectTransform parent, IReadOnlyList<float> percents,
            IReadOnlyList<PhraseGrade> phraseGrades, IReadOnlyList<IReadOnlyList<PartyPartResult>> partyPartResults,
            double awesomeThreshold, int maxHarmonyParts, int harmonyPartIndex, bool synthesizedLegacySummary)
        {
            var graphObject = new GameObject("Graph", typeof(RectTransform));
            var graphRect = (RectTransform) graphObject.transform;
            graphRect.SetParent(parent, false);
            AddLayoutElement(graphRect, GRAPH_HEIGHT);

            // Inset the bars to match the offset histogram's horizontal margins.
            var barsObject = new GameObject("Bars", typeof(RectTransform));
            var barsRect = (RectTransform) barsObject.transform;
            barsRect.SetParent(graphRect, false);
            barsRect.anchorMin = Vector2.zero;
            barsRect.anchorMax = Vector2.one;
            barsRect.offsetMin = new Vector2(HORIZONTAL_MARGIN, 0f);
            barsRect.offsetMax = new Vector2(-HORIZONTAL_MARGIN, 0f);

            // Inner rect inset by BAR_EDGE_PAD on each side — bars live here so the fixed outer
            // margin is independent of the inter-bar half-gap. Regions/axis stay on barsRect.
            var innerBarsObject = new GameObject("BarsInner", typeof(RectTransform));
            var innerBarsRect = (RectTransform) innerBarsObject.transform;
            innerBarsRect.SetParent(barsRect, false);
            innerBarsRect.anchorMin = Vector2.zero;
            innerBarsRect.anchorMax = Vector2.one;
            innerBarsRect.offsetMin = new Vector2(BAR_EDGE_PAD, 0f);
            innerBarsRect.offsetMax = new Vector2(-BAR_EDGE_PAD, 0f);

            float lineThickness = Mathf.Ceil(PixelUnit(barsRect));
            float onePixel = PixelUnit(barsRect);

            // Baseline axis line.
            var axis = new GameObject("XAxis", typeof(RectTransform), typeof(Image));
            var axisRect = (RectTransform) axis.transform;
            axisRect.SetParent(barsRect, false);
            axisRect.anchorMin = new Vector2(0f, 0f);
            axisRect.anchorMax = new Vector2(1f, 0f);
            // Axis top sits at BAR_BASE_Y so it doesn't overlap with the bar bottoms.
            axisRect.offsetMin = new Vector2(0f, BAR_BASE_Y - 3f);
            axisRect.offsetMax = new Vector2(0f, BAR_BASE_Y);
            var axisImage = axis.GetComponent<Image>();
            axisImage.color = new Color(1f, 1f, 1f, 0.25f);
            axisImage.raycastTarget = false;

            // Backdrop: party mode uses a single uniform grey wash (full-height per-part bars don't
            // line up with tier cutoffs); solo keeps the tier regions + boundary lines.
            bool partyMode = phraseGrades != null && phraseGrades.Count > 0;
            if (partyMode)
            {
                DrawSolidBackdrop(barsRect);
            }
            else
            {
                // Tier background regions at uniform 0.125 alpha, drawn BEHIND the bars.
                DrawTierRegions(barsRect);

                // Subtle tier boundary lines on top of the regions, still behind bars.
                for (int tier = 1; tier <= 4; tier++)
                {
                    var grade = (VocalPhraseGrade) tier;
                    float threshold = (float) grade.LowerBound();
                    var lineColor = grade switch
                    {
                        VocalPhraseGrade.Messy => LineColorMessy,
                        VocalPhraseGrade.Okay  => LineColorOkay,
                        VocalPhraseGrade.Good  => LineColorGood,
                        _                      => LineColorStrong
                    };
                    lineColor.a = 0.03125f;

                    var cutoffObj = new GameObject($"Cutoff {grade}", typeof(RectTransform), typeof(Image));
                    var cutoffRect = (RectTransform) cutoffObj.transform;
                    cutoffRect.SetParent(barsRect, false);
                    cutoffRect.anchorMin = new Vector2(0f, 0f);
                    cutoffRect.anchorMax = new Vector2(1f, 0f);
                    cutoffRect.pivot = new Vector2(0.5f, 0.5f);
                    cutoffRect.sizeDelta = new Vector2(0f, lineThickness);
                    cutoffRect.anchoredPosition = new Vector2(0f, BAR_BASE_Y + threshold * GRAPH_HEIGHT);
                    var cutoffImg = cutoffObj.GetComponent<Image>();
                    cutoffImg.color = lineColor;
                    cutoffImg.raycastTarget = false;
                }
            }


            // Push bars to the top of the render order so they sit in front of regions and lines.
            innerBarsRect.SetAsLastSibling();

            // Party-mode diagonal "transparency" hatch (clear + pure white), drawn once across the
            // bars rectangle behind the bars. Added as the first child of the bars rect so every
            // per-bar element renders above it; absent parts (nothing drawn) reveal the hatch.
            if (partyMode)
            {
                AddPartyHatchField(innerBarsRect, BAR_BASE_Y, GRAPH_HEIGHT);
            }

            int count = percents.Count;
            float halfGap = count < BAR_GAP_THRESHOLD ? BAR_HALF_GAP_PX : 0f;

            // partyMode was set above (with the backdrop); party bars render full-height segments.

            for (int i = 0; i < count; i++)
            {
                var grade = VocalPhraseGradeExtensions.Classify(percents[i]);

                // Party bars render one segment per available harmony part and are full-height
                // (the per-part fills encode performance, not the bar height). Solo bars keep
                // height = phrase score, as before.
                bool partyBar = partyMode && partyPartResults != null && i < partyPartResults.Count;
                float height = partyBar
                    ? (GRAPH_HEIGHT - BAR_BASE_Y)
                    : Mathf.Max(BAR_MIN_HEIGHT, Mathf.Clamp01(percents[i]) * GRAPH_HEIGHT);

                bool isBright = (i % 2) == 1; // odd bars brighter

                var barObject = new GameObject($"Bar {i}", typeof(RectTransform));
                var barRect = (RectTransform) barObject.transform;
                barRect.SetParent(innerBarsRect, false);
                barRect.anchorMin = new Vector2(i / (float) count, 0f);
                barRect.anchorMax = new Vector2((i + 1f) / count, 0f);
                barRect.pivot = new Vector2(0.5f, 0f);
                barRect.offsetMin = new Vector2(halfGap, BAR_BASE_Y);
                barRect.offsetMax = new Vector2(-halfGap, BAR_BASE_Y + height);

                if (partyBar)
                {
                    BuildPartyBar(barRect, partyPartResults[i], phraseGrades[i], awesomeThreshold, isBright, height, onePixel,
                        maxHarmonyParts, harmonyPartIndex, synthesizedLegacySummary);
                    continue;
                }

                // In party mode, an awesome bar is one where the phrase grade is not Miss
                // (i.e. Awesome/DoubleAwesome/TripleAwesome). In solo mode, use the fraction-based
                // classification as before.
                bool isAwesomeBar = grade == VocalPhraseGrade.Awesome;

                if (isAwesomeBar)
                {
                    var rawImage = barObject.AddComponent<RawImage>();
                    // Vertical gradient: gold (#FFD642) at top, UT Orange (#FF8413) at bottom.
                    // Brightness alternates via RawImage tint (same texture, no second allocation).
                    rawImage.texture = GetOrCreateAwesomeBarGradient();
                    float tint = isBright ? 1f : BAR_DIM_TINT;
                    rawImage.color = new Color(tint, tint, tint, BAR_ALPHA);
                    rawImage.raycastTarget = false;
                }
                else
                {
                    // Vertical gradient: top = bar color, bottom = BAR_GRADIENT_BOTTOM fraction of it.
                    // One normalized texture is shared; the tint color shifts between dim and bright.
                    var rawImage = barObject.AddComponent<RawImage>();
                    rawImage.texture = GetOrCreateGrayBarGradient();
                    var baseColor = isBright ? BarBrightColor : BarDefaultColor;
                    rawImage.color = new Color(baseColor.r, baseColor.g, baseColor.b, BAR_ALPHA);
                    rawImage.raycastTarget = false;

                    // Tier-colored cap line across the top of non-gold bars.
                    var capObj = new GameObject("Cap", typeof(RectTransform), typeof(Image));
                    var capRect = (RectTransform) capObj.transform;
                    capRect.SetParent(barObject.transform, false);
                    capRect.anchorMin = new Vector2(0f, 1f);
                    capRect.anchorMax = new Vector2(1f, 1f);
                    capRect.pivot = new Vector2(0.5f, 1f);
                    capRect.sizeDelta = new Vector2(0f, lineThickness);
                    capRect.anchoredPosition = new Vector2(0f, lineThickness * 0.5f);
                    var capImage = capObj.GetComponent<Image>();
                    var capColor = BarCapColor;
                    capColor.a = 0.5f;
                    capImage.color = capColor;
                    capImage.raycastTarget = false;
                }
            }
        }

        // Party-mode Awesome bar: a full-height bar split into one band per available harmony part
        // (lowest part number at the bottom), plus a thin status buffer at the very bottom that is
        // gold when the phrase ranked Awesome/Double/Triple and dark grey when it was a Miss. Each
        // band fills solid with its harmony line color if that part reached Awesome; otherwise it
        // fills from the band's bottom to (meter / threshold) in the harmony color dimmed 10%.
        // Thin dark-grey dividers separate the bands.
        private const float PARTY_BUFFER_HEIGHT = 5f;
        // Dim percentages are ADDITIVE per band (factor = 1 - sum): a not-awesome band on an odd
        // bar dims 30% + 10% = 40%. Absent-part gaps are exempt from the stripe.
        private const float PARTY_DIM_NOT_AWESOME = 0.30f;  // not-awesome available band
        private const float PARTY_STRIPE_DIM       = 0.10f;  // odd-bar alternating stripe (available content)
        private const float PARTY_GRADIENT_BOTTOM  = 0.80f;  // segment fills shade to 20% darker at the bottom
        private const float PARTY_TRACK_DIM         = 0.75f;  // available-segment lane background (harmony x 0.25)
        // Diagonal-stripe "transparency" backdrop shown wherever no part is available.
        private const int STRIPE_TEX_SIZE = 64;              // must be a multiple of (STRIPE_WIDTH*2) to tile seamlessly
        private const int STRIPE_WIDTH    = 4;               // on-pixels per stripe half-period
        private static readonly Color STRIPE_DARK  = new Color(0f, 0f, 0f, 0f);   // clear — lets the grey backdrop show through
        private static readonly Color STRIPE_LIGHT = new Color(0f, 0f, 0f, 0.3f); // black @ 0.3 — the diagonal hatch lines (subtly darken the field)

        private static void BuildPartyBar(RectTransform bar, IReadOnlyList<PartyPartResult> parts,
            PhraseGrade grade, double awesomeThreshold, bool oddBar, float barHeight, float onePixel,
            int segmentCount, int harmonyPartIndex, bool synthesizedLegacySummary)
        {
            // Legacy synthesized bars use one visual segment regardless of the selected HARM lane;
            // Party bars retain their actual segment count and per-part colors.
            int colorOffset = synthesizedLegacySummary ? Mathf.Clamp(harmonyPartIndex, 0, 2) : 0;

            // One segment per available harmony part (HARM1/HARM2/HARM3, bottom -> top), so a duet
            // (2 parts) renders two equal-height bands instead of leaving the top third empty. Each
            // fill carries a subtle vertical gradient (full color -> 20% darker at the bottom). An
            // absent part renders as a full fill dimmed 75% (a "gap") — exempt from the odd-bar
            // stripe and without a cap. A not-awesome segment gets a cap line at its fill top; a full
            // (Awesome) segment does not (so 99% is distinguishable from 100%). Dim percentages are
            // additive (factor = 1 - sum).
            int SEGMENTS = Mathf.Clamp(segmentCount, 1, 3);
            double[] meters = new double[SEGMENTS];
            bool[] available = new bool[SEGMENTS];
            if (parts != null)
            {
                foreach (var pr in parts)
                {
                    if (pr.PartIndex >= 0 && pr.PartIndex < SEGMENTS)
                    {
                        available[pr.PartIndex] = true;
                        meters[pr.PartIndex] = pr.Meter;
                    }
                }
            }

            float stripe = oddBar ? PARTY_STRIPE_DIM : 0f;
            bool awesomePhrase = grade != PhraseGrade.Miss;
            Texture2D segGradient = GetOrCreatePartySegmentGradient();

            float gapH = onePixel;

            float bufferH = SnapToScreenPixel(bar, PARTY_BUFFER_HEIGHT);

            // Bottom status buffer: gold (gold->UT-orange gradient) when Awesome; darker grey when
            // Miss. Opaque so the stripes don't bleed through. Not subject to the odd-bar stripe.
            if (awesomePhrase)
            {
                AddBarRawImage(bar, "Buffer", 0f, bufferH, GetOrCreateAwesomeBarGradient(),
                    Color.white, 1f);
            }
            else
            {
                AddBarImage(bar, "Buffer", 0f, bufferH, PartyMissMarkerColor, 1f);
            }

            // Above the buffer: SEGMENTS bands separated by one-pixel black gaps. Every vertical
            // position is snapped to a screen pixel; the top band is anchored to barHeight so the
            // bar fills exactly to the top (no gap under the backdrop edge).
            float bandHAvg = (barHeight - bufferH - SEGMENTS * gapH) / SEGMENTS;
            float y = bufferH;

            for (int p = 0; p < SEGMENTS; p++)
            {
                // Gap below this band = the black divider.
                AddBarImage(bar, $"Div {p}", y, gapH, Color.black, 0.8f);
                float bandBottom = SnapToScreenPixel(bar, y + gapH);
                float bandTop = (p == SEGMENTS - 1) ? barHeight : SnapToScreenPixel(bar, bandBottom + bandHAvg);
                float bandH = bandTop - bandBottom;
                Color lineColor = HarmonyColor(p + colorOffset);

                if (!available[p])
                {
                    // Absent part: no fill — the striped background shows through ("transparent").
                    y = bandTop;
                    continue;
                }

                bool awesome = awesomeThreshold > 0 && meters[p] >= awesomeThreshold;

                // Lane background: darkened harmony color covering the stripes for this segment.
                // Deliberately NOT subject to the odd-bar stripe: this is a constant "part is
                // available" marker (harmony x0.25) in every bar so the available/absent
                // distinction reads consistently across the row. Only the meter fill alternates.
                AddBarImage(bar, $"Track {p}", bandBottom, bandH,
                    Dim(lineColor, 1f - PARTY_TRACK_DIM), 1f);

                // Meter fill (gradient) on top of the track: full when Awesome, partial otherwise.
                float fillDim = (awesome ? 0f : PARTY_DIM_NOT_AWESOME) + stripe;
                float fillFrac = awesome ? 1f
                    : Mathf.Clamp01(awesomeThreshold > 0 ? (float) (meters[p] / awesomeThreshold) : 0f);
                float fillTop = awesome ? bandTop : SnapToScreenPixel(bar, bandBottom + fillFrac * bandH);
                float height = fillTop - bandBottom;
                AddBarRawImage(bar, $"Part {p}", bandBottom, height, segGradient,
                    Dim(lineColor, 1f - fillDim), 1f);

                // Cap highlight across the fill top, only when the segment isn't completely full.
                if (!awesome && height > onePixel)
                {
                    AddBarImage(bar, $"Cap {p}", fillTop - onePixel, onePixel,
                        BarCapColor, 0.20f);
                }

                y = bandTop;
            }
        }

        private static Color Dim(Color c, float factor) => new Color(c.r * factor, c.g * factor, c.b * factor);

        // Snap a local-space offset (relative to `parent`'s bottom) to an actual screen pixel, so
        // 1px elements land on whole pixels regardless of the parent's fractional screen position.
        private static float SnapToScreenPixel(RectTransform parent, float localY)
        {
            float scale = parent.lossyScale.y;
            if (scale <= 0f) return localY;
            float worldY = parent.position.y + localY * scale;
            return (Mathf.Round(worldY) - parent.position.y) / scale;
        }

        // Single diagonal "transparency" hatch field drawn once across the bars rectangle (full width
        // of the bars rect, from BAR_BASE_Y to the top), behind the bars. Clear pixels let the solid
        // grey backdrop show; pure-white pixels are the diagonal hatch lines. Tiled at 1:1 (one
        // STRIPE_TEX_SIZE repeat per STRIPE_TEX_SIZE screen pixels) so the pattern stays crisp and
        // continuous across the whole field — one continuous field, no per-bar restart, so no boundary
        // drift. Absent parts (nothing drawn over them) reveal the hatch; available parts' tracks cover it.
        private static void AddPartyHatchField(RectTransform parent, float bottom, float top)
        {
            float height = top - bottom;
            if (height <= 0f)
            {
                return;
            }

            var obj = new GameObject("Hatch", typeof(RectTransform), typeof(RawImage));
            var rect = (RectTransform) obj.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.offsetMin = new Vector2(0f, bottom);
            rect.offsetMax = new Vector2(0f, top);

            var img = obj.GetComponent<RawImage>();
            img.texture = GetOrCreateDiagonalStripeTexture();
            img.color = Color.white; // white tint so the texture's own clear/white pixels show as-is
            img.raycastTarget = false;

            // Tile at 1:1 screen pixels. The field's pixel size isn't known at build time (newly
            // created anchored rects report 0 until layout runs), so a tiler component recomputes the
            // uvRect from the rect's world-space size once layout settles / whenever it changes.
            obj.AddComponent<NativeResolutionTiler>().Setup(img, STRIPE_TEX_SIZE);
        }

        // Recomputes a RawImage's uvRect so its texture tiles at 1:1 screen pixels, re-applying
        // whenever the rect's dimensions change (OnRectTransformDimensionsChange fires once layout
        // assigns the field its real size). Measures via world corners -> screen pixels so it's
        // correct regardless of canvas render mode. No per-frame cost: it only runs on size change.
        private class NativeResolutionTiler : MonoBehaviour
        {
            private RawImage _image;
            private Canvas _canvas;
            private int _texSize;
            private float _lastW = -1f;
            private float _lastH = -1f;

            public void Setup(RawImage image, int texSize)
            {
                _image = image;
                _texSize = texSize;
                _canvas = image.GetComponentInParent<Canvas>();
                Apply();
            }

            private void OnRectTransformDimensionsChange() => Apply();

            private void Apply()
            {
                if (_image == null) return;
                var rt = _image.rectTransform;
                var corners = new Vector3[4];
                rt.GetWorldCorners(corners);
                Camera cam = _canvas != null ? _canvas.worldCamera : null;
                Vector2 bl = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
                Vector2 tr = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
                float w = Mathf.Abs(tr.x - bl.x);
                float h = Mathf.Abs(tr.y - bl.y);
                if (Mathf.Approximately(w, _lastW) && Mathf.Approximately(h, _lastH)) return;
                _lastW = w;
                _lastH = h;
                float tilesX = Mathf.Max(w / _texSize, 1f);
                float tilesY = Mathf.Max(h / _texSize, 1f);
                _image.uvRect = new Rect(0f, 0f, tilesX, tilesY);
            }
        }

        // A bottom-anchored, full-width solid-color block within `parent`, occupying [bottom, bottom+height].
        // Returns null (no GameObject) for non-positive height so zero-fill parts render nothing.
        // Like AddBarImage but a RawImage with a texture (for gradient fills); color tints the texture.
        private static RawImage AddBarRawImage(RectTransform parent, string name, float bottom, float height,
            Texture2D texture, Color color, float alpha)
        {
            if (height <= 0f)
            {
                return null;
            }

            var obj = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            var rect = (RectTransform) obj.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.offsetMin = new Vector2(0f, bottom);
            rect.offsetMax = new Vector2(0f, bottom + height);

            var img = obj.GetComponent<RawImage>();
            img.texture = texture;
            color.a = alpha;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static Image AddBarImage(RectTransform parent, string name, float bottom, float height,
            Color color, float alpha)
        {
            if (height <= 0f)
            {
                return null;
            }

            var obj = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform) obj.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.offsetMin = new Vector2(0f, bottom);
            rect.offsetMax = new Vector2(0f, bottom + height);

            var img = obj.GetComponent<Image>();
            color.a = alpha;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static Color HarmonyColor(int partIndex) => partIndex switch
        {
            0 => Harm1Cyan,
            1 => Harm2Orange,
            _ => Harm3Yellow,
        };

        private static void BuildTally(RectTransform parent, IReadOnlyList<float> percents,
            IReadOnlyList<PhraseGrade> phraseGrades, int maxHarmonyParts,
            Func<Transform, string, TextAlignmentOptions, TextMeshProUGUI> labelFactory, Color dividerColor,
            int percussionHits, int percussionTotal)
        {
            bool partyMode = phraseGrades != null && phraseGrades.Count > 0;

            var tallyRect = CreateLayoutColumn("Tally", parent, TALLY_SPACING);
            var tallyLayout = tallyRect.GetComponent<VerticalLayoutGroup>();
            tallyLayout.padding = new RectOffset((int) TALLY_SIDE_PADDING, (int) TALLY_SIDE_PADDING, 0, 0);

            if (partyMode)
            {
                // Party mode: the Awesome row splits into Triple/Double/Single sub-counts.
                // Miss phrases are classified by their fraction into the solo tiers below.
                int tripleCount = 0, doubleCount = 0, singleCount = 0;
                int tierCount = VocalPhraseGrade.Awesome - VocalPhraseGrade.Awful + 1;
                var missCounts = new int[tierCount]; // Strong → Awful (not Awesome)

                for (int i = 0; i < percents.Count; i++)
                {
                    if (i < phraseGrades.Count)
                    {
                        switch (phraseGrades[i])
                        {
                            case PhraseGrade.TripleAwesome: tripleCount++; continue;
                            case PhraseGrade.DoubleAwesome: doubleCount++; continue;
                            case PhraseGrade.Awesome:       singleCount++; continue;
                        }
                    }
                    // Miss (or no grade): classify by fraction into the solo tiers.
                    missCounts[(int) VocalPhraseGradeExtensions.Classify(percents[i])]++;
                }

                // Awesome row with three colored sub-counts (best → worst, left → right).
                BuildAwesomeTallyRow(tallyRect, tripleCount, doubleCount, singleCount, maxHarmonyParts, labelFactory);

                // Remaining tiers (Strong → Awful) for miss-classified phrases.
                for (int g = (int) VocalPhraseGrade.Strong; g >= (int) VocalPhraseGrade.Awful; g--)
                {
                    BuildTallyRow(tallyRect, (VocalPhraseGrade) g, missCounts[g], labelFactory);
                }
            }
            else
            {
                // Solo mode: tally phrases per tier (unchanged).
                int tierCount = VocalPhraseGrade.Awesome - VocalPhraseGrade.Awful + 1;
                var counts = new int[tierCount];
                for (int i = 0; i < percents.Count; i++)
                {
                    counts[(int) VocalPhraseGradeExtensions.Classify(percents[i])]++;
                }

                // Always show every tier (best -> worst) so multiple players' tables line up row-for-row,
                // even when a tier has no phrases.
                for (int grade = tierCount - 1; grade >= 0; grade--)
                {
                    BuildTallyRow(tallyRect, (VocalPhraseGrade) grade, counts[grade], labelFactory);
                }
            }

            // Vocal percussion (not a graded tier) gets its own row below the tiers, set off by a
            // divider in the card accent color. Omitted entirely when the chart has no percussion.
            if (percussionTotal > 0)
            {
                BuildDivider(tallyRect, dividerColor);
                BuildPercussionRow(tallyRect, percussionHits, percussionTotal, labelFactory);
            }
        }

        private static void BuildDivider(RectTransform parent, Color color)
        {
            var dividerObject = new GameObject("Divider", typeof(RectTransform), typeof(Image));
            var dividerRect = (RectTransform) dividerObject.transform;
            dividerRect.SetParent(parent, false);
            AddLayoutElement(dividerRect, DIVIDER_THICKNESS);

            var image = dividerObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        /// <summary>
        /// Party mode Awesome tally row: three tier entries (Triple/Double/Single Awesome), each a
        /// harmony part-count icon (InstrumentIcons vocals/twoVocals/harmVocals) plus its count. Icons use their
        /// default sheet colors; counts are white (muted when 0), matching the other tally rows.
        /// An icon is dimmed when that harmony part can't exist for this song (e.g. a duet dims the
        /// triple icon). The label is "AWESOME!" (same localization key as solo).
        /// </summary>
        private static void BuildAwesomeTallyRow(RectTransform parent, int tripleCount, int doubleCount,
            int singleCount, int maxHarmonyParts,
            Func<Transform, string, TextAlignmentOptions, TextMeshProUGUI> labelFactory)
        {
            var rowObject = new GameObject("Tally Awesome (Party)", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var rowRect = (RectTransform) rowObject.transform;
            rowRect.SetParent(parent, false);
            AddLayoutElement(rowRect, TALLY_ROW_HEIGHT);

            var rowLayout = rowObject.GetComponent<HorizontalLayoutGroup>();
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.spacing = AWESOME_TIER_SPACING;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;

            // Tier label, left-justified (flexible so the tier entries sit on the right).
            var label = labelFactory(rowRect, "Label", TextAlignmentOptions.Left);
            label.text = Localize.Key("Gameplay.Vocals.Performance", VocalPhraseGrade.Awesome.ToLocalizationKey());
            StyleText(label, _labelFont, CoolGrayColor, TextAlignmentOptions.Left);
            var labelLayout = label.gameObject.AddComponent<LayoutElement>();
            labelLayout.flexibleWidth = 1f;
            labelLayout.preferredHeight = TALLY_ROW_HEIGHT;

            // Three tier entries (Triple -> Double -> Single, left to right). Each is a harmony
            // part-count icon + its count; the icon is dimmed when that part can't exist here.
            int[] counts = { tripleCount, doubleCount, singleCount };
            for (int t = 0; t < 3; t++)
            {
                int partCount = 3 - t; // triple=3, double=2, single=1
                AddAwesomeTierEntry(rowRect, partCount, counts[t], partCount > maxHarmonyParts, labelFactory);
            }
        }

        // One Awesome tier entry: a harmony part-count icon (InstrumentIcons vocals/twoVocals/harmVocals, default
        // sheet color) + its count (white, muted when 0). The icon is dimmed when that part can't
        // exist for this chart (so it reads as unachievable rather than just zero).
        private static void AddAwesomeTierEntry(RectTransform parent, int partCount, int count, bool dimmed,
            Func<Transform, string, TextAlignmentOptions, TextMeshProUGUI> labelFactory)
        {
            var entryObject = new GameObject($"Awesome {partCount}", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var entryRect = (RectTransform) entryObject.transform;
            entryRect.SetParent(parent, false);

            var entryLayout = entryObject.GetComponent<HorizontalLayoutGroup>();
            entryLayout.childAlignment = TextAnchor.MiddleRight;
            entryLayout.spacing = 2f;
            entryLayout.childForceExpandWidth = false;
            entryLayout.childForceExpandHeight = false;
            entryLayout.childControlWidth = true;
            entryLayout.childControlHeight = true;

            // Harmony part-count icon (InstrumentIcons sub-sprite, matching the difficulty ring /
            // player-name display: vocals=1, twoVocals=2, harmVocals=3+).
            string iconKey = partCount switch
            {
                >= 3 => "InstrumentIcons[harmVocals]",
                2 => "InstrumentIcons[twoVocals]",
                _ => "InstrumentIcons[vocals]",
            };
            var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            var iconRect = (RectTransform) iconObject.transform;
            iconRect.SetParent(entryRect, false);
            var iconImage = iconObject.GetComponent<Image>();
            iconImage.sprite = Addressables.LoadAssetAsync<Sprite>(iconKey).WaitForCompletion();
            iconImage.color = dimmed ? new Color(AWESOME_ICON_DIM, AWESOME_ICON_DIM, AWESOME_ICON_DIM, 1f) : Color.white;
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            var iconLayout = iconObject.AddComponent<LayoutElement>();
            iconLayout.preferredWidth = TALLY_ROW_HEIGHT;
            iconLayout.preferredHeight = TALLY_ROW_HEIGHT;

            // Count (white when > 0, muted when 0). Content-sized (no LayoutElement) so the icon
            // sits tight against its own number — no fixed-width box gap — and triple-digit counts
            // (some long songs) only widen the entry when they actually appear.
            var countText = labelFactory(entryRect, "Count", TextAlignmentOptions.Right);
            countText.text = count.ToString();
            StyleText(countText, _labelFont, count > 0 ? (Color?) null : MutedColor, TextAlignmentOptions.Right);
        }

        private static void BuildPercussionRow(RectTransform parent, int hits, int total,
            Func<Transform, string, TextAlignmentOptions, TextMeshProUGUI> labelFactory)
        {
            var rowObject = new GameObject("Percussion", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var rowRect = (RectTransform) rowObject.transform;
            rowRect.SetParent(parent, false);
            AddLayoutElement(rowRect, TALLY_ROW_HEIGHT);

            var rowLayout = rowObject.GetComponent<HorizontalLayoutGroup>();
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.spacing = 0f;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;

            var label = labelFactory(rowRect, "Label", TextAlignmentOptions.Left);
            label.text = Localize.Key("Menu.ScoreScreen.Percussion");
            StyleText(label, _labelFont, CoolGrayColor, TextAlignmentOptions.Left);
            var labelLayout = label.gameObject.AddComponent<LayoutElement>();
            labelLayout.flexibleWidth = 1f;
            labelLayout.preferredHeight = TALLY_ROW_HEIGHT;

            // "hits / total" — numerator white, denominator muted, mirroring the regular stat rows.
            // When every percussion note was hit, the whole count goes gold (like a maxed tier).
            var countText = labelFactory(rowRect, "Count", TextAlignmentOptions.Right);
            bool allHit = hits == total;
            countText.text = allHit
                ? $"<color=#FFD642>{hits}</color> <color=#7D7DA3>/ {total}</color>"
                : $"{hits} <color=#7D7DA3>/ {total}</color>";
            StyleText(countText, _labelFont, null, TextAlignmentOptions.Right);
            var countLayout = countText.gameObject.AddComponent<LayoutElement>();
            countLayout.minWidth = TALLY_COUNT_WIDTH;
            countLayout.preferredWidth = TALLY_COUNT_WIDTH;
            countLayout.flexibleWidth = 0f;
            countLayout.preferredHeight = TALLY_ROW_HEIGHT;
        }

        private static void BuildTallyRow(RectTransform parent, VocalPhraseGrade grade, int count,
            Func<Transform, string, TextAlignmentOptions, TextMeshProUGUI> labelFactory)
        {
            var rowObject = new GameObject($"Tally {grade}", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var rowRect = (RectTransform) rowObject.transform;
            rowRect.SetParent(parent, false);
            AddLayoutElement(rowRect, TALLY_ROW_HEIGHT);

            var rowLayout = rowObject.GetComponent<HorizontalLayoutGroup>();
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.spacing = 0f;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;

            // Tier label, left-justified, standard white.
            var label = labelFactory(rowRect, "Label", TextAlignmentOptions.Left);
            label.text = Localize.Key("Gameplay.Vocals.Performance", grade.ToLocalizationKey());
            StyleText(label, _labelFont, CoolGrayColor, TextAlignmentOptions.Left);
            var labelLayout = label.gameObject.AddComponent<LayoutElement>();
            labelLayout.flexibleWidth = 1f;
            labelLayout.preferredHeight = TALLY_ROW_HEIGHT;

            // Count, right-justified. Same white as the label, dimmed to muted grey when zero.
            var countText = labelFactory(rowRect, "Count", TextAlignmentOptions.Right);
            countText.text = count.ToString();
            StyleText(countText, _labelFont, count > 0 ? (Color?) null : MutedColor,
                TextAlignmentOptions.Right);
            var countLayout = countText.gameObject.AddComponent<LayoutElement>();
            countLayout.minWidth = TALLY_COUNT_WIDTH;
            countLayout.preferredWidth = TALLY_COUNT_WIDTH;
            countLayout.flexibleWidth = 0f;
            countLayout.preferredHeight = TALLY_ROW_HEIGHT;
        }

        // Party-mode backdrop: one uniform grey wash (the "Okay" grey). The diagonal-stripe
        // "transparency" texture is drawn per-bar (see BuildPartyBar), not across the whole graph,
        // so the background between bars stays a clean grey.
        private static void DrawSolidBackdrop(RectTransform barsRect)
        {
            var obj = new GameObject("Backdrop", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform) obj.transform;
            rect.SetParent(barsRect, false);
            // Continuous faint grey wash filling the whole bars rect (#7a7a7a @ 0.125 — the darkness
            // the absent-part sections match). At 0.125 the baseline axis still shows through.
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var img = obj.GetComponent<Image>();
            img.color = new Color(BarDefaultColor.r, BarDefaultColor.g, BarDefaultColor.b, REGION_FILL_ALPHA);
            img.raycastTarget = false;
        }

        private static void DrawTierRegions(RectTransform barsRect)
        {
            // Five regions covering the full graph height, bottom to top.
            (float bottom, float top, Color color, int idx)[] regions =
            {
                ((float) VocalPhraseGrade.Awful.LowerBound(),  (float) VocalPhraseGrade.Messy.LowerBound(),  LineColorMessy,  0),
                ((float) VocalPhraseGrade.Messy.LowerBound(),  (float) VocalPhraseGrade.Okay.LowerBound(),   BarDefaultColor, 1),
                ((float) VocalPhraseGrade.Okay.LowerBound(),   (float) VocalPhraseGrade.Good.LowerBound(),   LineColorOkay,   2),
                ((float) VocalPhraseGrade.Good.LowerBound(),   (float) VocalPhraseGrade.Strong.LowerBound(), LineColorGood,   3),
                ((float) VocalPhraseGrade.Strong.LowerBound(), (float) VocalPhraseGrade.Awesome.LowerBound(),LineColorStrong, 4),
            };

            foreach (var (bottom, top, color, idx) in regions)
            {
                var obj = new GameObject($"Region {idx}", typeof(RectTransform), typeof(RawImage));
                var rect = (RectTransform) obj.transform;
                rect.SetParent(barsRect, false);
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(1f, 0f);
                rect.offsetMin = new Vector2(0f, BAR_BASE_Y + bottom * GRAPH_HEIGHT);
                rect.offsetMax = new Vector2(0f, BAR_BASE_Y + top * GRAPH_HEIGHT);

                var rawImage = obj.GetComponent<RawImage>();
                rawImage.texture = GetOrCreateGradientTexture(idx, color);
                rawImage.color = new Color(1f, 1f, 1f, REGION_FILL_ALPHA);
                rawImage.raycastTarget = false;
            }
        }

        private static Texture2D _partySegmentGradient;

        // Normalized vertical brightness ramp (white at top -> PARTY_GRADIENT_BOTTOM at bottom),
        // tinted per fill with the segment's base color so each fill shades subtly darker toward
        // its bottom. One texture shared across all party segment fills.
        private static Texture2D GetOrCreatePartySegmentGradient()
        {
            if (_partySegmentGradient != null)
            {
                return _partySegmentGradient;
            }

            var tex = new Texture2D(1, GRADIENT_TEX_WIDTH, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color[GRADIENT_TEX_WIDTH];
            for (int i = 0; i < GRADIENT_TEX_WIDTH; i++)
            {
                // Row 0 (v=0, bottom) = dimmed; top row = full white. Tinted per fill.
                float t = i / (GRADIENT_TEX_WIDTH - 1f);
                float b = Mathf.Lerp(PARTY_GRADIENT_BOTTOM, 1f, t);
                pixels[i] = new Color(b, b, b, 1f);
            }
            tex.SetPixels(pixels);
            tex.Apply();
            _partySegmentGradient = tex;
            return tex;
        }

        private static Texture2D _diagonalStripeTexture;

        // Tiling diagonal-stripe texture (faint, low-alpha) used as the party-mode "transparency"
        // backdrop. wrapMode=Repeat so it tiles via the RawImage's uvRect.
        private static Texture2D GetOrCreateDiagonalStripeTexture()
        {
            if (_diagonalStripeTexture != null) return _diagonalStripeTexture;
            var tex = new Texture2D(STRIPE_TEX_SIZE, STRIPE_TEX_SIZE, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Repeat;
            int period = STRIPE_WIDTH * 2;
            var pixels = new Color[STRIPE_TEX_SIZE * STRIPE_TEX_SIZE];
            for (int y = 0; y < STRIPE_TEX_SIZE; y++)
            {
                for (int x = 0; x < STRIPE_TEX_SIZE; x++)
                {
                    bool on = (((x - y) % period + period) % period) < STRIPE_WIDTH;
                    pixels[y * STRIPE_TEX_SIZE + x] = on ? STRIPE_LIGHT : STRIPE_DARK;
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            _diagonalStripeTexture = tex;
            return tex;
        }

        private static Texture2D GetOrCreateAwesomeBarGradient()
        {
            if (_awesomeBarGradient != null)
                return _awesomeBarGradient;

            // 1×N texture: pixel row 0 (UV v=0) = bar bottom = UT Orange; top row = Gold.
            var tex = new Texture2D(1, GRADIENT_TEX_WIDTH, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color[GRADIENT_TEX_WIDTH];
            for (int i = 0; i < GRADIENT_TEX_WIDTH; i++)
            {
                pixels[i] = Color.Lerp(UtOrangeColor, GoldColor, i / (GRADIENT_TEX_WIDTH - 1f));
            }
            tex.SetPixels(pixels);
            tex.Apply();
            _awesomeBarGradient = tex;
            return tex;
        }

        private static Texture2D GetOrCreateGrayBarGradient()
        {
            if (_grayBarGradient != null)
                return _grayBarGradient;

            // 1×N normalized brightness ramp: pixel row 0 (bar bottom) = BAR_GRADIENT_BOTTOM,
            // top row = 1.0. The actual bar color is applied via RawImage.color as a tint.
            var tex = new Texture2D(1, GRADIENT_TEX_WIDTH, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color[GRADIENT_TEX_WIDTH];
            for (int i = 0; i < GRADIENT_TEX_WIDTH; i++)
            {
                float t = i / (GRADIENT_TEX_WIDTH - 1f);
                float v = Mathf.Lerp(BAR_GRADIENT_BOTTOM, 1f, t);
                pixels[i] = new Color(v, v, v, 1f);
            }
            tex.SetPixels(pixels);
            tex.Apply();
            _grayBarGradient = tex;
            return tex;
        }

        private static Texture2D GetOrCreateGradientTexture(int idx, Color color)
        {
            if (_tierGradientTextures[idx] != null)
            {
                return _tierGradientTextures[idx];
            }

            // 1×N vertical texture: pixel row 0 (UV v=0) = region bottom, top row = region top.
            var tex = new Texture2D(1, GRADIENT_TEX_WIDTH, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color[GRADIENT_TEX_WIDTH];

            // All tiers: 1.0 at bottom (lower bound) → REGION_FADE_MIN at top (next cutoff).
            for (int i = 0; i < GRADIENT_TEX_WIDTH; i++)
            {
                float t = i / (GRADIENT_TEX_WIDTH - 1f);
                float alpha = Mathf.Lerp(1f, REGION_FADE_MIN, t);
                var p = color;
                p.a = alpha;
                pixels[i] = p;
            }

            tex.SetPixels(pixels);
            tex.Apply();
            _tierGradientTextures[idx] = tex;
            return tex;
        }

        private static void EnsureFonts()
        {
            // Null-check rather than a resolved flag: Unity's overloaded == null detects
            // destroyed objects (e.g. after domain reload with Enter Play Mode Options),
            // so the scan re-runs automatically when the cached references become stale.
            if (_headerFont != null && _labelFont != null)
            {
                return;
            }

            // The card's brand fonts are already loaded (used across the menu UI); pick them up by
            // name. Falls back to whatever the label factory provided if a font isn't found.
            foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (font.name == HEADER_FONT_NAME)
                {
                    _headerFont = font;
                }
                else if (font.name == LABEL_FONT_NAME)
                {
                    _labelFont = font;
                }
            }
        }

        // Pass null for color to inherit the prefab-derived color from the label factory.
        private static void StyleText(TextMeshProUGUI label, TMP_FontAsset font, Color? color,
            TextAlignmentOptions alignment)
        {
            if (font != null)
            {
                label.font = font;
                label.fontSharedMaterial = font.material;
            }

            label.fontSize = TEXT_SIZE;
            label.fontStyle = FontStyles.UpperCase;
            label.characterSpacing = 0f;
            if (color.HasValue)
                label.color = color.Value;
            label.alignment = alignment;
        }

        // Returns the canvas-unit size of one physical pixel, so callers can size thin elements to
        // always render as at least one visible pixel regardless of canvas DPI scaling.
        private static float PixelUnit(RectTransform rt)
        {
            var canvas = rt.GetComponentInParent<Canvas>();
            return canvas != null && canvas.scaleFactor > 0f ? 1f / canvas.scaleFactor : 1f;
        }

        private static RectTransform CreateLayoutColumn(string name, RectTransform parent, float spacing)
        {
            var columnObject = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            var columnRect = (RectTransform) columnObject.transform;
            columnRect.SetParent(parent, false);

            var layout = columnObject.GetComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            var fitter = columnObject.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            return columnRect;
        }

        private static void AddLayoutElement(RectTransform rect, float preferredHeight)
        {
            var layout = rect.gameObject.GetComponent<LayoutElement>();
            if (layout == null)
            {
                layout = rect.gameObject.AddComponent<LayoutElement>();
            }

            layout.preferredHeight = preferredHeight;
            layout.minHeight = preferredHeight;
        }
    }
}
