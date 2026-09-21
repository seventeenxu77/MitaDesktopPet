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
        public const float Period = 7.2f;
        public const float ArmPeriod = 3.6f, LegPeriod = 2.4f;
        public const float LiftDuration = 0.55f;
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
                    // Smooth interior curves and matching periodic endpoint slopes.
                    for (int j = 0; j < curve.length; j++)
                    {
                        AnimationUtility.SetKeyLeftTangentMode(curve, j, AnimationUtility.TangentMode.ClampedAuto);
                        AnimationUtility.SetKeyRightTangentMode(curve, j, AnimationUtility.TangentMode.ClampedAuto);
                    }
                    float slope=loop && curve.length>2 ? (curve.keys[1].value-curve.keys[curve.length-2].value)/(2*duration/frames) : 0;
                    foreach(int endpoint in new[]{0,curve.length-1})
                    {
                        AnimationUtility.SetKeyLeftTangentMode(curve,endpoint,AnimationUtility.TangentMode.Free);
                        AnimationUtility.SetKeyRightTangentMode(curve,endpoint,AnimationUtility.TangentMode.Free);
                        var key=curve.keys[endpoint]; key.inTangent=key.outTangent=slope; curve.MoveKey(endpoint,key);
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
                float breath = Mathf.Sin(p);
                var hips = Bone("Hips");
                hips.position = worldPositions[hips] + Vector3.up * 0.14f;
                AimBody("Hips", "Spine", new Vector3(0, -.90f, .44f));
                AimBody("Spine", "Chest", new Vector3(0, -.94f + .008f*breath, .34f));
                AimBody("Chest", "Neck2", new Vector3(0, -.96f + .008f*breath, .28f));
                // Spread head compensation across both neck joints. The face
                // remains readable instead of rolling with the pelvis.
                WorldTilt("Neck2", yaw * Quaternion.Euler(112, 0, 0));
                WorldTilt("Neck1", yaw * Quaternion.Euler(82, 0, 0));
                WorldTilt("Head", Quaternion.Euler(52 + 2 * Mathf.Sin(p - .5f), 22, -3));

                PoseArm("Left", -1, seconds);
                PoseArm("Right", 1, seconds-.22f);
                float kick=seconds/LegPeriod*Mathf.PI*2;
                PoseLeg("Left", -1, kick);
                PoseLeg("Right", 1, kick + Mathf.PI);
            }

            private static float Ease(float t) { t=Mathf.Clamp01(t); return t*t*t*(t*(t*6-15)+10); }
            private void PoseArm(string side, float sign, float seconds)
            {
                float phase=Mathf.Repeat(seconds/ArmPeriod,1);
                float reach=phase<.15f ? 0 : phase<.5f ? Ease((phase-.15f)/.35f) : phase<.62f ? 1 : 1-Ease((phase-.62f)/.38f);
                // Gather near the chest, open the arms, reach, curl the fingers as
                // if trying to catch something, then recover slowly. Not a leg kick.
                var chest=Bone("Chest");
                var chestFront=chest.rotation*Quaternion.Inverse(world[chest])*Vector3.forward;
                var folded=chest.position+chestFront*.18f+yaw*new Vector3(-sign*.025f,sign*.03f,0);
                var extended=Bone(side+" arm").position+yaw*new Vector3(sign*.08f,-.22f,.36f);
                var target=Vector3.Lerp(folded,extended,reach)+yaw*Vector3.right*(sign*.22f*Mathf.Sin(Mathf.PI*reach));
                SolveArm(side,target,yaw*new Vector3(sign,-.25f,-.1f));
                string suffix = side == "Left" ? "_L" : "_R";
                Aim(side + " wrist", "MiddleFinger1" + suffix, yaw*Vector3.Slerp(new Vector3(-sign*.4f,.1f,-.7f),new Vector3(sign*.08f,-.28f,1),reach));
                float grasp=phase>=.5f && phase<.73f ? Mathf.Sin((phase-.5f)/.23f*Mathf.PI) : 0;
                foreach (string finger in new[] {"IndexFinger", "MiddleFinger", "RingFinger", "LittleFinger"})
                    for (int joint = 1; joint <= 3; joint++)
                    {
                        var bone = Bone(finger + joint + suffix);
                        float curl=Mathf.Lerp(joint==1?28:38,joint==1?8:12,reach)+grasp*(joint==1?18:25);
                        bone.localRotation = local[bone] * Quaternion.Euler(-6,0,-sign*curl);
                    }
            }

            private void SolveArm(string side,Vector3 target,Vector3 pole)
            {
                var arm=Bone(side+" arm"); var elbow=Bone(side+" elbow"); var wrist=Bone(side+" wrist");
                float upper=Vector3.Distance(worldPositions[arm],worldPositions[elbow]);
                float lower=Vector3.Distance(worldPositions[elbow],worldPositions[wrist]);
                var axis=(target-arm.position).normalized;
                float distance=Mathf.Clamp(Vector3.Distance(target,arm.position),Mathf.Abs(upper-lower)+.005f,upper+lower-.008f);
                float along=(upper*upper-lower*lower+distance*distance)/(2*distance);
                var bend=(pole-axis*Vector3.Dot(pole,axis)).normalized;
                var joint=arm.position+axis*along+bend*Mathf.Sqrt(Mathf.Max(0,upper*upper-along*along));
                Aim(side+" arm",side+" elbow",joint-arm.position);
                Aim(side+" elbow",side+" wrist",arm.position+axis*distance-elbow.position);
            }

            private void PoseLeg(string side, float sign, float phase)
            {
                float wave = Mathf.Sin(phase);
                float follow=Mathf.Sin(phase-.65f);
                Aim(side + " leg", side + " knee", yaw * new Vector3(sign*.22f,-.87f+.16f*wave,-.48f-.18f*wave));
                Aim(side + " knee", side + " ankle", yaw * new Vector3(sign*.08f,-.78f+.55f*follow,-.42f-.30f*follow));
                Aim(side + " ankle", side + " toe", yaw * new Vector3(sign*.035f,-.65f+.10f*Mathf.Sin(phase-1),-.40f));
            }

            public void Lift(float progress)
            {
                Pose(0);
                float weight = Ease(progress);
                foreach (var b in Bones)
                {
                    b.localRotation = Quaternion.Slerp(local[b], b.localRotation, weight);
                    b.localPosition = Vector3.Lerp(positions[b], b.localPosition, weight);
                }
            }
        }
    }
}
