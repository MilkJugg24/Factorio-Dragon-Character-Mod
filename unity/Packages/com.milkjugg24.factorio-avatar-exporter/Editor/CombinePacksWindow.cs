using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MilkJugg24.FactorioAvatarExporter.Core;
using UnityEditor;
using UnityEngine;

namespace MilkJugg24.FactorioAvatarExporter.Editor
{
    /// <summary>
    /// Tools > Factorio Avatar Exporter > Combine Packs for a Server: for whoever runs a server. Combines the
    /// avatar packs the players exported (and earlier bundles) into one avatar bundle, a single mod zip every
    /// player and the server install (<see cref="PackBundler"/>).
    /// </summary>
    public sealed class CombinePacksWindow : EditorWindow
    {
        [SerializeField] private List<string> inputs = new List<string>();
        [SerializeField] private List<string> leftOut = new List<string>();
        [SerializeField] private string bundleName = "My server";
        [SerializeField] private string bundleVersion = PackContent.DefaultVersion;
        [SerializeField] private string author = PackContent.DefaultAuthor;
        [SerializeField] private string lastFolder;

        private List<(string path, BundleInput input, string error)> read;
        private List<BundleCharacter> merged = new List<BundleCharacter>();
        private readonly List<string> notes = new List<string>();
        private string saved;
        private string error;
        private Vector2 scroll;

        [MenuItem("Tools/Factorio Avatar Exporter/Combine Packs for a Server")]
        public static void Open() => GetWindow<CombinePacksWindow>("Combine Avatar Packs");

        private string BundleId => PackNaming.DeriveId(bundleName ?? "");

        private void OnGUI()
        {
            if (read == null)
            {
                Reload();
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.HelpBox("For whoever runs the server: add the avatar packs your players exported " +
                "(avatar-pack-...zip), and the previous bundle if there is one, and combine them into one bundle. " +
                "Every player and the server then put that one zip in Factorio's mods folder.", MessageType.Info);

            EditorGUILayout.LabelField("Packs and bundles", EditorStyles.boldLabel);
            for (int i = 0; i < read.Count; i++)
            {
                (string path, BundleInput input, string unreadable) = read[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    string what = unreadable ?? (input.IsBundle
                        ? $"bundle {input.Version}: {string.Join(", ", input.Characters.Select(c => c.Id))}"
                        : $"{input.Characters[0].Title} {input.Version}");
                    EditorGUILayout.LabelField(new GUIContent(Path.GetFileName(path), path), new GUIContent(what, what));
                    if (GUILayout.Button("Remove", GUILayout.Width(70)))
                    {
                        inputs.RemoveAt(i);
                        Reload();
                        GUIUtility.ExitGUI();
                    }
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add a Pack or Bundle..."))
                {
                    string path = EditorUtility.OpenFilePanel("Add an avatar pack or bundle", StartFolder(), "zip");
                    AddInputs(string.IsNullOrEmpty(path) ? new string[0] : new[] { path });
                }
                if (GUILayout.Button("Add a Folder of Packs..."))
                {
                    string folder = EditorUtility.OpenFolderPanel("Add every pack and bundle in a folder", StartFolder(), "");
                    AddInputs(string.IsNullOrEmpty(folder) ? new string[0] : Directory.GetFiles(folder, "*.zip")
                        .Where(f => Path.GetFileName(f).StartsWith(PackFormat.ModPrefix, StringComparison.Ordinal) ||
                            Path.GetFileName(f).StartsWith(PackBundler.ModPrefix, StringComparison.Ordinal)).ToArray());
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Characters", EditorStyles.boldLabel);
            if (merged.Count == 0)
            {
                EditorGUILayout.LabelField("None yet.", EditorStyles.wordWrappedMiniLabel);
            }
            foreach (BundleCharacter c in merged)
            {
                bool include = !leftOut.Contains(c.Id);
                string label = $"{c.Title} ({c.Id} {c.Version})" + (string.IsNullOrEmpty(c.Author) ? "" : " by " + c.Author);
                bool now = EditorGUILayout.ToggleLeft(new GUIContent(label, "from " + c.Source), include);
                if (now != include)
                {
                    if (now)
                    {
                        leftOut.Remove(c.Id);
                    }
                    else
                    {
                        leftOut.Add(c.Id);
                    }
                }
            }
            foreach (string note in notes)
            {
                EditorGUILayout.LabelField(note, EditorStyles.wordWrappedMiniLabel);
            }
            List<string> dropped = DroppedCharacters();
            if (dropped.Count > 0)
            {
                EditorGUILayout.HelpBox("Left out of the new bundle: " + string.Join(", ", dropped) + ". Before the " +
                    "server loads the new bundle, an admin must run " + string.Join(", ", dropped.Select(id => "/avatar-revert-all " + id)) +
                    " in the game, or the characters using them are deleted together with their items.", MessageType.Warning);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Bundle", EditorStyles.boldLabel);
            bundleName = EditorGUILayout.TextField(new GUIContent("Name", "Shown in Factorio's mod list. Keep it the " +
                "same for every new version of your server's bundle."), bundleName);
            EditorGUILayout.LabelField(" ", "mod: " + PackBundler.ModName(BundleId), EditorStyles.miniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                bundleVersion = EditorGUILayout.TextField(new GUIContent("Version", "Raise it for every new bundle: " +
                    "players with an older one can't join until they have the new one."), bundleVersion);
                string previous = PreviousVersion();
                if (previous != null && GUILayout.Button("After " + previous, GUILayout.Width(110)))
                {
                    bundleVersion = PackBundler.NextVersion(previous);
                    GUI.FocusControl(null);
                }
            }
            author = EditorGUILayout.TextField(new GUIContent("Author", "The name shown with the bundle. Factorio " +
                "needs one for every mod; any name will do."), author);

            List<BundleCharacter> chosen = Chosen();
            string problem = chosen.Count == 0 ? "Add at least one pack." : Problem();
            using (new EditorGUI.DisabledScope(problem != null))
            {
                if (GUILayout.Button("Combine...", GUILayout.Height(28)))
                {
                    Combine(chosen);
                }
            }
            if (problem != null)
            {
                EditorGUILayout.LabelField(problem, EditorStyles.wordWrappedMiniLabel);
            }
            if (error != null)
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }
            if (saved != null)
            {
                EditorGUILayout.HelpBox(saved, MessageType.Info);
            }
            EditorGUILayout.EndScrollView();
        }

        private string StartFolder() =>
            !string.IsNullOrEmpty(lastFolder) && Directory.Exists(lastFolder) ? lastFolder : Path.GetDirectoryName(Application.dataPath);

        private void AddInputs(IEnumerable<string> paths)
        {
            foreach (string path in paths)
            {
                if (!inputs.Contains(path))
                {
                    inputs.Add(path);
                }
                lastFolder = Path.GetDirectoryName(path);
            }
            Reload();
        }

        /// <summary>Reads every input again and merges their characters.</summary>
        private void Reload()
        {
            read = new List<(string, BundleInput, string)>();
            foreach (string path in inputs)
            {
                try
                {
                    using (FileStream stream = File.OpenRead(path))
                    {
                        read.Add((path, PackBundler.Read(stream, Path.GetFileName(path)), null));
                    }
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    read.Add((path, null, "can't be read: " + e.Message));
                }
            }
            notes.Clear();
            merged = PackBundler.Merge(read.Where(r => r.input != null).Select(r => r.input), notes);
            leftOut.RemoveAll(id => merged.All(c => c.Id != id));
            string previous = PreviousVersion();
            if (previous != null && PackBundler.CompareVersions(bundleVersion, previous) <= 0)
            {
                bundleVersion = PackBundler.NextVersion(previous);
            }
            saved = null;
            error = null;
            Repaint();
        }

        private List<BundleCharacter> Chosen() => merged.Where(c => !leftOut.Contains(c.Id)).ToList();

        /// <summary>The newest version of this bundle among the inputs, or null.</summary>
        private string PreviousVersion() => read?
            .Where(r => r.input != null && r.input.ModName == PackBundler.ModName(BundleId))
            .Select(r => r.input.Version)
            .OrderByDescending(v => v, Comparer<string>.Create(PackBundler.CompareVersions))
            .FirstOrDefault();

        /// <summary>Characters an earlier version of this bundle had that the new one won't.</summary>
        private List<string> DroppedCharacters()
        {
            var before = new HashSet<string>(read
                .Where(r => r.input != null && r.input.ModName == PackBundler.ModName(BundleId))
                .SelectMany(r => r.input.Characters.Select(c => c.Id)));
            return Chosen().Aggregate(before, (left, c) => { left.Remove(c.Id); return left; })
                .OrderBy(id => id, StringComparer.Ordinal).ToList();
        }

        private string Problem()
        {
            int length = PackNaming.TitleLength(bundleName ?? "");
            if (length == 0 || length > PackFormat.MaxTitleLength)
            {
                return "The name must be 1 to 80 characters.";
            }
            if (!PackNaming.IsValidVersion(bundleVersion))
            {
                return "The version must look like 1.0.0.";
            }
            if (string.IsNullOrWhiteSpace(author))
            {
                return "Enter an author (any name): Factorio needs one for every mod.";
            }
            string previous = PreviousVersion();
            if (previous != null && PackBundler.CompareVersions(bundleVersion, previous) <= 0)
            {
                return $"The version must be newer than the bundle's previous {previous}.";
            }
            return null;
        }

        private void Combine(List<BundleCharacter> characters)
        {
            string name = PackBundler.ZipFileName(BundleId, bundleVersion);
            string path = EditorUtility.SaveFilePanel("Save the avatar bundle", StartFolder(), name, "zip");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            if (Path.GetFileName(path) != name)
            {
                if (!EditorUtility.DisplayDialog("Keep the bundle's file name",
                    "Factorio expects a mod's zip to be named after the mod and its version, so this bundle must be " +
                    "called " + name + ". Save it under that name in the folder you chose?", "Save as " + name, "Cancel"))
                {
                    return;
                }
                path = Path.Combine(Path.GetDirectoryName(path), name);
            }
            lastFolder = Path.GetDirectoryName(path);
            try
            {
                var info = new BundleInfo
                {
                    Id = BundleId,
                    Title = bundleName.Trim(),
                    Version = bundleVersion,
                    Author = string.IsNullOrWhiteSpace(author) ? null : author.Trim(),
                    GeneratorVersion = ExporterInfo.Version,
                };
                string partial = path + ".partial";
                using (FileStream stream = File.Create(partial))
                {
                    PackBundler.Write(stream, info, characters);
                }
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                File.Move(partial, path);
                saved = $"Saved {path} with {characters.Count} characters. Give it to every player and put it on the " +
                    "server: everyone puts it, still zipped, in Factorio's mods folder next to the Avatar Characters mod, " +
                    "and removes older versions of this bundle and the separate packs it holds.";
                error = null;
                Debug.Log("Factorio Avatar Exporter: " + saved);
                EditorUtility.RevealInFinder(path);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                error = e.Message;
            }
        }
    }
}
