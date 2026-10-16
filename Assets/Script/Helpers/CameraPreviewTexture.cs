using System.Collections.Generic;
using UnityEngine;

namespace YARG.Helpers
{
    public class CameraPreviewTexture : MonoBehaviour
    {
        public static RenderTexture PreviewTexture { get; private set; }

        private static readonly List<CameraPreviewTexture> _previews = new();
        private bool _useOwnedTexture;

        /// <summary>
        /// When enabled, this camera is managed by its owner and is excluded from the
        /// shared settings preview texture. Set before assigning its own render target.
        /// </summary>
        public bool UseOwnedTexture
        {
            get => _useOwnedTexture;
            set
            {
                if (_useOwnedTexture == value)
                {
                    return;
                }

                _useOwnedTexture = value;
                if (!isActiveAndEnabled)
                {
                    return;
                }

                if (_useOwnedTexture)
                {
                    _previews.Remove(this);
                }
                else if (!_previews.Contains(this))
                {
                    _previews.Add(this);
                    UpdateRenderTexture();
                }
            }
        }

        private void OnEnable()
        {
            if (_useOwnedTexture)
            {
                return;
            }

            if (!_previews.Contains(this))
            {
                _previews.Add(this);
            }

            UpdateRenderTexture();
        }

        private void OnDisable()
        {
            _previews.Remove(this);
        }

        private void OnDestroy()
        {
            _previews.Remove(this);
        }

        private void UpdateRenderTexture()
        {
            SetCameraPreviewTarget(GetComponent<Camera>(), PreviewTexture);
        }

        private static RenderTexture CreatePreviewTexture(RenderTextureDescriptor descriptor, string textureName)
        {
            descriptor.mipCount = 0;
            var texture = new RenderTexture(descriptor)
            {
                name = textureName
            };
            texture.Create();
            return texture;
        }

        /// <summary>Creates a texture for a preview outside the shared settings lifecycle.</summary>
        public static RenderTexture CreateOwnedPreviewTexture(int width, int height, string textureName)
        {
            return CreatePreviewTexture(
                new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGBHalf, 32), textureName);
        }

        private static void SetCameraPreviewTarget(Camera camera, RenderTexture texture)
        {
            if (camera == null)
            {
                return;
            }

            var highwayRenderer = camera.GetComponent<YARG.Gameplay.Visuals.HighwayCameraRendering>();
            if (highwayRenderer != null)
            {
                highwayRenderer.SetPreviewRenderTarget(texture);
            }
            else
            {
                camera.targetTexture = texture;
            }
        }

        /// <summary>Releases and destroys a texture created by <see cref="CreateOwnedPreviewTexture"/>.</summary>
        public static void ReleaseOwnedPreviewTexture(RenderTexture texture)
        {
            if (texture == null)
            {
                return;
            }

            texture.Release();
            if (Application.isPlaying)
            {
                Object.Destroy(texture);
            }
            else
            {
                Object.DestroyImmediate(texture);
            }
        }

        /// <summary>
        /// Binds a caller-owned preview target to the cameras under a detached preview world.
        /// Highway renderers borrow the target through their existing target API.
        /// </summary>
        public static void SetOwnedPreviewTargets(Transform previewWorld, RenderTexture texture)
        {
            if (previewWorld == null || texture == null)
            {
                return;
            }

            foreach (var camera in previewWorld.GetComponentsInChildren<Camera>(true))
            {
                if (!camera.gameObject.activeInHierarchy)
                {
                    continue;
                }

                var ownership = camera.GetComponent<CameraPreviewTexture>();
                if (ownership != null)
                {
                    ownership.UseOwnedTexture = true;
                }

                SetCameraPreviewTarget(camera, texture);
            }
        }

        /// <summary>Detaches the caller-owned target before its texture is released.</summary>
        public static void ClearOwnedPreviewTargets(Transform previewWorld, RenderTexture texture)
        {
            if (previewWorld == null || texture == null)
            {
                return;
            }

            foreach (var camera in previewWorld.GetComponentsInChildren<Camera>(true))
            {
                SetCameraPreviewTarget(camera, null);
            }
        }

        public static void SetAllPreviews()
        {
            if (PreviewTexture != null)
            {
                PreviewTexture.Release();
                Destroy(PreviewTexture);
            }

            var descriptor = new RenderTextureDescriptor(
                Screen.width, Screen.height,
                RenderTextureFormat.ARGBHalf,
                32
            );
            PreviewTexture = CreatePreviewTexture(descriptor, "Settings preview");

            foreach (var script in _previews)
            {
                if (script != null && script.isActiveAndEnabled && !script._useOwnedTexture)
                {
                    script.UpdateRenderTexture();
                }
            }
        }
    }
}
