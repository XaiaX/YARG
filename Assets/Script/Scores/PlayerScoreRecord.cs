using System;
using SQLite;
using YARG.Core;
using YARG.Core.Game;
using YARG.Helpers.Extensions;

namespace YARG.Scores
{
    [Table("PlayerScores")]
    public class PlayerScoreRecord
    {
        // DO NOT change any of these field names
        // without changing the SQL queries!

        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Indexed]
        public int GameRecordId { get; set; }

        [Indexed]
        public Guid PlayerId { get; set; }

        [Indexed]
        public Instrument Instrument { get; set; }
        public Difficulty Difficulty { get; set; }

        /// <summary>
        /// The chart source used for this score. Older Elite scores have unknown provenance.
        /// </summary>
        public DrumScoreCategory DrumScoreCategory { get; set; }

        /// <summary>
        /// Null for ordinary or historical scores; 0/1 records known Elite playback's actual
        /// effective extra-kick contribution to score,
        /// independently of the output instrument and source category.
        /// </summary>
        public int? DrumExtraKicks { get; set; }

        public Guid EnginePresetId { get; set; }

        public int        Score { get; set; }
        public StarAmount Stars { get; set; }

        public int  NotesHit    { get; set; }
        public int  NotesMissed { get; set; }
        public bool IsFc        { get; set; }
        public bool IsReplay    { get; set; }

        /// <remarks>
        /// This property was added afterwards, so it is nullable.
        /// Use <see cref="GetPercent"/> to get the actual percent value.
        /// </remarks>
        public float? Percent { get; set; }

        public string GetDifficultyDisplayName(bool detailed = false)
            => GetDifficultyDisplayName(Difficulty, DrumExtraKicks, detailed);

        public static string GetDifficultyDisplayName(Difficulty difficulty, int? extraKicks, bool detailed = false)
        {
            // Unknown historical rows retain their literal stored difficulty, including Expert+.
            string label = difficulty.ToDisplayName();
            return extraKicks == 1 ? label + "+" : extraKicks.HasValue || !detailed
                ? label : label + " (extra kicks unknown)";
        }

        [Ignore]
        public double DifficultyOrder => GetDifficultyOrder(Difficulty, DrumExtraKicks);

        public static double GetDifficultyOrder(Difficulty difficulty, int? extraKicks)
        {
            var baseDifficulty = difficulty == Difficulty.ExpertPlus ? Difficulty.Expert : difficulty;
            // Known rows use base * 2 + knownPlus. In descending order, unknown rows follow
            // both known variants at the same normalized tier, but precede every lower tier.
            return (int) baseDifficulty * 2 + (extraKicks == 1 ? 1 : extraKicks.HasValue ? 0 : -0.5);
        }

        public float GetPercent()
        {
            return Percent
                ?? (float) NotesHit / (NotesHit + NotesMissed);
        }
    }
}