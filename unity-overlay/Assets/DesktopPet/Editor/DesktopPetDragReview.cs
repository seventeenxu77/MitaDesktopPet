using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace DesktopPetEditor
{
    // Local authoring/review commands; never changes the user's open scene.
    [InitializeOnLoad]
    public static class DesktopPetDragReview
    {
        public const string ReviewFolder = "Library/DesktopPetDragReview";
        private static double nextPoll;

        static DesktopPetDragReview() { EditorApplication.update += Poll; }

        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            var path = ReviewFolder + "/request.txt";
            if (!File.Exists(path)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.isPlaying = false;
                return;
            }
            var command = File.ReadAllText(path).Trim();
            File.Delete(path);
            try
            {
                if (command == "inspect") Inspect();
                else if (command == "bake") Bake();
                else if (command == "refresh") AssetDatabase.Refresh();
                else if (command == "check-seat") DesktopPetSeatReview.Run();
                else if (command == "check-look") DesktopPetMouseLookReview.Run();
                else if (command == "check-chat") DesktopPetChatReview.Run();
                else if (command == "preview-chat") DesktopPetChatPreview.Run();
                else throw new ArgumentException("Unknown drag review command: " + command);
                File.WriteAllText(ReviewFolder + "/status.txt", "OK " + command + " " + DateTime.Now.ToString("O"));
            }
            catch (Exception ex)
            {
                File.WriteAllText(ReviewFolder + "/status.txt", ex.ToString());
                Debug.LogException(ex);
            }
        }

        public static void Bake()
        {
            Directory.CreateDirectory(ReviewFolder);
            DesktopPetDragAuthoring.Generate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/learn/MitaDreamer.prefab"));
            using (var view = new ReviewScene())
            {
                var loop = AssetDatabase.LoadAssetAtPath<AnimationClip>(DesktopPetDragAuthoring.Folder + "/DragFlailLoop.anim");
                Validate(view, loop);
                for (int i = 0; i < 60; i++)
                {
                    loop.SampleAnimation(view.Pet, i / 60f);
                    view.Render(ReviewFolder + "/loop-" + i.ToString("D3") + ".png");
                }
                var lift = AssetDatabase.LoadAssetAtPath<AnimationClip>(DesktopPetDragAuthoring.Folder + "/DragLiftStart.anim");
                for (int i = 0; i <= 18; i++)
                {
                    lift.SampleAnimation(view.Pet, i / 60f);
                    view.Render(ReviewFolder + "/lift-" + i.ToString("D3") + ".png");
                }
                VerifyAnimator(view);
            }
            const string prefabPath = "Assets/DesktopPet/Prefabs/DesktopMita.prefab";
            var contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var oldOffsets = contents.GetComponent<DesktopPet.ProceduralDragPoseController>();
                if (oldOffsets != null) oldOffsets.enabled = false;
                PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            AssetDatabase.SaveAssets();
        }

        private static void Validate(ReviewScene view, AnimationClip loop)
        {
            var bones = view.Pet.transform.Find("Armature").GetComponentsInChildren<Transform>(true);
            loop.SampleAnimation(view.Pet, 0);
            var start = new Quaternion[bones.Length];
            var positions = new Vector3[bones.Length];
            for (int i = 0; i < bones.Length; i++) { start[i] = bones[i].localRotation; positions[i] = bones[i].localPosition; }
            loop.SampleAnimation(view.Pet, loop.length);
            float seamAngle = 0, seamPosition = 0;
            for (int i = 0; i < bones.Length; i++)
            {
                seamAngle = Mathf.Max(seamAngle, Quaternion.Angle(start[i], bones[i].localRotation));
                seamPosition = Mathf.Max(seamPosition, Vector3.Distance(positions[i], bones[i].localPosition));
            }
            if (seamAngle > 0.1f || seamPosition > 0.0001f) throw new Exception("Loop seam mismatch");
            var lift = AssetDatabase.LoadAssetAtPath<AnimationClip>(DesktopPetDragAuthoring.Folder + "/DragLiftStart.anim");
            lift.SampleAnimation(view.Pet, lift.length);
            float liftAngle = 0;
            for (int i = 0; i < bones.Length; i++) liftAngle = Mathf.Max(liftAngle, Quaternion.Angle(start[i], bones[i].localRotation));
            if (liftAngle > 0.1f) throw new Exception("Lift end does not match loop start");
            var hips = view.Pet.transform.Find("Armature/Hips");
            var head = hips.Find("Spine/Chest/Neck2/Neck1/Head");
            var hipOrigin = hips.position;
            float drift = 0, headBelowHip = float.MaxValue;
            var bounds = new Bounds();
            bool first = true;
            for (int frame = 0; frame <= 60; frame++)
            {
                loop.SampleAnimation(view.Pet, frame / 60f);
                drift = Mathf.Max(drift, Vector3.Distance(hipOrigin, hips.position));
                headBelowHip = Mathf.Min(headBelowHip, hips.position.y - head.position.y);
                foreach (var b in bones)
                {
                    var point = view.Camera.WorldToViewportPoint(b.position);
                    if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                    else bounds.Encapsulate(point);
                }
            }
            if (drift > 0.0001f || headBelowHip < 0.1f) throw new Exception("Pickup pivot/silhouette check failed");
            File.WriteAllText(ReviewFolder + "/validation.txt", "Clip duration: " + loop.length
                + "\nLoop seam max rotation degrees: " + seamAngle
                + "\nLoop seam max translation: " + seamPosition
                + "\nLift to loop rotation error degrees: " + liftAngle
                + "\nHip pivot drift: " + drift
                + "\nHead minimum distance below hips: " + headBelowHip
                + "\nAll bone viewport bounds (includes unused IK controls): " + bounds.ToString("F4") + "\n");
        }

        private static void VerifyAnimator(ReviewScene view)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/DesktopPet/Animators/DesktopMita.controller");
            foreach (var s in controller.layers[0].stateMachine.states)
            {
                if (s.state.name == "DragStart") s.state.motion = AssetDatabase.LoadAssetAtPath<AnimationClip>(DesktopPetDragAuthoring.Folder + "/DragLiftStart.anim");
                if (s.state.name == "DragLoop") s.state.motion = AssetDatabase.LoadAssetAtPath<AnimationClip>(DesktopPetDragAuthoring.Folder + "/DragFlailLoop.anim");
                if (s.state.name == "DragStart")
                    foreach (var t in s.state.transitions)
                        if (t.destinationState != null && t.destinationState.name == "DragLoop")
                        { t.exitTime = 1; t.hasFixedDuration = true; t.duration = 0.05f; }
            }
            var animator = view.Pet.GetComponent<Animator>();
            animator.enabled = true;
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            animator.Update(0);
            animator.SetBool("IsDragging", true);
            for (int i = 0; i < 180; i++) animator.Update(1f / 60);
            if (!animator.GetCurrentAnimatorStateInfo(0).IsName("DragLoop")) throw new Exception("Animator did not enter DragLoop");
            view.Render(ReviewFolder + "/animator-drag.png");
            animator.SetBool("IsDragging", false);
            animator.SetBool("IsSitting", true);
            for (int i = 0; i < 360; i++) animator.Update(1f / 60);
            if (!animator.GetCurrentAnimatorStateInfo(0).IsName("SitLoop")) throw new Exception("Animator did not enter SitLoop");
            view.Render(ReviewFolder + "/animator-sit.png");
            animator.SetBool("IsDragging", true);
            for (int i = 0; i < 180; i++) animator.Update(1f / 60);
            if (!animator.GetCurrentAnimatorStateInfo(0).IsName("DragLoop")) throw new Exception("Animator did not return from sitting to dragging");
            animator.SetBool("IsDragging", false);
            animator.SetBool("IsSitting", false);
            for (int i = 0; i < 120; i++) animator.Update(1f / 60);
            if (!animator.GetCurrentAnimatorStateInfo(0).IsName("Idle")) throw new Exception("Animator did not return to idle");
            File.AppendAllText(ReviewFolder + "/validation.txt", "Animator: Idle -> DragStart -> DragLoop -> SitEnter -> SitLoop -> DragLoop -> Idle PASS\n");
            animator.enabled = false;
            EditorUtility.SetDirty(controller);
        }

        public static void Inspect()
        {
            Directory.CreateDirectory(ReviewFolder);
            using (var view = new ReviewScene())
            {
                var dump = new StringBuilder();
                foreach (var bone in view.Pet.GetComponentsInChildren<Transform>(true))
                {
                    dump.AppendLine(AnimationUtility.CalculateTransformPath(bone, view.Pet.transform)
                        + " position=" + bone.position.ToString("F5") + " rotation=" + bone.rotation.ToString("F5")
                        + " local=" + bone.localRotation.ToString("F5"));
                }
                File.WriteAllText(ReviewFolder + "/bones.txt", dump.ToString());
                view.Render(ReviewFolder + "/rest.png");
                var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/AnimationClip/MitaDreamer.anim");
                idle.SampleAnimation(view.Pet, 0);
                view.Render(ReviewFolder + "/idle.png");
                var old = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/DesktopPet/Animations/DragFlailLoop.anim");
                if (old != null)
                {
                    old.SampleAnimation(view.Pet, 0.3f);
                    view.Render(ReviewFolder + "/current.png");
                }
            }
        }

        public sealed class ReviewScene : IDisposable
        {
            public GameObject Pet;
            public Camera Camera;
            private Scene scene;
            private RenderTexture target;
            private readonly List<SkinnedMeshRenderer> skins = new List<SkinnedMeshRenderer>();
            private readonly List<Mesh> bakedMeshes = new List<Mesh>();

            public ReviewScene(GameObject sourcePrefab = null)
            {
                scene = EditorSceneManager.NewPreviewScene();
                var prefab = sourcePrefab != null ? sourcePrefab : AssetDatabase.LoadAssetAtPath<GameObject>("Assets/learn/MitaDreamer.prefab");
                Pet = Object.Instantiate(prefab);
                SceneManager.MoveGameObjectToScene(Pet, scene);
                Pet.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                foreach (var script in Pet.GetComponentsInChildren<MonoBehaviour>(true))
                    if (script != null) Object.DestroyImmediate(script);
                foreach (var animator in Pet.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
                // Multiple offline samples are rendered in a single Editor frame.
                // BakeMesh avoids Unity reusing the first frame's GPU skin cache.
                foreach (var renderer in Pet.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    renderer.updateWhenOffscreen = true;
                    if (!renderer.enabled) continue;
                    var mesh = new Mesh();
                    mesh.indexFormat = renderer.sharedMesh.indexFormat;
                    var bakedGo = new GameObject("Baked " + renderer.name, typeof(MeshFilter), typeof(MeshRenderer));
                    bakedGo.transform.SetParent(renderer.transform, false);
                    bakedGo.GetComponent<MeshFilter>().sharedMesh = mesh;
                    bakedGo.GetComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
                    renderer.enabled = false;
                    skins.Add(renderer);
                    bakedMeshes.Add(mesh);
                }
                var go = new GameObject("Review Camera", typeof(Camera));
                SceneManager.MoveGameObjectToScene(go, scene);
                Camera = go.GetComponent<Camera>();
                Camera.scene = scene;
                Camera.orthographic = true;
                Camera.orthographicSize = 1.1278037f;
                Camera.transform.position = new Vector3(0, 0.9111755f, 5);
                Camera.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
                Camera.clearFlags = CameraClearFlags.SolidColor;
                Camera.backgroundColor = new Color(0.16f, 0.20f, 0.26f);
                Camera.nearClipPlane = 0.01f;
                Camera.farClipPlane = 100;
                Camera.allowHDR = false;
                Camera.allowMSAA = false;
                Camera.enabled = false;
                target = new RenderTexture(520, 700, 24, RenderTextureFormat.ARGB32);
                Camera.targetTexture = target;
                Light("Key", new Vector3(35, 145, 0), 1.1f);
                Light("Fill", new Vector3(10, -40, 0), 0.6f);
            }

            private void Light(string name, Vector3 rotation, float intensity)
            {
                var go = new GameObject(name, typeof(Light));
                SceneManager.MoveGameObjectToScene(go, scene);
                go.transform.rotation = Quaternion.Euler(rotation);
                var light = go.GetComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = intensity;
                light.shadows = LightShadows.None;
            }

            public void Render(string path)
            {
                for (int i = 0; i < skins.Count; i++) skins[i].BakeMesh(bakedMeshes[i]);
                Camera.Render();
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                try
                {
                    texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                    texture.Apply();
                    File.WriteAllBytes(path, texture.EncodeToPNG());
                }
                finally { RenderTexture.active = previous; Object.DestroyImmediate(texture); }
            }

            public void Dispose()
            {
                Camera.targetTexture = null;
                target.Release();
                Object.DestroyImmediate(target);
                foreach (var mesh in bakedMeshes) Object.DestroyImmediate(mesh);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
