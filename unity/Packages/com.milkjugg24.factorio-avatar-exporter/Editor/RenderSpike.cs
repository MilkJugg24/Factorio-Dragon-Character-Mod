using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using MilkJugg24.FactorioAvatarExporter.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using CoreColorSpace = MilkJugg24.FactorioAvatarExporter.Core.ColorSpace;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace MilkJugg24.FactorioAvatarExporter.Editor
{
    /// <summary>
    /// The rendering spike (stage 3a): renders the selected avatar, as it stands, from the Factorio sprite
    /// camera in all 8 directions, on black and on white in an isolated preview scene, and turns the renders
    /// into transparent sprites with Core's difference matting. It checks that real avatar shaders render
    /// cleanly this way before the full exporter is built. Nothing in the open scene is changed.
    ///
    /// Output goes to FactorioAvatarSpike/&lt;avatar&gt;/ next to the project's Assets folder:
    /// direction-1..8.png (north first, clockwise), strip.png and strip-on-grey.png (all directions side
    /// by side), south-on-black.png and south-on-white.png (the raw renders) and report.txt.
    /// </summary>
    public static class RenderSpike
    {
        private const string MenuPath = "Tools/Factorio Avatar Exporter/Spike: Render Selected Avatar";
        private const int PixelsPerTile = 64;
        private const int Supersampling = 2;
        private const int Directions = 8;
        private const int SouthDirection = 4;
        private static readonly Color Ambient = new Color(0.45f, 0.45f, 0.45f);
        private const float LightIntensity = 1.0f;

        [MenuItem(MenuPath, true)]
        private static bool CanRender() => Selection.activeGameObject != null;

        [MenuItem(MenuPath)]
        private static void Render()
        {
            GameObject source = Selection.activeGameObject;
            string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "FactorioAvatarSpike",
                SafeFileName(source.name));
            try
            {
                string report = RenderTo(source, folder);
                Debug.Log("Factorio avatar spike: wrote " + folder + "\n" + report);
                EditorUtility.RevealInFinder(Path.Combine(folder, "strip-on-grey.png"));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("Factorio avatar spike", "The render failed: " + e.Message, "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>
        /// Command-line entry point, for tests/run_unity_spike.ps1:
        /// <c>Unity -batchmode -projectPath &lt;project&gt; -executeMethod
        /// MilkJugg24.FactorioAvatarExporter.Editor.RenderSpike.RenderFromCommandLine [-spikeScene &lt;scene&gt;]
        /// [-spikeAvatar &lt;prefab or object name&gt;] [-spikeOut &lt;folder&gt;]</c>. Renders a prefab
        /// (Assets/....prefab), the top-level object of that name in the scene, or with only a scene every
        /// active humanoid avatar at its top level, each into &lt;folder&gt;/&lt;avatar&gt;/, writes
        /// summary.txt and exits Unity with 1 when anything failed.
        /// </summary>
        public static void RenderFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            string Arg(string name)
            {
                int i = Array.IndexOf(args, name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }
            string scenePath = Arg("-spikeScene");
            string avatarName = Arg("-spikeAvatar");
            string output = Arg("-spikeOut") ?? Path.Combine(Path.GetDirectoryName(Application.dataPath), "FactorioAvatarSpike");
            var summary = new StringBuilder();
            int failed = 0;
            try
            {
                var sources = new List<GameObject>();
                if (avatarName != null && avatarName.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(avatarName);
                    if (prefab == null)
                    {
                        throw new ArgumentException("no prefab at " + avatarName);
                    }
                    sources.Add(prefab);
                }
                else if (scenePath != null)
                {
                    Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    foreach (GameObject root in scene.GetRootGameObjects())
                    {
                        if (avatarName != null ? root.name == avatarName : IsActiveHumanoid(root))
                        {
                            sources.Add(root);
                        }
                    }
                }
                else
                {
                    throw new ArgumentException("pass -spikeScene <scene>, -spikeAvatar <prefab>, or both");
                }
                if (sources.Count == 0)
                {
                    throw new ArgumentException("no avatar found" + (avatarName != null ? " named " + avatarName : " in " + scenePath));
                }
                foreach (GameObject source in sources)
                {
                    summary.AppendLine("== " + source.name);
                    try
                    {
                        summary.Append(RenderTo(source, Path.Combine(output, SafeFileName(source.name))));
                    }
                    catch (Exception e)
                    {
                        failed++;
                        summary.AppendLine("FAILED: " + e);
                    }
                }
            }
            catch (Exception e)
            {
                failed++;
                summary.AppendLine("FAILED: " + e);
            }
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "summary.txt"), summary.ToString());
            Debug.Log("Factorio avatar spike:\n" + summary);
            EditorApplication.Exit(failed == 0 ? 0 : 1);
        }

        private static bool IsActiveHumanoid(GameObject root)
        {
            Animator animator = root.activeInHierarchy ? root.GetComponent<Animator>() : null;
            return animator != null && animator.avatar != null && animator.avatar.isHuman;
        }

        private static string RenderTo(GameObject source, string folder)
        {
            var clock = Stopwatch.StartNew();
            var report = new StringBuilder();
            report.AppendLine("avatar: " + source.name);
            report.AppendLine("unity: " + Application.unityVersion + ", color space " + PlayerSettings.colorSpace +
                ", render pipeline " + (GraphicsSettings.currentRenderPipeline == null ? "built-in" : GraphicsSettings.currentRenderPipeline.name));
            bool linear = PlayerSettings.colorSpace == UnityEngine.ColorSpace.Linear;

            Scene scene = EditorSceneManager.NewPreviewScene();
            var temporary = new List<Object>();
            // Unity draws a cyan placeholder while a shader variant compiles in the background; compile
            // each one before it is drawn instead
            bool asyncCompilation = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                // the avatar hangs under a pivot at the origin, its feet on the ground and its hips over the pivot
                var pivot = new GameObject("Pivot");
                SceneManager.MoveGameObjectToScene(pivot, scene);
                GameObject avatar = Object.Instantiate(source);
                avatar.name = source.name;
                SceneManager.MoveGameObjectToScene(avatar, scene);
                avatar.transform.SetParent(pivot.transform, false);
                avatar.transform.localPosition = Vector3.zero;
                avatar.transform.localRotation = Quaternion.identity;
                Strip(avatar, report);

                List<Vector3> points = MeshPoints(avatar, temporary);
                if (points.Count == 0)
                {
                    throw new InvalidOperationException("the avatar has no visible meshes");
                }
                float bottom = points.Min(p => p.y);
                float top = points.Max(p => p.y);
                Vector3 centre = HipsOrCentre(avatar, points);
                avatar.transform.localPosition = new Vector3(-centre.x, -bottom, -centre.z);
                for (int i = 0; i < points.Count; i++)
                {
                    points[i] += avatar.transform.localPosition;
                }
                double height = top - bottom;
                double metersPerTile = height / Conventions.DefaultCharacterHeight;
                report.AppendLine($"height: {height:0.000} m, drawn {Conventions.DefaultCharacterHeight} tiles tall ({metersPerTile:0.000} m per tile)");

                // one canvas for all directions, from the meshes turned to each facing
                Bounds3 bounds = Bounds3.Empty;
                for (int d = 0; d < Directions; d++)
                {
                    Quaternion turn = Facing(d);
                    foreach (Vector3 p in points)
                    {
                        Vector3 q = turn * p;
                        bounds = bounds.Encapsulate(UnitySpace.ToCore(q.x / metersPerTile, q.y / metersPerTile, q.z / metersPerTile));
                    }
                }
                CanvasSpec canvas = CanvasPlanner.PlanColor(new[] { bounds }, PixelsPerTile, Supersampling);
                UnityCameraSetup view = FactorioCamera.Setup(canvas).ToUnity(metersPerTile);
                report.AppendLine($"canvas: {canvas.Width}x{canvas.Height} px, anchor {canvas.AnchorX},{canvas.AnchorY}, rendered at {view.TargetWidth}x{view.TargetHeight}");

                Camera camera = MakeCamera(scene, view);
                MakeLight(scene);
                var target = new RenderTexture(view.TargetWidth, view.TargetHeight, 24, RenderTextureFormat.ARGBHalf,
                    RenderTextureReadWrite.Linear) { antiAliasing = 1 };
                temporary.Add(target);
                camera.targetTexture = target;
                var readback = new Texture2D(view.TargetWidth, view.TargetHeight, TextureFormat.RGBAFloat, false, true);
                temporary.Add(readback);

                Directory.CreateDirectory(folder);
                var sprites = new Image8[Directions];
                for (int d = 0; d < Directions; d++)
                {
                    EditorUtility.DisplayProgressBar("Factorio avatar spike", $"direction {d + 1} of {Directions}", (float)d / Directions);
                    pivot.transform.rotation = Facing(d);
                    ImageF onBlack = RenderOnce(scene, camera, readback, Color.black);
                    ImageF onWhite = RenderOnce(scene, camera, readback, Color.white);
                    ImageF matte = Matting.FromBlackAndWhite(onBlack, onWhite, linear);
                    sprites[d] = ImageConvert.ToImage8(Downsample.Box(matte, Supersampling));
                    File.WriteAllBytes(Path.Combine(folder, $"direction-{d + 1}.png"), PngWriter.Encode(sprites[d]));
                    if (d == SouthDirection)
                    {
                        File.WriteAllBytes(Path.Combine(folder, "south-on-black.png"), PngWriter.Encode(Opaque(onBlack, linear)));
                        File.WriteAllBytes(Path.Combine(folder, "south-on-white.png"), PngWriter.Encode(Opaque(onWhite, linear)));
                    }
                }
                Image8 strip = Strip(sprites);
                File.WriteAllBytes(Path.Combine(folder, "strip.png"), PngWriter.Encode(strip));
                File.WriteAllBytes(Path.Combine(folder, "strip-on-grey.png"), PngWriter.Encode(OnGrey(strip)));
                report.AppendLine($"rendered {2 * Directions} images in {clock.Elapsed.TotalSeconds:0.0} s");
                File.WriteAllText(Path.Combine(folder, "report.txt"), report.ToString());
                return report.ToString();
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = asyncCompilation;
                EditorSceneManager.ClosePreviewScene(scene);
                foreach (Object o in temporary)
                {
                    Object.DestroyImmediate(o);
                }
            }
        }

        /// <summary>Sheet row <paramref name="direction"/>: north first, clockwise seen from above.</summary>
        private static Quaternion Facing(int direction) => Quaternion.Euler(0, 360f * direction / Directions, 0);

        /// <summary>
        /// Removes every script (VRChat descriptors, PhysBones, constraints and other tools' components) and
        /// every EditorOnly object, so only meshes, bones and materials remain, and makes skinned meshes follow
        /// their bones exactly at every render.
        /// </summary>
        private static void Strip(GameObject avatar, StringBuilder report)
        {
            foreach (Transform t in avatar.GetComponentsInChildren<Transform>(true).ToArray())
            {
                if (t != null && t != avatar.transform && t.CompareTag("EditorOnly"))
                {
                    Object.DestroyImmediate(t.gameObject);
                }
            }
            int missing = 0;
            foreach (Transform t in avatar.GetComponentsInChildren<Transform>(true))
            {
                missing += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            }
            var removed = new SortedDictionary<string, int>();
            // a component another one requires can only go after it, so repeat until nothing changes
            for (bool changed = true; changed;)
            {
                changed = false;
                foreach (MonoBehaviour script in avatar.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (script == null || IsRequiredByAnother(script))
                    {
                        continue;
                    }
                    string name = script.GetType().Name;
                    Object.DestroyImmediate(script);
                    if (script == null) // Unity refuses, with a Console error, when something else still needs it
                    {
                        removed[name] = removed.TryGetValue(name, out int n) ? n + 1 : 1;
                        changed = true;
                    }
                }
            }
            foreach (Animator animator in avatar.GetComponentsInChildren<Animator>(true))
            {
                animator.enabled = false;
            }
            foreach (SkinnedMeshRenderer skinned in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                skinned.updateWhenOffscreen = true;
                skinned.forceMatrixRecalculationPerRender = true;
            }
            report.AppendLine("removed scripts: " + (removed.Count == 0 ? "none" :
                string.Join(", ", removed.Select(r => r.Value == 1 ? r.Key : $"{r.Key} x{r.Value}"))) +
                (missing > 0 ? $", {missing} missing scripts" : ""));
        }

        private static bool IsRequiredByAnother(Component component)
        {
            Type type = component.GetType();
            foreach (Component other in component.GetComponents<Component>())
            {
                if (other == null || other == component)
                {
                    continue;
                }
                foreach (RequireComponent require in other.GetType().GetCustomAttributes(typeof(RequireComponent), true))
                {
                    if (Requires(require.m_Type0, type) || Requires(require.m_Type1, type) || Requires(require.m_Type2, type))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool Requires(Type required, Type type) => required != null && required.IsAssignableFrom(type);

        /// <summary>
        /// The vertices of every enabled renderer, in the avatar's parent space, meters: skinned meshes as
        /// posed now, so the canvas fits what is drawn.
        /// </summary>
        private static List<Vector3> MeshPoints(GameObject avatar, List<Object> temporary)
        {
            var points = new List<Vector3>();
            Matrix4x4 toParent = avatar.transform.parent.worldToLocalMatrix;
            var baked = new Mesh();
            temporary.Add(baked);
            foreach (Renderer renderer in avatar.GetComponentsInChildren<Renderer>(false))
            {
                if (!renderer.enabled)
                {
                    continue;
                }
                Mesh mesh;
                Matrix4x4 toWorld;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    if (skinned.sharedMesh == null)
                    {
                        continue;
                    }
                    skinned.BakeMesh(baked, true);
                    mesh = baked;
                    toWorld = Matrix4x4.TRS(skinned.transform.position, skinned.transform.rotation, Vector3.one);
                }
                else if (renderer is MeshRenderer)
                {
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null)
                    {
                        continue;
                    }
                    mesh = filter.sharedMesh;
                    toWorld = renderer.transform.localToWorldMatrix;
                }
                else
                {
                    continue; // particles, lines and trails are not measured in the spike
                }
                foreach (Vector3 v in mesh.vertices)
                {
                    points.Add(toParent.MultiplyPoint3x4(toWorld.MultiplyPoint3x4(v)));
                }
            }
            return points;
        }

        private static Vector3 HipsOrCentre(GameObject avatar, List<Vector3> points)
        {
            Animator animator = avatar.GetComponentInChildren<Animator>(true);
            if (animator != null && animator.isHuman)
            {
                Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                if (hips != null)
                {
                    return avatar.transform.parent.InverseTransformPoint(hips.position);
                }
            }
            return new Vector3((points.Min(p => p.x) + points.Max(p => p.x)) / 2, 0, (points.Min(p => p.z) + points.Max(p => p.z)) / 2);
        }

        private static Camera MakeCamera(Scene scene, UnityCameraSetup view)
        {
            var go = new GameObject("Sprite camera");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.SetPositionAndRotation(
                new Vector3((float)view.Position.x, (float)view.Position.y, (float)view.Position.z),
                new Quaternion((float)view.Rotation.x, (float)view.Rotation.y, (float)view.Rotation.z, (float)view.Rotation.w));
            Camera camera = go.AddComponent<Camera>();
            camera.enabled = false;
            camera.scene = scene;
            camera.cameraType = CameraType.Preview;
            camera.orthographic = true;
            camera.orthographicSize = (float)view.HalfHeight;
            camera.aspect = (float)(view.HalfWidth / view.HalfHeight);
            camera.nearClipPlane = (float)view.Near;
            camera.farClipPlane = (float)view.Far;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.allowHDR = true;
            camera.allowMSAA = false;
            camera.useOcclusionCulling = false;
            camera.depthTextureMode = DepthTextureMode.Depth; // for shader effects that read depth (Poiyomi depth rim, soft fades)
            return camera;
        }

        /// <summary>The key light of the dragon renderer (Conventions.ShadeLight), without shadows.</summary>
        private static void MakeLight(Scene scene)
        {
            var go = new GameObject("Key light");
            SceneManager.MoveGameObjectToScene(go, scene);
            (double x, double y, double z) toLight = UnitySpace.ToUnity(Conventions.ShadeLight);
            go.transform.rotation = Quaternion.LookRotation(-new Vector3((float)toLight.x, (float)toLight.y, (float)toLight.z));
            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = LightIntensity;
            light.shadows = LightShadows.None;
        }

        /// <summary>Renders over a solid background and reads the result back with row 0 at the top.</summary>
        private static ImageF RenderOnce(Scene scene, Camera camera, Texture2D readback, Color background)
        {
            camera.backgroundColor = background;
            Unsupported.SetOverrideLightingSettings(scene);
            try
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = Ambient;
                camera.Render();
                // should asynchronous compilation still have kicked in, wait for it and draw again
                for (int tries = 0; ShaderUtil.anythingCompiling && tries < 600; tries++)
                {
                    System.Threading.Thread.Sleep(50);
                    if (!ShaderUtil.anythingCompiling)
                    {
                        camera.Render();
                    }
                }
            }
            finally
            {
                Unsupported.RestoreOverrideLightingSettings();
            }
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = camera.targetTexture;
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

        /// <summary>A raw render as an opaque 8-bit sRGB image, for looking at.</summary>
        private static Image8 Opaque(ImageF image, bool linear)
        {
            var result = new Image8(image.Width, image.Height);
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        double v = image[x, y, c];
                        result[x, y, c] = ImageConvert.Quantize(linear ? CoreColorSpace.LinearToSrgb(CoreColorSpace.Clamp01(v)) : v);
                    }
                    result[x, y, 3] = 255;
                }
            }
            return result;
        }

        private static Image8 Strip(Image8[] sprites)
        {
            int w = sprites[0].Width, h = sprites[0].Height;
            var strip = new Image8(w * sprites.Length, h);
            for (int i = 0; i < sprites.Length; i++)
            {
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        for (int c = 0; c < 4; c++)
                        {
                            strip[i * w + x, y, c] = sprites[i][x, y, c];
                        }
                    }
                }
            }
            return strip;
        }

        /// <summary>The sprites over mid grey, roughly as on Factorio's ground, to spot fringes and halos.</summary>
        private static Image8 OnGrey(Image8 image)
        {
            const double grey = 0.36;
            var result = new Image8(image.Width, image.Height);
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    double a = image[x, y, 3] / 255.0;
                    for (int c = 0; c < 3; c++)
                    {
                        result[x, y, c] = ImageConvert.Quantize(image[x, y, c] / 255.0 * a + grey * (1 - a));
                    }
                    result[x, y, 3] = 255;
                }
            }
            return result;
        }

        private static string SafeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }
    }
}
