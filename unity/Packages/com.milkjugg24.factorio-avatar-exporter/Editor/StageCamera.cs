using System;
using System.Collections.Generic;
using MilkJugg24.FactorioAvatarExporter.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MilkJugg24.FactorioAvatarExporter.Editor
{
    /// <summary>
    /// The Factorio sprite camera in an <see cref="AvatarStage"/>'s scene: a key light, flat ambient light, and
    /// renders on black and on white turned into a transparent frame with Core's difference matting.
    ///
    /// Shaders are compiled before they are drawn (Unity would otherwise draw a cyan placeholder while they
    /// compile in the background), and a render that still shows the placeholder is taken again.
    /// </summary>
    internal sealed class StageCamera : IDisposable
    {
        public const int Supersampling = 2;
        private const int RenderAttempts = 5;
        private static readonly Color Ambient = new Color(0.45f, 0.45f, 0.45f);
        private const float LightIntensity = 1.0f;

        private readonly AvatarStage stage;
        private readonly Camera camera;
        private readonly bool linear = PlayerSettings.colorSpace == UnityEngine.ColorSpace.Linear;
        private RenderTexture target;
        private Texture2D readback;

        public StageCamera(AvatarStage stage)
        {
            this.stage = stage;
            var cameraObject = new GameObject("Sprite camera");
            SceneManager.MoveGameObjectToScene(cameraObject, stage.Scene);
            camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.scene = stage.Scene;
            camera.cameraType = CameraType.Preview;
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.allowHDR = true;
            camera.allowMSAA = false;
            camera.useOcclusionCulling = false;
            camera.depthTextureMode = DepthTextureMode.Depth; // for shader effects that read depth

            var lightObject = new GameObject("Key light");
            SceneManager.MoveGameObjectToScene(lightObject, stage.Scene);
            (double x, double y, double z) toLight = UnitySpace.ToUnity(Conventions.ShadeLight);
            lightObject.transform.rotation = Quaternion.LookRotation(-new Vector3((float)toLight.x, (float)toLight.y, (float)toLight.z));
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = LightIntensity;
            light.shadows = LightShadows.None;
        }

        /// <summary>Retakes because a shader was still compiling, since the camera was made.</summary>
        public int Retakes { get; private set; }

        /// <summary>Placeholder pixels left after the last attempt, since the camera was made.</summary>
        public int PlaceholderLeft { get; private set; }

        /// <summary>Points the camera at <paramref name="canvas"/> (render size = canvas size x <see cref="Supersampling"/>).</summary>
        public void Aim(CanvasSpec canvas)
        {
            UnityCameraSetup view = FactorioCamera.Setup(canvas).ToUnity(stage.MetersPerTile);
            camera.transform.SetPositionAndRotation(
                new Vector3((float)view.Position.x, (float)view.Position.y, (float)view.Position.z),
                new Quaternion((float)view.Rotation.x, (float)view.Rotation.y, (float)view.Rotation.z, (float)view.Rotation.w));
            if (target == null || target.width != view.TargetWidth || target.height != view.TargetHeight)
            {
                ReleaseTargets();
                target = new RenderTexture(view.TargetWidth, view.TargetHeight, 24, RenderTextureFormat.ARGBHalf,
                    RenderTextureReadWrite.Linear) { antiAliasing = 1 };
                readback = new Texture2D(view.TargetWidth, view.TargetHeight, TextureFormat.RGBAFloat, false, true);
            }
            camera.targetTexture = target;
            camera.orthographic = true;
            camera.orthographicSize = (float)view.HalfHeight;
            camera.nearClipPlane = (float)view.Near;
            camera.farClipPlane = (float)view.Far;
            // set the projection itself, after the target: Unity resets the aspect ratio to the target's,
            // which would squeeze the Factorio view (its height is stretched 1.25 times)
            camera.projectionMatrix = Matrix4x4.Ortho((float)-view.HalfWidth, (float)view.HalfWidth,
                (float)-view.HalfHeight, (float)view.HalfHeight, (float)view.Near, (float)view.Far);
        }

        /// <summary>
        /// The avatar as posed now, as a transparent frame at the canvas size (premultiplied, linear light):
        /// rendered on black and on white, matted and reduced by <see cref="Supersampling"/>.
        /// </summary>
        public ImageF RenderFrame()
        {
            using (new SynchronousShaders())
            {
                ImageF onBlack = RenderChecked(Color.black);
                ImageF onWhite = RenderChecked(Color.white);
                return Downsample.Box(Matting.FromBlackAndWhite(onBlack, onWhite, linear), Supersampling);
            }
        }

        /// <summary>
        /// True when a pixel on the outer edge of <paramref name="frame"/> is drawn on: the canvas was too small
        /// and something is cut off.
        /// </summary>
        public static bool TouchesEdge(Image8 frame)
        {
            int w = frame.Width, h = frame.Height;
            for (int x = 0; x < w; x++)
            {
                if (frame[x, 0, 3] != 0 || frame[x, h - 1, 3] != 0)
                {
                    return true;
                }
            }
            for (int y = 0; y < h; y++)
            {
                if (frame[0, y, 3] != 0 || frame[w - 1, y, 3] != 0)
                {
                    return true;
                }
            }
            return false;
        }

        private ImageF RenderChecked(Color background)
        {
            for (int attempt = 1; ; attempt++)
            {
                ImageF image = RenderOnce(background);
                int placeholder = PlaceholderPixels(image);
                if (placeholder == 0)
                {
                    return image;
                }
                if (attempt == RenderAttempts)
                {
                    PlaceholderLeft += placeholder;
                    return image;
                }
                Retakes++;
                CompileShaders(stage.Avatar);
                for (int wait = 0; ShaderUtil.anythingCompiling && wait < 200; wait++)
                {
                    System.Threading.Thread.Sleep(50);
                }
            }
        }

        /// <summary>Renders over a solid background and reads the result back with row 0 at the top.</summary>
        private ImageF RenderOnce(Color background)
        {
            camera.backgroundColor = background;
            Unsupported.SetOverrideLightingSettings(stage.Scene);
            try
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = Ambient;
                camera.Render();
            }
            finally
            {
                Unsupported.RestoreOverrideLightingSettings();
            }
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, readback.width, readback.height), 0, 0, false);
            RenderTexture.active = previous;
            Color[] pixels = readback.GetPixels();
            int w = readback.width, h = readback.height;
            var image = new ImageF(w, h);
            for (int y = 0; y < h; y++)
            {
                int row = (h - 1 - y) * w;
                for (int x = 0; x < w; x++)
                {
                    Color c = pixels[row + x];
                    image[x, y, 0] = c.r;
                    image[x, y, 1] = c.g;
                    image[x, y, 2] = c.b;
                    image[x, y, 3] = 1;
                }
            }
            return image;
        }

        /// <summary>
        /// Compiles every pass of every material on <paramref name="avatar"/> now, waiting for each. Returns how
        /// many passes needed compiling.
        /// </summary>
        public static int CompileShaders(GameObject avatar)
        {
            int compiled = 0;
            var seen = new HashSet<Material>();
            foreach (Renderer renderer in avatar.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null || material.shader == null || !seen.Add(material))
                    {
                        continue;
                    }
                    for (int pass = 0; pass < material.passCount; pass++)
                    {
                        if (!ShaderUtil.IsPassCompiled(material, pass))
                        {
                            ShaderUtil.CompilePass(material, pass, true);
                            compiled++;
                        }
                    }
                }
            }
            return compiled;
        }

        /// <summary>Pixels in Unity's shader-compilation placeholder colour, an unlit cyan no lit surface gives.</summary>
        private static int PlaceholderPixels(ImageF image)
        {
            int count = 0;
            float[] p = image.Pixels;
            for (int i = 0; i < p.Length; i += 4)
            {
                if (p[i] < 0.02f && p[i + 1] > 0.97f && p[i + 2] > 0.97f)
                {
                    count++;
                }
            }
            return count;
        }

        private void ReleaseTargets()
        {
            camera.targetTexture = null;
            if (target != null)
            {
                Object.DestroyImmediate(target);
            }
            if (readback != null)
            {
                Object.DestroyImmediate(readback);
            }
        }

        public void Dispose()
        {
            ReleaseTargets();
        }

        /// <summary>Turns asynchronous shader compilation off while it lives, then restores the settings.</summary>
        private sealed class SynchronousShaders : IDisposable
        {
            private readonly bool allowAsync = ShaderUtil.allowAsyncCompilation;
            private readonly bool asyncSetting = EditorSettings.asyncShaderCompilation;

            public SynchronousShaders()
            {
                ShaderUtil.allowAsyncCompilation = false;
                EditorSettings.asyncShaderCompilation = false;
            }

            public void Dispose()
            {
                ShaderUtil.allowAsyncCompilation = allowAsync;
                EditorSettings.asyncShaderCompilation = asyncSetting;
            }
        }
    }
}
