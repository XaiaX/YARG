using System.Collections.Generic;
using UnityEngine;
using YARG.Core;
using YARG.Core.Chart;
using YARG.Core.Engine.Drums;
using YARG.Core.Game;
using YARG.Gameplay.Player;
using YARG.Settings;
using YARG.Themes;

namespace YARG.Gameplay.Visuals
{
    public abstract class DrumsNoteElement : NoteElement<DrumNote, DrumsPlayer>, IThemeNoteCreator
    {
        // pattern: Imperative Shell
        private Vector3? _scalingFactor = null;
        [SerializeField] private NoteGroup[] _flamCompanions;
        [SerializeField] private NoteGroup[] _starFlamCompanions;
        private readonly Dictionary<NoteGroup, EliteDrumsRuntimeGeometry.ModelGeometry> _geometry = new();
        private NoteGroup _flamCompanion;
        private int _selectedGroup;
        private bool _selectedCymbal;

        protected enum NoteType
        {
            Normal        = 0,
            Cymbal        = 1,
            Kick          = 2,
            Accent        = 3,
            Ghost         = 4,
            CymbalAccent  = 5,
            CymbalGhost   = 6,
            Wildcard      = 7,
            DedicatedLaneKick = 8,
            KickFlam = 9,
            DedicatedLaneKickFlam = 10,

            Count
        }

        public override void SetThemeModels(
            Dictionary<ThemeNoteType, GameObject> models,
            Dictionary<ThemeNoteType, GameObject> starpowerModels)
        {
            CreateNoteGroupArrays((int) NoteType.Count);

            AssignNoteGroup(models, starpowerModels, (int) NoteType.Normal,         ThemeNoteType.Normal);
            AssignNoteGroup(models, starpowerModels, (int) NoteType.Cymbal,         ThemeNoteType.Cymbal);
            AssignNoteGroup(models, starpowerModels, (int) NoteType.Kick,           ThemeNoteType.Kick);
            AssignNoteGroup(models, starpowerModels, (int) NoteType.Accent,         ThemeNoteType.Accent);
            AssignNoteGroup(models, starpowerModels, (int) NoteType.Ghost,          ThemeNoteType.Ghost);
            AssignNoteGroup(models, starpowerModels, (int) NoteType.CymbalAccent,   ThemeNoteType.CymbalAccent);
            AssignNoteGroup(models, starpowerModels, (int) NoteType.CymbalGhost,    ThemeNoteType.CymbalGhost);
            AssignNoteGroup(models, starpowerModels, (int) NoteType.Wildcard,       ThemeNoteType.Wildcard);
            AssignNoteGroup(models, starpowerModels, (int) NoteType.DedicatedLaneKick, ThemeNoteType.DedicatedLaneKick);
            AssignNoteGroup(models, starpowerModels, (int) NoteType.KickFlam, ThemeNoteType.KickFlam);
            AssignNoteGroup(models, starpowerModels, (int) NoteType.DedicatedLaneKickFlam, ThemeNoteType.DedicatedLaneKickFlam);
            _flamCompanions = new NoteGroup[(int)NoteType.Count];
            _starFlamCompanions = new NoteGroup[(int)NoteType.Count];
            foreach (var type in new[] { NoteType.Normal, NoteType.Cymbal, NoteType.Accent, NoteType.Ghost,
                NoteType.CymbalAccent, NoteType.CymbalGhost })
            {
                int index = (int)type;
                _flamCompanions[index] = Instantiate(NoteGroups[index], transform);
                _starFlamCompanions[index] = StarPowerNoteGroups[index] == NoteGroups[index]
                    ? _flamCompanions[index] : Instantiate(StarPowerNoteGroups[index], transform);
                _flamCompanions[index].SetActive(false);
                _starFlamCompanions[index].SetActive(false);
            }
        }

        public override void HitNote()
        {
            base.HitNote();

            ParentPool.Return(this);
        }

        protected override bool CalcStarPowerVisible()
        {
            if (!NoteRef.IsStarPower)
            {
                return false;
            }
            return !(((DrumsEngineParameters) Player.BaseParameters).NoStarPowerOverlap && Player.BaseStats.IsStarPowerActive);
        }

        protected override void HideElement()
        {
            HideNotes();
            HideFlamCompanions();
        }

        internal static int GetKickGroup(DrumNote note, bool dedicated) => (int)(dedicated
            ? note.IsKickFlam ? NoteType.DedicatedLaneKickFlam : NoteType.DedicatedLaneKick
            : note.IsKickFlam ? NoteType.KickFlam : NoteType.Kick);

        protected void InitializeNoteGroup(int index, bool cymbal)
        {
            _selectedGroup = index;
            _selectedCymbal = cymbal;
            NoteGroup = (IsStarPowerVisible ? StarPowerNoteGroups : NoteGroups)[index];
            NoteGroup.SetActive(true);
            NoteGroup.Initialize();
            ConfigureHandFlam();
            ConfigureKickFlam();
            ConfigureGhostStrip();
        }

        protected void SetNoteColors(Color color, Color regular, Color metal, Color? secondaryKickColor = null)
        {
            NoteGroup.SetColorWithEmission(color, regular);
            NoteGroup.SetMetalColor(metal);
            if (NoteRef.IsKickFlam && NoteGroup.HasKickFlamColors)
                NoteGroup.SetKickFlamColors(color, secondaryKickColor ?? color, LeftyFlip);
            if (_flamCompanion == null) return;
            _flamCompanion.SetColorWithEmission(color, regular);
            _flamCompanion.SetMetalColor(metal);
        }

        private EliteDrumsRuntimeGeometry.ModelGeometry Geometry(NoteGroup group)
        {
            if (!_geometry.TryGetValue(group, out var geometry))
            {
                geometry = new EliteDrumsRuntimeGeometry.ModelGeometry(group.transform);
                _geometry.Add(group, geometry);
            }
            return geometry;
        }

        private void HideFlamCompanions()
        {
            if (_flamCompanions != null)
                foreach (var group in _flamCompanions) if (group != null) group.SetActive(false);
            if (_starFlamCompanions != null)
                foreach (var group in _starFlamCompanions) if (group != null) group.SetActive(false);
            _flamCompanion = null;
        }

        private void ConfigureHandFlam()
        {
            HideFlamCompanions();
            foreach (var pair in _geometry) pair.Value.Reset(pair.Key.transform);
            if (!NoteRef.IsHandFlam || NoteRef.WasHit) return;
            // Pair fitting supplies the lane's X scaling. Keep the wrapper's
            // authored height/depth, without scaling the fitted X a second time.
            var scale = transform.localScale;
            transform.localScale = new Vector3(1f, scale.y, scale.z);
            _flamCompanion = (IsStarPowerVisible ? _starFlamCompanions : _flamCompanions)[_selectedGroup];
            float center = transform.localPosition.x;
            EliteDrumsRuntimeGeometry.FitPairWithAuthoredWidth(Geometry(NoteGroup), NoteGroup.transform,
                Geometry(_flamCompanion), _flamCompanion.transform, Player.LaneCount, center,
                TrackPlayer.TRACK_WIDTH / Player.LaneCount, center, !NoteRef.IsFlatFlam,
                Player.Player.Profile.FlamLayout, _selectedCymbal, LeftyFlip,
                TrackPlayer.TRACK_WIDTH / (Player.Player.Profile.CurrentInstrument == Instrument.FiveLaneDrums ? 5f : 4f));
            _flamCompanion.SetActive(true);
            _flamCompanion.Initialize();
        }

        private void ConfigureKickFlam()
        {
            if (!NoteRef.IsKickFlam || Player.NumberOfDedicatedKickLanes > 0) return;
            var split = NoteGroup.GetComponentInChildren<ClassicKickFlamVisual>(true);
            if (split == null) return; // Custom themes may retain their own stacked/fallback shape.
            int first = -1, second = -1;
            foreach (var member in NoteRef.ParentOrSelf.AllNotes)
            {
                if (member.Pad == 0 || member.Pad == (int)FourLaneDrumPad.Wildcard) continue;
                int column = Player.GetHighwayOrderingInfo(member.Pad).Position;
                if (first < 0) first = column;
                else { second = column; break; }
            }
            var endpoints = ClassicKickFlamLayout.GetInnerColumns(Player.LaneCount, first, second,
                Player.Player.Profile.CurrentInstrument == Instrument.FiveLaneDrums, LeftyFlip);
            float pitch = TrackPlayer.TRACK_WIDTH / Player.LaneCount;
            split.Configure(transform.parent, TrackPlayer.TRACK_WIDTH,
                endpoints.Left * pitch - TrackPlayer.TRACK_WIDTH * .5f,
                endpoints.Right * pitch - TrackPlayer.TRACK_WIDTH * .5f);
        }

        private void ConfigureGhostStrip()
        {
            if (NoteRef.Pad == 0 || !NoteRef.IsGhost) return;
            var colors = Player.Player.ColorProfile;
            float emission = (Player.Player.Profile.CurrentInstrument == Instrument.FiveLaneDrums
                ? colors.FiveLaneDrums.GhostStripEmission : colors.FourLaneDrums.GhostStripEmission) / 100f;
            if (emission < 0f) return;
            NoteGroup.OverrideZeroEmission(emission);
            if (_flamCompanion != null) _flamCompanion.OverrideZeroEmission(emission);
        }

        protected int GetNoteGroup(bool isCymbal)
        {
            if (NoteRef.IsAccent)
            {
                return (int) (isCymbal ? NoteType.CymbalAccent : NoteType.Accent);
            }

            if (NoteRef.IsGhost)
            {
                return (int) (isCymbal ? NoteType.CymbalGhost : NoteType.Ghost);
            }

            return (int) (isCymbal ? NoteType.Cymbal : NoteType.Normal);
        }

        protected override void InitializeElement()
        {
            HideFlamCompanions();
            foreach (var pair in _geometry) pair.Value.Reset(pair.Key.transform);
            base.InitializeElement();

            _scalingFactor ??= new Vector3(Player.NoteScaleFactor, Player.NoteScaleFactor, Player.NoteScaleFactor);

            if ((NoteRef.Pad != 0 || Player.NumberOfDedicatedKickLanes > 0) && NoteRef.Pad != (int)FourLaneDrumPad.Wildcard)
            {
                gameObject.transform.localScale = Vector3.Scale(transform.localScale, _scalingFactor.Value);
            }
        }

        protected abstract void UpdateColor();

        public override void OnStarPowerUpdated()
        {
            base.OnStarPowerUpdated();
            ConfigureHandFlam();
            ConfigureKickFlam();
            ConfigureGhostStrip();
            UpdateColor();
        }

        protected static int GetColorFromPulse(int color, float pulse)
        {
            float intensity = Mathf.Pow(pulse - 1, 3) + 1f;
            return (int) (intensity * color);
        }
    }
}
