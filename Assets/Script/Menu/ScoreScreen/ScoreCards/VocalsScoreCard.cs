using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.UI;
using YARG.Core.Engine.Vocals;
using YARG.Helpers.Extensions;
using YARG.Localization;

namespace YARG.Menu.ScoreScreen
{
    public class VocalsScoreCard : ScoreCard<VocalsStats>
    {
        private const float PHRASE_HISTOGRAM_TOTAL_HEIGHT = 154f;
        private const float PHRASE_HISTOGRAM_GRAPH_HEIGHT = 110f;
        private const float PHRASE_HISTOGRAM_SUMMARY_HEIGHT = 130f;
        private const float PHRASE_HISTOGRAM_HORIZONTAL_MARGIN = 54f;
        private const float PHRASE_HISTOGRAM_MIN_BAR_HEIGHT = 4f;
        private const float PHRASE_HISTOGRAM_BAR_GAP = 2f;
        private const float PHRASE_SUMMARY_LINE_HEIGHT = 22f;
        private const float PHRASE_SUMMARY_FONT_SIZE = 20f;

        // Harmony-line colors from VocalTrack.Colors (HARM1=Cyan, HARM2=Orange, HARM3=Yellow).
        // Used for the Party Vocals Triple/Double/Single Awesome sub-columns.
        private static readonly Color ColorHarm1 = new(0f, 0.800f, 1f);   // Cyan
        private static readonly Color ColorHarm2 = new(1f, 0.522f, 0f);   // Orange
        private static readonly Color ColorHarm3 = new(1f, 0.859f, 0f);   // Yellow

        private static readonly Color ColorAwesome = new Color32(0xFF, 0xD7, 0x00, 0xFF); // Gold
        private static readonly Color ColorStrong  = new Color32(0x4C, 0xAF, 0x50, 0xFF); // Green
        private static readonly Color ColorGood    = new Color32(0x8B, 0xC3, 0x4A, 0xFF); // Yellow-green
        private static readonly Color ColorOkay    = new Color32(0xFF, 0xC1, 0x07, 0xFF); // Yellow
        private static readonly Color ColorMessy   = new Color32(0xFF, 0x98, 0x00, 0xFF); // Orange
        private static readonly Color ColorAwful   = new Color32(0xF4, 0x43, 0x36, 0xFF); // Red

        // Phrase data injected from the score screen (captured live during gameplay).
        private IReadOnlyList<float>       _phrasePercents;
        private int                         _percussionHits;
        private int                         _percussionTotal;
        private IReadOnlyList<PhraseGrade>  _phraseGrades;

        private GameObject _phraseHistogramObject;
        private readonly List<RectTransform> _phraseBarPool = new();
        private readonly List<GameObject> _phraseSummaryLabels = new();
        private RectTransform _phraseBarContainerRect;
        private RectTransform _phraseSummaryContainerRect;

        public override void SetCardContents()
        {
            base.SetCardContents();

            // Set background icon
            _instrumentIcon.sprite = Addressables
                .LoadAssetAsync<Sprite>($"InstrumentIcons[{Player.Profile.CurrentInstrument.ToResourceName()}]")
                .WaitForCompletion();
        }

        /// <summary>
        /// Injects live-captured phrase data from the gameplay player.
        /// Must be called before SetCardContents / BuildOffsetHistogram.
        /// </summary>
        public void SetPhraseData(IReadOnlyList<float> phrasePercents, int percussionHits, int percussionTotal)
        {
            _phrasePercents = phrasePercents;
            _percussionHits = percussionHits;
            _percussionTotal = percussionTotal;
        }

        /// <summary>
        /// Injects Party Vocals phrase grades (Triple/Double/Awesome/Miss per phrase).
        /// When non-empty, the tally shows Triple/Double/Single Awesome sub-columns.
        /// </summary>
        public void SetPhraseGrades(IReadOnlyList<PhraseGrade> phraseGrades)
        {
            _phraseGrades = phraseGrades;
        }

        protected override void BuildOffsetHistogram()
        {
            // Vocals don't use the offset histogram — replace with a phrase histogram + grade summary.
            // Hide any offset histogram the base class may have created.
            SetOffsetHistogramActive(false);

            var phrases = _phrasePercents;
            if (phrases == null || phrases.Count == 0)
            {
                // No phrase data (e.g. old replay). Clean up and return.
                DestroyPhraseVisualization();
                return;
            }

            if (!TryGetHistogramSection(out var sectionContainer, out int insertIndex))
            {
                DestroyPhraseVisualization();
                return;
            }

            EnsurePhraseVisualization(sectionContainer, insertIndex, phrases.Count);
        }

        private void EnsurePhraseVisualization(Transform sectionContainer, int insertIndex, int phraseCount)
        {
            if (_phraseHistogramObject == null)
            {
                CreatePhraseVisualization();
            }

            _phraseHistogramObject.transform.SetParent(sectionContainer, false);
            _phraseHistogramObject.transform.SetSiblingIndex(insertIndex);
            _phraseHistogramObject.SetActive(true);

            PopulatePhraseBars(phraseCount);
            PopulateGradeSummary();
        }

        private void CreatePhraseVisualization()
        {
            _phraseHistogramObject = new GameObject("Phrase Histogram", typeof(RectTransform), typeof(LayoutElement));
            var rootRect = (RectTransform) _phraseHistogramObject.transform;

            float totalHeight = PHRASE_HISTOGRAM_TOTAL_HEIGHT + PHRASE_HISTOGRAM_SUMMARY_HEIGHT;
            var layoutElement = _phraseHistogramObject.GetComponent<LayoutElement>();
            layoutElement.preferredHeight = totalHeight;
            layoutElement.minHeight = totalHeight;

            // --- Histogram section ---
            var graphContainer = new GameObject("Graph", typeof(RectTransform));
            var graphRect = (RectTransform) graphContainer.transform;
            graphRect.SetParent(rootRect, false);
            graphRect.anchorMin = new Vector2(0f, 1f);
            graphRect.anchorMax = new Vector2(1f, 1f);
            graphRect.pivot = new Vector2(0.5f, 1f);
            graphRect.offsetMin = new Vector2(PHRASE_HISTOGRAM_HORIZONTAL_MARGIN, -(PHRASE_HISTOGRAM_TOTAL_HEIGHT - 22f));
            graphRect.offsetMax = new Vector2(-PHRASE_HISTOGRAM_HORIZONTAL_MARGIN, 0f);

            // X-axis baseline
            var axisObject = new GameObject("XAxis", typeof(RectTransform), typeof(Image));
            var axisRect = (RectTransform) axisObject.transform;
            axisRect.SetParent(graphRect, false);
            axisRect.anchorMin = new Vector2(0f, 0f);
            axisRect.anchorMax = new Vector2(1f, 0f);
            float axisThickness = 2f;
            axisRect.offsetMin = new Vector2(0f, -axisThickness * 0.5f);
            axisRect.offsetMax = new Vector2(0f, axisThickness * 0.5f);
            var axisImage = axisObject.GetComponent<Image>();
            axisImage.color = new Color(1f, 1f, 1f, 0.25f);
            axisImage.raycastTarget = false;

            // Bars container
            var barsObject = new GameObject("Bars", typeof(RectTransform));
            var barsRect = (RectTransform) barsObject.transform;
            barsRect.SetParent(graphRect, false);
            barsRect.anchorMin = Vector2.zero;
            barsRect.anchorMax = Vector2.one;
            barsRect.offsetMin = Vector2.zero;
            barsRect.offsetMax = Vector2.zero;

            // --- Summary section ---
            var summaryContainer = new GameObject("Grade Summary", typeof(RectTransform));
            var summaryRect = (RectTransform) summaryContainer.transform;
            summaryRect.SetParent(rootRect, false);
            summaryRect.anchorMin = new Vector2(0f, 0f);
            summaryRect.anchorMax = new Vector2(1f, 0f);
            summaryRect.pivot = new Vector2(0.5f, 0f);
            summaryRect.offsetMin = new Vector2(PHRASE_HISTOGRAM_HORIZONTAL_MARGIN, 0f);
            summaryRect.offsetMax = new Vector2(-PHRASE_HISTOGRAM_HORIZONTAL_MARGIN, PHRASE_HISTOGRAM_SUMMARY_HEIGHT);

            // Store references for population
            _phraseBarPool.Clear();
            _phraseSummaryLabels.Clear();
            _phraseBarContainerRect = barsRect;
            _phraseSummaryContainerRect = summaryRect;
        }

        private void PopulatePhraseBars(int phraseCount)
        {
            var phrases = _phrasePercents;

            // Find min/max for normalization (but always map 1.0 to full height)
            float maxFraction = 0f;
            for (int i = 0; i < phrases.Count; i++)
            {
                if (phrases[i] > maxFraction) maxFraction = phrases[i];
            }
            float normalizationScale = maxFraction > 0f ? 1f / maxFraction : 1f;

            // Force layout rebuild so we know the actual width
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_phraseBarContainerRect);

            float scaleFactor = GetCanvasScaleFactor(_phraseBarContainerRect);
            float graphWidthUnits = _phraseBarContainerRect.rect.width;
            bool canSnap = graphWidthUnits > 0.01f;
            float graphWidthPixels = canSnap ? Mathf.Max(1f, graphWidthUnits * scaleFactor) : 0f;
            float gapPixels = PHRASE_HISTOGRAM_BAR_GAP * scaleFactor;

            bool isPartyMode = _phraseGrades != null && _phraseGrades.Count > 0;

            int barPoolIndex = 0;
            for (int i = 0; i < phrases.Count; i++)
            {
                float fraction = Mathf.Max(0f, phrases[i]);
                float normalized = fraction * normalizationScale;
                float barHeight = PHRASE_HISTOGRAM_MIN_BAR_HEIGHT +
                    normalized * (PHRASE_HISTOGRAM_GRAPH_HEIGHT - PHRASE_HISTOGRAM_MIN_BAR_HEIGHT);

                Color barColor;
                if (isPartyMode)
                {
                    barColor = GetPartyBarColor(_phraseGrades[i]);
                }
                else
                {
                    barColor = GetGradeColor(VocalsStats.SoloGradeFromFraction(fraction));
                }
                barColor.a = 0.85f;

                var barRect = GetOrCreatePhraseBar(barPoolIndex++);
                var image = barRect.GetComponent<Image>();
                image.color = barColor;
                image.raycastTarget = false;

                if (canSnap)
                {
                    float barHeightPixels = Mathf.Max(1f, Mathf.Round(barHeight * scaleFactor));
                    float totalGapPixels = gapPixels * (phraseCount - 1);
                    float availableWidth = graphWidthPixels - totalGapPixels;
                    float barWidthPixels = Mathf.Max(1f, availableWidth / phraseCount);
                    float slotLeftPixels = i * (barWidthPixels + gapPixels);

                    barRect.anchorMin = Vector2.zero;
                    barRect.anchorMax = Vector2.zero;
                    barRect.pivot = Vector2.zero;
                    barRect.anchoredPosition = new Vector2(slotLeftPixels / scaleFactor, 2f / scaleFactor);
                    barRect.sizeDelta = new Vector2(barWidthPixels / scaleFactor, barHeightPixels / scaleFactor);
                }
                else
                {
                    float slotWidth = 1f / phraseCount;
                    float gapUnits = PHRASE_HISTOGRAM_BAR_GAP / scaleFactor;
                    barRect.anchorMin = new Vector2(i * slotWidth, 0f);
                    barRect.anchorMax = new Vector2(i * slotWidth, 0f);
                    barRect.pivot = Vector2.zero;
                    barRect.offsetMin = new Vector2(gapUnits * 0.5f, 2f);
                    barRect.offsetMax = new Vector2(gapUnits * 0.5f + slotWidth * graphWidthUnits - gapUnits, 2f + barHeight);
                }

                barRect.gameObject.SetActive(true);
            }

            // Hide unused bars
            for (int i = barPoolIndex; i < _phraseBarPool.Count; i++)
            {
                _phraseBarPool[i].gameObject.SetActive(false);
            }
        }

        private void PopulateGradeSummary()
        {
            // Clean up old labels
            foreach (var label in _phraseSummaryLabels)
            {
                if (label != null) Destroy(label);
            }
            _phraseSummaryLabels.Clear();

            bool isPartyMode = _phraseGrades != null && _phraseGrades.Count > 0;

            if (isPartyMode)
            {
                PopulatePartyGradeSummary();
            }
            else
            {
                PopulateSoloGradeSummary();
            }
        }

        private void PopulateSoloGradeSummary()
        {
            var phrases = _phrasePercents;

            // Count grades
            int awesomeCount = 0, strongCount = 0, goodCount = 0;
            int okayCount = 0, messyCount = 0, awfulCount = 0;

            for (int i = 0; i < phrases.Count; i++)
            {
                switch (VocalsStats.SoloGradeFromFraction(phrases[i]))
                {
                    case PhraseGrade.Awesome:  awesomeCount++; break;
                    case PhraseGrade.Strong:   strongCount++;  break;
                    case PhraseGrade.Good:     goodCount++;    break;
                    case PhraseGrade.Okay:     okayCount++;    break;
                    case PhraseGrade.Messy:    messyCount++;   break;
                    case PhraseGrade.Awful:    awfulCount++;   break;
                }
            }

            // Build rows: Awesome → Awful
            var rows = new (PhraseGrade grade, int count, Color color)[]
            {
                (PhraseGrade.Awesome, awesomeCount, ColorAwesome),
                (PhraseGrade.Strong,  strongCount,  ColorStrong),
                (PhraseGrade.Good,    goodCount,    ColorGood),
                (PhraseGrade.Okay,    okayCount,    ColorOkay),
                (PhraseGrade.Messy,   messyCount,   ColorMessy),
                (PhraseGrade.Awful,   awfulCount,   ColorAwful),
            };

            for (int i = 0; i < rows.Length; i++)
            {
                var (grade, count, color) = rows[i];
                CreateGradeLabel(i, GetGradeLocalizedName(grade), count.ToString(), color);
            }

            // Percussion row (solo Vocals only — Harmony/Party Vocals have no percussion).
            if (_percussionTotal > 0)
            {
                int rowIndex = rows.Length;
                string percText = $"{_percussionHits} / {_percussionTotal}";
                CreateGradeLabel(rowIndex, Localize.Key("Gameplay.Vocals", "Percussion"), percText,
                    ColorStrong);
            }
        }

        private void PopulatePartyGradeSummary()
        {
            var phrases = _phrasePercents;
            var grades = _phraseGrades;

            // Count party grades from the PhraseGrade list.
            int tripleCount = 0, doubleCount = 0, singleCount = 0;
            int strongCount = 0, goodCount = 0, okayCount = 0, messyCount = 0, awfulCount = 0;

            for (int i = 0; i < grades.Count; i++)
            {
                switch (grades[i])
                {
                    case PhraseGrade.TripleAwesome: tripleCount++; break;
                    case PhraseGrade.DoubleAwesome: doubleCount++; break;
                    case PhraseGrade.Awesome:       singleCount++; break;
                    case PhraseGrade.Miss:
                        // For Miss phrases, classify their fraction via the solo tier system.
                        var soloGrade = VocalsStats.SoloGradeFromFraction(phrases[i]);
                        switch (soloGrade)
                        {
                            case PhraseGrade.Strong: strongCount++; break;
                            case PhraseGrade.Good:   goodCount++;   break;
                            case PhraseGrade.Okay:   okayCount++;   break;
                            case PhraseGrade.Messy:  messyCount++;  break;
                            default:                 awfulCount++;  break;
                        }
                        break;
                }
            }

            int rowIndex = 0;

            // Awesome row with Triple/Double/Single sub-columns: "3× N  2× N  1× N"
            // Color-coded: Triple = Yellow (HARM3), Double = Orange (HARM2), Single = Cyan (HARM1).
            string tripleHex = ColorUtility.ToHtmlStringRGBA(ColorHarm3);
            string doubleHex = ColorUtility.ToHtmlStringRGBA(ColorHarm2);
            string singleHex = ColorUtility.ToHtmlStringRGBA(ColorHarm1);
            string awesomeText =
                $"<color=#{tripleHex}>3× {tripleCount}</color>  " +
                $"<color=#{doubleHex}>2× {doubleCount}</color>  " +
                $"<color=#{singleHex}>1× {singleCount}</color>";
            CreateGradeLabelRaw(rowIndex++, GetGradeLocalizedName(PhraseGrade.Awesome), awesomeText, ColorAwesome);

            // Remaining rows: Strong → Awful (misses classified by fraction)
            var missRows = new (PhraseGrade grade, int count, Color color)[]
            {
                (PhraseGrade.Strong, strongCount, ColorStrong),
                (PhraseGrade.Good,   goodCount,   ColorGood),
                (PhraseGrade.Okay,   okayCount,   ColorOkay),
                (PhraseGrade.Messy,  messyCount,  ColorMessy),
                (PhraseGrade.Awful,  awfulCount,  ColorAwful),
            };

            foreach (var (grade, count, color) in missRows)
            {
                CreateGradeLabel(rowIndex++, GetGradeLocalizedName(grade), count.ToString(), color);
            }

            // No percussion row for Party Vocals — harmony charts have no percussion.
        }

        private void CreateGradeLabel(int rowIndex, string label, string value, Color color)
        {
            string hex = ColorUtility.ToHtmlStringRGBA(color);
            string text = $"<color=#{hex}>{label}: {value}</color>";
            CreateGradeLabelRaw(rowIndex, label, text, color);
        }

        private void CreateGradeLabelRaw(int rowIndex, string name, string richText, Color color)
        {
            var label = CreateHistogramLabel(_phraseSummaryContainerRect,
                $"GradeLabel_{name}", TextAlignmentOptions.Left);
            label.fontSize = PHRASE_SUMMARY_FONT_SIZE;
            label.text = richText;

            var labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.pivot = new Vector2(0f, 1f);
            float yOff = -rowIndex * PHRASE_SUMMARY_LINE_HEIGHT;
            labelRect.offsetMin = new Vector2(0f, yOff - PHRASE_SUMMARY_LINE_HEIGHT);
            labelRect.offsetMax = new Vector2(0f, yOff);

            _phraseSummaryLabels.Add(label.gameObject);
        }

        private RectTransform GetOrCreatePhraseBar(int index)
        {
            while (_phraseBarPool.Count <= index)
            {
                var barObject = new GameObject($"PhraseBar_{_phraseBarPool.Count}", typeof(RectTransform), typeof(Image));
                var barRect = (RectTransform) barObject.transform;
                barRect.SetParent(_phraseBarContainerRect, false);
                _phraseBarPool.Add(barRect);
            }

            return _phraseBarPool[index];
        }

        private void DestroyPhraseVisualization()
        {
            if (_phraseHistogramObject != null)
            {
                Destroy(_phraseHistogramObject);
                _phraseHistogramObject = null;
            }
            _phraseBarPool.Clear();
            _phraseSummaryLabels.Clear();
        }

        private static Color GetGradeColor(PhraseGrade grade)
        {
            return grade switch
            {
                PhraseGrade.Awesome => ColorAwesome,
                PhraseGrade.Strong  => ColorStrong,
                PhraseGrade.Good    => ColorGood,
                PhraseGrade.Okay    => ColorOkay,
                PhraseGrade.Messy   => ColorMessy,
                _                   => ColorAwful,
            };
        }

        /// <summary>
        /// Party Vocals bar color: gold for any Awesome grade, brighter for higher tiers.
        /// Miss bars are colored by their solo fraction grade.
        /// </summary>
        private static Color GetPartyBarColor(PhraseGrade grade)
        {
            return grade switch
            {
                PhraseGrade.TripleAwesome => new Color(1f, 0.90f, 0.2f),  // Bright gold
                PhraseGrade.DoubleAwesome => new Color(1f, 0.82f, 0.1f),  // Gold
                PhraseGrade.Awesome       => new Color(0.95f, 0.78f, 0f), // Dimmer gold
                _ => GetGradeColor(VocalsStats.SoloGradeFromFraction(0f)), // Miss → Awful red
            };
        }

        private static string GetGradeLocalizedName(PhraseGrade grade)
        {
            string key = grade switch
            {
                PhraseGrade.Awesome       => "Awesome",
                PhraseGrade.DoubleAwesome => "DoubleAwesome",
                PhraseGrade.TripleAwesome => "TripleAwesome",
                PhraseGrade.Strong        => "Strong",
                PhraseGrade.Good          => "Good",
                PhraseGrade.Okay          => "Okay",
                PhraseGrade.Messy         => "Messy",
                _                         => "Awful",
            };
            return Localize.Key("Gameplay.Vocals.Performance", key);
        }

        private static float GetCanvasScaleFactor(Component component)
        {
            var canvas = component.GetComponentInParent<Canvas>();
            return canvas != null ? Mathf.Max(0.0001f, canvas.scaleFactor) : 1f;
        }
    }
}
