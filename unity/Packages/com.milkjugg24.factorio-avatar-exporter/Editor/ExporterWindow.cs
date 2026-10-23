using System;
using System.IO;
using System.Linq;
using System.Threading;
using MilkJugg24.FactorioAvatarExporter.Core;
using UnityEditor;
using UnityEngine;
using AnimationInfo = MilkJugg24.FactorioAvatarExporter.Core.AnimationInfo;

namespace MilkJugg24.FactorioAvatarExporter.Editor
{
    /// <summary>
    /// Tools > Factorio Avatar Exporter > Exporter: loads an avatar, reports on its rig, previews it posed in
    /// every animation, direction and frame of an avatar pack, and exports the pack (Export All).
    /// </summary>
    public sealed class ExporterWindow : EditorWindow
    {
        private const double FramesPerSecond = 12;
        private static readonly Color Ground = new Color(0.36f, 0.36f, 0.36f);
        private static readonly Color Anchor = new Color(1f, 0.85f, 0.2f);

        [SerializeField] private GameObject source;
        [SerializeField] private int animationIndex;
        [SerializeField] private int row = 4;
        [SerializeField] private int frame;
        [SerializeField] private int zoom = 2;

        // the pack
        [SerializeField] private string packFor;
        [SerializeField] private string packTitle;
        [SerializeField] private string packId;
        [SerializeField] private string packAuthor;
        [SerializeField] private string packVersion = PackContent.DefaultVersion;
        [SerializeField] private int pixelsPerTile = Conventions.DefaultPixelsPerTile;
        [SerializeField] private bool flight = true;
        [SerializeField] private string saveFolder;
        [SerializeField] private bool permission;
        [SerializeField] private string permissionNote;
        private string exported;

        private PosePreview preview;
        private string error;
        private bool playing;
        private double nextFrameAt;
        private Texture2D texture;
        private PreviewFrame shown;
        private Vector2 scroll;

        [MenuItem("Tools/Factorio Avatar Exporter/Exporter")]
        public static void Open()
        {
            ExporterWindow window = GetWindow<ExporterWindow>("Factorio Avatar Exporter");
            if (window.source == null && Selection.activeGameObject != null)
            {
                window.source = Selection.activeGameObject;
            }
        }

        private void OnDisable()
        {
            Unload();
        }

        private void Unload()
        {
            playing = false;
            preview?.Dispose();
            preview = null;
            shown = null;
            if (texture != null)
            {
                DestroyImmediate(texture);
                texture = null;
            }
        }

        private void Load()
        {
            Unload();
            error = null;
            try
            {
                EditorUtility.DisplayProgressBar("Factorio Avatar Exporter", "Loading " + source.name, 0.5f);
                preview = PosePreview.Load(source);
                if (packFor != source.name)
                {
                    // another avatar: its own name, id and permission
                    packFor = source.name;
                    packTitle = source.name;
                    packId = "";
                    permission = false;
                    permissionNote = "";
                }
                exported = null;
                Rerender();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                error = e.Message;
                Unload();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Avatar", EditorStyles.boldLabel);
            source = (GameObject)EditorGUILayout.ObjectField("Avatar", source, typeof(GameObject), true);
            using (new EditorGUI.DisabledScope(source == null))
            {
                if (GUILayout.Button(preview == null ? "Load avatar" : "Reload avatar"))
                {
                    Load();
                }
            }
            if (error != null)
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }
            if (preview == null)
            {
                EditorGUILayout.HelpBox("Pick your avatar (the object with the VRChat avatar descriptor, or its prefab) " +
                    "and press Load avatar. A copy is made in a hidden scene; your scene and avatar are not changed.",
                    MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }
            foreach (string note in preview.Notes)
            {
                EditorGUILayout.LabelField(note, EditorStyles.wordWrappedMiniLabel);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            string[] names = AnimationSpec.All.Select(a => a.Name).ToArray();
            EditorGUI.BeginChangeCheck();
            animationIndex = EditorGUILayout.Popup("Animation", Mathf.Clamp(animationIndex, 0, names.Length - 1), names);
            AnimationInfo info = AnimationSpec.All[animationIndex];
            row = EditorGUILayout.IntSlider("Direction", Mathf.Clamp(row, 0, info.DirectionCount - 1), 0, info.DirectionCount - 1);
            EditorGUILayout.LabelField(" ", PosePreview.DirectionName(info.Name, row));
            frame = EditorGUILayout.IntSlider("Frame", Mathf.Clamp(frame, 0, info.FrameCount - 1), 0, info.FrameCount - 1);
            zoom = EditorGUILayout.IntSlider("Zoom", zoom, 1, 4);
            bool changed = EditorGUI.EndChangeCheck();
            playing = GUILayout.Toggle(playing, playing ? "Pause" : "Play", "Button");
            if (changed)
            {
                Rerender();
            }

            if (texture != null && shown != null)
            {
                Rect rect = GUILayoutUtility.GetRect(texture.width * zoom, texture.height * zoom,
                    GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
                EditorGUI.DrawRect(rect, Ground);
                GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, true);
                // the entity position: where the character stands in the game
                var anchor = new Vector2(rect.x + shown.Canvas.AnchorX * zoom, rect.y + shown.Canvas.AnchorY * zoom);
                EditorGUI.DrawRect(new Rect(anchor.x - 4, anchor.y, 9, 1), Anchor);
                EditorGUI.DrawRect(new Rect(anchor.x, anchor.y - 4, 1, 9), Anchor);
                string check = double.IsNaN(shown.JointErrorPixels)
                    ? "pose check: not done for this pose"
                    : $"pose check: bones within {shown.JointErrorPixels:0.0} px of where Core put them" +
                      (shown.JointErrorPixels > 1 ? " (more than 1 px: please report)" : "");
                EditorGUILayout.LabelField($"{texture.width} x {texture.height} px; {check}", EditorStyles.wordWrappedMiniLabel);
                if (preview.Retakes > 0 || preview.PlaceholderLeft > 0 || preview.Growths > 0)
                {
                    EditorGUILayout.LabelField($"retaken while shaders compiled: {preview.Retakes}; placeholder pixels " +
                        $"left: {preview.PlaceholderLeft}; canvas grown to fit: {preview.Growths} times",
                        EditorStyles.wordWrappedMiniLabel);
                }
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Export Selected Animation"))
            {
                SaveSheet(info.Name);
            }

            EditorGUILayout.Space();
            PackGUI();
            EditorGUILayout.EndScrollView();
        }

        private static readonly string[] Resolutions = { "64 pixels per tile (sharp)", "32 pixels per tile (a quarter of the memory)" };
        private static readonly int[] ResolutionValues = { 64, 32 };

        /// <summary>The avatar pack's settings and the Export All button.</summary>
        private void PackGUI()
        {
            EditorGUILayout.LabelField("Avatar pack", EditorStyles.boldLabel);
            packTitle = EditorGUILayout.TextField(new GUIContent("Name", "Shown in the game's avatar window."), packTitle);
            string derived = PackNaming.DeriveId(packTitle ?? "");
            string id = EditorGUILayout.TextField(new GUIContent("Pack id", "Lowercase letters, digits and -. The mod is " +
                "called avatar-pack-<id>; keep it the same when you export a new version."), string.IsNullOrEmpty(packId) ? derived : packId);
            packId = id == derived ? "" : id;
            packAuthor = EditorGUILayout.TextField(new GUIContent("Author (optional)", "Your name, shown with the pack."), packAuthor);
            packVersion = EditorGUILayout.TextField(new GUIContent("Version", "Raise it (1.0.1, 1.1.0, ...) when you export " +
                "the pack again for a server: every player needs the same version."), packVersion);
            pixelsPerTile = EditorGUILayout.IntPopup("Resolution", pixelsPerTile, Resolutions, ResolutionValues);
            flight = EditorGUILayout.Toggle(new GUIContent("Flight animations", "Hovering and flying with Space Age's mech armor. " +
                "Without them the avatar walks in the air."), flight);

            permission = EditorGUILayout.ToggleLeft("I made this avatar, or its author allows me to use it this way", permission);
            EditorGUILayout.LabelField("Note for the pack's README (optional): the avatar's license, or who gave permission",
                EditorStyles.wordWrappedMiniLabel);
            permissionNote = EditorGUILayout.TextArea(permissionNote ?? "", GUILayout.MinHeight(36));
            EditorGUILayout.HelpBox("Many avatars may only be used in VRChat. Only share a pack with others when the avatar's " +
                "author allows it. In multiplayer, every player needs the pack too.", MessageType.None);

            PackOptions options = Options();
            string problem = permission ? options.Problem() : "Tick the permission box to export.";
            using (new EditorGUI.DisabledScope(problem != null))
            {
                if (GUILayout.Button(new GUIContent("Export All", "Asks where to save the avatar pack (a Factorio mod zip), " +
                    "then renders every animation with its shadow. Takes a few minutes."), GUILayout.Height(28)))
                {
                    if (ChooseZip(options))
                    {
                        ExportAll(options);
                    }
                }
            }
            if (problem != null)
            {
                EditorGUILayout.LabelField(problem, EditorStyles.wordWrappedMiniLabel);
            }
            if (exported != null)
            {
                EditorGUILayout.HelpBox(exported, MessageType.Info);
            }
        }

        /// <summary>
        /// Asks where to save the pack, under the file name Factorio expects for a mod
        /// (avatar-pack-&lt;id&gt;_&lt;version&gt;.zip). False when cancelled.
        /// </summary>
        private bool ChooseZip(PackOptions options)
        {
            string name = PackNaming.ZipFileName(options.Id, options.Version);
            string folder = !string.IsNullOrEmpty(saveFolder) && Directory.Exists(saveFolder)
                ? saveFolder
                : Path.GetDirectoryName(Application.dataPath);
            string path = EditorUtility.SaveFilePanel("Save the avatar pack", folder, name, "zip");
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            if (Path.GetFileName(path) != name)
            {
                if (!EditorUtility.DisplayDialog("Keep the pack's file name",
                    "Factorio expects a mod's zip to be named after the mod and its version, so this pack must be called " +
                    name + ". Save it under that name in the folder you chose?", "Save as " + name, "Cancel"))
                {
                    return false;
                }
                path = Path.Combine(Path.GetDirectoryName(path), name);
            }
            saveFolder = Path.GetDirectoryName(path);
            options.ZipPath = path;
            return true;
        }

        private PackOptions Options() => new PackOptions
        {
            Title = packTitle,
            Id = string.IsNullOrEmpty(packId) ? PackNaming.DeriveId(packTitle ?? "") : packId,
            Author = packAuthor,
            Version = packVersion,
            PixelsPerTile = pixelsPerTile,
            Flight = flight,
            Confirmed = permission,
            PermissionNote = permissionNote,
            PreviewFolder = PreviewFolder,
        };

        private void ExportAll(PackOptions options)
        {
            playing = false;
            exported = null;
            var cancel = new CancellationTokenSource();
            try
            {
                PackExportResult result = preview.ExportPack(options, p =>
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Factorio Avatar Exporter",
                        $"Rendering {p.Animation} ({p.Done} of {p.Total})", (float)p.Fraction))
                    {
                        cancel.Cancel();
                    }
                }, cancel.Token);
                exported = $"Saved {result.ZipPath}. Put it, still zipped, in Factorio's mods folder together with the " +
                    "Avatar Characters mod, start Factorio and choose " + $"\"{options.Title.Trim()}\" in the avatar window " +
                    "(shortcut bar, or Ctrl + Shift + A). README.md in Assets/FactorioAvatarExporter has every step. " +
                    $"A report and preview images are in {options.PreviewFolder}.";
                Debug.Log("Factorio Avatar Exporter:\n" + result.Report);
                EditorUtility.RevealInFinder(result.ZipPath);
            }
            catch (OperationCanceledException)
            {
                Debug.Log("Factorio Avatar Exporter: export cancelled; nothing was written");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                error = e.Message;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                cancel.Dispose();
            }
        }

        private void Update()
        {
            if (!playing || preview == null || EditorApplication.timeSinceStartup < nextFrameAt)
            {
                return;
            }
            nextFrameAt = EditorApplication.timeSinceStartup + 1 / FramesPerSecond;
            frame = (frame + 1) % AnimationSpec.All[animationIndex].FrameCount;
            Rerender();
            Repaint();
        }

        private void Rerender()
        {
            if (preview == null)
            {
                return;
            }
            try
            {
                shown = preview.Render(AnimationSpec.All[animationIndex].Name, row, frame);
                texture = ToTexture(shown.Image, texture);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                error = e.Message;
                playing = false;
            }
        }

        private string PreviewFolder => Path.Combine(Path.GetDirectoryName(Application.dataPath), "FactorioAvatarExport",
            preview.AvatarName, "preview");

        private void SaveSheet(string animation)
        {
            playing = false;
            try
            {
                Image8 sheet = preview.Sheet(animation, out double worst,
                    p => EditorUtility.DisplayProgressBar("Factorio Avatar Exporter", "Rendering " + animation, p));
                string folder = PreviewFolder;
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, animation + ".png");
                File.WriteAllBytes(path, PngWriter.Encode(sheet));
                Debug.Log($"Factorio Avatar Exporter: wrote {path} (a row per direction; bones within {worst:0.0} px of Core's joints)");
                EditorUtility.RevealInFinder(path);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                error = e.Message;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>A Core image as a texture to draw (Unity textures have row 0 at the bottom).</summary>
        private static Texture2D ToTexture(Image8 image, Texture2D reuse)
        {
            Texture2D texture = reuse;
            if (texture == null || texture.width != image.Width || texture.height != image.Height)
            {
                if (texture != null)
                {
                    DestroyImmediate(texture);
                }
                texture = new Texture2D(image.Width, image.Height, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }
            var pixels = new Color32[image.Width * image.Height];
            for (int y = 0; y < image.Height; y++)
            {
                int row = (image.Height - 1 - y) * image.Width;
                for (int x = 0; x < image.Width; x++)
                {
                    pixels[row + x] = new Color32(image[x, y, 0], image[x, y, 1], image[x, y, 2], image[x, y, 3]);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
