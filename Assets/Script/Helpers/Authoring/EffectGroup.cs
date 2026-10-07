using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace YARG.Helpers.Authoring
{
    // WARNING: Changing this could break themes or venues!
    //
    // This script is used a lot in theme creation.
    // Changing the serialized fields in this file will result in older themes
    // not working properly. Only change if you need to.

    public class EffectGroup : MonoBehaviour
    {
        public List<EffectParticle> EffectParticles { get; private set; }
        public List<EffectLight>    EffectLights    { get; private set; }

        private void Awake()
        {
            EffectParticles = GetComponentsInChildren<EffectParticle>().ToList();
            EffectLights = GetComponentsInChildren<EffectLight>().ToList();
        }

        private void EnsureInitialized()
        {
            // Prefabs held inactive (e.g. settings preview sources) never ran Awake.
            EffectParticles ??= GetComponentsInChildren<EffectParticle>(true).ToList();
            EffectLights ??= GetComponentsInChildren<EffectLight>(true).ToList();
        }

        public void SetColor(Color c)
        {
            EnsureInitialized();
            foreach (var particles in EffectParticles)
                particles.InitializeColor(c);
            foreach (var lights in EffectLights)
                lights.InitializeColor(c);
        }

        public void Play()
        {
            foreach (var particles in EffectParticles)
                particles.Play();
            foreach (var lights in EffectLights)
                lights.Play();
        }

        public void Stop()
        {
            foreach (var particles in EffectParticles)
                particles.Stop();
            foreach (var lights in EffectLights)
                lights.Stop();
        }

        public void SetBreMode(bool breMode)
        {
            foreach (var particles in EffectParticles)
            {
                particles.SetBreMode(breMode);
            }
        }
    }
}