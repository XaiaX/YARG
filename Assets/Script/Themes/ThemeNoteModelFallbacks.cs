using System.Collections.Generic;
using UnityEngine;

// pattern: Functional Core

namespace YARG.Themes
{
    public static class ThemeNoteModelFallbacks
    {
        public static (Dictionary<ThemeNoteType, GameObject> Regular,
            Dictionary<ThemeNoteType, GameObject> StarPower) ResolveEliteModels(
            IReadOnlyDictionary<ThemeNoteType, GameObject> models,
            IReadOnlyDictionary<ThemeNoteType, GameObject> starPowerModels)
        {
            var (regular, starPower) = ResolveHiHatModels(models, starPowerModels);
            if (!regular.TryGetValue(ThemeNoteType.Wildcard, out var wildcard))
            {
                if (!regular.TryGetValue(ThemeNoteType.Kick, out wildcard)) return (regular, starPower);
                regular.Add(ThemeNoteType.Wildcard, wildcard);
                if (!starPower.ContainsKey(ThemeNoteType.Wildcard) &&
                    starPower.TryGetValue(ThemeNoteType.Kick, out var starKick))
                    starPower.Add(ThemeNoteType.Wildcard, starKick);
            }
            // An authored regular wildcard without SP keeps its shape, rather
            // than inheriting the default theme's separate SP silhouette.
            if (!starPower.ContainsKey(ThemeNoteType.Wildcard))
                starPower.Add(ThemeNoteType.Wildcard, wildcard);
            return (regular, starPower);
        }

        public static (Dictionary<ThemeNoteType, GameObject> Regular,
            Dictionary<ThemeNoteType, GameObject> StarPower) ResolveHiHatModels(
            IReadOnlyDictionary<ThemeNoteType, GameObject> models,
            IReadOnlyDictionary<ThemeNoteType, GameObject> starPowerModels)
        {
            var regular = new Dictionary<ThemeNoteType, GameObject>(models);
            var starPower = starPowerModels == null
                ? new Dictionary<ThemeNoteType, GameObject>()
                : new Dictionary<ThemeNoteType, GameObject>(starPowerModels);

            foreach (var (hat, cymbal) in new[]
            {
                (ThemeNoteType.OpenHiHat, ThemeNoteType.Cymbal),
                (ThemeNoteType.OpenHiHatAccent, ThemeNoteType.CymbalAccent),
                (ThemeNoteType.OpenHiHatGhost, ThemeNoteType.CymbalGhost),
                (ThemeNoteType.ClosedHiHat, ThemeNoteType.Cymbal),
                (ThemeNoteType.ClosedHiHatAccent, ThemeNoteType.CymbalAccent),
                (ThemeNoteType.ClosedHiHatGhost, ThemeNoteType.CymbalGhost)
            })
            {
                // A theme without the optional slot keeps its own ordinary cymbal.
                // An authored regular hat model keeps its shape during Star Power even
                // if it has no separate SP model; NoteGroup supplies the recoloring.
                if (regular.ContainsKey(hat)) continue;
                if (regular.TryGetValue(cymbal, out var model)) regular.Add(hat, model);
                if (!starPower.ContainsKey(hat) && starPower.TryGetValue(cymbal, out var starModel))
                    starPower.Add(hat, starModel);
            }

            return (regular, starPower);
        }
    }
}