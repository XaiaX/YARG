using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using YARG.Core;
using YARG.Core.Chart;
using YARG.Core.Game;

namespace YARG.Tests.EditMode
{
    public sealed class DrumScoreIdentityEditModeTests
    {
        private static Type RuntimeType(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).First(type => type != null);

        private static object RuntimePlayer(YargProfile profile) => Activator.CreateInstance(
            RuntimeType("YARG.Player.YargPlayer"), new object[] { profile, null });

        private static object Property(object target, string name) =>
            target.GetType().GetProperty(name).GetValue(target);

        private static void SetPlayback(object player, ResolvedDrumPlayback resolved)
        {
            InstrumentDifficulty<DrumNote> classic = null;
            InstrumentDifficulty<EliteDrumNote> elite = null;
            if (resolved.RequestedOutput == Instrument.EliteDrums)
            {
                elite = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums,
                    resolved.BaseDifficulty);
            }
            else
            {
                classic = new InstrumentDifficulty<DrumNote>(resolved.RequestedOutput,
                    resolved.BaseDifficulty);
            }
            player.GetType().GetMethod("SetDrumPlayback").Invoke(player,
                new object[] { resolved, classic, elite });
        }

        private static ResolvedDrumPlayback Playback(Instrument output = Instrument.EliteDrums,
            DrumSourceFormat source = DrumSourceFormat.Elite,
            Difficulty tier = Difficulty.Hard, bool effectiveExtraContent = true)
        {
            var category = ResolvedDrumPlayback.CategoryFor(output, source);
            var sourceTier = tier == Difficulty.Beginner ? Difficulty.Easy : tier;
            return new ResolvedDrumPlayback(output, source, tier, sourceTier,
                effectiveExtraContent ? DrumExtraKickPolicy.Include : DrumExtraKickPolicy.Remove,
                effectiveExtraContent, category);
        }

        private static YargProfile Profile(GameMode mode = GameMode.EliteDrums,
            Instrument output = Instrument.EliteDrums, Difficulty tier = Difficulty.Hard) =>
            new YargProfile(Guid.NewGuid())
            {
                GameMode = mode,
                CurrentInstrument = output,
                CurrentDifficulty = tier,
                IsBot = true,
            };

        [TestCase(Difficulty.Beginner, false)]
        [TestCase(Difficulty.Beginner, true)]
        [TestCase(Difficulty.Easy, false)]
        [TestCase(Difficulty.Easy, true)]
        [TestCase(Difficulty.Medium, false)]
        [TestCase(Difficulty.Medium, true)]
        [TestCase(Difficulty.Hard, false)]
        [TestCase(Difficulty.Hard, true)]
        [TestCase(Difficulty.Expert, false)]
        [TestCase(Difficulty.Expert, true)]
        public void EliteSaveValues_UseScoreIdentityBaseTierAndKickContribution(
            Difficulty tier, bool hasExtraKickContribution)
        {
            var profile = Profile(tier: tier);
            var player = RuntimePlayer(profile);
            var resolved = Playback(tier: tier);
            SetPlayback(player, resolved);
            player.GetType().GetMethod("SetDrumScoreIdentity").Invoke(player,
                new object[] { new DrumScoreIdentity(tier, hasExtraKickContribution, resolved.ScoreCategory) });

            Assert.That(Property(player, "ScoreDifficulty"), Is.EqualTo(tier));
            Assert.That(Property(player, "ScoreDrumExtraKicks"), Is.EqualTo(hasExtraKickContribution ? 1 : 0));
        }

        [Test]
        public void ScoreIdentity_SourceCategoryAndOutputAreIndependent()
        {
            var profile = Profile();
            var player = RuntimePlayer(profile);
            var resolved = Playback(Instrument.EliteDrums, DrumSourceFormat.FourLane);
            SetPlayback(player, resolved);
            var identity = new DrumScoreIdentity(Difficulty.Hard, true,
                DrumScoreCategory.FourLaneDerivedElite);
            player.GetType().GetMethod("SetDrumScoreIdentity").Invoke(player, new object[] { identity });

            Assert.That(Property(player, "ResolvedDrumPlayback"), Is.SameAs(resolved));
            Assert.That(Property(player, "DrumScoreIdentity"), Is.EqualTo(identity));
            Assert.That(Property(player, "ScoreDrumExtraKicks"), Is.EqualTo(1));
            Assert.That(Property(player, "ScoreDifficulty"), Is.EqualTo(Difficulty.Hard));
        }

        [TestCase(GameMode.FourLaneDrums, Instrument.FourLaneDrums)]
        [TestCase(GameMode.FiveLaneDrums, Instrument.FiveLaneDrums)]
        public void OrdinaryDrumModes_KeepExtraKickSaveValueNull(GameMode mode, Instrument output)
        {
            var profile = Profile(mode, output, Difficulty.Medium);
            var player = RuntimePlayer(profile);
            var resolved = Playback(output, output == Instrument.EliteDrums
                ? DrumSourceFormat.Elite : DrumSourceFormat.FourLane, Difficulty.Hard);
            SetPlayback(player, resolved);
            player.GetType().GetMethod("SetDrumScoreIdentity").Invoke(player,
                new object[] { new DrumScoreIdentity(Difficulty.Hard, true, resolved.ScoreCategory) });

            Assert.That(Property(player, "ScoreDrumExtraKicks"), Is.Null);
            Assert.That(Property(player, "ScoreDifficulty"), Is.EqualTo(Difficulty.Hard));
        }

        [Test]
        public void ClearDrumPlayback_ResetsResolvedAndScoreIdentity()
        {
            var player = RuntimePlayer(Profile());
            var resolved = Playback();
            SetPlayback(player, resolved);
            player.GetType().GetMethod("SetDrumScoreIdentity").Invoke(player,
                new object[] { new DrumScoreIdentity(Difficulty.Hard, true, resolved.ScoreCategory) });

            player.GetType().GetMethod("ClearDrumPlayback").Invoke(player, null);

            Assert.That(Property(player, "ResolvedDrumPlayback"), Is.Null);
            Assert.That(Property(player, "DrumScoreIdentity"), Is.Null);
            Assert.That(Property(player, "ScoreDrumExtraKicks"), Is.Null);
            Assert.That(Property(player, "ScoreDifficulty"), Is.EqualTo(Difficulty.Hard));
        }

        [Test]
        public void ScoreIdentity_IsNotSharedBetweenPlayers()
        {
            var first = RuntimePlayer(Profile());
            var second = RuntimePlayer(Profile());
            var resolved = Playback();
            SetPlayback(first, resolved);
            first.GetType().GetMethod("SetDrumScoreIdentity").Invoke(first,
                new object[] { new DrumScoreIdentity(Difficulty.Hard, true, resolved.ScoreCategory) });

            Assert.That(Property(first, "DrumScoreIdentity"), Is.Not.Null);
            Assert.That(Property(second, "DrumScoreIdentity"), Is.Null);
            Assert.That(Property(second, "ScoreDrumExtraKicks"), Is.Null);
        }

        [Test]
        public void ReplayScoreIdentity_DoesNotAlterPlayback()
        {
            var profile = Profile();
            var replayPlayback = Playback(Instrument.EliteDrums, DrumSourceFormat.FiveLane,
                Difficulty.Hard, effectiveExtraContent: false);
            profile.ReplayDrumPlayback = replayPlayback;
            var replayBytesBefore = SerializePlayback(profile.ReplayDrumPlayback);
            var player = RuntimePlayer(profile);
            SetPlayback(player, replayPlayback);
            player.GetType().GetMethod("SetDrumScoreIdentity").Invoke(player,
                new object[] { new DrumScoreIdentity(Difficulty.Hard, true, replayPlayback.ScoreCategory) });

            Assert.That(replayPlayback.EffectiveExtraContent, Is.False);
            Assert.That(Property(player, "ResolvedDrumPlayback"), Is.SameAs(replayPlayback));
            Assert.That(SerializePlayback((ResolvedDrumPlayback)Property(player, "ResolvedDrumPlayback")),
                Is.EqualTo(replayBytesBefore));
            Assert.That(SerializePlayback(profile.ReplayDrumPlayback), Is.EqualTo(replayBytesBefore));
        }

        private static byte[] SerializePlayback(ResolvedDrumPlayback playback)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream))
                playback.Serialize(writer);
            return stream.ToArray();
        }
    }
}
