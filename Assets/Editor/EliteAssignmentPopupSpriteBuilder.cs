using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YARG.Editor
{
    /// <summary>Creates sliced artwork and opaque rounded backings used only by the assignment popup.</summary>
    public static class EliteAssignmentPopupSpriteBuilder
    {
        public const string HEADER_PATH = "Assets/Art/Menu/EliteLayout/AssignmentPopupHeader.png";
        public const string FOOTER_PATH = "Assets/Art/Menu/EliteLayout/AssignmentPopupFooter.png";
        private const string SOURCE_FOLDER = "Assets/Art/Menu/DifficultySelect/";
        private const string OUTPUT_FOLDER = "Assets/Art/Menu/EliteLayout";
        private const float SPRITE_PIXELS_PER_UNIT = 100f;

        public readonly struct Band
        {
            public readonly Sprite Artwork;
            public readonly Sprite Backing;
            public readonly float AuthoredHeight;
            public readonly Vector4 Border;

            internal Band(Sprite artwork, Sprite backing, float authoredHeight, Vector4 border)
            {
                Artwork = artwork;
                Backing = backing;
                AuthoredHeight = authoredHeight;
                Border = border;
            }

            /// <summary>
            /// Use Image.Type.Sliced, fillCenter=true, and this multiplier on both images.
            /// Place the backing directly behind the artwork at the same size; tint it opaque black.
            /// Keep the band at AuthoredHeight. Pass the containing Canvas.referencePixelsPerUnit.
            /// </summary>
            public float PixelsPerUnitMultiplier(float referencePixelsPerUnit = 100f)
            {
                if (referencePixelsPerUnit <= 0f)
                    throw new ArgumentOutOfRangeException(nameof(referencePixelsPerUnit));
                return Artwork.rect.height / AuthoredHeight * referencePixelsPerUnit / Artwork.pixelsPerUnit;
            }
        }

        public static Band EnsureHeader() => EnsureBand("MenuHeader.png", HEADER_PATH, 80f, true);
        public static Band EnsureFooter() => EnsureBand("MenuFooter.png", FOOTER_PATH, 15f, false);

        private static Band EnsureBand(string sourceName, string destination, float height, bool header)
        {
            EnsureFolder(OUTPUT_FOLDER);
            byte[] sourceBytes = File.ReadAllBytes(SOURCE_FOLDER + sourceName);
            // Decode separately instead of changing the shared source importer's readability or borders.
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!ImageConversion.LoadImage(texture, sourceBytes))
                    throw new InvalidOperationException("Cannot decode popup artwork: " + sourceName);
                Color32[] pixels = texture.GetPixels32();
                int width = texture.width;
                int pixelHeight = texture.height;
                int leftExtent = 0;
                int rightExtent = 0;
                bool foundOpaqueRow = false;
                var silhouette = new Color32[pixels.Length];
                for (int y = 0; y < pixelHeight; y++)
                {
                    int first = -1;
                    int last = -1;
                    for (int x = 0; x < width; x++)
                    {
                        if (pixels[y * width + x].a != byte.MaxValue) continue;
                        if (first < 0) first = x;
                        last = x;
                    }
                    // The footer has a translucent shadow below its rounded edge. Do not turn it opaque.
                    if (first < 0) continue;
                    foundOpaqueRow = true;
                    leftExtent = Mathf.Max(leftExtent, first);
                    rightExtent = Mathf.Max(rightExtent, width - 1 - last);
                    for (int x = first; x <= last; x++)
                        silhouette[y * width + x] = new Color32(255, 255, 255, 255);
                    // Retain the immediately adjacent coverage pixels for antialiasing, never a square fill.
                    if (first > 0)
                        silhouette[y * width + first - 1] = new Color32(255, 255, 255,
                            pixels[y * width + first - 1].a);
                    if (last < width - 1)
                        silhouette[y * width + last + 1] = new Color32(255, 255, 255,
                            pixels[y * width + last + 1].a);
                }
                if (!foundOpaqueRow || leftExtent + rightExtent + 2 >= width)
                    throw new InvalidOperationException("Popup artwork has no usable opaque contour: " + sourceName);

                // Preserve the entire native vertical profile. Only the horizontal center may stretch.
                var border = new Vector4(leftExtent + 1, header ? 1 : pixelHeight - 1,
                    rightExtent + 1, header ? pixelHeight - 1 : 1);
                WriteIfChanged(destination, sourceBytes);
                Sprite artwork = ImportSprite(destination, border);
                texture.SetPixels32(silhouette);
                texture.Apply();
                string backingPath = destination.Replace(".png", "Backing.png");
                WriteIfChanged(backingPath, texture.EncodeToPNG());
                Sprite backing = ImportSprite(backingPath, border);
                return new Band(artwork, backing, height, border);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static void WriteIfChanged(string path, byte[] bytes)
        {
            // Never delete/recreate the asset or copy source metadata: existing GUIDs remain stable.
            if (File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(bytes)) return;
            File.WriteAllBytes(path, bytes);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }

        private static Sprite ImportSprite(string path, Vector4 border)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Missing popup texture importer: " + path);
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            bool changed = settings.textureType != TextureImporterType.Sprite ||
                settings.spriteMode != (int) SpriteImportMode.Single ||
                settings.spriteMeshType != SpriteMeshType.FullRect || settings.spriteBorder != border ||
                settings.spritePixelsPerUnit != SPRITE_PIXELS_PER_UNIT || settings.mipmapEnabled ||
                !settings.alphaIsTransparency || settings.npotScale != TextureImporterNPOTScale.None ||
                settings.filterMode != FilterMode.Bilinear || settings.wrapMode != TextureWrapMode.Clamp ||
                importer.textureCompression != TextureImporterCompression.Uncompressed || importer.maxTextureSize != 2048;
            if (changed)
            {
                settings.textureType = TextureImporterType.Sprite;
                settings.spriteMode = (int) SpriteImportMode.Single;
                settings.spriteMeshType = SpriteMeshType.FullRect;
                settings.spriteBorder = border;
                settings.spritePixelsPerUnit = SPRITE_PIXELS_PER_UNIT;
                settings.mipmapEnabled = false;
                settings.alphaIsTransparency = true;
                settings.npotScale = TextureImporterNPOTScale.None;
                settings.filterMode = FilterMode.Bilinear;
                settings.wrapMode = TextureWrapMode.Clamp;
                importer.SetTextureSettings(settings);
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 2048;
                importer.SaveAndReimport();
            }
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) throw new InvalidOperationException("Cannot load popup sprite: " + path);
            return sprite;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/'));
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
        }
    }
}
