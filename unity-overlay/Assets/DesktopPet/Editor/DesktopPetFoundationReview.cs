using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DesktopPetEditor
{
    public static class DesktopPetFoundationReview
    {
        private const string PrefabPath = "Assets/DesktopPet/Prefabs/DesktopMita.prefab";
        private const string ScenePath = "Assets/DesktopPet/Scenes/DesktopPetPrototype.unity";
        private const string ReportFolder = "Library/DesktopPetFoundationReview";

        public static void Run()
        {
            Directory.CreateDirectory(ReportFolder);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Require(prefab != null, "DesktopMita prefab is missing");
            Require(prefab.GetComponent<DesktopPet.PetBehaviorDirector>() != null,
                "PetBehaviorDirector is missing from DesktopMita");
            Require(prefab.GetComponent<DesktopPet.PetSurfaceMotionController>() != null,
                "PetSurfaceMotionController is missing from DesktopMita");
            Require(prefab.GetComponent<DesktopPet.PetExpressionController>() != null,
                "PetExpressionController is missing from DesktopMita");

            var action = ScriptableObject.CreateInstance<DesktopPet.PetAnimationAction>();
            try
            {
                Require(action.LoopSeconds.x >= 0f && action.LoopSeconds.y >= action.LoopSeconds.x,
                    "PetAnimationAction loop range is invalid");
                Require(action.Weight >= 0f && action.CooldownSeconds >= 0f,
                    "PetAnimationAction defaults are invalid");
            }
            finally { UnityEngine.Object.DestroyImmediate(action); }

            var scoreMethod = typeof(DesktopPet.DesktopWindowAnchorController).GetMethod("TryScoreWindowTop",
                BindingFlags.NonPublic | BindingFlags.Static);
            Require(scoreMethod != null, "Window-top score method is missing");
            var accepted = new object[] { new Vector2Int(250, 108), new RectInt(100, 100, 300, 200), 10, 5, 0 };
            Require((bool)scoreMethod.Invoke(null, accepted) && (int)accepted[4] == 32,
                "Window-top score did not use the expected hip-to-top distance");
            var rejected = new object[] { new Vector2Int(250, 140), new RectInt(100, 100, 300, 200), 10, 5, 0 };
            Require(!(bool)scoreMethod.Invoke(null, rejected),
                "Window-top score accepted a point outside vertical tolerance");

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var runtime = GameObject.Find("DesktopPetRuntime");
            Require(runtime != null, "DesktopPetRuntime is missing from prototype scene");
            Require(runtime.GetComponent<DesktopPet.DesktopPetRuntimeSettings>() != null,
                "DesktopPetRuntimeSettings is missing from prototype scene");
            Require(runtime.GetComponent<DesktopPet.DesktopWindowController>() != null,
                "DesktopWindowController is missing from prototype scene");
            Require(runtime.GetComponent<DesktopPet.DesktopWindowAnchorController>() != null,
                "DesktopWindowAnchorController is missing from prototype scene");
            Require(UnityEngine.Object.FindObjectOfType<DesktopPet.DesktopChatController>() != null,
                "DesktopChatController is missing from prototype scene");

            var report = "PASS foundation " + DateTime.Now.ToString("O") + Environment.NewLine +
                "- Persistent runtime settings component present" + Environment.NewLine +
                "- Behavior director and surface-motion component present" + Environment.NewLine +
                "- Hip-to-window-top score boundary passed" + Environment.NewLine +
                "- Existing prototype window/chat components preserved" + Environment.NewLine;
            File.WriteAllText(ReportFolder + "/report.txt", report);
            Debug.Log(report);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
