#nullable enable

using System;
using UnityEngine;
using YARG.Core.Audio;
using YARG.Core.Chart;
using YARG.Core.Engine.Vocals.Engines;
using YARG.Helpers;
using YARG.Input;

namespace YARG.Gameplay.Player
{
    /// <summary>
    /// One mic's worth of Party Vocals state: its single-mic sub-engine, its input
    /// context, and its needle + particle group visuals. Owned by PartyVocalsPlayer.
    /// </summary>
    public sealed class PartyVocalsMicSlot
    {
        public readonly int Index;
        public readonly MicDevice Device;
        public readonly MicInputContext InputContext;
        public readonly YargFreeVocalsEngine Engine;

        // Visuals
        public readonly GameObject NeedleVisualContainer;
        public readonly Transform NeedleTransform;
        public readonly MeshRenderer NeedleRenderer;
        public readonly Material NeedleMaterial;
        public readonly ParticleGroup HittingParticleGroup;

        // Per-slot state mirrors what base VocalsPlayer holds for single-mic.
        public VocalNote LastTargetNote;
        public double? LastHitTime;
        public double? LastSingTime;

        // Subscriptions (held for cleanup).
        public Action<VocalNote>? OnTargetNoteHandler;
        public Action<bool>? OnHitHandler;
        public Action<bool>? OnSingHandler;

        public PartyVocalsMicSlot(int index, MicDevice device, MicInputContext inputContext,
            YargFreeVocalsEngine engine, GameObject needleContainer, Transform needleTransform,
            MeshRenderer needleRenderer, Material needleMaterial, ParticleGroup particleGroup)
        {
            Index = index;
            Device = device;
            InputContext = inputContext;
            Engine = engine;
            NeedleVisualContainer = needleContainer;
            NeedleTransform = needleTransform;
            NeedleRenderer = needleRenderer;
            NeedleMaterial = needleMaterial;
            HittingParticleGroup = particleGroup;
        }
    }
}
