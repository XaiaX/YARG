using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YARG.Core;
using YARG.Core.Song;
using YARG.Helpers.Extensions;
using YARG.Settings;

namespace YARG.Menu.MusicLibrary
{
    public enum DifficultyRingMode
    {
        Classic,
        Expanded,
    }

    public class DifficultyRing : MonoBehaviour, IPointerClickHandler
    {
        private static readonly Dictionary<string, Sprite> ICON_CACHE = new();

        [SerializeField]
        private Image _instrumentIcon;

        [SerializeField]
        private Image _ringSprite;
        [SerializeField]
        private Image _ringBase;

        [SerializeField]
        private TextMeshProUGUI _intensityNumber;

        [Space]
        [SerializeField]
        private Color _ringEmptyColor;
        [SerializeField]
        private Color _ringWhiteColor;
        [SerializeField]
        private Color _ringRedColor;
        [SerializeField]
        private Color _ringPurpleColor;
        [SerializeField]
        private Material _ringRainbowMaterial;
        [SerializeField]
        private Color _partSelectedColor;

        private SongSearchingField _songSearchingField;
        private Instrument _instrument;
        private int _intensity;
        private bool _active;

        private const float ACTIVE_OPACITY = 1f;
        private const float INACTIVE_OPACITY = 0.2f;

        private void Awake()
        {
            _songSearchingField = FindFirstObjectByType<SongSearchingField>();
        }

        public void SetInfo(string assetName, Instrument instrument, PartValues values)
        {
            // Set instrument icon
            var icon = GetIcon(assetName);
            _instrumentIcon.sprite = icon;
            _instrument = instrument;
            _intensity = values.Intensity;

            // Determine how many rings to use
            uint ringCount;
            if (values.SubTracks == 0)
            {
                // No part
                _active = false;
                ringCount = 0;
                // Parts which copy their intensities from other instruments
                // may have a greater-than-zero value here
                values.Intensity = 0;
            }
            else
            {
                // Part present
                _active = true;
                if (values.Intensity < 1)
                {
                    ringCount = 0;
                }
                else
                {
                    ringCount = 1 + (uint) ((values.Intensity - 1) % 5);
                }
            }

            // Determine ring color and set intensity number text
            var ringColor = _ringWhiteColor;
            var ringBaseColor = _ringEmptyColor;
            Material ringMaterial = null;
            switch (SettingsManager.Settings.DifficultyRings.Value)
            {
                case DifficultyRingMode.Classic:
                {
                    if (values.Intensity > 5)
                    {
                        ringCount = 5;
                        ringColor = _ringRedColor;
                    }

                    _intensityNumber.text = values.Intensity > 6
                        ? values.Intensity.ToString()
                        : string.Empty;
                    break;
                }
                case DifficultyRingMode.Expanded:
                {
                    if (values.Intensity > 15)
                    {
                        ringCount = 5;
                    }

                    switch (values.Intensity)
                    {
                        case > 15:
                            ringMaterial = _ringRainbowMaterial;
                            break;
                        case > 10:
                            ringColor = _ringPurpleColor;
                            ringBaseColor = _ringRedColor;
                            break;
                        case > 5:
                            ringColor = _ringRedColor;
                            ringBaseColor = _ringWhiteColor;
                            break;
                    }

                    _intensityNumber.text = values.Intensity > 5
                        ? values.Intensity.ToString()
                        : string.Empty;
                    break;
                }
            }

            // Set ring sprite properties
            float fill = ringCount / 5f;
            _ringSprite.fillAmount = fill;
            _ringBase.fillAmount = 1 - fill;
            _ringSprite.color = ringColor;
            _ringBase.color = ringBaseColor;
            _ringSprite.material = ringMaterial;

            // Set opacity
            if (_active)
            {
                _ringSprite.color = _ringSprite.color.WithAlpha(ACTIVE_OPACITY);
                _ringBase.color = _ringBase.color.WithAlpha(ACTIVE_OPACITY);
            }
            else
            {
                _ringSprite.color = _ringSprite.color.WithAlpha(INACTIVE_OPACITY);
                _ringBase.color = _ringBase.color.WithAlpha(INACTIVE_OPACITY);
            }

            UpdateIconColor();
        }

        /// <summary>
        /// Tints the instrument icon (not the ring). Menus use this to dim or
        /// highlight instruments. Call after <see cref="SetInfo"/>, which resets
        /// the icon color.
        /// </summary>
        public void SetIconColor(Color color)
        {
            _instrumentIcon.color = color;
        }

        /// <summary>
        /// Dims the filled ring segments (not the base or icon). Menus use this
        /// to de-emphasize non-selected instruments. Call after
        /// <see cref="SetInfo"/>, which resets the segment opacity.
        /// </summary>
        public void SetRingOpacity(float alpha)
        {
            _ringSprite.color = _ringSprite.color.WithAlpha(alpha);
        }

        /// <summary>
        /// Draws a solid circle behind the whole ring, <paramref name="extraSize"/>
        /// units larger than the ring rect, so it rims the outside and shows
        /// through the gaps between wheel segments and around the icon. Menus use
        /// it to mark the selected instrument.
        /// </summary>
        public void ShowSelectionBackdrop(Color color, float extraSize = 2f)
        {
            var backdrop = new GameObject("SelectionBackdrop", typeof(RectTransform), typeof(Image));
            backdrop.layer = gameObject.layer;

            var rt = (RectTransform) backdrop.transform;
            rt.SetParent(transform, false);
            // First sibling renders behind the ring, icon, and intensity number.
            rt.SetAsFirstSibling();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = new Vector2(extraSize, extraSize);

            var image = backdrop.GetComponent<Image>();
            image.sprite = GetCircleSprite();
            image.color = color;
            image.raycastTarget = false;
        }

        private static Sprite _circleSprite;

        // A plain filled circle, generated once at runtime — the project has no
        // solid-circle UI sprite, and Unity's builtin UI sprites aren't reliably
        // loadable, which renders as a square.
        private static Sprite GetCircleSprite()
        {
            if (_circleSprite != null)
            {
                return _circleSprite;
            }

            const int SIZE = 64;
            float radius = SIZE / 2f - 1f;
            float center = (SIZE - 1) / 2f;

            var pixels = new Color32[SIZE * SIZE];
            for (int y = 0; y < SIZE; y++)
            {
                for (int x = 0; x < SIZE; x++)
                {
                    float distance = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
                    // One-pixel antialiased falloff at the rim
                    byte alpha = (byte) (Mathf.Clamp01(radius - distance + 0.5f) * 255f);
                    pixels[y * SIZE + x] = new Color32(255, 255, 255, alpha);
                }
            }

            var texture = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();

            _circleSprite = Sprite.Create(texture, new Rect(0f, 0f, SIZE, SIZE),
                new Vector2(0.5f, 0.5f));
            return _circleSprite;
        }

        private static Sprite GetIcon(string assetName)
        {
            string assetKey = $"InstrumentIcons[{assetName}]";
            if (!ICON_CACHE.TryGetValue(assetKey, out var icon))
            {
                ICON_CACHE[assetKey] = icon = Addressables.LoadAssetAsync<Sprite>(assetKey).WaitForCompletion();
            }

            return icon;
        }

        private void UpdateIconColor()
        {
            if (!_active)
            {
                _instrumentIcon.color = Color.white.WithAlpha(INACTIVE_OPACITY);
                return;
            }

            if (_songSearchingField != null && _songSearchingField.HasInstrumentFilter(_instrument))
            {
                _instrumentIcon.color = _partSelectedColor.WithAlpha(ACTIVE_OPACITY);
                return;
            }
            _instrumentIcon.color = Color.white.WithAlpha(ACTIVE_OPACITY);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_songSearchingField == null)
            {
                return;
            }

            if (eventData.button == PointerEventData.InputButton.Right)
            {
                _songSearchingField.SetSearchInput(_instrument.ToSortAttribute(), $"\"{_intensity}\"");
            }
            else if (eventData.button == PointerEventData.InputButton.Left)
            {
                if (_instrument == Instrument.Band)
                {
                    // Don't allow filtering by "Band Instrument". That would be silly.
                    return;
                }
                _songSearchingField.SetSearchInput(_instrument.ToSortAttribute(), $"");
            }

            UpdateIconColor();
        }
    }
}
