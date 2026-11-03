using DG.Tweening;
using System;
using UnityEngine;
using UnityEngine.UIElements;
using YARG.Core.Chart;
using YARG.Gameplay.Player;
using YARG.Helpers.Extensions;
using static YARG.Core.Game.ColorProfile;
using Color = System.Drawing.Color;

namespace YARG.Gameplay.Visuals
{
    public sealed class FourLaneDrumsNoteElement : DrumsNoteElement
    {
        // pattern: Imperative Shell
        protected override void InitializeElement()
        {
            base.InitializeElement();

            int groupIndex;
            bool isCymbal = false;

            if (NoteRef.Pad != 0 && NoteRef.Pad != (int) FourLaneDrumPad.Wildcard)
            {
                // Deal with non-kick/wildcard notes
                var position = Player.GetHighwayOrderingInfo(NoteRef.Pad).Position;

                isCymbal = NoteRef.Pad >= (int) FourLaneDrumPad.YellowCymbal;

                // Set the position
                transform.localPosition = new Vector3(GetElementX(position, Player.LaneCount), 0f, 0f);

                // Get which note model to use
                groupIndex = GetNoteGroup(isCymbal);
            }
            else if (NoteRef.Pad == 0 && Player.NumberOfDedicatedKickLanes > 0)
            {
                // Deal with dedicated-lane kick notes
                int highwayIndex;
                if (NoteRef.IsDoubleKick && Player.NumberOfDedicatedKickLanes == 2)
                {
                    highwayIndex = DrumsPlayer.DOUBLE_KICK_FRET_INDEX;
                }
                else
                {
                    highwayIndex = (int)FourLaneDrumPad.Kick;
                }

                // Set the position
                var position = Player.GetHighwayOrderingInfo(highwayIndex).Position;
                transform.localPosition = new Vector3(GetElementX(position, Player.LaneCount), 0f, 0f);

                groupIndex = GetKickGroup(NoteRef, true);
            }
            else
            {
                // Deal with wildcard and regular kick notes
                groupIndex = NoteRef.Pad == 0 ? GetKickGroup(NoteRef, false) : (int)NoteType.Wildcard;
                transform.localPosition = Vector3.zero;
            }

            // Show and set material properties
            InitializeNoteGroup(groupIndex, isCymbal);

            // Set note color
            UpdateColor();
        }

        protected override void UpdateElement()
        {
            // Potentially update flash in case of activation note
            UpdateColor();
        }

        protected override void UpdateColor()
        {
            var colors = Player.Player.ColorProfile.FourLaneDrums;

            // Get pad index
            int colorIndex;

            if (NoteRef.IsDoubleKick && Player.NumberOfDedicatedKickLanes is 2)
            {
                colorIndex = (int)FourLaneDrumsFret.DoubleKick;
            }
            else
            {
                colorIndex = Player.GetHighwayOrderingInfo(NoteRef.Pad).ColorIndex;
            }

            var colorNoStarPower = colors.GetNoteColor(colorIndex);
            Color DisplayColor(int index)
            {
                if (NoteRef.WasMissed) return colors.Miss;
                if (NoteRef.IsStarPowerActivator && Player.Engine.CanStarPowerActivate && !Player.Engine.BaseStats.IsStarPowerActive)
                {
                    float pulse = (float)GameManager.BeatEventHandler.Visual.StrongBeat.CurrentPercentage;
                    var fullColor = colors.GetActivationNoteColor(index);
                    return Color.FromArgb(fullColor.A, GetColorFromPulse(fullColor.R, pulse),
                        GetColorFromPulse(fullColor.G, pulse), GetColorFromPulse(fullColor.B, pulse));
                }
                return IsStarPowerVisible ? colors.GetNoteStarPowerColor(index) : colors.GetNoteColor(index);
            }
            var color = DisplayColor(colorIndex);

            // Set the note color if not hidden
            if (!NoteRef.WasHit)
            {
                UnityEngine.Color? secondary = NoteRef.IsKickFlam
                    ? DisplayColor((int)FourLaneDrumsFret.DoubleKick).ToUnityColor() : null;
                SetNoteColors(color.ToUnityColor(), colorNoStarPower.ToUnityColor(),
                    colors.GetMetalColor(IsStarPowerVisible).ToUnityColor(), secondary);
            }
        }
    }
}