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
        public bool  IsReplay;

        public YargPlayer Player;
        public BaseStats  Stats;

        // Runtime-only vocal phrase data. These captures are not part of persisted stats or replay data;
        // they are passed directly from the gameplay player to the score card for the current song.
        public IReadOnlyList<float> VocalPhrasePercents;
        public IReadOnlyList<PhraseGrade> VocalPhraseGrades;
        public IReadOnlyList<IReadOnlyList<PartyPartResult>> VocalPhrasePartResults;
        public double VocalAwesomeThreshold;
        public int VocalHarmonyPartIndex;
        public int VocalPercussionHits;
        public int VocalPercussionTotal;
    }

    public struct ScoreScreenStats
    {
        public PlayerScoreCard[] PlayerScores;

        public int BandStars;
        public int BandScore;

        public double MeanAverageOffset;

#nullable enable
        public ReplayInfo? ReplayInfo;
        public bool? ReplayWasConsistent;
#nullable disable
    }
}