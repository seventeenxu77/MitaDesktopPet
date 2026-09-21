using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DesktopPetEditor
{
    // All posing is done in character/world space against measured bind axes,
    // then baked into local quaternion/position tracks for the existing rig.
    public static class DesktopPetDragAuthoring
    {
        public const float Period = 1.0f;
        public const float LiftDuration = 0.30f;
        public const string Folder = "Assets/DesktopPet/Animations";

        public static void Generate(GameObject prefab)
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/DesktopPet", "Animations");
            using (var view = new DesktopPetDragReview.ReviewScene(prefab))
            {
                var rig = new PoseRig(view.Pet);
                Bake(rig, "DragLiftStart", LiftDuration, false);
                Bake(rig, "DragFlailLoop", Period, true);
            }
            AssetDatabase.SaveAssets();
        }

        private static void Bake(PoseRig rig, string name, float duration, bool loop)
        {
            const int fps = 60;
            int frames = Mathf.RoundToInt(duration * fps);
            var tracks = new Dictionary<Transform, AnimationCurve[]>();
            var previous = new Dictionary<Transform, Quaternion>();
            foreach (var bone in rig.Bones)
            {
                var curves = new AnimationCurve[7];
                for (int k = 0; k < curves.Length; k++) curves[k] = new AnimationCurve();
                tracks.Add(bone, curves);
            }
            for (int frame = 0; frame <= frames; frame++)
            {
                float time = duration * frame / frames;
                if (loop) rig.Pose(time);
                else rig.Lift(time / duration);
                foreach (var bone in rig.Bones)
                {
                    var q = bone.localRotation;
                    if (previous.TryGetValue(bone, out var last) && Quaternion.Dot(q, last) < 0)
                        q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                    previous[bone] = q;
                    var v = bone.localPosition;
                    var values = new[] {q.x, q.y, q.z, q.w, v.x, v.y, v.z};
                    for (int k = 0; k < values.Length; k++) tracks[bone][k].AddKey(time, values[k]);
                }
            }
            var clip = new AnimationClip {name = name, frameRate = fps};
            var bindings = new List<EditorCurveBinding>();
            var allCurves = new List<AnimationCurve>();
            string[] props = {"m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w", "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z"};
            foreach (var pair in tracks)
            {
                string path = AnimationUtility.CalculateTransformPath(pair.Key, rig.Root.transform);
                for (int k = 0; k < props.Length; k++)
                {
                    var curve = pair.Value[k];
                    bool constant = true;
                    var keys = curve.keys;
                    for (int j = 1; j < keys.Length; j++)
                        if (Mathf.Abs(keys[j].value - keys[0].value) > 0.000001f) { constant = false; break; }
                    if (constant) curve = new AnimationCurve(new Keyframe(0, keys[0].value), new Keyframe(duration, keys[0].value));
                    // Dense linear baking preserves the authored pose, including
                    // the exact duplicate end frame and periodic velocity.
                    for (int j = 0; j < curve.length; j++)
                    {
                        AnimationUtility.SetKeyLeftTangentMode(curve, j, AnimationUtility.TangentMode.Linear);
                        AnimationUtility.SetKeyRightTangentMode(curve, j, AnimationUtility.TangentMode.Linear);
                    }
                    bindings.Add(EditorCurveBinding.FloatCurve(path, typeof(Transform), props[k]));
                    allCurves.Add(curve);
                }
            }
            AnimationUtility.SetEditorCurves(clip, bindings.ToArray(), allCurves.ToArray());
            clip.EnsureQuaternionContinuity();
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.startTime = 0;
            settings.stopTime = duration;
            settings.loopTime = loop;
            settings.loopBlend = false;
            settings.keepOriginalOrientation = true;
            settings.keepOriginalPositionXZ = true;
            settings.keepOriginalPositionY = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            var pathOut = Folder + "/" + name + ".anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(pathOut);
            if (existing == null) AssetDatabase.CreateAsset(clip, pathOut);
            else { EditorUtility.CopySerialized(clip, existing); EditorUtility.SetDirty(existing); Object.DestroyImmediate(clip); }
        }

        public sealed class PoseRig
        {
            public readonly GameObject Root;
            public readonly Transform[] Bones;
            private readonly Dictionary<Transform, Quaternion> local = new Dictionary<Transform, Quaternion>();
            private readonly Dictionary<Transform, Vector3> positions = new Dictionary<Transform, Vector3>();
            private readonly Dictionary<Transform, Quaternion> world = new Dictionary<Transform, Quaternion>();
            private readonly Dictionary<Transform, Vector3> worldPositions = new Dictionary<Transform, Vector3>();
            private readonly Dictionary<string, Transform> names = new Dictionary<string, Transform>();
            private readonly Quaternion yaw = Quaternion.Euler(0, 30, 0);

            public PoseRig(GameObject root)
            {
                Root = root;
                Bones = root.transform.Find("Armature").GetComponentsInChildren<Transform>(true);
                foreach (var b in Bones)
                {
                    local[b] = b.localRotation;
                    positions[b] = b.localPosition;
                    world[b] = b.rotation;
                    worldPositions[b] = b.position;
                    if (!names.ContainsKey(b.name)) names[b.name] = b;
                }
            }

            public Transform Bone(string name) { return names[name]; }

            public void Reset()
            {
                foreach (var b in Bones) { b.localPosition = positions[b]; b.localRotation = local[b]; }
            }

            private void WorldTilt(string name, Quaternion rotation)
            {
                var b = Bone(name);
                b.rotation = rotation * world[b];
            }

            private void Aim(string name, string childName, Vector3 direction)
            {
                var bone = Bone(name);
                var restDirection = worldPositions[Bone(childName)] - worldPositions[bone];
                bone.rotation = Quaternion.FromToRotation(restDirection, direction.normalized) * world[bone];
            }

            private void AimBody(string name, string childName, Vector3 direction)
            {
                var bone = Bone(name);
                var restDirection = worldPositions[Bone(childName)] - worldPositions[bone];
                bone.rotation = yaw * Quaternion.FromToRotation(restDirection, direction.normalized) * world[bone];
            }

            public void Pose(float seconds)
            {
                Reset();
                float p = seconds / Period * Mathf.PI * 2;
                float flutter = Mathf.Sin(p * 2);
                var hips = Bone("Hips");
                hips.position = worldPositions[hips] + Vector3.up * 0.14f;
                AimBody("Hips", "Spine", new Vector3(0, -0.56f, 0.83f));
                AimBody("Spine", "Chest", new Vector3(0, -0.63f + 0.025f * flutter, 0.77f));
                AimBody("Chest", "Neck2", new Vector3(0, -0.70f + 0.025f * flutter, 0.71f));
                // Spread head compensation across both neck joints. The face
                // remains readable instead of rolling with the pelvis.
                WorldTilt("Neck2", yaw * Quaternion.Euler(77, 0, 0));
                WorldTilt("Neck1", yaw * Quaternion.Euler(48, 0, 0));
                WorldTilt("Head", Quaternion.Euler(22 + 2 * Mathf.Sin(2*p - 0.5f), 18, -4));

                PoseArm("Left", -1, p * 2);
                PoseArm("Right", 1, p * 2 + 0.65f);
                PoseLeg("Left", -1, p * 2);
                PoseLeg("Right", 1, p * 2 + Mathf.PI);
            }

            private void PoseArm(string side, float sign, float phase)
            {
                float wave = Mathf.Sin(phase);
                Aim(side + " arm", side + " elbow", yaw * new Vector3(sign * 0.36f, -0.80f + wave * 0.18f, 0.36f));
                Aim(side + " elbow", side + " wrist", yaw * new Vector3(sign * 0.20f, 0.38f + wave * 0.28f, 0.90f));
                string suffix = side == "Left" ? "_L" : "_R";
                Aim(side + " wrist", "MiddleFinger1" + suffix, yaw * new Vector3(sign * 0.08f, 0.04f, 1));
                // Compact fists keep the silhouette close to the reference.
                foreach (string finger in new[] {"IndexFinger", "MiddleFinger", "RingFinger", "LittleFinger"})
                    for (int joint = 1; joint <= 3; joint++)
                    {
                        var bone = Bone(finger + joint + suffix);
                        bone.localRotation = local[bone] * Quaternion.Euler(-12, 0, -sign * (joint == 1 ? 46 : 62));
                    }
            }

            private void PoseLeg(string side, float sign, float phase)
            {
                float wave = Mathf.Sin(phase);
                Aim(side + " leg", side + " knee", yaw * new Vector3(sign * 0.38f, -0.75f + wave * 0.15f, -0.45f - wave * 0.12f));
                Aim(side + " knee", side + " ankle", yaw * new Vector3(sign * 0.28f, 0.40f + wave * 0.24f, -0.85f));
                Aim(side + " ankle", side + " toe", yaw * new Vector3(sign * 0.07f, -0.20f, -0.95f));
            }

            public void Lift(float progress)
            {
                Pose(0);
                float weight = Mathf.SmoothStep(0, 1, progress);
                foreach (var b in Bones)
                {
                    b.localRotation = Quaternion.Slerp(local[b], b.localRotation, weight);
                    b.localPosition = Vector3.Lerp(positions[b], b.localPosition, weight);
                }
            }
        }
    }
}
