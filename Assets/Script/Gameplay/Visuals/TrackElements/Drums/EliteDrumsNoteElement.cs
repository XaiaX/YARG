using System.Collections.Generic;
using UnityEngine;
using YARG.Core.Chart;
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
            // Pedal events share the yellow lane's geometry but keep their own input identity.
            // V1 deliberately distinguishes stomp (ghost drum) from splash (accent drum).
            int group = foot ? KICK : NoteRef.Pad == (int) EliteDrumNote.EliteDrumPad.HatPedal
                ? NoteRef.IsSplash ? ACCENT : GHOST
                : NoteRef.IsAccent ? (EliteDrumsPlayer.IsCymbal(NoteRef.Pad) ? CYMBAL_ACCENT : ACCENT) :
                NoteRef.IsGhost ? (EliteDrumsPlayer.IsCymbal(NoteRef.Pad) ? CYMBAL_GHOST : GHOST) :
                EliteDrumsPlayer.IsCymbal(NoteRef.Pad) && Player.Player.Profile.UseCymbalModels ? CYMBAL : NORMAL;
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

        private void UpdateColor()
        {
            if (NoteGroup == null || NoteRef.WasHit) return;
            var colors = Player.Player.ColorProfile.FiveLaneDrums;
            int colorIndex = EliteDrumsPlayer.GetColorIndex(NoteRef.Pad);
            var proColors = Player.Player.ColorProfile.FourLaneDrums;
            bool kick = EliteDrumsPlayer.IsFootPad(NoteRef.Pad);
            bool pedal = NoteRef.Pad == (int) EliteDrumNote.EliteDrumPad.HatPedal;
            int proColorIndex = NoteRef.IsDoubleKick || pedal
                ? (int) YARG.Core.Game.ColorProfile.FourLaneDrumsFret.DoubleKick
                : (int) YARG.Core.Game.ColorProfile.FourLaneDrumsFret.Kick;
            // Kick uses the Pro drum 1x/2x colors (orange/purple); pedal placeholders
            // use purple against the yellow hand hi-hat, without changing their lane.
            var original = kick || pedal ? proColors.GetNoteColor(proColorIndex) : colors.GetNoteColor(colorIndex);
            var starPowerColor = kick || pedal ? proColors.GetNoteStarPowerColor(proColorIndex) :
                colors.GetNoteStarPowerColor(colorIndex);
            var color = NoteRef.WasMissed ? colors.Miss : IsStarPowerVisible ? starPowerColor : original;
            NoteGroup.SetColorWithEmission(color.ToUnityColor(), original.ToUnityColor());
            NoteGroup.SetMetalColor(colors.GetMetalColor(IsStarPowerVisible).ToUnityColor());
        }
    }
}
