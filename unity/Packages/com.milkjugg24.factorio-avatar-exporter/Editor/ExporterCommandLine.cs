using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using MilkJugg24.FactorioAvatarExporter.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using AnimationInfo = MilkJugg24.FactorioAvatarExporter.Core.AnimationInfo;

namespace MilkJugg24.FactorioAvatarExporter.Editor
{
    /// <summary>The exporter in Unity's batch mode, for tests/run_unity_spike.ps1.</summary>
    public static class ExporterCommandLine
    {
        /// <summary>
        /// <c>Unity -batchmode -projectPath &lt;project&gt; -executeMethod
        /// MilkJugg24.FactorioAvatarExporter.Editor.ExporterCommandLine.RenderPoses [-spikeScene &lt;scene&gt;]
        /// [-spikeAvatar &lt;prefab or object name&gt;] [-spikeOut &lt;folder&gt;]</c>: for each avatar (as the
        /// rendering spike picks them), renders every animation posed by Core, a row per direction, into
        /// &lt;folder&gt;/&lt;avatar&gt;/&lt;animation&gt;.png with report.txt, writes summary.txt and exits Unity
        /// with 1 when anything failed.
        /// </summary>
        public static void RenderPoses()
        {
            string[] args = Environment.GetCommandLineArgs();
            string Arg(string name)
            {
                int i = Array.IndexOf(args, name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }
            string output = Arg("-spikeOut") ?? Path.Combine(Path.GetDirectoryName(Application.dataPath), "FactorioAvatarExport");
            var summary = new StringBuilder();
            int failed = 0;
            try
            {
                foreach (GameObject source in FindAvatars(Arg("-spikeScene"), Arg("-spikeAvatar")))
                {
                    summary.AppendLine("== " + source.name);
                    try
                    {
                        summary.Append(PreviewAvatar(source, Path.Combine(output, source.name)));
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
            Debug.Log("Factorio Avatar Exporter:\n" + summary);
            EditorApplication.Exit(failed == 0 ? 0 : 1);
        }

        private static string PreviewAvatar(GameObject source, string folder)
        {
            var clock = Stopwatch.StartNew();
            var report = new StringBuilder();
            Directory.CreateDirectory(folder);
            using (PosePreview preview = PosePreview.Load(source))
            {
                foreach (string note in preview.Notes)
                {
                    report.AppendLine(note);
                }
                foreach (AnimationInfo info in AnimationSpec.All)
                {
                    Image8 sheet = preview.Sheet(info.Name, out double worst);
                    File.WriteAllBytes(Path.Combine(folder, info.Name + ".png"), PngWriter.Encode(sheet));
                    report.AppendLine($"{info.Name}: {info.DirectionCount} directions x {info.FrameCount} frames of " +
                        $"{sheet.Width / info.FrameCount}x{sheet.Height / info.DirectionCount} px; bones within {worst:0.0} px of Core's joints");
                }
                report.AppendLine($"retaken while shaders compiled: {preview.Retakes}; placeholder pixels left: " +
                    $"{preview.PlaceholderLeft}; canvas grown to fit: {preview.Growths} times");
            }
            report.AppendLine($"done in {clock.Elapsed.TotalSeconds:0.0} s");
            File.WriteAllText(Path.Combine(folder, "report.txt"), report.ToString());
            return report.ToString();
        }

        /// <summary>A prefab, a named top-level object of a scene, or every active humanoid at a scene's top level.</summary>
        internal static List<GameObject> FindAvatars(string scenePath, string avatarName)
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
                    Animator animator = root.activeInHierarchy ? root.GetComponent<Animator>() : null;
                    bool humanoid = animator != null && animator.avatar != null && animator.avatar.isHuman;
                    if (avatarName != null ? root.name == avatarName : humanoid)
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
            return sources;
        }
    }
}
