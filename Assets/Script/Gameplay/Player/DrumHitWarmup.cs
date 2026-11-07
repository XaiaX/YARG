// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using Unity.Profiling;
using YARG.Core;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Game;
using YARG.Core.Input;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Gameplay.Player
{
    /// <summary>Compiles the common first-hit path on a private chart and engine during loading.</summary>
    internal static class DrumHitWarmup
    {
        private const double HIT_TIME = 0.5;
        private const uint HIT_TICK = 480;

        private static readonly ProfilerMarker _prepareMarker = new("YARG.DrumHitWarmup");
        private static readonly HashSet<(Instrument, bool, bool, bool)> _warmed = new();

        internal static int Prepare(IEnumerable<BasePlayer> players)
        {
            using var timing = _prepareMarker.Auto();
            int hits = 0;
            foreach (var player in players)
            {
                bool bot = player.Player.Profile.IsBot;
                switch (player)
                {
                    case EliteDrumsPlayer elite:
                        if (PrepareOnce(Instrument.EliteDrums, bot, true, elite.EngineParams)) hits++;
                        break;
                    case DrumsPlayer classic:
                        if (PrepareOnce(classic.NoteTrack.Instrument, bot,
                            player.Player.Profile.GameMode == GameMode.EliteDrums, classic.EngineParams)) hits++;
                        break;
                }
            }
            return hits;
        }

        private static bool PrepareOnce(Instrument instrument, bool bot, bool midi, DrumsEngineParameters parameters)
        {
            // A replay can use parameters with sustains disabled.
            bool sustains = instrument == Instrument.EliteDrums && parameters.EliteHiHatSustains != 0;
            var key = (instrument, bot, midi, sustains);
            if (_warmed.Contains(key)) return false;

            var sync = new SyncTrack(480);
            sync.Tempos.Add(new TempoChange(120, 0, 0));
            if (instrument == Instrument.EliteDrums)
            {
                var note = new EliteDrumNote(EliteDrumPad.HiHat, DrumNoteType.Neutral,
                    sustains ? EliteDrumsHatState.Open : EliteDrumsHatState.Closed,
                    EliteDrumsHatPedalType.Stomp, false,
                    DrumNoteFlags.None, NoteFlags.None, EliteDrumsChannelFlag.None, HIT_TIME, HIT_TICK, false);
                var chart = new InstrumentDifficulty<EliteDrumNote>(instrument, Difficulty.Expert,
                    new() { note }, new(), new());
                var engine = new EliteDrumsEngine(chart, sync, parameters, bot, true);
                // Register on a private manager so the first-hit fail-meter callback is
                // compiled without dispatching any live player, audio or visual events.
                var manager = new EngineManager();
                manager.Register(engine, chart, new SongChart(sync.Resolution) { SyncTrack = sync },
                    RockMeterPreset.Normal);
                manager.InitializeHappiness(noFail: true);
                Hit(engine, bot, GameInput.Create(HIT_TIME,
                    sustains ? EliteDrumsAction.EliteOpenHiHat : EliteDrumsAction.EliteClosedHiHat, 0.8f));
                if (!note.WasHit || (sustains && engine.ActiveHiHatSustain == null)) return false;
            }
            else
            {
                // Red is shared by all classic modes. Pro bots use a cymbal to warm the
                // same scoring path as the first strike in the performance fixture.
                bool cymbal = bot && instrument == Instrument.ProDrums;
                int pad = cymbal ? (int) FourLaneDrumPad.YellowCymbal : (int) FourLaneDrumPad.RedDrum;
                var note = new DrumNote(pad, DrumNoteType.Neutral, DrumNoteFlags.None,
                    NoteFlags.None, HIT_TIME, HIT_TICK);
                var chart = new InstrumentDifficulty<DrumNote>(instrument, Difficulty.Expert,
                    new() { note }, new(), new());
                var engine = new YargDrumsEngine(chart, sync, parameters, bot, midi);
                var input = midi ? GameInput.Create(HIT_TIME, instrument == Instrument.FiveLaneDrums
                    ? EliteDrumsAction.FiveLaneRedDrum : EliteDrumsAction.FourLaneRedDrum, 0.8f)
                    : GameInput.Create(HIT_TIME, DrumsAction.RedDrum, 0.8f);
                Hit(engine, bot, input);
                if (!note.WasHit) return false;
            }

            _warmed.Add(key);
            return true;
        }

        private static void Hit(BaseEngine engine, bool bot, GameInput input)
        {
            engine.Update(-1);
            if (!bot) engine.QueueInput(ref input);
            engine.Update(HIT_TIME + 0.1);
        }
    }
}
