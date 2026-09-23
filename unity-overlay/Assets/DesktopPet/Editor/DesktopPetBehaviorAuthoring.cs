using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DesktopPet;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace DesktopPetEditor
{
    public static class DesktopPetBehaviorAuthoring
    {
        private const string ControllerPath = "Assets/DesktopPet/Animators/DesktopMita.controller";
        private const string PrefabPath = "Assets/DesktopPet/Prefabs/DesktopMita.prefab";
        private const string BehaviorFolder = "Assets/DesktopPet/Config/Behaviors";

        private sealed class ActionDefinition
        {
            public string fileName, id, entry, loop, exit;
            public PetActionLocation location;
            public float weight, cooldown, idle, entrySeconds, loopMin, loopMax, exitSeconds, fade, speed;
            public bool quiet, moves, edge;
            public int edgeMargin;
        }

        private static readonly Dictionary<string, string> BodyStates = new Dictionary<string, string>
        {
            {"TiredEnter", "Assets/AnimationClip/Mita Start Tired.anim"},
            {"TiredLoop", "Assets/AnimationClip/Mita Tired.anim"},
            {"StandSleepEnter", "Assets/AnimationClip/Mita Stay StartSleep.anim"},
            {"StandSleepLoop", "Assets/AnimationClip/Mita Stay IdleSleep.anim"},
            {"StandSleepExit", "Assets/AnimationClip/Mita Stay StopSleep.anim"},
            {"Yawn", "Assets/AnimationClip/Mita Yawns.anim"},
            {"DeepSleepEnter", "Assets/AnimationClip/Mita Start Sleep.anim"},
            {"DeepSleepLoop", "Assets/AnimationClip/Mita Sleep.anim"},
            {"DeepSleepExit", "Assets/AnimationClip/Mita Wakeup.anim"},
            {"SitHalfSleepEnter", "Assets/AnimationClip/Mita SitIdle Start HalfSleep.anim"},
            {"SitHalfSleepLoop", "Assets/AnimationClip/Mita SitIdle HalfSleep.anim"},
            {"SurfaceWalk", "Assets/AnimationClip/Mita Walk.anim"},
            {"SurfaceWallEnter", "Assets/AnimationClip/Mita StartWall.anim"},
            {"SurfaceWallLoop", "Assets/AnimationClip/Mita IdleWall.anim"},
            {"SurfaceWallExit", "Assets/AnimationClip/Mita StopWall.anim"},
            {"SurfaceJump", "Assets/AnimationClip/Mita Jump.anim"}
        };

        private static readonly Dictionary<string, string> FaceStates = new Dictionary<string, string>
        {
            {"FaceNeutral", "Assets/AnimationClip/Mita E-None D.anim"},
            {"FaceHalfSleep", "Assets/AnimationClip/Mita E-HalfSleep.anim"},
            {"FaceSleep", "Assets/AnimationClip/Mita E-Sleep.anim"},
            {"FaceTry", "Assets/AnimationClip/Mita E-Try.anim"},
            {"FaceSuspicion", "Assets/AnimationClip/Mita E-Suspicion.anim"},
            {"FaceShy", "Assets/AnimationClip/Mita E-Shy.anim"},
            {"FaceDiscontent", "Assets/AnimationClip/Mita E-Discontent.anim"},
            {"FaceSurprise", "Assets/AnimationClip/Mita E-SurpriseO.anim"}
        };

        public static void Install()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) throw new FileNotFoundException("Desktop-pet Animator is missing.", ControllerPath);
            var missing = BodyStates.Concat(FaceStates)
                .Where(pair => AssetDatabase.LoadAssetAtPath<AnimationClip>(pair.Value) == null)
                .Select(pair => pair.Value).ToArray();
            if (missing.Length > 0) throw new FileNotFoundException("Required source animations are missing:\n" + string.Join("\n", missing));

            ConfigureBodyStates(controller);
            ConfigureFaceLayer(controller);
            EnsureFloatParameter(controller, "SurfaceSpeed");
            EnsureFolder(BehaviorFolder);
            var actions = CreateActions();
            ConfigurePrefab(actions);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Desktop-pet autonomous behaviors and facial expressions installed: " + actions.Count + " actions.");
        }

        private static void ConfigureBodyStates(AnimatorController controller)
        {
            var machine = controller.layers[0].stateMachine;
            var drag = machine.states.FirstOrDefault(child => child.state.name == "DragStart").state;
            if (drag == null) throw new InvalidOperationException("DragStart state is missing from the base Animator layer.");
            int column = 0;
            foreach (var pair in BodyStates)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(pair.Value);
                var state = machine.states.FirstOrDefault(child => child.state.name == pair.Key).state;
                if (state == null) state = machine.AddState(pair.Key,
                    new Vector3(250f + column % 4 * 250f, 380f + column / 4 * 90f));
                state.motion = clip;
                state.speed = pair.Key == "SurfaceWalk" ? 0.68f : 1f;
                state.speedParameterActive = pair.Key == "SurfaceWalk";
                state.speedParameter = pair.Key == "SurfaceWalk" ? "SurfaceSpeed" : string.Empty;
                state.writeDefaultValues = true;
                foreach (var transition in state.transitions.ToArray()) state.RemoveTransition(transition);
                var pickup = state.AddTransition(drag);
                pickup.hasExitTime = false;
                pickup.hasFixedDuration = true;
                pickup.duration = 0.1f;
                pickup.AddCondition(AnimatorConditionMode.If, 0f, "IsDragging");
                EditorUtility.SetDirty(state);
                column++;
            }
            EditorUtility.SetDirty(machine);
        }

        private static void ConfigureFaceLayer(AnimatorController controller)
        {
            int oldIndex = Array.FindIndex(controller.layers, layer => layer.name == "Face");
            if (oldIndex >= 0) controller.RemoveLayer(oldIndex);
            controller.AddLayer("Face");
            var layers = controller.layers;
            var layer = layers[layers.Length - 1];
            layer.defaultWeight = 1f;
            layer.blendingMode = AnimatorLayerBlendingMode.Override;
            layers[layers.Length - 1] = layer;
            controller.layers = layers;
            var machine = layer.stateMachine;
            int index = 0;
            foreach (var pair in FaceStates)
            {
                var state = machine.AddState(pair.Key, new Vector3(250f + index % 3 * 230f, 40f + index / 3 * 90f));
                state.motion = AssetDatabase.LoadAssetAtPath<AnimationClip>(pair.Value);
                state.writeDefaultValues = true;
                if (pair.Key == "FaceNeutral") machine.defaultState = state;
                index++;
            }
            EditorUtility.SetDirty(machine);
        }

        private static List<PetAnimationAction> CreateActions()
        {
            var definitions = new[]
            {
                Def("FreeTired", "Tired", PetActionLocation.Free, 3.2f, 24f, 8f,
                    "TiredEnter", "TiredLoop", "", 1.3f, 5f, 9f, 0f, .18f),
                Def("FreeStandSleep", "StandSleep", PetActionLocation.Free, 2.2f, 42f, 14f,
                    "StandSleepEnter", "StandSleepLoop", "StandSleepExit", 1.633f, 8f, 15f, 7.967f, .2f),
                Def("FreeYawn", "Yawn", PetActionLocation.Free, 2.5f, 30f, 10f,
                    "Yawn", "", "", 5.3f, .1f, .1f, 0f, .16f),
                Def("FreeDeepSleep", "DeepSleep", PetActionLocation.Free, .55f, 90f, 25f,
                    "DeepSleepEnter", "DeepSleepLoop", "DeepSleepExit", 2.6f, 10f, 18f, 6.827f, .22f),
                Surface("SurfaceWalk", "SurfaceWalk", 3.4f, 25f, 8f,
                    "", "SurfaceWalk", "", 0f, 5f, 10f, 0f, .2f, 120f, false),
                Surface("SurfaceWall", "SurfaceWall", 1.3f, 38f, 9f,
                    "SurfaceWallEnter", "SurfaceWallLoop", "SurfaceWallExit", .933f, 4f, 8f, 1.267f, .18f, 0f, true),
                Def("SurfaceJump", "SurfaceJump", PetActionLocation.AttachedToWindowTop, .8f, 35f, 12f,
                    "SurfaceJump", "", "", .767f, .1f, .1f, 0f, .12f)
            };
            var result = new List<PetAnimationAction>();
            foreach (var definition in definitions) result.Add(CreateOrUpdateAction(definition));
            return result;
        }

        private static ActionDefinition Def(string file, string id, PetActionLocation location,
            float weight, float cooldown, float idle, string entry, string loop, string exit,
            float entrySeconds, float loopMin, float loopMax, float exitSeconds, float fade)
        {
            return new ActionDefinition { fileName = file, id = id, location = location, weight = weight,
                cooldown = cooldown, idle = idle, entry = entry, loop = loop, exit = exit,
                entrySeconds = entrySeconds, loopMin = loopMin, loopMax = loopMax,
                exitSeconds = exitSeconds, fade = fade };
        }

        private static ActionDefinition Surface(string file, string id, float weight, float cooldown,
            float idle, string entry, string loop, string exit, float entrySeconds, float loopMin,
            float loopMax, float exitSeconds, float fade, float speed, bool edge)
        {
            var value = Def(file, id, PetActionLocation.AttachedToWindowTop, weight, cooldown, idle,
                entry, loop, exit, entrySeconds, loopMin, loopMax, exitSeconds, fade);
            value.moves = speed != 0f;
            value.speed = speed;
            value.edge = edge;
            value.edgeMargin = 90;
            return value;
        }

        private static PetAnimationAction CreateOrUpdateAction(ActionDefinition definition)
        {
            string path = BehaviorFolder + "/" + definition.fileName + ".asset";
            var action = AssetDatabase.LoadAssetAtPath<PetAnimationAction>(path);
            if (action == null)
            {
                action = ScriptableObject.CreateInstance<PetAnimationAction>();
                AssetDatabase.CreateAsset(action, path);
            }
            var serialized = new SerializedObject(action);
            Set(serialized, "actionId", definition.id);
            serialized.FindProperty("location").enumValueIndex = (int)definition.location;
            Set(serialized, "weight", definition.weight);
            Set(serialized, "cooldownSeconds", definition.cooldown);
            Set(serialized, "minimumIdleSeconds", definition.idle);
            Set(serialized, "allowedInQuietMode", definition.quiet);
            Set(serialized, "entryState", definition.entry);
            Set(serialized, "loopState", definition.loop);
            Set(serialized, "exitState", definition.exit);
            Set(serialized, "entrySeconds", definition.entrySeconds);
            serialized.FindProperty("loopSeconds").vector2Value = new Vector2(definition.loopMin, definition.loopMax);
            Set(serialized, "exitSeconds", definition.exitSeconds);
            Set(serialized, "crossFadeSeconds", definition.fade);
            Set(serialized, "movesAlongSurface", definition.moves);
            Set(serialized, "surfaceSpeedPixelsPerSecond", definition.speed);
            Set(serialized, "requiresSurfaceEdge", definition.edge);
            Set(serialized, "surfaceEdgeMarginPixels", definition.edgeMargin);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(action);
            return action;
        }

        private static void ConfigurePrefab(IReadOnlyList<PetAnimationAction> actions)
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var director = root.GetComponent<PetBehaviorDirector>() ?? root.AddComponent<PetBehaviorDirector>();
                if (root.GetComponent<PetSurfaceMotionController>() == null) root.AddComponent<PetSurfaceMotionController>();
                if (root.GetComponent<PetExpressionController>() == null) root.AddComponent<PetExpressionController>();
                var serialized = new SerializedObject(director);
                var list = serialized.FindProperty("actions");
                list.arraySize = actions.Count;
                for (int i = 0; i < actions.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = actions[i];
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void EnsureFloatParameter(AnimatorController controller, string name)
        {
            if (controller.parameters.Any(parameter => parameter.name == name)) return;
            controller.AddParameter(name, AnimatorControllerParameterType.Float);
        }

        private static void EnsureFolder(string path)
        {
            string current = "Assets";
            foreach (var part in path.Split('/').Skip(1))
            {
                string next = current + "/" + part;
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, part);
                current = next;
            }
        }

        private static void Set(SerializedObject serialized, string name, string value) => serialized.FindProperty(name).stringValue = value;
        private static void Set(SerializedObject serialized, string name, float value) => serialized.FindProperty(name).floatValue = value;
        private static void Set(SerializedObject serialized, string name, int value) => serialized.FindProperty(name).intValue = value;
        private static void Set(SerializedObject serialized, string name, bool value) => serialized.FindProperty(name).boolValue = value;
    }
}
