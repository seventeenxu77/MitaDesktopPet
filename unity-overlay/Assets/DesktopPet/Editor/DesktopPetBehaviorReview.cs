using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DesktopPet;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace DesktopPetEditor
{
    public static class DesktopPetBehaviorReview
    {
        private const string ControllerPath = "Assets/DesktopPet/Animators/DesktopMita.controller";
        private const string PrefabPath = "Assets/DesktopPet/Prefabs/DesktopMita.prefab";
        private const string Folder = "Library/DesktopPetBehaviorReview";
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static int _checks;

        private static readonly string[] BodyStates =
        {
            "TiredEnter", "TiredLoop", "StandSleepEnter", "StandSleepLoop", "StandSleepExit", "Yawn",
            "DeepSleepEnter", "DeepSleepLoop", "DeepSleepExit", "SitHalfSleepEnter", "SitHalfSleepLoop",
            "SurfaceWalk", "SurfaceWallEnter", "SurfaceWallLoop", "SurfaceWallExit", "SurfaceJump"
        };

        private static readonly string[] FaceStates =
        {
            "FaceNeutral", "FaceHalfSleep", "FaceSleep", "FaceTry", "FaceSuspicion", "FaceShy",
            "FaceDiscontent", "FaceSurprise"
        };

        public static void Run()
        {
            _checks = 0;
            Directory.CreateDirectory(Folder);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            Check(controller != null, "Animator controller exists");
            Check(controller.layers.Length == 2, "Animator has exactly base and face layers");
            var baseStates = controller.layers[0].stateMachine.states.ToDictionary(child => child.state.name, child => child.state);
            foreach (string name in BodyStates)
            {
                Check(baseStates.TryGetValue(name, out var state), "Body state exists: " + name);
                Check(state.motion is AnimationClip, "Body state has a clip: " + name);
                Check(state.transitions.Count(transition => transition.conditions.Any(condition =>
                    condition.parameter == "IsDragging")) == 1, "Body state has one drag interruption: " + name);
            }
            Check(Mathf.Abs(baseStates["SurfaceWalk"].speed - 0.68f) < 0.001f,
                "Surface walk uses the approved slower gait speed");
            Check(baseStates["SurfaceWalk"].speedParameterActive &&
                baseStates["SurfaceWalk"].speedParameter == "SurfaceSpeed",
                "Surface gait playback is coupled to actual surface motion");
            var surfaceSpeed = controller.parameters.FirstOrDefault(parameter => parameter.name == "SurfaceSpeed");
            Check(surfaceSpeed != null && surfaceSpeed.type == AnimatorControllerParameterType.Float &&
                Mathf.Approximately(surfaceSpeed.defaultFloat, 1f),
                "Surface playback multiplier has a safe normalized default");
            CheckSurfaceFacing();

            int faceIndex = Array.FindIndex(controller.layers, layer => layer.name == "Face");
            Check(faceIndex == 1 && Mathf.Approximately(controller.layers[faceIndex].defaultWeight, 1f),
                "Face layer is enabled at full weight");
            var faceStates = controller.layers[faceIndex].stateMachine.states.ToDictionary(child => child.state.name, child => child.state);
            foreach (string name in FaceStates)
            {
                Check(faceStates.TryGetValue(name, out var state), "Face state exists: " + name);
                var clip = state.motion as AnimationClip;
                Check(clip != null, "Face state has a clip: " + name);
                var bindings = AnimationUtility.GetCurveBindings(clip);
                Check(bindings.Length >= 30 && bindings.All(binding =>
                    binding.type == typeof(SkinnedMeshRenderer) && binding.propertyName.StartsWith("blendShape.")),
                    "Face state only writes blend shapes: " + name);
            }

            using (var runtime = new DesktopPetDragReview.ReviewScene())
            {
                var runtimeAnimator = runtime.Pet.GetComponent<Animator>();
                runtimeAnimator.enabled = true;
                runtimeAnimator.runtimeAnimatorController = controller;
                runtimeAnimator.Rebind();
                runtimeAnimator.Update(0f);
                foreach (string name in BodyStates)
                    Check(runtimeAnimator.HasState(0, Animator.StringToHash("Base Layer." + name)),
                        "Runtime body-state hash resolves: " + name);
                foreach (string name in FaceStates)
                    Check(runtimeAnimator.HasState(faceIndex, Animator.StringToHash("Face." + name)),
                        "Runtime face-state hash resolves: " + name);
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Check(prefab != null, "DesktopMita prefab exists");
            var director = prefab.GetComponent<PetBehaviorDirector>();
            Check(director != null, "Behavior director exists");
            Check(prefab.GetComponent<PetSurfaceMotionController>() != null, "Surface motion exists");
            Check(prefab.GetComponent<PetExpressionController>() != null, "Expression controller exists");
            var directorObject = new SerializedObject(director);
            var actions = directorObject.FindProperty("actions");
            Check(actions.arraySize == 7, "Seven actions are assigned after excluding the rear-facing seated candidate");
            var ids = new HashSet<string>();
            for (int i = 0; i < actions.arraySize; i++)
            {
                var action = actions.GetArrayElementAtIndex(i).objectReferenceValue as PetAnimationAction;
                Check(action != null, "Action reference " + i + " is valid");
                Check(ids.Add(action.ActionId), "Action ID is unique: " + action.ActionId);
                Check(action.Weight > 0f && action.CooldownSeconds >= 0f, "Action timing is valid: " + action.ActionId);
                if (action.MovesAlongSurface)
                {
                    Check(action.Location == PetActionLocation.AttachedToWindowTop &&
                        Mathf.Abs(action.SurfaceSpeedPixelsPerSecond) >= 1f, "Moving action requires a window surface");
                    Check(Mathf.Abs(action.SurfaceSpeedPixelsPerSecond) >= 120f,
                        "Surface walk covers enough desktop distance per gait cycle");
                }
            }

            CheckSurfaceContactProjection();
            File.WriteAllText(Folder + "/validation.txt",
                "PASS " + _checks + " checks: 7 scheduled actions with the rear-facing seated candidate excluded, body states, drag interruption, face-only layer, " +
                "expression ownership, window-surface contact anchors and unique configuration.\n");
            Debug.Log(File.ReadAllText(Folder + "/validation.txt"));
        }

        private static void CheckSurfaceContactProjection()
        {
            var composeMethod = typeof(DesktopWindowAnchorController).GetMethod(
                "ComposeStableSurfaceContact", BindingFlags.Static | BindingFlags.NonPublic);
            Check(composeMethod != null, "Stable surface contact composition exists");
            var core = new Vector2Int(200, 150);
            var fullContact = (Vector2Int)composeMethod.Invoke(null, new object[] { core, 300f, 1f });
            var halfContact = (Vector2Int)composeMethod.Invoke(null, new object[] { core, 300f, 0.5f });
            Check(fullContact == new Vector2Int(200, 450),
                "Walking contact keeps horizontal position on the hip core");
            Check(halfContact == new Vector2Int(200, 300),
                "Walking contact blends vertical height without horizontal pullback");

            using (var view = new DesktopPetDragReview.ReviewScene())
            {
                var controller = view.Pet.AddComponent<DesktopWindowAnchorController>();
                var bones = view.Pet.transform.Find("Armature").GetComponentsInChildren<Transform>(true);
                Transform Bone(string name) => bones.First(bone => bone.name == name);
                Set(controller, "seatAnchor", Bone("Hips"));
                Set(controller, "leftSurfaceAnchor", Bone("Left toe"));
                Set(controller, "rightSurfaceAnchor", Bone("Right toe"));
                Set(controller, "petCamera", view.Camera);
                var seatMethod = typeof(DesktopWindowAnchorController).GetMethod("TryGetSeatContactOffset", Hidden);
                var surfaceMethod = typeof(DesktopWindowAnchorController).GetMethod("TryGetSurfaceContactOffset", Hidden);
                var walk = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/AnimationClip/Mita Walk.anim");
                var rect = new RectInt(-700, 140, 520, 700);
                for (int frame = 0; frame < 8; frame++)
                {
                    walk.SampleAnimation(view.Pet, walk.length * frame / 8f);
                    var seatArgs = new object[] { rect, Vector2Int.zero };
                    var surfaceArgs = new object[] { rect, Vector2Int.zero };
                    Check((bool)seatMethod.Invoke(controller, seatArgs), "Hip contact projects during walk");
                    Check((bool)surfaceMethod.Invoke(controller, surfaceArgs), "Foot contact projects during walk");
                    var seat = (Vector2Int)seatArgs[1];
                    var surface = (Vector2Int)surfaceArgs[1];
                    Check(surface.y > seat.y + 80, "Foot contact remains below the hip during walk");
                }
            }
        }

        private static void CheckSurfaceFacing()
        {
            var method = typeof(PetSurfaceMotionController).GetMethod("CalculateFacingRotation",
                BindingFlags.Static | BindingFlags.NonPublic);
            Check(method != null, "Surface facing calculation exists");
            var cameraRight = Vector3.left;
            var faceRight = (Quaternion)method.Invoke(null, new object[] { cameraRight, 1f });
            var faceLeft = (Quaternion)method.Invoke(null, new object[] { cameraRight, -1f });
            Check(Vector3.Dot(faceRight * Vector3.forward, cameraRight) > 0.999f,
                "Positive desktop motion faces screen-right");
            Check(Vector3.Dot(faceLeft * Vector3.forward, -cameraRight) > 0.999f,
                "Negative desktop motion faces screen-left");
            Check(Vector3.Dot(faceRight * Vector3.up, Vector3.up) > 0.999f &&
                Vector3.Dot(faceLeft * Vector3.up, Vector3.up) > 0.999f,
                "Both walk directions remain upright");
        }

        private static void Set(object instance, string field, object value)
        {
            instance.GetType().GetField(field, Hidden).SetValue(instance, value);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Behavior review: " + message);
            _checks++;
        }
    }
}
