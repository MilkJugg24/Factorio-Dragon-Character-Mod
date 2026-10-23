using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using MilkJugg24.FactorioAvatarExporter.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

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
        public static void RenderPoses() => ForEachAvatar(PreviewAvatar);

        /// <summary>
        /// <c>... -executeMethod MilkJugg24.FactorioAvatarExporter.Editor.ExporterCommandLine.ExportPacks</c>, with the
        /// same arguments as <see cref="RenderPoses"/>: exports each avatar as an avatar pack, the zip, report.txt
        /// and a preview sheet per animation in &lt;folder&gt;/&lt;avatar&gt;/. For testing: the pack's README
        /// says so instead of a permission.
        /// </summary>
        public static void ExportPacks() => ForEachAvatar(ExportPack);

        /// <summary>Runs <paramref name="export"/>(avatar, its folder) for every avatar the arguments name.</summary>
        private static void ForEachAvatar(Func<GameObject, string, string> export)
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
                        summary.Append(export(source, Path.Combine(output, source.name)));
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
            using (PosePreview preview = PosePreview.Load(source))
            {
                return preview.SaveAllSheets(folder);
            }
        }

        private static string ExportPack(GameObject source, string folder)
        {
            using (PosePreview preview = PosePreview.Load(source))
            {
                var options = new PackOptions
                {
                    Title = source.name,
                    Id = PackNaming.DeriveId(source.name),
                    Confirmed = false,
                    PermissionNote = "Exported by tests/run_unity_spike.ps1 to test the exporter; not for sharing.",
                    OutputFolder = folder,
                    PreviewFolder = folder,
                };
                return preview.ExportPack(options, null, CancellationToken.None).Report;
            }
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
