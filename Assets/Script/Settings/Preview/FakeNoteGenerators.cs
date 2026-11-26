using System;
using System.Collections.Generic;
using YARG.Core;
using YARG.Core.Game;
using YARG.Core.Chart;
using YARG.Core.Engine.Keys;
using YARG.Themes;
using EliteRole = YARG.Core.Game.EliteDrumsColorRole;

// pattern: Functional Core

namespace YARG.Settings.Preview
{
    internal enum EliteGlowPreview { None, OpenHats, CymbalFlams, Pedals }

    public interface IFakeNoteRandom
    {
        int Range(int minInclusive, int maxExclusive);
    }

    public readonly struct FakeNoteSpotlight
    {
        public readonly int Fret;
        public readonly bool CenterNote;
        public readonly bool Cymbal;
        public readonly bool LeftyFlip;

        public FakeNoteSpotlight(int fret, bool centerNote, bool cymbal, bool leftyFlip)
        {
            Fret = fret;
            CenterNote = centerNote;
            Cymbal = cymbal;
            LeftyFlip = leftyFlip;
        }
    }

    public interface IFakeNoteGenerator
    {
        FakeNoteData CreateNote(double time, IFakeNoteRandom random);

        FakeNoteData CreateSpotlightNote(double time, FakeNoteSpotlight spotlight,
            IFakeNoteRandom random);

        FakeNoteData CreateTypeSpotlightNote(double time, ThemeNoteType noteType,
            IFakeNoteRandom random);

        IEnumerable<FakeNoteData> CreateChordNotes(double time, FakeNoteData baseNote,
            IFakeNoteRandom random);
    }

    /// <summary>Finite deterministic coverage of Elite roles and dynamics; no chart or device input.</summary>
    public sealed class EliteDrumsFakeNoteGenerator : IFakeNoteGenerator
    {
        private int _index;
        private readonly EliteDrumsLayoutKickMode _kickMode;
        internal EliteGlowPreview GlowPreview { get; set; }
        private static readonly EliteDrumsColorRole[] CymbalRoles =
        {
            EliteDrumsColorRole.HatIndifferent, EliteDrumsColorRole.HatClosed, EliteDrumsColorRole.HatOpen,
            EliteDrumsColorRole.LeftCrash, EliteDrumsColorRole.Ride, EliteDrumsColorRole.RightCrash
        };
        private static readonly ThemeNoteType[] GlowDynamics = { ThemeNoteType.Normal, ThemeNoteType.Ghost, ThemeNoteType.Accent };
        private static readonly ThemeNoteType[] Dynamics = { ThemeNoteType.Ghost, ThemeNoteType.Normal, ThemeNoteType.Accent };
        private static readonly EliteRole[] DrumRoles = { EliteRole.Snare, EliteRole.Tom1, EliteRole.Tom2, EliteRole.Tom3 };
        private static readonly EliteRole[] HatRoles = { EliteRole.HatClosed, EliteRole.HatIndifferent, EliteRole.HatOpen };
        private static readonly EliteRole[] CrashAndRideRoles = { EliteRole.LeftCrash, EliteRole.Ride, EliteRole.RightCrash };
        private static readonly Sample[][] Beats = BuildSequence();
        public static int CycleLength => Beats.Length;
        public bool HasNext => true;

        public EliteDrumsFakeNoteGenerator() : this(EliteDrumsLayoutKickMode.Bars)
        {
        }

        public EliteDrumsFakeNoteGenerator(EliteDrumsLayoutKickMode kickMode = EliteDrumsLayoutKickMode.Bars)
        {
            _kickMode = kickMode;
        }

        public static ElitePreviewDescriptor Describe(EliteDrumsColorRole role,
            ThemeNoteType dynamics = ThemeNoteType.Normal) =>
            DescribeForKickLayout(role, dynamics, EliteDrumsLayoutKickMode.Bars);

        public static ElitePreviewDescriptor DescribeForKickLayout(EliteDrumsColorRole role,
            ThemeNoteType dynamics, EliteDrumsLayoutKickMode kickMode)
        {
            int fret = role switch
            {
                EliteDrumsColorRole.Snare or EliteDrumsColorRole.HandFlam => 1,
                EliteDrumsColorRole.HatIndifferent or EliteDrumsColorRole.HatOpen
                    or EliteDrumsColorRole.HatClosed => 2,
                EliteDrumsColorRole.LeftCrash or EliteDrumsColorRole.Tom1 => 3,
                EliteDrumsColorRole.Ride or EliteDrumsColorRole.Tom2 => 4,
                EliteDrumsColorRole.RightCrash or EliteDrumsColorRole.Tom3 => 5,
                _ => 0
            };
            bool bar = fret == 0;
            bool cymbal = role is EliteDrumsColorRole.HatIndifferent or EliteDrumsColorRole.HatOpen
                or EliteDrumsColorRole.HatClosed or EliteDrumsColorRole.LeftCrash
                or EliteDrumsColorRole.Ride or EliteDrumsColorRole.RightCrash;
            var type = bar ? ThemeNoteType.Kick : cymbal ? dynamics switch
            {
                ThemeNoteType.Accent => ThemeNoteType.CymbalAccent,
                ThemeNoteType.Ghost => ThemeNoteType.CymbalGhost,
                _ => ThemeNoteType.Cymbal
            } : dynamics;
            string dedicatedLaneSource = null;
            if (role is EliteDrumsColorRole.Kick or EliteDrumsColorRole.DoubleKick or EliteDrumsColorRole.KickFlam)
            {
                bool dedicated = kickMode != EliteDrumsLayoutKickMode.Bars;
                type = EliteKickAppearance.Select(dynamics == ThemeNoteType.Accent, dynamics == ThemeNoteType.Ghost,
                    role == EliteDrumsColorRole.KickFlam, dedicated);
                if (dedicated)
                {
                    bar = false;
                    dedicatedLaneSource = kickMode == EliteDrumsLayoutKickMode.Combined ? "kick" :
                        role == EliteDrumsColorRole.DoubleKick ? "kick2" : "kick1";
                    fret = dedicatedLaneSource == "kick1" ? 8 : 9;
                }
            }
            if (role == EliteDrumsColorRole.HatOpen)
            {
                type = dynamics switch
                {
                    ThemeNoteType.Accent => ThemeNoteType.OpenHiHatAccent,
                    ThemeNoteType.Ghost => ThemeNoteType.OpenHiHatGhost,
                    _ => ThemeNoteType.OpenHiHat
                };
            }
            if (role == EliteDrumsColorRole.HatClosed)
            {
                type = dynamics switch
                {
                    ThemeNoteType.Accent => ThemeNoteType.ClosedHiHatAccent,
                    ThemeNoteType.Ghost => ThemeNoteType.ClosedHiHatGhost,
                    _ => ThemeNoteType.ClosedHiHat
                };
            }
            if (role == EliteDrumsColorRole.Stomp) type = ThemeNoteType.HatStomp;
            if (role == EliteDrumsColorRole.Splash) type = ThemeNoteType.HatSplash;
            if (role == EliteDrumsColorRole.Wildcard) type = ThemeNoteType.Wildcard;
            float width = role is EliteDrumsColorRole.Stomp or EliteDrumsColorRole.Splash ? 0.5f : 1f;
            float offset = role is EliteDrumsColorRole.Stomp
                or EliteDrumsColorRole.Splash ? -0.5f : 0f;
            if (dedicatedLaneSource != null)
            {
                width = 1f;
                offset = 0f;
            }
            return new ElitePreviewDescriptor(role, fret, type, bar, width, offset, dedicatedLaneSource);
        }

        public static FakeNoteData CreateRoleNote(double time, ElitePreviewDescriptor descriptor) => new()
        {
            Time = time, Fret = descriptor.Fret, CenterNote = descriptor.IsBar,
            NoteType = descriptor.ModelType, EliteDescriptor = descriptor
        };

        private readonly struct Sample
        {
            public readonly EliteRole Role;
            public readonly ThemeNoteType Strength;
            public readonly bool HandFlam;
            public readonly bool FlatFlam;

            public Sample(EliteRole role, ThemeNoteType strength = ThemeNoteType.Normal,
                bool handFlam = false, bool flatFlam = false)
            {
                Role = role;
                Strength = strength;
                HandFlam = handFlam;
                FlatFlam = flatFlam;
            }

            public FakeNoteData At(double time, EliteDrumsLayoutKickMode kickMode)
            {
                var note = CreateRoleNote(time, DescribeForKickLayout(Role, Strength, kickMode));
                note.EliteHandFlam = HandFlam;
                note.EliteFlatFlam = FlatFlam;
                return note;
            }
        }

        private static readonly EliteRole[] ComparisonToms = { EliteRole.Tom1, EliteRole.Tom2, EliteRole.Tom3 };
        private static readonly EliteRole[] ComparisonCymbals =
            { EliteRole.HatIndifferent, EliteRole.LeftCrash, EliteRole.Ride, EliteRole.RightCrash };
        internal static int ComparisonBeats(EliteRole role) => ComparisonFamily(role).Count switch
        {
            3 => 8,
            4 => 9,
            _ => 0
        };

        internal static EliteRole ComparisonRole(EliteRole member, int beat) =>
            member == EliteRole.HatIndifferent ? HatRoles[beat % HatRoles.Length] : member;

        internal static IReadOnlyList<EliteRole> ComparisonFamily(EliteRole role) => role switch
        {
            EliteRole.Tom1 or EliteRole.Tom2 or EliteRole.Tom3 => ComparisonToms,
            EliteRole.HatClosed or EliteRole.HatIndifferent or EliteRole.HatOpen or EliteRole.LeftCrash or
                EliteRole.Ride or EliteRole.RightCrash => ComparisonCymbals,
            _ => Array.Empty<EliteRole>()
        };

        internal static int ColorPassLength(EliteRole role) =>
            ComparisonBeats(role) + 2 * VariationCount(role);

        internal static int VariationCount(EliteRole role) => role switch
        {
            EliteRole.Stomp or EliteRole.Splash or EliteRole.Wildcard => 1,
            EliteRole.Kick or EliteRole.DoubleKick or EliteRole.KickFlam => Dynamics.Length,
            _ => 3 * Dynamics.Length
        };

        private static Sample Variation(EliteRole role, int index)
        {
            if (VariationCount(role) == 1) return new Sample(role);
            int form = index / Dynamics.Length;
            return new Sample(role, Dynamics[index % Dynamics.Length], handFlam: form > 0, flatFlam: form == 2);
        }

        internal FakeNoteData CreateColorVariation(double time, EliteRole role, int index) =>
            Variation(role, index).At(time, _kickMode);

        private static Sample[][] BuildSequence()
        {
            var beats = new List<Sample[]>();
            void AddVariations(EliteRole role)
            {
                for (int i = 0; i < VariationCount(role); i++)
                    beats.Add(new[] { Variation(role, i) });
            }

            AddVariations(EliteRole.Snare);
            foreach (var hat in HatRoles) AddVariations(hat);
            AddVariations(EliteRole.Stomp);
            AddVariations(EliteRole.Splash);
            AddVariations(EliteRole.Kick);
            AddVariations(EliteRole.DoubleKick);
            AddVariations(EliteRole.KickFlam);
            foreach (var strength in Dynamics)
            {
                var kick = new Sample(EliteRole.Kick, strength);
                beats.Add(new[] { kick, new Sample(EliteRole.Stomp) });
                beats.Add(new[] { kick, new Sample(EliteRole.Splash) });
            }

            AddVariations(EliteRole.Tom1);
            AddVariations(EliteRole.Tom2);
            AddVariations(EliteRole.Tom3);
            foreach (var cymbal in CrashAndRideRoles) AddVariations(cymbal);
            AddVariations(EliteRole.Wildcard);

            int collisionBeat = 0, kickIndex = 0, pedalIndex = 0;
            void AddDrumPairs(EliteRole cymbal, bool includePedal)
            {
                foreach (var drum in DrumRoles)
                {
                    collisionBeat++;
                    var chord = new List<Sample> { new(cymbal), new(drum) };
                    if (collisionBeat % 2 == 0)
                        chord.Add(new Sample(EliteRole.Kick, Dynamics[kickIndex++ % Dynamics.Length]));
                    if (includePedal && collisionBeat % 3 == 0)
                        chord.Add(new Sample(pedalIndex++ % 2 == 0 ? EliteRole.Stomp : EliteRole.Splash));
                    beats.Add(chord.ToArray());
                }
            }

            foreach (var hat in HatRoles) AddDrumPairs(hat, false);
            foreach (var cymbal in CrashAndRideRoles) AddDrumPairs(cymbal, true);

            // These three chords also exercise the profile's cymbal merge/expansion rules.
            for (int i = 0; i < CrashAndRideRoles.Length; i++)
                for (int j = i + 1; j < CrashAndRideRoles.Length; j++)
                    beats.Add(new[] { new Sample(CrashAndRideRoles[i]), new Sample(CrashAndRideRoles[j]) });
            return beats.ToArray();
        }

        public FakeNoteData CreateNote(double time, IFakeNoteRandom random)
        {
            if (GlowPreview != EliteGlowPreview.None)
                return CreateGlowPreviewNote(time);

            var note = Beats[_index][0].At(time, _kickMode);
            note.ElitePreviewBeat = _index;
            _index = (_index + 1) % Beats.Length;
            return note;
        }

        private FakeNoteData CreateGlowPreviewNote(double time)
        {
            if (GlowPreview == EliteGlowPreview.Pedals)
            {
                var pedal = _index == 0 ? EliteDrumsColorRole.Stomp : EliteDrumsColorRole.Splash;
                _index = 1 - _index;
                return CreateRoleNote(time, DescribeForKickLayout(pedal, ThemeNoteType.Normal, _kickMode));
            }

            bool openOnly = GlowPreview == EliteGlowPreview.OpenHats;
            int forms = openOnly ? 3 : 2;
            int form = _index % forms;
            var role = openOnly ? EliteDrumsColorRole.HatOpen : CymbalRoles[_index / (forms * GlowDynamics.Length)];
            var strength = GlowDynamics[(_index / forms) % GlowDynamics.Length];
            var note = CreateRoleNote(time, Describe(role, strength));
            note.EliteHandFlam = !openOnly || form != 0;
            note.EliteFlatFlam = form == forms - 1;
            _index = (_index + 1) % (forms * GlowDynamics.Length * (openOnly ? 1 : CymbalRoles.Length));
            return note;
        }

        public FakeNoteData CreateSpotlightNote(double time, FakeNoteSpotlight spotlight,
            IFakeNoteRandom random)
        {
            var role = spotlight.CenterNote ? EliteDrumsColorRole.Kick :
                (EliteDrumsColorRole)Math.Clamp(spotlight.Fret - 1, 0, (int)EliteDrumsColorRole.Wildcard);
            if (role == EliteDrumsColorRole.HandFlam) role = EliteDrumsColorRole.Snare;
            return CreateRoleNote(time, Describe(role));
        }

        public FakeNoteData CreateTypeSpotlightNote(double time, ThemeNoteType noteType,
            IFakeNoteRandom random)
        {
            return CreateRoleNote(time, Describe(EliteDrumsColorRole.Snare, noteType));
        }

        public IEnumerable<FakeNoteData> CreateChordNotes(double time, FakeNoteData baseNote,
            IFakeNoteRandom random)
        {
            if (baseNote.ElitePreviewBeat < 0) yield break;
            var beat = Beats[baseNote.ElitePreviewBeat];
            for (int i = 1; i < beat.Length; i++)
                yield return beat[i].At(time, _kickMode);
        }

        /// <summary>Adapts synthetic cymbal notes through gameplay's shared playable projection.</summary>
        public static void ProjectCymbalNotes(IReadOnlyList<FakeNoteData> notes, EliteDrumsLayoutPreset layout)
        {
            if (layout?.Merges == null || layout.Merges.Length == 0) return;
            var difficulty = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Expert);
            var sources = new Dictionary<int, FakeNoteData>();
            EliteDrumNote parent = null;
            foreach (var data in notes)
            {
                if (data.EliteDescriptor is not { } descriptor) continue;
                var pad = descriptor.Role switch
                {
                    EliteDrumsColorRole.LeftCrash => EliteDrumNote.EliteDrumPad.LeftCrash,
                    EliteDrumsColorRole.Ride => EliteDrumNote.EliteDrumPad.Ride,
                    EliteDrumsColorRole.RightCrash => EliteDrumNote.EliteDrumPad.RightCrash,
                    _ => (EliteDrumNote.EliteDrumPad)(-1)
                };
                if ((int)pad < 0) continue;
                var note = new EliteDrumNote(pad, DrumNoteType.Neutral,
                    EliteDrumNote.EliteDrumsHatState.Indifferent, EliteDrumNote.EliteDrumsHatPedalType.Stomp,
                    false, DrumNoteFlags.None, NoteFlags.None, EliteDrumNote.EliteDrumsChannelFlag.None,
                    data.Time, 0, false);
                sources.Add((int)pad, data);
                if (parent == null) { parent = note; difficulty.Notes.Add(note); }
                else parent.AddChildNote(note);
            }
            if (parent == null) return;
            EliteDrumsCymbalProjection.Apply(difficulty, layout);
            foreach (var chord in difficulty.Notes)
            foreach (var playable in chord.AllNotes)
            {
                var data = sources[playable.OriginalPad];
                data.ElitePlayableNote = playable;
                var role = (EliteDrumNote.EliteDrumPad)playable.Pad switch
                {
                    EliteDrumNote.EliteDrumPad.HiHat => EliteDrumsColorRole.HatIndifferent,
                    EliteDrumNote.EliteDrumPad.LeftCrash => EliteDrumsColorRole.LeftCrash,
                    EliteDrumNote.EliteDrumPad.Ride => EliteDrumsColorRole.Ride,
                    _ => EliteDrumsColorRole.RightCrash
                };
                var strength = data.NoteType is ThemeNoteType.CymbalAccent ? ThemeNoteType.Accent
                    : data.NoteType is ThemeNoteType.CymbalGhost ? ThemeNoteType.Ghost : ThemeNoteType.Normal;
                var target = Describe(role, strength);
                data.EliteDescriptor = target;
                data.Fret = target.Fret;
                data.CenterNote = target.IsBar;
                data.NoteType = target.ModelType;
            }
        }
    }

    public sealed class FiveFretGuitarFakeNoteGenerator : IFakeNoteGenerator
    {
        public FakeNoteData CreateNote(double time, IFakeNoteRandom random)
        {
            int fret = random.Range(0, 6);
            if (fret == 0)
            {
                return CreateOpenNote(time, random.Range(0, 2) == 0 ? ThemeNoteType.Open : ThemeNoteType.OpenHOPO);
            }

            return CreateFretNote(time, fret, CreateGuitarNoteType(random));
        }

        public FakeNoteData CreateSpotlightNote(double time, FakeNoteSpotlight spotlight,
            IFakeNoteRandom random)
        {
            if (spotlight.CenterNote)
            {
                return new FakeNoteData
                {
                    Time = time,
                    Fret = (int) FiveFretGuitarFret.Open,
                    CenterNote = true,
                    NoteType = ThemeNoteType.Open
                };
            }

            int fret = spotlight.Fret;
            if (spotlight.LeftyFlip)
            {
                fret = 6 - fret;
            }

            return new FakeNoteData
            {
                Time = time,
                Fret = fret,
                CenterNote = false,
                NoteType = CreateGuitarNoteType(random)
            };
        }

        public FakeNoteData CreateTypeSpotlightNote(double time, ThemeNoteType noteType,
            IFakeNoteRandom random)
        {
            if (noteType is ThemeNoteType.Open or ThemeNoteType.OpenHOPO)
            {
                return CreateOpenNote(time, noteType);
            }

            return CreateFretNote(time, random.Range(1, 6), noteType);
        }

        public IEnumerable<FakeNoteData> CreateChordNotes(double time, FakeNoteData baseNote,
            IFakeNoteRandom random)
        {
            if (baseNote.CenterNote)
            {
                yield break;
            }

            int placed = 0;
            int denominator = 4;
            for (int fret = baseNote.Fret + 1; fret <= 5 && placed < 2; fret++)
            {
                // A three-note chord may not contain both green and orange.
                if (placed == 1 && fret == 5 && baseNote.Fret == 1)
                {
                    continue;
                }

                if (random.Range(0, denominator) != 0)
                {
                    continue;
                }

                placed++;
                denominator *= 2;
                yield return CopyOverrides(baseNote, new FakeNoteData
                {
                    Time = time,
                    Fret = fret,
                    CenterNote = false,
                    NoteType = baseNote.NoteType
                });
            }
        }

        private static ThemeNoteType CreateGuitarNoteType(IFakeNoteRandom random) => random.Range(0, 3) switch
        {
            0 => ThemeNoteType.Normal,
            1 => ThemeNoteType.HOPO,
            _ => ThemeNoteType.Tap
        };

        private static FakeNoteData CreateFretNote(double time, int fret, ThemeNoteType noteType) => new()
        {
            Time = time,
            Fret = fret,
            CenterNote = false,
            NoteType = noteType
        };

        private static FakeNoteData CreateOpenNote(double time, ThemeNoteType noteType) => new()
        {
            Time = time,
            Fret = (int) FiveFretGuitarFret.Open,
            CenterNote = true,
            NoteType = noteType
        };

        private static FakeNoteData CopyOverrides(FakeNoteData source, FakeNoteData note)
        {
            note.ForceMiss = source.ForceMiss;
            note.ForceStarPower = source.ForceStarPower;
            return note;
        }
    }

    public sealed class DrumFakeNoteGenerator : IFakeNoteGenerator
    {
        private readonly bool _fiveLane;

        public DrumFakeNoteGenerator(bool fiveLane)
        {
            _fiveLane = fiveLane;
        }

        public FakeNoteData CreateNote(double time, IFakeNoteRandom random)
        {
            int fret = random.Range(0, _fiveLane ? 6 : 5);
            return CreateNoteForFret(time, fret, random);
        }

        public FakeNoteData CreateSpotlightNote(double time, FakeNoteSpotlight spotlight,
            IFakeNoteRandom random)
        {
            if (spotlight.CenterNote)
            {
                return CreateKickNote(time, CreateKickNoteType(random));
            }

            return new FakeNoteData
            {
                Time = time,
                Fret = spotlight.Fret,
                CenterNote = false,
                NoteType = CreateDrumNoteType(spotlight.Cymbal, random)
            };
        }

        public FakeNoteData CreateTypeSpotlightNote(double time, ThemeNoteType noteType,
            IFakeNoteRandom random)
        {
            if (EliteKickAppearance.IsKick(noteType))
            {
                bool flam = noteType is ThemeNoteType.KickFlam or ThemeNoteType.KickFlamGhost or ThemeNoteType.KickFlamAccent
                    or ThemeNoteType.DedicatedLaneKickFlam or ThemeNoteType.DedicatedLaneKickFlamGhost or ThemeNoteType.DedicatedLaneKickFlamAccent;
                return CreateKickNote(time, EliteKickAppearance.Select(false, false, flam, EliteKickAppearance.IsDedicated(noteType)));
            }
            int fret = random.Range(1, _fiveLane ? 6 : 5);
            var note = CreateNoteForFret(time, fret, random);
            note.ClassicHandFlam = false;
            note.ClassicFlatFlam = false;
            note.NoteType = noteType switch
            {
                ThemeNoteType.Ghost when note.NoteType is ThemeNoteType.Cymbal
                    or ThemeNoteType.CymbalAccent or ThemeNoteType.CymbalGhost => ThemeNoteType.CymbalGhost,
                ThemeNoteType.Accent when note.NoteType is ThemeNoteType.Cymbal
                    or ThemeNoteType.CymbalAccent or ThemeNoteType.CymbalGhost => ThemeNoteType.CymbalAccent,
                _ => noteType
            };
            return note;
        }

        public IEnumerable<FakeNoteData> CreateChordNotes(double time, FakeNoteData baseNote,
            IFakeNoteRandom random)
        {
            if (baseNote.CenterNote)
            {
                // Sometimes add a pad/cymbal companion (same probability as
                // non-kick extras) so kick chord generation is independent.
                if (random.Range(0, 3) == 0)
                {
                    int fret = random.Range(1, _fiveLane ? 6 : 5);
                    yield return CopyOverrides(baseNote, CreateNoteForFret(time, fret, random));
                }
                yield break;
            }

            if (!baseNote.ClassicHandFlam && random.Range(0, 3) == 0)
            {
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    var extra = CreateNote(time, random);
                    if (!extra.CenterNote && !extra.ClassicHandFlam && extra.Fret != baseNote.Fret)
                    {
                        yield return CopyOverrides(baseNote, extra);
                        break;
                    }
                }
            }

            if (random.Range(0, 3) == 0)
            {
                yield return CopyOverrides(baseNote, CreateKickNote(time, CreateKickNoteType(random)));
            }
        }

        private FakeNoteData CreateNoteForFret(double time, int fret, IFakeNoteRandom random)
        {
            if (fret == 0)
            {
                return CreateKickNote(time, CreateKickNoteType(random));
            }

            bool cymbal = _fiveLane ? fret is 2 or 4 : fret != 1 && random.Range(0, 100) < 75;
            var note = new FakeNoteData
            {
                Time = time,
                Fret = fret,
                CenterNote = false,
                NoteType = CreateDrumNoteType(cymbal, random)
            };
            int articulation = random.Range(0, 100);
            note.ClassicHandFlam = articulation < 12;
            note.ClassicFlatFlam = articulation >= 8 && articulation < 12;
            return note;
        }

        private static FakeNoteData CreateKickNote(double time, ThemeNoteType type) => new()
        {
            Time = time, Fret = 0, CenterNote = true, NoteType = type
        };

        private static ThemeNoteType CreateKickNoteType(IFakeNoteRandom random)
        {
            return random.Range(0, 100) < 85 ? ThemeNoteType.Kick : ThemeNoteType.KickFlam;
        }

        private static ThemeNoteType CreateDrumNoteType(bool cymbal, IFakeNoteRandom random)
        {
            int variant = random.Range(0, 100);
            if (cymbal)
            {
                return variant < 75 ? ThemeNoteType.Cymbal
                    : variant < 90 ? ThemeNoteType.CymbalAccent
                    : ThemeNoteType.CymbalGhost;
            }

            return variant < 75 ? ThemeNoteType.Normal
                : variant < 90 ? ThemeNoteType.Accent
                : ThemeNoteType.Ghost;
        }

        private static FakeNoteData CopyOverrides(FakeNoteData source, FakeNoteData note)
        {
            note.ForceMiss = source.ForceMiss;
            note.ForceStarPower = source.ForceStarPower;
            return note;
        }
    }

    public sealed class FiveLaneKeysFakeNoteGenerator : IFakeNoteGenerator
    {
        public FakeNoteData CreateNote(double time, IFakeNoteRandom random) => new()
        {
            Time = time,
            Fret = random.Range(1, 6),
            CenterNote = false,
            NoteType = ThemeNoteType.Normal
        };

        public FakeNoteData CreateSpotlightNote(double time, FakeNoteSpotlight spotlight,
            IFakeNoteRandom random)
        {
            if (spotlight.CenterNote)
            {
                return new FakeNoteData
                {
                    Time = time,
                    Fret = (int) FiveFretGuitarFret.Open,
                    CenterNote = true,
                    NoteType = ThemeNoteType.Open
                };
            }

            return new FakeNoteData
            {
                Time = time,
                Fret = spotlight.Fret,
                CenterNote = false,
                NoteType = ThemeNoteType.Normal
            };
        }

        public FakeNoteData CreateTypeSpotlightNote(double time, ThemeNoteType noteType,
            IFakeNoteRandom random)
        {
            if (noteType is ThemeNoteType.Open or ThemeNoteType.OpenHOPO)
            {
                return new FakeNoteData
                {
                    Time = time,
                    Fret = (int) FiveFretGuitarFret.Open,
                    CenterNote = true,
                    NoteType = noteType
                };
            }

            return CreateNote(time, random);
        }

        public IEnumerable<FakeNoteData> CreateChordNotes(double time, FakeNoteData baseNote,
            IFakeNoteRandom random)
        {
            if (baseNote.CenterNote)
            {
                yield break;
            }

            int placed = 0;
            int denominator = 4;
            for (int fret = baseNote.Fret + 1; fret <= 5 && placed < 2; fret++)
            {
                if (random.Range(0, denominator) != 0)
                {
                    continue;
                }

                placed++;
                denominator *= 2;
                yield return CopyOverrides(baseNote, new FakeNoteData
                {
                    Time = time,
                    Fret = fret,
                    CenterNote = false,
                    NoteType = ThemeNoteType.Normal
                });
            }
        }

        private static FakeNoteData CopyOverrides(FakeNoteData source, FakeNoteData note)
        {
            note.ForceMiss = source.ForceMiss;
            note.ForceStarPower = source.ForceStarPower;
            return note;
        }
    }

    public sealed class SixFretGuitarFakeNoteGenerator : IFakeNoteGenerator
    {
        public FakeNoteData CreateNote(double time, IFakeNoteRandom random)
        {
            // 0 = Open, 1-6 = 6 frets, 7 = Wildcard
            int fret = random.Range(0, 8);

            // Open notes
            if (fret == 0)
            {
                return new FakeNoteData
                {
                    Time = time,
                    Fret = (int) SixFretGuitarFret.Open,
                    CenterNote = true,
                    NoteType = ThemeNoteType.Open
                };
            }

            // Wildcard
            if (fret == 7)
            {
                return new FakeNoteData
                {
                    Time = time,
                    Fret = (int) SixFretGuitarFret.Wildcard,
                    CenterNote = true,
                    NoteType = ThemeNoteType.Wildcard
                };
            }

            return new FakeNoteData
            {
                Time = time,
                Fret = fret,
                CenterNote = false,
                NoteType = CreateFretNoteType(fret, random)
            };
        }

        public FakeNoteData CreateSpotlightNote(double time, FakeNoteSpotlight spotlight,
            IFakeNoteRandom random)
        {
            if (spotlight.CenterNote)
            {
                return new FakeNoteData
                {
                    Time = time,
                    Fret = (int) SixFretGuitarFret.Open,
                    CenterNote = true,
                    NoteType = ThemeNoteType.Open
                };
            }

            return new FakeNoteData
            {
                Time = time,
                Fret = spotlight.Fret,
                CenterNote = false,
                NoteType = CreateFretNoteType(spotlight.Fret, random)
            };
        }

        public FakeNoteData CreateTypeSpotlightNote(double time, ThemeNoteType noteType,
            IFakeNoteRandom random)
        {
            switch (noteType)
            {
                case ThemeNoteType.Open:
                    return new FakeNoteData
                    {
                        Time = time,
                        Fret = (int) SixFretGuitarFret.Open,
                        CenterNote = true,
                        NoteType = noteType
                    };
                case ThemeNoteType.Wildcard:
                    return new FakeNoteData
                    {
                        Time = time,
                        Fret = (int) SixFretGuitarFret.Wildcard,
                        CenterNote = true,
                        NoteType = noteType
                    };
                case ThemeNoteType.SixFretUp or ThemeNoteType.SixFretUpHOPO
                    or ThemeNoteType.SixFretUpTap:
                    // Black frets (1-3) render as "up" notes
                    return new FakeNoteData
                    {
                        Time = time,
                        Fret = random.Range(1, 4),
                        CenterNote = false,
                        NoteType = noteType
                    };
                case ThemeNoteType.SixFretDown or ThemeNoteType.SixFretDownHOPO
                    or ThemeNoteType.SixFretDownTap:
                    // White frets (4-6) render as "down" notes
                    return new FakeNoteData
                    {
                        Time = time,
                        Fret = random.Range(4, 7),
                        CenterNote = false,
                        NoteType = noteType
                    };
                case ThemeNoteType.SixFretBarre or ThemeNoteType.SixFretBarreHOPO or ThemeNoteType.SixFretBarreTap:
                    return new FakeNoteData
                    {
                        Time = time,
                        Fret = random.Range(1, 7),
                        CenterNote = false,
                        NoteType = noteType
                    };
                default:
                    var note = CreateNote(time, random);
                    note.NoteType = noteType;
                    return note;
            }
        }

        public IEnumerable<FakeNoteData> CreateChordNotes(double time, FakeNoteData baseNote,
            IFakeNoteRandom random)
        {
            // Six-fret preview has no chord generation
            yield break;
        }

        /// <summary>
        /// Picks the note type for a fretted six-fret note: black frets (1-3) are
        /// "up" notes, white frets (4-6) are "down" notes, with a 30% chance of a
        /// barre. Up/down/barre notes randomly use the strum, HOPO, or Tap variant.
        /// </summary>
        private static ThemeNoteType CreateFretNoteType(int fret, IFakeNoteRandom random)
        {
            bool isUp = fret is >= 1 and <= 3; // Black1(1)-Black3(3)
            bool isBarre = random.Range(0, 100) < 30;

            // Map to theme note type
            ThemeNoteType themeType = isBarre ? ThemeNoteType.SixFretBarre
                : isUp ? ThemeNoteType.SixFretUp : ThemeNoteType.SixFretDown;

            // Override to HOPO/Tap variants randomly
            return random.Range(0, 3) switch
            {
                0 => themeType, // Strum
                1 => (isUp ? ThemeNoteType.SixFretUpHOPO :
                      isBarre ? ThemeNoteType.SixFretBarreHOPO :
                               ThemeNoteType.SixFretDownHOPO),
                _ => (isUp ? ThemeNoteType.SixFretUpTap :
                      isBarre ? ThemeNoteType.SixFretBarreTap :
                               ThemeNoteType.SixFretDownTap)
            };
        }
    }

    public sealed class ProKeysFakeNoteGenerator : IFakeNoteGenerator
    {
        public FakeNoteData CreateNote(double time, IFakeNoteRandom random)
        {
            int fret = random.Range(0, 17);
            return new FakeNoteData
            {
                Time = time,
                Fret = fret,
                CenterNote = false,
                NoteType = GetNoteType(fret)
            };
        }

        public FakeNoteData CreateSpotlightNote(double time, FakeNoteSpotlight spotlight,
            IFakeNoteRandom random)
        {
            return new FakeNoteData
            {
                Time = time,
                Fret = spotlight.Fret,
                CenterNote = false,
                NoteType = GetNoteType(spotlight.Fret)
            };
        }

        public FakeNoteData CreateTypeSpotlightNote(double time, ThemeNoteType noteType,
            IFakeNoteRandom random)
        {
            if (noteType is ThemeNoteType.White or ThemeNoteType.Black)
            {
                bool black = noteType == ThemeNoteType.Black;
                int colorIndex = random.Range(0,
                    black ? ProKeysUtilities.BLACK_KEY_COUNT : ProKeysUtilities.WHITE_KEY_COUNT);
                return new FakeNoteData
                {
                    Time = time,
                    Fret = ProKeysUtilities.GetKeyIndexForColor(black, colorIndex),
                    CenterNote = false,
                    NoteType = noteType
                };
            }

            var note = CreateNote(time, random);
            note.NoteType = noteType;
            return note;
        }

        public IEnumerable<FakeNoteData> CreateChordNotes(double time, FakeNoteData baseNote,
            IFakeNoteRandom random)
        {
            int noteCount = 1;
            int denominator = 4;
            int fret = baseNote.Fret + 2;
            int maxFret = baseNote.Fret + 8;
            if (maxFret > 16)
            {
                maxFret = 16;
            }

            while (noteCount < 4 && fret <= maxFret)
            {
                if (random.Range(0, denominator) == 0)
                {
                    yield return CopyOverrides(baseNote, new FakeNoteData
                    {
                        Time = time,
                        Fret = fret,
                        CenterNote = false,
                        NoteType = GetNoteType(fret)
                    });
                    noteCount++;
                    denominator *= 2;
                    fret += 2;
                }
                else
                {
                    fret++;
                }
            }
        }

        private static ThemeNoteType GetNoteType(int fret) =>
            ProKeysUtilities.IsBlackKey(fret % 12) ? ThemeNoteType.Black : ThemeNoteType.White;

        private static FakeNoteData CopyOverrides(FakeNoteData source, FakeNoteData note)
        {
            note.ForceMiss = source.ForceMiss;
            note.ForceStarPower = source.ForceStarPower;
            return note;
        }
    }
}
