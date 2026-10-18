using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace YARG.Editor
{
    /// <summary>Creates fonts used only by the Elite Drums layout transform buttons.</summary>
    public static class EliteDrumsTransformFontBuilder
    {
        public const string SOURCE_PATH = "Assets/Art/Fonts/NotoSans/NotoSansJP-Regular.ttf";
        public const string ARROW_PATH = "Assets/Art/Fonts/NotoSans/EliteDrumsTransform Arrows SDF.asset";
        public const string LABEL_PATH = "Assets/Art/Fonts/Barlow/EliteDrumsTransform Barlow SDF.asset";
        private static readonly uint[] ARROWS = { 0x2190, 0x2192, 0x21C4, 0x21C6 };

        /// <summary>
        /// Pass the existing button's Barlow font. Only the returned copy is changed; shared fonts
        /// and TMP settings are untouched. Generated asset paths and subassets are reused on rebuild.
        /// </summary>
        public static TMP_FontAsset EnsureTransformFont(TMP_FontAsset barlow)
        {
            if (barlow == null) throw new ArgumentNullException(nameof(barlow));
            if (AssetDatabase.GetAssetPath(barlow) == LABEL_PATH)
                throw new ArgumentException("Pass the original Barlow font, not the generated copy.", nameof(barlow));

            Font source = AssetDatabase.LoadAssetAtPath<Font>(SOURCE_PATH);
            if (source == null) throw new InvalidOperationException("Missing bundled font: " + SOURCE_PATH);
            FontEngine.InitializeFontEngine();
            if (FontEngine.LoadFontFace(source, 90) != FontEngineError.Success)
                throw new InvalidOperationException("Unable to load the bundled Noto font face.");
            var glyphIndices = new uint[ARROWS.Length];
            for (int i = 0; i < ARROWS.Length; i++)
            {
                bool found = FontEngine.TryGetGlyphWithUnicodeValue(ARROWS[i], GlyphLoadFlags.LOAD_NO_BITMAP, out var glyph);
                glyphIndices[i] = found ? glyph.index : 0;
                if (glyphIndices[i] == 0)
                    throw new InvalidOperationException($"Noto is missing U+{ARROWS[i]:X4}.");
            }

            TMP_FontAsset arrows = LoadExisting(ARROW_PATH);
            if (arrows == null)
            {
                arrows = TMP_FontAsset.CreateFontAsset(source, 90, 9, GlyphRenderMode.SDFAA,
                    512, 512, AtlasPopulationMode.Dynamic, false);
                if (arrows == null) throw new InvalidOperationException("Unable to create the arrow SDF font.");
                try
                {
                    if (!arrows.TryAddCharacters(ARROWS, out uint[] missing, false) ||
                        (missing != null && missing.Length != 0))
                        throw new InvalidOperationException("Unable to bake all four transform arrows.");
                    arrows.atlasPopulationMode = AtlasPopulationMode.Static;
                    var serializedArrows = new SerializedObject(arrows);
                    serializedArrows.FindProperty("m_ClearDynamicDataOnBuild").boolValue = false;
                    serializedArrows.ApplyModifiedPropertiesWithoutUndo();
                    arrows.name = "EliteDrumsTransform Arrows SDF";
                    ValidateBake(arrows, glyphIndices);
                }
                catch
                {
                    foreach (var texture in arrows.atlasTextures)
                        if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                    if (arrows.material != null) UnityEngine.Object.DestroyImmediate(arrows.material);
                    UnityEngine.Object.DestroyImmediate(arrows);
                    throw;
                }
                AssetDatabase.CreateAsset(arrows, ARROW_PATH);
                AssetDatabase.AddObjectToAsset(arrows.material, arrows);
                foreach (var texture in arrows.atlasTextures)
                    AssetDatabase.AddObjectToAsset(texture, arrows);
                EditorUtility.SetDirty(arrows);
                AssetDatabase.SaveAssetIfDirty(arrows);
            }
            ValidateBake(arrows, glyphIndices);

            TMP_FontAsset labels = LoadExisting(LABEL_PATH);
            if (labels == null)
            {
                string originalPath = AssetDatabase.GetAssetPath(barlow);
                if (string.IsNullOrEmpty(originalPath) || !AssetDatabase.CopyAsset(originalPath, LABEL_PATH))
                    throw new InvalidOperationException("Unable to copy the Barlow font for transform labels.");
                labels = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(LABEL_PATH);
                if (labels == null) throw new InvalidOperationException("The copied Barlow font could not be loaded.");
            }
            // Keep the original localization fallbacks after the dedicated, explicit arrow fallback.
            var fallbacks = new List<TMP_FontAsset> { arrows };
            if (barlow.fallbackFontAssetTable != null)
                foreach (var fallback in barlow.fallbackFontAssetTable)
                    if (fallback != null && fallback != arrows && fallback != labels)
                        fallbacks.Add(fallback);
            bool changed = labels.fallbackFontAssetTable == null ||
                labels.fallbackFontAssetTable.Count != fallbacks.Count;
            if (!changed)
                for (int i = 0; i < fallbacks.Count; i++)
                    changed |= labels.fallbackFontAssetTable[i] != fallbacks[i];
            if (changed)
            {
                labels.fallbackFontAssetTable = fallbacks;
                EditorUtility.SetDirty(labels);
                AssetDatabase.SaveAssetIfDirty(labels);
            }
            return labels;
        }

        private static TMP_FontAsset LoadExisting(string path)
        {
            var existing = AssetDatabase.LoadMainAssetAtPath(path);
            if (existing != null && !(existing is TMP_FontAsset))
                throw new InvalidOperationException("A non-font asset already exists at " + path);
            return existing as TMP_FontAsset;
        }

        private static void ValidateBake(TMP_FontAsset font, uint[] glyphIndices)
        {
            if (font.atlasPopulationMode != AtlasPopulationMode.Static ||
                font.atlasRenderMode != GlyphRenderMode.SDFAA || font.isMultiAtlasTexturesEnabled ||
                font.atlasTextures == null || font.atlasTextures.Length != 1 ||
                font.atlasTextures[0] == null || font.material == null ||
                font.material.mainTexture != font.atlasTextures[0])
                throw new InvalidOperationException("The transform arrow font is not a single static SDF atlas.");
            font.ReadFontAssetDefinition();
            Texture2D atlas = font.atlasTextures[0];
            foreach (uint unicode in ARROWS)
            {
                int index = Array.IndexOf(ARROWS, unicode);
                if (!font.characterLookupTable.TryGetValue(unicode, out var character) ||
                    character.glyph == null || character.glyph.index != glyphIndices[index] ||
                    character.glyph.atlasIndex != 0)
                    throw new InvalidOperationException($"U+{unicode:X4} was not baked from the expected Noto glyph.");
                var rect = character.glyph.glyphRect;
                if (rect.width <= 0 || rect.height <= 0 || rect.x < 0 || rect.y < 0 ||
                    rect.x + rect.width > atlas.width || rect.y + rect.height > atlas.height)
                    throw new InvalidOperationException($"U+{unicode:X4} has an invalid atlas rectangle.");
                bool hasInk = false;
                foreach (Color pixel in atlas.GetPixels(rect.x, rect.y, rect.width, rect.height))
                    hasInk |= pixel.a > 0f;
                if (!hasInk) throw new InvalidOperationException($"U+{unicode:X4} has an empty SDF bake.");
            }
        }
    }
}
