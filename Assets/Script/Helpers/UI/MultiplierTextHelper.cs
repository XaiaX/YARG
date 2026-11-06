// pattern: Imperative Shell
using Cysharp.Text;
using TMPro;

namespace YARG.Helpers.UI
{
    public static class MultiplierTextHelper
    {
        /// <summary>
        /// Create a TMP cache of all available multiplier texts.
        /// </summary>
        /// <param name="maxMultiplier">The max multiplier to generate WITHOUT Star Power.</param>
        /// <param name="multiplierTextPrefab">The prefab of the text to instantiate.</param>
        /// <param name="isMultiplayer"> Whether we are playing multiplayer (and therefore do not need to generate SP multipliers).</param>
        /// <typeparam name="T">TextMeshPro type being used</typeparam>
        /// <returns>Array where the corresponding TMP object is at arr[multiplier - 2]</returns>
        public static T[] CreateMultiplierTextCache<T>(int maxMultiplier, T multiplierTextPrefab,
            bool isMultiplayer) where T : TMP_Text
        {
            var textCache = isMultiplayer ? new T[maxMultiplier - 1] : new T[2 * maxMultiplier - 1];
            for (int i = 2; i <= maxMultiplier; i++)
            {
                if (textCache[i - 2] == null)
                {
                    textCache[i - 2] = GenerateMultiplierText(i, multiplierTextPrefab);
                }

                // Also SP, but only in single-player as multiplayer uses band multipliers
                if (!isMultiplayer && textCache[i * 2 - 2] == null)
                {
                    textCache[i * 2 - 2] = GenerateMultiplierText(i * 2, multiplierTextPrefab);
                }
            }

            return textCache;
        }

        private static T GenerateMultiplierText<T>(int multiplier, T multiplierTextPrefab) where T : TMP_Text
        {
            var text = UnityEngine.Object.Instantiate(multiplierTextPrefab, multiplierTextPrefab.transform.parent);
            text.SetTextFormat("{0}<sub>x</sub>", multiplier);
            // The template is disabled. Prepare dynamic glyphs and geometry now,
            // while loading, instead of when the combo first reaches this value.
            // Enabling first ensures OnDisable detaches the prepared render mesh;
            // forcing an already-disabled TMP can otherwise leave it visible.
            text.enabled = true;
            text.ForceMeshUpdate(ignoreActiveState: true);
            text.enabled = false;
            return text;
        }
    }
}
