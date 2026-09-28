using System.Collections.Generic;
using UnityEngine;
using YARG.Core.Chart;
using YARG.Core.Game;
using YARG.Gameplay.Player;
using YARG.Helpers.Extensions;
using YARG.Themes;

namespace YARG.Gameplay.Visuals
{
    /// <summary>Typed native Elite notes projected onto the existing five-lane drum models.</summary>
    public sealed class EliteDrumsNoteElement : NoteElement<EliteDrumNote, EliteDrumsPlayer>
    {
        private const int NORMAL = 0;
        private const int CYMBAL = 1;
        private const int KICK = 2;
        private const int ACCENT = 3;
        private const int GHOST = 4;
        private const int CYMBAL_ACCENT = 5;
        private const int CYMBAL_GHOST = 6;
        private const int COUNT = 7;

        public override void SetThemeModels(Dictionary<ThemeNoteType, GameObject> models,
            Dictionary<ThemeNoteType, GameObject> starpowerModels)
        {
            CreateNoteGroupArrays(COUNT);
            AssignNoteGroup(models, starpowerModels, NORMAL, ThemeNoteType.Normal);
            AssignNoteGroup(models, starpowerModels, CYMBAL, ThemeNoteType.Cymbal);
            AssignNoteGroup(models, starpowerModels, KICK, ThemeNoteType.Kick);
            AssignNoteGroup(models, starpowerModels, ACCENT, ThemeNoteType.Accent);
            AssignNoteGroup(models, starpowerModels, GHOST, ThemeNoteType.Ghost);
            AssignNoteGroup(models, starpowerModels, CYMBAL_ACCENT, ThemeNoteType.CymbalAccent);
            AssignNoteGroup(models, starpowerModels, CYMBAL_GHOST, ThemeNoteType.CymbalGhost);
        }

        protected override bool CalcStarPowerVisible() => NoteRef.IsStarPower &&
            !(((YARG.Core.Engine.Drums.DrumsEngineParameters) Player.BaseParameters).NoStarPowerOverlap &&
                Player.BaseStats.IsStarPowerActive);

        protected override void HideElement() => HideNotes();

        protected override void InitializeElement()
        {
            base.InitializeElement();
            bool foot = EliteDrumsPlayer.IsFootPad(NoteRef.Pad);
            int lane = Player.GetVisualPosition(NoteRef.Pad);
            transform.localPosition = foot ? Vector3.zero :
                new Vector3(GetElementX(lane, Player.LaneCount), 0, 0);
            var groups = IsStarPowerVisible ? StarPowerNoteGroups : NoteGroups;
            int group = GetGemGroup(NoteRef, Player.Player.Profile.UseCymbalModels);
            NoteGroup = groups[group];
            NoteGroup.SetActive(true);
            NoteGroup.Initialize();
            UpdateColor();
        }

        protected override void UpdateElement() => base.UpdateElement();

        public override void HitNote()
        {
            base.HitNote();
            ParentPool.Return(this);
        }

        public override void OnStarPowerUpdated()
        {
            base.OnStarPowerUpdated();
            UpdateColor();
        }

        internal static int GetGemGroup(EliteDrumNote note, bool useCymbalModels)
        {
            // The authored flam flag is a visual cue in native V1, not a second scored hit.
            if (note.IsFlam) return ACCENT;
            if (EliteDrumsPlayer.IsFootPad(note.Pad)) return KICK;
            // Pedal events share the hi-hat lane but retain their own ghost/accent shapes.
            if (note.Pad == (int) EliteDrumNote.EliteDrumPad.HatPedal)
                return note.IsSplash ? ACCENT : GHOST;
            bool cymbal = EliteDrumsPlayer.IsCymbal(note.Pad);
            if (note.IsAccent) return cymbal ? CYMBAL_ACCENT : ACCENT;
            if (note.IsGhost) return cymbal ? CYMBAL_GHOST : GHOST;
            return cymbal && useCymbalModels ? CYMBAL : NORMAL;
        }

        internal static (bool FourLane, int Index) GetGemColorSlot(EliteDrumNote note)
        {
            if (note.IsFlam)
                return (true, (int) ColorProfile.FourLaneDrumsFret.RedDrum);

            if (note.Pad == (int) EliteDrumNote.EliteDrumPad.Kick)
                return (false, note.IsDoubleKick ? (int) ColorProfile.FiveLaneDrumsFret.DoubleKick :
                    (int) ColorProfile.FiveLaneDrumsFret.Kick);

            // Pedal events stay on the hi-hat lane with ghost/accent models; only their gem color changes.
            if (note.Pad == (int) EliteDrumNote.EliteDrumPad.HatPedal)
                return (true, note.IsSplash ? (int) ColorProfile.FourLaneDrumsFret.DoubleKick :
                    (int) ColorProfile.FourLaneDrumsFret.Kick);

            if (note.Pad == (int) EliteDrumNote.EliteDrumPad.HiHat)
                return (true, note.HatState switch
                {
                    EliteDrumNote.EliteDrumsHatState.Open => (int) ColorProfile.FourLaneDrumsFret.BlueCymbal,
                    EliteDrumNote.EliteDrumsHatState.Closed => (int) ColorProfile.FourLaneDrumsFret.GreenCymbal,
                    _ => (int) ColorProfile.FourLaneDrumsFret.YellowCymbal,
                });

            return (EliteDrumNote.EliteDrumPad) note.Pad switch
            {
                EliteDrumNote.EliteDrumPad.Tom1 => (true, (int) ColorProfile.FourLaneDrumsFret.YellowDrum),
                EliteDrumNote.EliteDrumPad.Tom2 => (true, (int) ColorProfile.FourLaneDrumsFret.BlueDrum),
                EliteDrumNote.EliteDrumPad.Tom3 => (true, (int) ColorProfile.FourLaneDrumsFret.GreenDrum),
                _ => (false, EliteDrumsPlayer.GetColorIndex(note.Pad)),
            };
        }

        private void UpdateColor()
        {
            if (NoteGroup == null || NoteRef.WasHit) return;
            var fiveLane = Player.Player.ColorProfile.FiveLaneDrums;
            var fourLane = Player.Player.ColorProfile.FourLaneDrums;
            var (useFourLane, index) = GetGemColorSlot(NoteRef);
            var original = useFourLane ? fourLane.GetNoteColor(index) : fiveLane.GetNoteColor(index);
            var starPowerColor = useFourLane ? fourLane.GetNoteStarPowerColor(index) :
                fiveLane.GetNoteStarPowerColor(index);
            var color = NoteRef.WasMissed ? fiveLane.Miss : IsStarPowerVisible ? starPowerColor : original;
            NoteGroup.SetColorWithEmission(color.ToUnityColor(), original.ToUnityColor());
            NoteGroup.SetMetalColor(fiveLane.GetMetalColor(IsStarPowerVisible).ToUnityColor());
        }
    }
}
