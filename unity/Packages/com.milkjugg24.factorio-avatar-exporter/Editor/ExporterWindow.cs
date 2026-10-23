using System;
using System.IO;
using System.Linq;
using MilkJugg24.FactorioAvatarExporter.Core;
using UnityEditor;
using UnityEngine;
using AnimationInfo = MilkJugg24.FactorioAvatarExporter.Core.AnimationInfo;

namespace MilkJugg24.FactorioAvatarExporter.Editor
{
    /// <summary>
    /// Tools > Factorio Avatar Exporter > Exporter: loads an avatar, reports on its rig and previews it posed in
    /// every animation, direction and frame of an avatar pack. (Writing the pack itself comes next.)
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
            if (GUILayout.Button("Save every frame of this animation as an image"))
            {
                SaveSheet(info.Name);
            }
            EditorGUILayout.EndScrollView();
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

        private void SaveSheet(string animation)
        {
            playing = false;
            try
            {
                Image8 sheet = preview.Sheet(animation, out double worst,
                    p => EditorUtility.DisplayProgressBar("Factorio Avatar Exporter", "Rendering " + animation, p));
                string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "FactorioAvatarExport",
                    preview.AvatarName, "preview");
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
