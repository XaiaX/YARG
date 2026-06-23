using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using YARG.Core.Engine.Vocals;
using YARG.Core.Engine.Vocals.Engines;
using YARG.Helpers.Extensions;

namespace YARG.Menu.ScoreScreen
{
    public class VocalsScoreCard : ScoreCard<VocalsStats>
    {
        // The hit-offset histogram is meaningless for vocals (graded per phrase, not per note);
        // we render a phrase summary in its place instead.
        protected override bool ShouldShowOffsetHistogram => false;

        private IReadOnlyList<float> _phrasePercents;
        private IReadOnlyList<PhraseGrade> _phraseGrades;
        private IReadOnlyList<IReadOnlyList<PartyPartResult>> _phrasePartResults;
        private double _awesomeThreshold;
        private int _percussionHits;
        private int _percussionTotal;

        public void SetPhrasePercents(IReadOnlyList<float> percents)
        {
            _phrasePercents = percents;
        }

        public void SetPhraseGrades(IReadOnlyList<PhraseGrade> grades)
        {
            _phraseGrades = grades;
        }

        public void SetPhrasePartResults(IReadOnlyList<IReadOnlyList<PartyPartResult>> results, double awesomeThreshold)
        {
            _phrasePartResults = results;
            _awesomeThreshold = awesomeThreshold;
        }

        public void SetPercussion(int hits, int total)
        {
            _percussionHits = hits;
            _percussionTotal = total;
        }

        public override void SetCardContents()
        {
            base.SetCardContents();

            // Set background icon. Party vocals use the part-count mic icon (vocals/twoVocals/
            // harmVocals — same logic as PlayerNameDisplay) instead of the single-mic instrument icon.
            _instrumentIcon.sprite = Addressables.LoadAssetAsync<Sprite>(GetVocalsIconKey()).WaitForCompletion();

            // Build the phrase histogram + tally into the Advanced view (advanced-only automatically).
            // Renders nothing if the list is null/empty.
            VocalsPhraseHistogram.Build(AdvancedStatsRect, _phrasePercents, CreateStatLabel, AdvancedAccentColor,
                _percussionHits, _percussionTotal, _phraseGrades, _phrasePartResults, _awesomeThreshold);
        }

        // Bare harmony part-count icon name (vocals/twoVocals/harmVocals) for party vocals, based
        // on the highest harmony part present in the phrase results; solo/traditional vocals fall
        // back to the instrument resource name. Feeds both the background icon and the difficulty ring.
        private string GetVocalsPartIconName()
        {
            if (_phrasePartResults != null && _phrasePartResults.Count > 0)
            {
                int partCount = 1;
                foreach (var phrase in _phrasePartResults)
                {
                    if (phrase == null) continue;
                    foreach (var pr in phrase)
                    {
                        int pc = pr.PartIndex + 1;
                        if (pc > partCount) partCount = pc;
                    }
                }
                return partCount switch
                {
                    >= 3 => "harmVocals",
                    2 => "twoVocals",
                    _ => "vocals",
                };
            }
            return Player.Profile.CurrentInstrument.ToResourceName();
        }

        // The background icon uses the full Addressable key; the difficulty ring takes the bare name.
        private string GetVocalsIconKey() => $"InstrumentIcons[{GetVocalsPartIconName()}]";

        protected override string GetDifficultyRingAsset() => GetVocalsPartIconName();

        // Upstream (#fb8cfd52) hides all advanced stats for vocals with an empty override here.
        // Our branch intentionally shows a phrase-level histogram as the advanced view instead,
        // so we deliberately do NOT override SetAdvancedStatsShown.
    }
}
