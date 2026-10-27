using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;
using UnityEngine.AddressableAssets;
using UnityEngine.UI;
using YARG.Core;
using YARG.Helpers;
using YARG.Helpers.Extensions;
using YARG.Settings.Preview;
using YARG.Themes;

// pattern: Imperative Shell

namespace YARG.Settings.Metadata
{
    public class TrackPreviewBuilder : IPreviewBuilder
    {
        // Prefabs needed for this tab type
        private GameObject _trackPreview;
        private GameObject _trackPreviewUI;
        private readonly Func<string, GameObject> _loadAsset;

        public GameMode? StartingGameMode { get; set; }
        public YARG.Core.Game.EliteDrumsLayoutPreset EliteLayoutOverride { get; set; }
        public bool AllowIncompleteEliteLayoutPreview { get; set; }
        public YARG.Core.Game.ColorProfile ColorProfileOverride { get; set; }

        private readonly bool _forceShowHitWindow;

        private bool _forceGroove;
        public bool ForceGroove
        {
            get => _forceGroove;
            set
            {
                _forceGroove = value;
                if (_currentTrackPreview != null)
                {
                    _currentTrackPreview.ForceGroove = value;
                }
            }
        }

        private bool _forceStarPower;
        public bool ForceStarPower
        {
            get => _forceStarPower;
            set
            {
                _forceStarPower = value;
                if (_currentTrackPreview != null)
                {
                    _currentTrackPreview.ForceStarPower = value;
                }
            }
        }

        private FakeTrackPlayer _currentTrackPreview;
        private bool _forceStarPowerNotes;
        public bool ForceStarPowerNotes
        {
            get => _forceStarPowerNotes;
            set
            {
                _forceStarPowerNotes = value;

                // Propagate to the live player so existing notes recolor without a
                // rebuild. The auto-fired SettingsMenu.OnSettingChanged() drives the
                // recolor via the SettingChanged event. Use Unity's overloaded !=
                // (not `is not null`) so a destroyed/old player is treated as null.
                if (_currentTrackPreview != null)
                {
                    _currentTrackPreview.ForceStarPowerNotes = value;
                    // The world exists before Start assigns its color profile. Keep
                    // the option pending during same-frame preset refreshes.
                    if (_currentTrackPreview.ColorProfile != null)
                    {
                        _currentTrackPreview.RefreshColors(_currentTrackPreview.ColorProfile);
                    }
                }
            }
        }

        private bool _leftyFlip;
        public bool LeftyFlip
        {
            get => _leftyFlip;
            set
            {
                _leftyFlip = value;

                // Propagate to the live player (mirrors ForceStarPowerNotes).
                if (_currentTrackPreview != null)
                {
                    _currentTrackPreview.LeftyFlip = value;
                    if (_currentTrackPreview.ColorProfile != null)
                    {
                        _currentTrackPreview.RefreshColors(_currentTrackPreview.ColorProfile);
                    }
                }
            }
        }

        /// <summary>
        /// Forwards a lane spotlight request to the live preview (see
        /// <see cref="Preview.FakeTrackPlayer.SpotlightLane"/>). No-op when no
        /// preview is currently alive.
        /// </summary>
        public void RefreshColors(YARG.Core.Game.ColorProfile colorProfile)
        {
            ColorProfileOverride = colorProfile;
            _currentTrackPreview?.RefreshColors(colorProfile);
        }

        public void SpotlightLane(int fret, bool centerNote, bool cymbal, bool starPower)
        {
            if (_currentTrackPreview != null)
            {
                _currentTrackPreview.SpotlightLane(fret, centerNote, cymbal, starPower);
            }
        }

        /// <summary>
        /// Forwards to <see cref="FakeTrackPlayer.SpotlightNoteType"/>.
        /// No-op when no preview is currently alive.
        /// </summary>
        public void SpotlightNoteType(ThemeNoteType noteType, bool? starPower = null)
        {
            if (_currentTrackPreview != null)
            {
                _currentTrackPreview.SpotlightNoteType(noteType, starPower);
            }
        }

        /// <summary>
        /// Forwards a Pro Keys white/black note spotlight to the live preview.
        /// </summary>
        public void SpotlightProKeysNoteType(bool black, bool starPower)
        {
            if (_currentTrackPreview != null)
            {
                _currentTrackPreview.SpotlightProKeysNoteType(black, starPower);
            }
        }

        /// <summary>
        /// Forwards to <see cref="FakeTrackPlayer.SpotlightMiss"/>.
        /// </summary>
        public void SpotlightEliteRole(YARG.Core.Game.EliteDrumsColorRole role, bool starPower,
            ElitePreviewDescriptor? descriptor = null)
        {
            if (_currentTrackPreview != null)
                _currentTrackPreview.SpotlightEliteRole(role, starPower, descriptor);
        }

        public void SpotlightMiss()
        {
            if (_currentTrackPreview != null)
            {
                _currentTrackPreview.SpotlightMiss();
            }
        }

        /// <summary>
        /// Forwards to <see cref="FakeTrackPlayer.SpotlightStarPower"/>.
        /// </summary>
        public void SpotlightStarPower()
        {
            if (_currentTrackPreview != null)
            {
                _currentTrackPreview.SpotlightStarPower();
            }
        }

        public TrackPreviewBuilder(bool forceShowHitWindow = false, bool forceGroove = false,
            bool forceStarPower = false, Func<string, GameObject> loadAsset = null)
        {
            _loadAsset = loadAsset ?? (address => Addressables.LoadAssetAsync<GameObject>(address).WaitForCompletion());
            _forceShowHitWindow = forceShowHitWindow;
            _forceGroove = forceGroove;
            ForceStarPower = forceStarPower;
        }

        public UniTask BuildPreviewWorld(Transform worldContainer)
        {
            if (_trackPreview == null)
            {
                _trackPreview = _loadAsset("SettingPreviews/TrackPreview");
            }
            var trackObj = Object.Instantiate(_trackPreview, worldContainer);
            var trackPreview = trackObj.GetComponentInChildren<FakeTrackPlayer>();
            _currentTrackPreview = trackPreview;
            trackPreview.EliteLayoutOverride = EliteLayoutOverride?.Copy();
            trackPreview.AllowIncompleteEliteLayoutPreview = AllowIncompleteEliteLayoutPreview;
            trackPreview.ColorProfileOverride = ColorProfileOverride;

            trackPreview.ForceShowHitWindow = _forceShowHitWindow;
            trackPreview.ForceGroove = _forceGroove;
            trackPreview.ForceStarPower = ForceStarPower;
            trackPreview.ForceStarPowerNotes = ForceStarPowerNotes;
            trackPreview.LeftyFlip = LeftyFlip;

            // If null, just use the default value and skip setting it
            if (StartingGameMode is not null)
            {
                trackPreview.SelectedGameMode = StartingGameMode.Value;
            }

            return UniTask.CompletedTask;
        }

        public async UniTask BuildPreviewUI(Transform uiContainer)
        {
            if (_trackPreviewUI == null)
            {
                _trackPreviewUI = _loadAsset("SettingPreviews/TrackPreviewUI");
            }
            var go = Object.Instantiate(_trackPreviewUI, uiContainer);

            // Let newly enabled layout components initialize without requiring a rendered
            // end-of-frame coroutine or a SettingsMenu coroutine host.
            await UniTask.NextFrame(PlayerLoopTiming.Update);

            // Skip the game object was somehow destroyed
            if (go == null) return;
            Canvas.ForceUpdateCanvases();

            // Show and frame the raw image using the same path used by other track previews.
            ConfigurePreviewImage(go.GetComponentInChildren<RawImage>(), CameraPreviewTexture.PreviewTexture);
        }

        /// <summary>
        /// Assigns and frames a track preview image using the canonical settings-preview
        /// viewport. Callers may provide an independently owned render target.
        /// </summary>
        internal static void ConfigurePreviewImage(RawImage previewImage, RenderTexture texture)
        {
            previewImage.texture = texture;
            previewImage.color = Color.white;

            var canvas = previewImage.canvas;
            var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            var rect = previewImage.rectTransform.ToViewportSpaceCentered(v: false, scale: 0.9f, camera: camera);
            rect.y = 0f;
            previewImage.uvRect = rect;
        }
    }
}
