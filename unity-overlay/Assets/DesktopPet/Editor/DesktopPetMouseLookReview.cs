using System;
using System.IO;
using System.Reflection;
using DesktopPet;
using UnityEditor;
using UnityEngine;

namespace DesktopPetEditor
{
    public static class DesktopPetMouseLookReview
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly Type LookType = typeof(PetMouseLookController);

        public static void Run()
        {
            const string folder = "Library/DesktopPetMouseLookReview";
            Directory.CreateDirectory(folder);
            int passed = 0;
            var apply = LookType.GetMethod("ApplyLook", Private);
            var restore = LookType.GetMethod("RestoreAnimationPose", Private);
            var disable = LookType.GetMethod("OnDisable", Private);
            using (var view = new DesktopPetDragReview.ReviewScene())
            {
                var look = view.Pet.AddComponent<PetMouseLookController>();
                var head = view.Pet.transform.Find("Armature/Hips/Spine/Chest/Neck2/Neck1/Head");
                var neck = head.parent;
                var hips = view.Pet.transform.Find("Armature/Hips");
                LookType.GetField("head", Private).SetValue(look, head);
                LookType.GetField("neck", Private).SetValue(look, neck);
                LookType.GetField("petCamera", Private).SetValue(look, view.Camera);
                var paths = new[] { "Assets/AnimationClip/MitaDreamer.anim",
                    "Assets/DesktopPet/Animations/DragFlailLoop.anim", "Assets/AnimationClip/Mita Sit Normal.anim" };
                var names = new[] { "idle", "drag", "sit" };
                var offsets = new[] { Vector2.zero, Vector2.left * 180, Vector2.right * 180,
                    Vector2.up * 160, Vector2.down * 160, new Vector2(5000, -5000) };
                for (int p = 0; p < paths.Length; p++)
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(paths[p]);
                    Check(clip != null, "Clip exists: " + paths[p], ref passed);
                    for (int d = 0; d < offsets.Length; d++)
                    {
                        disable.Invoke(look, null);
                        clip.SampleAnimation(view.Pet, clip.length * 0.35f);
                        var baseHead = head.localRotation;
                        var baseNeck = neck.localRotation;
                        var baseHips = hips.localRotation;
                        var baseHipPosition = hips.position;
                        var baseWorld = head.rotation;
                        var centre = (Vector2)view.Camera.WorldToScreenPoint(head.position);
                        var pointer = centre + offsets[d];
                        var expected = (Vector2)LookType.GetMethod("GetLookAngles", BindingFlags.NonPublic | BindingFlags.Static)
                            .Invoke(null, new object[] { view.Camera, head.position, baseWorld, pointer, 0.65f, 50f, 25f });
                        for (int frame = 0; frame < 180; frame++)
                        {
                            restore.Invoke(look, null);
                            Check(Quaternion.Angle(head.localRotation, baseHead) < 0.1f &&
                                Quaternion.Angle(neck.localRotation, baseNeck) < 0.1f, "No cumulative twist", ref passed);
                            apply.Invoke(look, new object[] { pointer, true, 1f / 60f });
                        }
                        var angles = (Vector2)LookType.GetField("_angles", Private).GetValue(look);
                        Check(Vector2.Distance(angles, expected) < 0.1f, "Smoothed target reached", ref passed);
                        Check(Mathf.Abs(angles.x) <= 50.01f && Mathf.Abs(angles.y) <= 25.01f, "Joint limits", ref passed);
                        Check(Quaternion.Angle(hips.localRotation, baseHips) < 0.1f &&
                            Vector3.Distance(hips.position, baseHipPosition) < 0.0001f, "Seat anchor unchanged", ref passed);
                        if (p == 0 && d == 1) Check(angles.x > 0, "Screen-left yaw", ref passed);
                        if (p == 0 && d == 2) Check(angles.x < 0, "Screen-right yaw", ref passed);
                        if (p == 0 && d == 3) Check(angles.y < 0, "Screen-up pitch", ref passed);
                        if (p == 0 && d == 4) Check(angles.y > 0, "Screen-down pitch", ref passed);
                        view.Render(folder + "/" + names[p] + "-" + d + ".png");
                        for (int frame = 0; frame < 180; frame++)
                            apply.Invoke(look, new object[] { pointer, false, 1f / 60f });
                        Check(((Vector2)LookType.GetField("_angles", Private).GetValue(look)).magnitude < 0.01f,
                            "Missing cursor returns to animation", ref passed);
                        disable.Invoke(look, null);
                        Check(Quaternion.Angle(head.localRotation, baseHead) < 0.1f &&
                            Quaternion.Angle(neck.localRotation, baseNeck) < 0.1f, "Disable restores animation", ref passed);
                    }
                }
            }
            using (var view = new DesktopPetDragReview.ReviewScene())
            {
                var animator = view.Pet.GetComponent<Animator>();
                animator.enabled = true;
                animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                    "Assets/DesktopPet/Animators/DesktopMita.controller");
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();
                animator.Update(0);
                var look = view.Pet.AddComponent<PetMouseLookController>();
                var head = view.Pet.transform.Find("Armature/Hips/Spine/Chest/Neck2/Neck1/Head");
                LookType.GetField("head", Private).SetValue(look, head);
                LookType.GetField("neck", Private).SetValue(look, head.parent);
                LookType.GetField("petCamera", Private).SetValue(look, view.Camera);
                // Six simulated seconds include the two-second sit delay and
                // the crossing/settling clip, so the hold loop is now expected.
                var states = new[] { "Idle", "DragLoop", "SitCrossLegLoop", "DragLoop", "Idle" };
                for (int stage = 0; stage < states.Length; stage++)
                {
                    animator.SetBool("IsDragging", stage == 1 || stage == 3);
                    animator.SetBool("IsSitting", stage == 2);
                    for (int frame = 0; frame < 360; frame++)
                    {
                        restore.Invoke(look, null);
                        animator.Update(1f / 60f);
                        var baseHead = head.localRotation;
                        var baseNeck = head.parent.localRotation;
                        var pointer = (Vector2)view.Camera.WorldToScreenPoint(head.position) + new Vector2(180, 80);
                        apply.Invoke(look, new object[] { pointer, true, 1f / 60f });
                        var angles = (Vector2)LookType.GetField("_angles", Private).GetValue(look);
                        Check(!float.IsNaN(angles.x) && Mathf.Abs(angles.x) <= 50.01f && Mathf.Abs(angles.y) <= 25.01f,
                            "Animator transition remains bounded", ref passed);
                        if (frame == 359) view.Render(folder + "/animator-" + stage + ".png");
                        restore.Invoke(look, null);
                        Check(Quaternion.Angle(head.localRotation, baseHead) < 0.1f &&
                            Quaternion.Angle(head.parent.localRotation, baseNeck) < 0.1f,
                            "Animated base pose preserved", ref passed);
                    }
                    Check(animator.GetCurrentAnimatorStateInfo(0).IsName(states[stage]), "Animator reached " + states[stage], ref passed);
                }
            }
            const string prefabPath = "Assets/DesktopPet/Prefabs/DesktopMita.prefab";
            var prefab = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                if (prefab.GetComponent<PetMouseLookController>() == null)
                    prefab.AddComponent<PetMouseLookController>();
                PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
            AssetDatabase.SaveAssets();
            File.WriteAllText(folder + "/validation.txt", "PASS " + passed +
                " checks: idle/drag/sit, four directions, cursor outside window, limits, smoothing, no pose drift, hip anchor, missing cursor, disable restoration, live Animator transitions.\n");
        }

        private static void Check(bool condition, string message, ref int passed)
        {
            if (!condition) throw new Exception("Mouse look regression: " + message);
            passed++;
        }
    }
}
