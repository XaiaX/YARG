using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using YARG.Core;
using YARG.Core.Game;

namespace YARG.Tests.EditMode
{
    public sealed class DifficultySelectNativeSelectionTests
    {
        private static readonly Type MenuType = ProductionType(
            "YARG.Menu.DifficultySelect.DifficultySelectMenu");

        [Test]
        public void NativeSelection_Clears_EliteTarget_WhenOutputMatchesCurrentInstrument()
        {
            var profile = Profile(GameMode.EliteDrums, Instrument.FourLaneDrums,
                Instrument.FourLaneDrums, Instrument.FourLaneDrums);

            Invoke("SelectNativeInstrument", profile, Instrument.FourLaneDrums);

            Assert.That(profile.EliteDrumsDownchartTarget, Is.Null);
            Assert.That(profile.CurrentInstrument, Is.EqualTo(Instrument.FourLaneDrums));
            Assert.That(Invoke<bool>("IsNativeInstrumentSelected", profile,
                Instrument.FourLaneDrums), Is.True);
        }

        [Test]
        public void NativeSelection_Clears_EliteTarget_WhenSwitchingAcrossNativeRows()
        {
            var profile = Profile(GameMode.EliteDrums, Instrument.ProDrums,
                Instrument.ProDrums, Instrument.ProDrums);

            Invoke("SelectNativeInstrument", profile, Instrument.FiveLaneDrums);

            Assert.That(profile.EliteDrumsDownchartTarget, Is.Null);
            Assert.That(profile.CurrentInstrument, Is.EqualTo(Instrument.FiveLaneDrums));
            Assert.That(profile.PreferredInstrument, Is.EqualTo(Instrument.ProDrums));
            Assert.That(Invoke<bool>("IsNativeInstrumentSelected", profile,
                Instrument.FiveLaneDrums), Is.True);
        }

        [Test]
        public void GeneratedTargetSelection_RemainsExplicit_AndActive()
        {
            var profile = Profile(GameMode.EliteDrums, Instrument.FourLaneDrums,
                Instrument.FourLaneDrums, Instrument.FourLaneDrums);

            Invoke("SelectEliteDrumsDownchartTarget", profile, Instrument.ProDrums);

            Assert.That(profile.EliteDrumsDownchartTarget, Is.EqualTo(Instrument.ProDrums));
            Assert.That(profile.CurrentInstrument, Is.EqualTo(Instrument.ProDrums));
            Assert.That(profile.PreferredInstrument, Is.EqualTo(Instrument.ProDrums));
            Assert.That(EliteDrumsDownchartRules.IsDownchartTargetActive(profile), Is.True);
            Assert.That(Invoke<bool>("IsNativeInstrumentSelected", profile,
                Instrument.ProDrums), Is.False);
        }

        [Test]
        public void TargetCleanup_OnlyTreats_UnofferedTargets_AsStale()
        {
            var profile = Profile(GameMode.EliteDrums, Instrument.ProDrums,
                Instrument.ProDrums, Instrument.ProDrums);

            Assert.That(Invoke<bool>("IsStaleEliteDrumsDownchartTarget", profile,
                new[] { Instrument.ProDrums }), Is.False,
                "An offered generated target must survive difficulty refresh.");
            Assert.That(EliteDrumsDownchartRules.IsDownchartTargetActive(profile), Is.True);

            profile.EliteDrumsDownchartTarget = (Instrument) 99;
            profile.CurrentInstrument = (Instrument) 99;
            Assert.That(Invoke<bool>("IsStaleEliteDrumsDownchartTarget", profile,
                new[] { Instrument.ProDrums }), Is.True);
            Assert.That(EliteDrumsDownchartRules.IsDownchartTargetActive(profile), Is.False,
                "Malformed targets must remain rejected by the strict Core predicate.");
        }

        [Test]
        public void EliteNativeChoices_AreProFirstForAvailabilityAndFallback()
        {
            var available = new[]
            {
                Instrument.FourLaneDrums,
                Instrument.FiveLaneDrums,
                Instrument.ProDrums,
            };

            var ordered = (IReadOnlyList<Instrument>) MaestroRule("OrderNativeInstruments",
                GameMode.EliteDrums, available);

            Assert.That(ordered, Is.EqualTo(new[]
            {
                Instrument.ProDrums,
                Instrument.FourLaneDrums,
                Instrument.FiveLaneDrums,
            }));
            Assert.That(MaestroRule("SelectNativeInstrumentFallback",
                Instrument.EliteDrums, GameMode.EliteDrums, ordered),
                Is.EqualTo(Instrument.ProDrums));
        }

        [Test]
        public void MixedShowEliteRequest_KeepsPreferredIdentityUntilChartResolution()
        {
            // Selection offers the native Elite request even when each song's actual
            // native drum format differs. The loaded chart resolves playback per song.
            var profile = Profile(GameMode.EliteDrums, Instrument.ProDrums,
                Instrument.EliteDrums, null);
            var available = new[] { Instrument.EliteDrums };

            var selected = (Instrument) MaestroRule("SelectNativeInstrumentFallback",
                profile.PreferredInstrument, profile.GameMode, available);
            Invoke("SelectNativeInstrument", profile, selected);

            Assert.That(profile.PreferredInstrument, Is.EqualTo(Instrument.EliteDrums));
            Assert.That(profile.CurrentInstrument, Is.EqualTo(Instrument.EliteDrums));
            Assert.That(profile.EliteDrumsDownchartTarget, Is.Null);
        }

        [Test]
        public void NativeFallback_PreservesAvailablePreference_AndOtherModeOrder()
        {
            var eliteAvailable = new[] { Instrument.FourLaneDrums, Instrument.ProDrums };
            Assert.That(MaestroRule("SelectNativeInstrumentFallback",
                Instrument.FourLaneDrums, GameMode.EliteDrums, eliteAvailable),
                Is.EqualTo(Instrument.FourLaneDrums));

            var nonEliteAvailable = new[] { Instrument.FourLaneDrums, Instrument.ProDrums };
            Assert.That(MaestroRule("OrderNativeInstruments",
                GameMode.FourLaneDrums, nonEliteAvailable),
                Is.EqualTo(nonEliteAvailable));
            Assert.That(MaestroRule("SelectNativeInstrumentFallback",
                Instrument.FiveLaneDrums, GameMode.FourLaneDrums, nonEliteAvailable),
                Is.EqualTo(Instrument.FourLaneDrums));
        }

        private static object MaestroRule(string name, params object[] args)
        {
            var type = ProductionType("YARG.Menu.Maestro.MaestroSelectionRules");
            var method = type.GetMethod(name, BindingFlags.Static | BindingFlags.Public);
            Assert.That(method, Is.Not.Null);
            return method.Invoke(null, args);
        }

        private static YargProfile Profile(GameMode mode, Instrument current,
            Instrument preferred, Instrument? target)
        {
            return new YargProfile
            {
                GameMode = mode,
                CurrentInstrument = current,
                PreferredInstrument = preferred,
                EliteDrumsDownchartTarget = target,
            };
        }

        private static object Invoke(string name, YargProfile profile, Instrument instrument)
        {
            var method = MenuType.GetMethod(name,
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Missing Difficulty Select seam {name}.");
            return method.Invoke(null, new object[] { profile, instrument });
        }

        private static T Invoke<T>(string name, YargProfile profile, Instrument instrument)
        {
            return (T) Invoke(name, profile, instrument);
        }

        private static object Invoke(string name, YargProfile profile,
            IReadOnlyCollection<Instrument> offeredTargets)
        {
            var method = MenuType.GetMethod(name,
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Missing Difficulty Select seam {name}.");
            return method.Invoke(null, new object[] { profile, offeredTargets });
        }

        private static T Invoke<T>(string name, YargProfile profile,
            IReadOnlyCollection<Instrument> offeredTargets)
        {
            return (T) Invoke(name, profile, offeredTargets);
        }

        private static Type ProductionType(string name)
        {
            var type = Type.GetType(name + ", Assembly-CSharp");
            if (type is null)
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    type = assembly.GetType(name);
                    if (type is not null)
                        break;
                }
            }

            Assert.That(type, Is.Not.Null,
                $"Production type {name} is missing from loaded Unity assemblies.");
            return type;
        }
    }
}
