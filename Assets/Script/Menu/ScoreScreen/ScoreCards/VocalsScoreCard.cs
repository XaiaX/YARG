using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using YARG.Core;
using YARG.Core.Engine.Vocals;
using YARG.Core.Engine.Vocals.Engines;
using YARG.Helpers.Extensions;

namespace YARG.Menu.ScoreScreen
{
    public class VocalsScoreCard : ScoreCard<VocalsStats>
    {
        // Party phrase performance replaces the note-offset histogram in its advanced view.
        protected override bool ShouldShowOffsetHistogram =>
            Player?.Profile.GameMode != GameMode.PartyVocals;

        private IReadOnlyList<float> _phrasePercents;
        private IReadOnlyList<PhraseGrade> _phraseGrades;
        private IReadOnlyList<IReadOnlyList<PartyPartResult>> _phrasePartResults;
        private double _awesomeThreshold;
        private int _harmonyPartIndex;
        private int _percussionHits;
        private int _percussionTotal;

        public void SetPhraseSummary(IReadOnlyList<float> phrasePercents,
            IReadOnlyList<PhraseGrade> phraseGrades,
            IReadOnlyList<IReadOnlyList<PartyPartResult>> phrasePartResults,
            double awesomeThreshold, int harmonyPartIndex, int percussionHits, int percussionTotal)
        {
            _phrasePercents = phrasePercents;
            _phraseGrades = phraseGrades;
            _phrasePartResults = phrasePartResults;
            _awesomeThreshold = awesomeThreshold;
            _harmonyPartIndex = harmonyPartIndex;
            _percussionHits = percussionHits;
            _percussionTotal = percussionTotal;
        }

        public override void SetCardContents()
        {
            base.SetCardContents();

            // Party Vocals uses the same part-count icon convention as the gameplay HUD.
            string iconName = Player.Profile.GameMode == GameMode.PartyVocals
                ? GlobalVariables.State.CurrentSong.VocalsCount switch
                {
                    >= 3 => "harmVocals",
                    2 => "twoVocals",
                    _ => "vocals",
                }
                : Player.Profile.CurrentInstrument.ToResourceName();
            _instrumentIcon.sprite = Addressables
                .LoadAssetAsync<Sprite>($"InstrumentIcons[{iconName}]")
                .WaitForCompletion();

            if (Player.Profile.GameMode == GameMode.PartyVocals &&
                _phrasePercents != null && _phrasePercents.Count > 0)
            {
                VocalsPhraseHistogram.Build(AdvancedStatsRect, _phrasePercents, CreateStatLabel,
                    AdvancedAccentColor, _percussionHits, _percussionTotal, _phraseGrades,
                    _phrasePartResults, _awesomeThreshold, _harmonyPartIndex);
            }
        }

        // Traditional vocals retain their existing single-view score card. Party Vocals has a
        // populated phrase summary, so its advanced presentation is available for explicit toggling.
        public bool HasPartyPhraseSummary => Player?.Profile.GameMode == GameMode.PartyVocals &&
            _phrasePercents is { Count: > 0 };

        public override void SetAdvancedStatsShown(bool showAdvanced)
        {
            if (HasPartyPhraseSummary)
            {
                base.SetAdvancedStatsShown(showAdvanced);
            }
        }
    }
}