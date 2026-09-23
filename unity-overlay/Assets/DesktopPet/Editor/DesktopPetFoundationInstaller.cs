using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DesktopPetEditor
{
    public static class DesktopPetFoundationInstaller
    {
        private const string PrefabPath = "Assets/DesktopPet/Prefabs/DesktopMita.prefab";
        private const string ScenePath = "Assets/DesktopPet/Scenes/DesktopPetPrototype.unity";

        public static void Install()
        {
            var prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Ensure<DesktopPet.PetSurfaceMotionController>(prefab);
                Ensure<DesktopPet.PetBehaviorDirector>(prefab);
                Ensure<DesktopPet.PetExpressionController>(prefab);
                PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefab);
            }

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var runtime = GameObject.Find("DesktopPetRuntime");
            if (runtime == null) throw new InvalidOperationException("DesktopPetRuntime was not found in " + ScenePath);
            Ensure<DesktopPet.DesktopPetRuntimeSettings>(runtime);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("Desktop-pet foundation installed without rebuilding the Animator or scene layout.");
        }

        private static T Ensure<T>(GameObject target) where T : Component
        {
            var component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }
    }
}
