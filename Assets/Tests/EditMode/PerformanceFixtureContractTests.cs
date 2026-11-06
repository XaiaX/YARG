// pattern: Functional Core
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using NUnit.Framework;
using YARG.Core;
using YARG.Core.Game;
using YARG.Core.Song;

namespace YARG.Tests.EditMode
{
    public sealed class PerformanceFixtureContractTests
    {
        private static Type Definition => Type.GetType("YARG.PerformanceTesting.PerformanceFixtureDefinition, Assembly-CSharp", true);
        private static Type Catalog => Type.GetType("YARG.PerformanceTesting.PerformanceFixtureCatalog, Assembly-CSharp", true);
        private const string VALID = "{\"mode\":\"run\",\"songFolders\":[\"/songs\"],\"song\":{\"hash\":\"0000000000000000000000000000000000000000\"}";

        [TestCase(",\"surprise\":true}")]
        [TestCase(",\"version\":2}")]
        [TestCase(",\"songSpeed\":0}")]
        [TestCase(",\"warmupSeconds\":-1}")]
        [TestCase(",\"durationSeconds\":0}")]
        [TestCase(",\"settings\":null}")]
        [TestCase(",\"$type\":\"System.IO.FileInfo, System.Private.CoreLib\"}")]
        [TestCase(",\"bots\":[{\"instrument\":\"Bogus\"}]}")]
        [TestCase(",\"bots\":[{\"instrument\":\"20\"}]}")]
        [TestCase(",\"bots\":[{\"instrument\":\"ProDrums\",\"difficulty\":\"99\"}]}")]
        [TestCase(",\"upgradeDtaPath\":\"relative.dta\"}")]
        [TestCase(",\"bots\":[{\"instrument\":\"Vocals\"}]}")]
        [TestCase(",\"bots\":[{\"instrument\":\"Harmony\"}]}")]
        [TestCase(",\"bots\":[{\"instrument\":\"FourLaneDrums\"}]}")]
        [TestCase(",\"bots\":[{\"instrument\":\"FiveLaneDrums\"}]}")]
        [TestCase(",\"bots\":[{\"instrument\":\"ProGuitar_17Fret\"}]}")]
        [TestCase(",\"bots\":[{\"instrument\":\"ProBass_22Fret\"}]}")]
        public void InvalidContractsFailBeforeStartup(string suffix)
        {
            var error = Assert.Throws<TargetInvocationException>(() => Parse(VALID + suffix));
            Assert.That(error.InnerException, Is.InstanceOf<ArgumentException>());
        }

        [Test]
        public void DefaultsAndDiscoveryDoNotRequireInstalledProfiles()
        {
            var definition = Parse("{\"mode\":\"discover\",\"songFolders\":[\"/songs\"]}");
            Assert.That(Property(definition, "Version"), Is.EqualTo(1));
            Assert.That(Property(definition, "RequireFocus"), Is.EqualTo(true));
            Assert.That(Property(definition, "WarmupSeconds"), Is.EqualTo(10d));
            Assert.That(Property(definition, "DurationSeconds"), Is.Null);
            var botDefinition = Parse(VALID + ",\"bots\":[{\"instrument\":\"ProDrums\"}]}");
            var firstBot = ((IEnumerable) Property(botDefinition, "Bots")).Cast<object>().Single();
            Assert.That(Property(firstBot, "Required"), Is.False);
        }

        [Test]
        public void LeadOnlySongSupportsPartyVocalsAtExpertAndNotExpertPlus()
        {
            var parts = AvailableParts.Default;
            parts.LeadVocals.SubTracks = 1;
            var song = Song(parts);
            var profiles = Bots(song, "[{\"instrument\":\"PartyVocals\"}]");
            Assert.That(profiles.Single().GameMode, Is.EqualTo(GameMode.PartyVocals));
            Assert.That(profiles.Single().PartyVocalsMicCountOverride, Is.Zero);
            var error = Assert.Throws<TargetInvocationException>(() => Bots(song,
                "[{\"instrument\":\"PartyVocals\",\"difficulty\":\"ExpertPlus\"}]"));
            Assert.That(error.InnerException, Is.InstanceOf<ArgumentException>());
        }

        [Test]
        public void NativeEliteRequirementDoesNotSilentlyUseClassicChart()
        {
            var parts = AvailableParts.Default;
            parts.ProDrums.ActivateDifficulty(Difficulty.Expert);
            var song = Song(parts);
            var error = Assert.Throws<TargetInvocationException>(() => Bots(song, "[{\"instrument\":\"EliteDrums\"}]"));
            Assert.That(error.InnerException, Is.InstanceOf<ArgumentException>());
            var converted = Bots(song, "[{\"instrument\":\"EliteDrums\",\"requireNativeElite\":false}]").Single();
            Assert.That(converted.IsModifierActive(Modifier.EnableEliteUpconversion), Is.True);
            parts.EliteDrums.ActivateDifficulty(Difficulty.Expert);
            var native = Bots(Song(parts), "[{\"instrument\":\"EliteDrums\"}]").Single();
            Assert.That(native.IsModifierActive(Modifier.EnableEliteUpconversion), Is.False);
        }

        [Test]
        public void OptionalMissingPartsSkipButRequiredPartsAndEmptyRostersFail()
        {
            var parts = AvailableParts.Default;
            parts.FiveFretGuitar.ActivateDifficulty(Difficulty.Expert);
            var song = Song(parts);
            Assert.That(Bots(song, "[{\"instrument\":\"FiveFretGuitar\"},{\"instrument\":\"ProKeys\"}]").Count, Is.EqualTo(1));
            Assert.Throws<TargetInvocationException>(() => Bots(song, "[{\"instrument\":\"FiveFretGuitar\"},{\"instrument\":\"ProKeys\",\"required\":true}]"));
            Assert.Throws<TargetInvocationException>(() => Bots(Song(AvailableParts.Default), "[]"));
        }

        [TestCase("\"IsBot\":false")]
        [TestCase("\"noteSpeed\":7")]
        [TestCase("\"NoteSpeed\":\"7\"")]
        [TestCase("\"NoteSpeed\":0")]
        [TestCase("\"NoteSpeed\":-1")]
        [TestCase("\"NoteSpeed\":1e100")]
        [TestCase("\"NoteSpeed\":NaN")]
        [TestCase("\"HighwayLength\":1e-100")]
        [TestCase("\"HighwayLength\":null")]
        [TestCase("\"LeftyFlip\":1")]
        [TestCase("\"UseCymbalModels\":\"true\"")]
        public void InvalidProfileOverridesFailBeforeSongAvailabilityIsConsidered(string entry)
        {
            var error = Assert.Throws<TargetInvocationException>(() => Parse(VALID +
                ",\"bots\":[{\"instrument\":\"ProDrums\",\"profileOverrides\":{" + entry + "}}]}"));
            Assert.That(error.InnerException, Is.InstanceOf<ArgumentException>());
        }

        [Test]
        public void OverridesStayWithTheirRequestAfterMissingOptionalPartsAreSkipped()
        {
            var parts = AvailableParts.Default;
            parts.EliteDrums.ActivateDifficulty(Difficulty.Expert);
            var json = "[{\"instrument\":\"ProKeys\",\"profileOverrides\":{\"NoteSpeed\":2}}," +
                "{\"instrument\":\"EliteDrums\",\"profileOverrides\":{\"NoteSpeed\":7.5,\"HighwayLength\":1.25," +
                "\"LeftyFlip\":true,\"UseCymbalModels\":false,\"KeepOriginalCymbalColors\":true," +
                "\"OneFretHiHatSustains\":true,\"HideHiHatSustains\":true}}]";
            var profile = Bots(Song(parts), json).Single();
            Assert.That(profile.NoteSpeed, Is.EqualTo(7.5f));
            Assert.That(profile.HighwayLength, Is.EqualTo(1.25f));
            Assert.That(profile.LeftyFlip, Is.True);
            Assert.That(profile.UseCymbalModels, Is.False);
            Assert.That(profile.KeepOriginalCymbalColors, Is.True);
            Assert.That(profile.OneFretHiHatSustains, Is.True);
            Assert.That(profile.HideHiHatSustains, Is.True);
            Assert.That(profile.IsBot, Is.True);
            Assert.That(Bots(Song(parts), "[]").Single().NoteSpeed, Is.EqualTo(new YargProfile().NoteSpeed));
        }

        [Test]
        public void AutomaticRosterCanOmitAnyMainPartIncludingGuitarAndVocals()
        {
            var parts = AvailableParts.Default;
            parts.Keys.ActivateDifficulty(Difficulty.Expert);
            Assert.That(Bots(Song(parts), "[]").Select(profile => profile.CurrentInstrument),
                Is.EqualTo(new[] { Instrument.Keys }));
        }

        [Test]
        public void RepeatedRostersAreIndependentAndCannotMutateSavedProfiles()
        {
            var parts = AvailableParts.Default;
            parts.FiveFretGuitar.ActivateDifficulty(Difficulty.Expert);
            parts.FiveFretBass.ActivateDifficulty(Difficulty.Expert);
            var song = Song(parts);
            var first = Bots(song, "[]");
            var second = Bots(song, "[]");
            Assert.That(first.Select(p => p.Name), Is.EqualTo(second.Select(p => p.Name)));
            Assert.That(first.All(p => p.IsBot && !p.MaestroSittingOut), Is.True);
            Assert.That(first.Select(p => p.Id).Intersect(second.Select(p => p.Id)), Is.Empty);
            first[0].Name = "edited";
            Assert.That(second[0].Name, Is.Not.EqualTo("edited"));
            Assert.That(second.All(p => p.CurrentDifficulty == Difficulty.Expert), Is.True);
        }

        [Test]
        public void AutomaticRosterCoversAllSevenMainPartsInStableOrder()
        {
            var parts = AvailableParts.Default;
            parts.HarmonyVocals.SubTracks = 7;
            parts.FiveFretGuitar.ActivateDifficulty(Difficulty.Expert);
            parts.FiveFretBass.ActivateDifficulty(Difficulty.Expert);
            parts.Keys.ActivateDifficulty(Difficulty.Expert);
            parts.ProKeys.ActivateDifficulty(Difficulty.Expert);
            parts.ProDrums.ActivateDifficulty(Difficulty.Expert);
            parts.EliteDrums.ActivateDifficulty(Difficulty.Expert);
            Assert.That(Bots(Song(parts), "[]").Select(profile => profile.CurrentInstrument), Is.EqualTo(new[]
            {
                Instrument.PartyVocals, Instrument.FiveFretGuitar, Instrument.FiveFretBass,
                Instrument.Keys, Instrument.ProKeys, Instrument.ProDrums, Instrument.EliteDrums
            }));
        }

        [Test]
        public void DiscoveryListsVocalDifficultiesWithoutInventingNativeElite()
        {
            var parts = AvailableParts.Default;
            parts.LeadVocals.SubTracks = 1;
            parts.ProDrums.ActivateDifficulty(Difficulty.Expert);
            var description = Catalog.GetMethod("Describe").Invoke(null, new object[] { Song(parts) });
            var entries = ((IEnumerable) Property(description, "Parts")).Cast<object>().ToArray();
            Assert.That(entries.Select(entry => Property(entry, "Instrument")),
                Is.EquivalentTo(new[] { "PartyVocals", "ProDrums" }));
            var vocals = entries.Single(entry => (string) Property(entry, "Instrument") == "PartyVocals");
            Assert.That((IEnumerable) Property(vocals, "Difficulties"), Does.Contain("Expert"));
            Assert.That((IEnumerable) Property(vocals, "Difficulties"), Does.Not.Contain("ExpertPlus"));
        }

        [Test]
        public void DiscoveryCanSearchNamesButRunsUseExactHashAndDirectory()
        {
            var first = Song(AvailableParts.Default, "/songs/one", "Foreplay/Long Time", "Boston");
            var second = Song(AvailableParts.Default, "/songs/two", "Other song", "Boston");
            var source = new[] { second, first };
            var discovery = Parse("{\"mode\":\"discover\",\"songFolders\":[\"/songs\"],\"song\":{\"name\":\"foreplay\",\"artist\":\"BOST\"}}");
            var matches = (SongEntry[]) Catalog.GetMethod("Find").Invoke(null, new[] { source, Property(discovery, "Song"), true });
            Assert.That(matches, Is.EqualTo(new[] { first }));

            var exact = Parse("{\"songFolders\":[\"/songs\"],\"song\":{\"hash\":\"0000000000000000000000000000000000000000\",\"directory\":\"/songs/two\"}}");
            matches = (SongEntry[]) Catalog.GetMethod("Find").Invoke(null, new[] { source, Property(exact, "Song"), false });
            Assert.That(matches, Is.EqualTo(new[] { second }));
        }

        private static object Parse(string json) => Definition.GetMethod("Parse").Invoke(null, new object[] { json });
        private static object Property(object value, string name) => value.GetType().GetProperty(name).GetValue(value);
        private static List<YargProfile> Bots(SongEntry song, string json)
        {
            var definition = Parse(VALID + ",\"bots\":" + json + "}");
            return (List<YargProfile>) Catalog.GetMethod("BuildBots").Invoke(null, new[] { song, Property(definition, "Bots") });
        }

        private static SongEntry Song(AvailableParts parts, string location = "/songs/example", string name = "Example", string artist = "Artist")
        {
            var entryType = typeof(SongEntry).Assembly.GetType("YARG.Core.Song.UnpackedIniEntry", true);
            var song = (SongEntry) FormatterServices.GetUninitializedObject(entryType);
            typeof(SongEntry).GetField("_parts", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(song, parts);
            typeof(SongEntry).GetField("_name", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(song, new SortString(name));
            typeof(SongEntry).GetField("_artist", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(song, new SortString(artist));
            entryType.BaseType.GetField("_location", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(song, location);
            return song;
        }
    }
}
