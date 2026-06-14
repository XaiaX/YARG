using System.Collections.Generic;
using YARG.Core.Engine;
using YARG.Core.Engine.Vocals;
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

        // Vocals phrase-summary data (captured live during gameplay, not replay-serialized).
        // Null for non-vocal players or old replays.
        public List<float>       VocalPhrasePercents;
        public int               VocalPercussionHits;
        public int               VocalPercussionTotal;
        public List<PhraseGrade> VocalPhraseGrades;
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