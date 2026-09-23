using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DesktopPetEditor
{
    /// <summary>
    /// Produces a read-only inventory of animation and facial assets that may be
    /// reusable by the desktop pet. The report is written under Library so it
    /// never becomes a shipping asset or dirties scenes/prefabs.
    /// </summary>
    public static class DesktopPetAssetInventory
    {
        private const string PrefabPath = "Assets/learn/MitaDreamer.prefab";
        private const string ReferenceClipPath = "Assets/AnimationClip/MitaDreamer.anim";
        private const string ReportFolder = "Library/DesktopPetAssetInventory";

        private static readonly string[] CuratedClipNames =
        {
            "Mita Yawns",
            "Mita Start Tired", "Mita Tired",
            "Mita Start Sleep", "Mita Sleep",
            "Mita Sleep Idle 1", "Mita Sleep Idle 2", "Mita Sleep Idle 3",
            "Mita Sleep WakeUp", "Mita Wakeup", "Mita WakeUp Idle",
            "Mita SitIdle Start HalfSleep", "Mita SitIdle HalfSleep",
            "Mita Stay StartSleep", "Mita Stay IdleSleep", "Mita Stay StopSleep",
            "Mita Repose 1>2", "Mita Repose 2>3", "Mita Repose 3>1",
            "Mita Sit NormalStart", "Mita Sit Normal", "Mita Sit Idle",
            "Mita Walk", "Mita WalkStop", "Mita Jump",
            "Mita Fall Start", "Mita Fall Idle",
            "Mita StartWall", "Mita IdleWall", "Mita StopWall",
            "Mita Inertion L Alt", "Mita Inertion M Alt", "Mita Inertion R Alt",
            "Mita TargetHead", "Mita TargetHead Idle", "Mita StopClick"
        };

        public static void GenerateReport()
        {
            Directory.CreateDirectory(ReportFolder);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var reference = AssetDatabase.LoadAssetAtPath<AnimationClip>(ReferenceClipPath);
            if (prefab == null) throw new FileNotFoundException("Desktop-pet source prefab was not found", PrefabPath);
            if (reference == null) throw new FileNotFoundException("Reference animation was not found", ReferenceClipPath);

            var referencePaths = BindingPaths(reference);
            var allClipPaths = AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets/AnimationClip" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var clipsByName = allClipPaths
                .Select(path => new { path, clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path) })
                .Where(item => item.clip != null)
                .GroupBy(item => item.clip.name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

            var blendShapes = ReadBlendShapes(prefab);
            var markdown = new StringBuilder();
            markdown.AppendLine("# Desktop-pet animation and expression inventory");
            markdown.AppendLine();
            markdown.AppendLine("Generated: " + DateTime.Now.ToString("O"));
            markdown.AppendLine("Source prefab: `" + PrefabPath + "`");
            markdown.AppendLine("Reference clip: `" + ReferenceClipPath + "`");
            markdown.AppendLine("AnimationClip assets under Assets/AnimationClip: " + allClipPaths.Length);
            markdown.AppendLine();
            markdown.AppendLine("Compatibility is based on binding-path coverage against the known working MitaDreamer clip. It is a candidate filter, not visual approval.");
            markdown.AppendLine();
            markdown.AppendLine("## Curated body/action candidates");
            markdown.AppendLine();
            markdown.AppendLine("| Clip | Seconds | Loop | Shared reference paths | Missing reference paths | Extra paths | Asset |");
            markdown.AppendLine("| --- | ---: | :---: | ---: | ---: | ---: | --- |");
            foreach (var name in CuratedClipNames)
            {
                if (!clipsByName.TryGetValue(name, out var candidates))
                {
                    markdown.AppendLine("| " + Escape(name) + " | - | - | - | - | - | not found |");
                    continue;
                }
                foreach (var item in candidates)
                {
                    var paths = BindingPaths(item.clip);
                    int shared = paths.Count(path => referencePaths.Contains(path));
                    int missing = referencePaths.Count(path => !paths.Contains(path));
                    int extra = paths.Count(path => !referencePaths.Contains(path));
                    markdown.Append("| ").Append(Escape(item.clip.name)).Append(" | ")
                        .Append(item.clip.length.ToString("0.###")).Append(" | ")
                        .Append(item.clip.isLooping ? "yes" : "no").Append(" | ")
                        .Append(shared).Append('/').Append(referencePaths.Count).Append(" | ")
                        .Append(missing).Append(" | ").Append(extra).Append(" | `")
                        .Append(item.path).AppendLine("` |");
                }
            }

            markdown.AppendLine();
            markdown.AppendLine("## Face mesh blend shapes");
            markdown.AppendLine();
            foreach (var renderer in blendShapes.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                markdown.AppendLine("### `" + renderer.Key + "`");
                markdown.AppendLine();
                markdown.AppendLine(renderer.Value.Count == 0 ? "No blend shapes." : string.Join(", ", renderer.Value.Select(value => "`" + value + "`")));
                markdown.AppendLine();
            }

            markdown.AppendLine("## Existing expression clips");
            markdown.AppendLine();
            markdown.AppendLine("| Clip | Seconds | Blend-shape curves | Shapes present on prefab | Asset |");
            markdown.AppendLine("| --- | ---: | ---: | ---: | --- |");
            var availableShapeNames = new HashSet<string>(blendShapes.SelectMany(pair => pair.Value), StringComparer.OrdinalIgnoreCase);
            foreach (var path in allClipPaths)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null || !clip.name.StartsWith("Mita E-", StringComparison.OrdinalIgnoreCase)) continue;
                var shapeNames = AnimationUtility.GetCurveBindings(clip)
                    .Where(binding => binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
                    .Select(binding => binding.propertyName.Substring("blendShape.".Length))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                int present = shapeNames.Count(availableShapeNames.Contains);
                markdown.Append("| ").Append(Escape(clip.name)).Append(" | ")
                    .Append(clip.length.ToString("0.###")).Append(" | ")
                    .Append(shapeNames.Length).Append(" | ").Append(present).Append('/').Append(shapeNames.Length)
                    .Append(" | `").Append(path).AppendLine("` |");
            }

            markdown.AppendLine();
            markdown.AppendLine("## Review boundary");
            markdown.AppendLine();
            markdown.AppendLine("- A high binding match only means the clip probably targets the same exported rig.");
            markdown.AppendLine("- Root position, camera-facing direction, prop dependencies, contacts, clothing penetration and character appropriateness still require preview review.");
            markdown.AppendLine("- Expression clips can reset many unused blend shapes; combine them through a single facial owner or an Animator layer/mask, not several competing writers.");

            var reportPath = ReportFolder + "/report.md";
            File.WriteAllText(reportPath, markdown.ToString(), new UTF8Encoding(false));
            File.WriteAllText(ReportFolder + "/status.txt", "OK inventory " + DateTime.Now.ToString("O"), new UTF8Encoding(false));
            Debug.Log("Desktop-pet asset inventory written to " + Path.GetFullPath(reportPath));
        }

        public static void RenderCandidatePreviews()
        {
            var names = new[]
            {
                "Mita Yawns",
                "Mita Start Tired", "Mita Tired",
                "Mita Start Sleep", "Mita Sleep", "Mita Sleep WakeUp", "Mita Wakeup",
                "Mita SitIdle Start HalfSleep", "Mita SitIdle HalfSleep",
                "Mita Stay StartSleep", "Mita Stay IdleSleep", "Mita Stay StopSleep",
                "Mita Repose 1>2", "Mita Repose 2>3", "Mita Repose 3>1",
                "Mita Walk", "Mita Jump", "Mita Fall Start", "Mita Fall Idle",
                "Mita StartWall", "Mita IdleWall", "Mita StopWall",
                "Mita E-HalfSleep", "Mita E-Sleep", "Mita E-Try",
                "Mita E-Suspicion", "Mita E-SurpriseO", "Mita E-Shy", "Mita E-Discontent"
            };
            var paths = AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets/AnimationClip" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var clipsByName = paths
                .Select(path => AssetDatabase.LoadAssetAtPath<AnimationClip>(path))
                .Where(clip => clip != null)
                .GroupBy(clip => clip.name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            var previewRoot = ReportFolder + "/previews";
            Directory.CreateDirectory(previewRoot);
            foreach (var name in names)
            {
                if (!clipsByName.TryGetValue(name, out var clip)) continue;
                var folder = previewRoot + "/" + SafeName(name);
                Directory.CreateDirectory(folder);
                using (var view = new DesktopPetDragReview.ReviewScene())
                {
                    int frames = Mathf.Max(2, Mathf.CeilToInt(clip.length * 30f) + 1);
                    for (int frame = 0; frame < frames; frame++)
                    {
                        float time = Mathf.Min(clip.length, frame / 30f);
                        clip.SampleAnimation(view.Pet, time);
                        view.Render(folder + "/frame-" + frame.ToString("D4") + ".png");
                    }
                }
            }
            File.WriteAllText(ReportFolder + "/preview-status.txt", "OK preview " + DateTime.Now.ToString("O"), new UTF8Encoding(false));
            Debug.Log("Desktop-pet candidate previews written to " + Path.GetFullPath(previewRoot));
        }

        private static HashSet<string> BindingPaths(AnimationClip clip)
        {
            return new HashSet<string>(AnimationUtility.GetCurveBindings(clip).Select(binding => binding.path), StringComparer.Ordinal);
        }

        private static Dictionary<string, List<string>> ReadBlendShapes(GameObject prefab)
        {
            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var renderer in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh == null || renderer.sharedMesh.blendShapeCount == 0) continue;
                var path = AnimationUtility.CalculateTransformPath(renderer.transform, prefab.transform);
                var names = new List<string>();
                for (int i = 0; i < renderer.sharedMesh.blendShapeCount; i++)
                    names.Add(renderer.sharedMesh.GetBlendShapeName(i));
                result[path] = names;
            }
            return result;
        }

        private static string Escape(string value)
        {
            return value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
        }

        private static string SafeName(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
            return value.Replace('>', '-');
        }
    }
}
