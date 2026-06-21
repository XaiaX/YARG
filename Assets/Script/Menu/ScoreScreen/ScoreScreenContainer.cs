using System.Collections.Generic;
using YARG.Core.Engine;
using YARG.Core.Engine.Vocals;
using YARG.Core.Engine.Vocals.Engines;
using YARG.Core.Replays;
using YARG.Player;
using YARG.Replays;

namespace YARG.Menu.ScoreScreen
{
    public struct PlayerScoreCard
    {
        public bool  IsHighScore;
        public float AverageMultiplier;

        public YargPlayer Player;
        public BaseStats  Stats;

        // Per-phrase normalized hit percents for vocals players, in song order. Null for
        // non-vocals players. Feeds the vocals phrase summary in the Advanced view.
        public IReadOnlyList<float> VocalPhrasePercents;

        // Vocal percussion hits / total (0 for non-vocals or charts without percussion).
        public int VocalPercussionHits;
        public int VocalPercussionTotal;

        // Per-phrase Party Vocals grades (Miss/Awesome/DoubleAwesome/TripleAwesome), in song order.
        // Null for non-vocals players and solo/traditional-harmony vocals (no grades list). When
        // non-empty, the score screen histogram shows Triple/Double/Single Awesome breakdowns.
        public IReadOnlyList<PhraseGrade> VocalPhraseGrades;

        // Per-phrase per-part Party Vocals results: for each phrase, the active harmony parts and
        // their canonical meters. Null for non-vocals/solo. Drives the per-part Awesome segments.
        public IReadOnlyList<IReadOnlyList<PartyPartResult>> VocalPhrasePartResults;

        // Awesome meter threshold (PhraseHitPercent); 0 when not a vocals player. Used to scale
        // each part's meter into a 0..1 fill in the histogram.
        public double VocalAwesomeThreshold;
    }

    public struct ScoreScreenStats
    {
        public PlayerScoreCard[] PlayerScores;

        public int BandStars;
        public int BandScore;

#nullable enable
        public ReplayInfo? ReplayInfo;
#nullable disable
    }
}
