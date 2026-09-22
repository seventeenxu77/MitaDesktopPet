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
        public const float ArmPeriod = 7.2f, LegPeriod = 2.4f;
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
                // Apply last so the chest, shoulders, arms and head ride the
                // torso instead of having their world-space targets cancel it.
                var pitchAxis=yaw*Vector3.right;
                Bone("Spine").rotation=Quaternion.AngleAxis(8*Mathf.Sin(2*p-.25f)+1.5f*Mathf.Sin(p),pitchAxis)*
                    Quaternion.AngleAxis(2*Mathf.Sin(p-.4f),yaw*Vector3.forward)*Bone("Spine").rotation;
                Bone("Chest").rotation=Quaternion.AngleAxis(4*Mathf.Sin(2*p-.70f),pitchAxis)*Bone("Chest").rotation;
            }

            private static float Ease(float t) { t=Mathf.Clamp01(t); return t*t*t*(t*(t*6-15)+10); }
            private void PoseArm(string side, float sign, float seconds)
            {
                float phase=Mathf.Repeat(seconds/ArmPeriod,1);
                var downUpper=new Vector3(sign*.22f,-1,.12f).normalized;
                var downLower=new Vector3(sign*.10f,-1,.38f).normalized;
                var torso=Quaternion.Inverse(yaw)*(Bone("Neck2").position-Bone("Chest").position).normalized;
                // Open below the shoulders along the hanging torso, never level
                // with the screen. Elbows stay outside the imaginary held object.
                var levelUpper=new Vector3(sign*.72f,torso.y*.80f,torso.z+.10f).normalized;
                var levelLower=new Vector3(sign*.34f,torso.y*.75f,torso.z+.42f).normalized;
                var hugUpper=new Vector3(sign*.50f,torso.y*.90f,torso.z+.08f).normalized;
                var hugLower=new Vector3(-sign*.62f,-.55f,.65f).normalized;
                Vector3 upperDirection,lowerDirection;
                float reach=Ease((phase-.10f)/.24f)*(1-Ease((phase-.78f)/.22f));
                float searching=phase>=.49f && phase<.78f ? (phase-.49f)/.29f : 0;
                float searchWeight=Mathf.Pow(Mathf.Sin(searching*Mathf.PI),2);
                if(phase<.34f)
                {
                    float t=Ease((phase-.10f)/.24f);
                    upperDirection=Vector3.Slerp(downUpper,levelUpper,t);
                    lowerDirection=Vector3.Slerp(downLower,levelLower,t);
                }
                else if(phase<.49f)
                {
                    float t=Ease((phase-.34f)/.15f);
                    upperDirection=Vector3.Slerp(levelUpper,hugUpper,t);
                    lowerDirection=Vector3.Slerp(levelLower,hugLower,t);
                }
                else if(phase<.78f)
                {
                    // Soft, offset attempts to catch something: not mirrored flaps.
                    float lateral=Mathf.Sin(searching*Mathf.PI*2*1.25f+sign*.35f);
                    float vertical=Mathf.Sin(searching*Mathf.PI*2*1.5f-sign*.3f);
                    upperDirection=hugUpper+searchWeight*new Vector3(.09f*lateral,.11f*vertical,0);
                    lowerDirection=hugLower+searchWeight*new Vector3(.14f*lateral,.14f*vertical,.03f*vertical);
                }
                else
                {
                    float t=Ease((phase-.78f)/.22f);
                    upperDirection=Vector3.Slerp(hugUpper,downUpper,t);
                    lowerDirection=Vector3.Slerp(hugLower,downLower,t);
                }
                PoseArmChain(side,yaw*upperDirection,yaw*lowerDirection);
                string suffix = side == "Left" ? "_L" : "_R";
                var elbow=Bone(side+" elbow"); var wrist=Bone(side+" wrist");
                float grasp=searchWeight*(.55f+.45f*Mathf.Sin(searching*Mathf.PI*4-sign*.3f));
                var palmNormal=Vector3.Cross(worldPositions[Bone("IndexFinger1"+suffix)]-worldPositions[Bone("LittleFinger1"+suffix)],
                    worldPositions[Bone("MiddleFinger1"+suffix)]-worldPositions[wrist]).normalized;
                if(Vector3.Dot(palmNormal,Vector3.down)<0) palmNormal=-palmNormal;
                var forearmAxis=(wrist.position-elbow.position).normalized;
                var forearmDelta=elbow.rotation*Quaternion.Inverse(world[elbow]);
                var palm=Vector3.ProjectOnPlane(forearmDelta*palmNormal,forearmAxis);
                var inward=Vector3.ProjectOnPlane(yaw*new Vector3(-sign,-.08f,.10f),forearmAxis);
                // Forearm supination turns the palms toward the held object.
                // This is axial rotation, not wrist bending; the elbow and wrist
                // positions and the elbow's anatomical flexion are unchanged.
                float twist=Vector3.SignedAngle(palm,inward,forearmAxis);
                elbow.rotation=Quaternion.AngleAxis(twist,forearmAxis)*elbow.rotation;
                var restHandForward=worldPositions[Bone("MiddleFinger1"+suffix)]-worldPositions[wrist];
                wrist.rotation=Quaternion.LookRotation(forearmAxis,inward)*
                    Quaternion.Inverse(Quaternion.LookRotation(restHandForward,palmNormal))*world[wrist];
                foreach (string finger in new[] {"IndexFinger", "MiddleFinger", "RingFinger", "LittleFinger"})
                    for (int joint = 1; joint <= 3; joint++)
                    {
                        var bone = Bone(finger + joint + suffix);
                        var next=Bone(finger+(joint==3?2:joint+1)+suffix);
                        var direction=joint==3 ? worldPositions[bone]-worldPositions[next] : worldPositions[next]-worldPositions[bone];
                        var axis=Quaternion.Inverse(world[bone])*Vector3.Cross(direction,palmNormal).normalized;
                        float curl=Mathf.Lerp(joint==1?18:24,joint==1?5:9,reach)+grasp*(joint==1?22:30);
                        bone.localRotation = local[bone] * Quaternion.AngleAxis(curl,axis);
                    }
            }

            private void PoseArmChain(string side,Vector3 upperDirection,Vector3 lowerDirection)
            {
                var arm=Bone(side+" arm"); var elbow=Bone(side+" elbow"); var wrist=Bone(side+" wrist");
                var normal=Vector3.Cross(upperDirection,lowerDirection).normalized;
                var restUpper=worldPositions[elbow]-worldPositions[arm];
                var restLower=worldPositions[wrist]-worldPositions[elbow];
                // Use the rig's actual elbow bend, not an assumed world-forward
                // plane. Both bones share this plane and always bend its same way.
                var restNormal=Vector3.Cross(restUpper,restLower).normalized;
                arm.rotation=Quaternion.LookRotation(upperDirection,normal)*
                    Quaternion.Inverse(Quaternion.LookRotation(restUpper,restNormal))*world[arm];
                elbow.rotation=Quaternion.LookRotation(lowerDirection,normal)*
                    Quaternion.Inverse(Quaternion.LookRotation(restLower,restNormal))*world[elbow];
            }

            private void PoseLeg(string side, float sign, float phase)
            {
                float wave = Mathf.Sin(phase);
                float follow=Mathf.Sin(phase-.65f);
                Aim(side + " leg", side + " knee", yaw * new Vector3(sign*.22f,-.87f+.16f*wave,-.48f-.18f*wave));
                Aim(side + " knee", side + " ankle", yaw * new Vector3(sign*.08f,-.78f+.55f*follow,-.42f-.30f*follow));
                var knee=Bone(side+" knee"); var ankle=Bone(side+" ankle"); var toe=Bone(side+" toe");
                var restShin=worldPositions[ankle]-worldPositions[knee];
                var restFoot=worldPositions[toe]-worldPositions[ankle];
                var neutralShin=Quaternion.FromToRotation(restShin,(yaw*new Vector3(sign*.08f,-.78f,-.42f)).normalized)*world[knee];
                var neutralFoot=Quaternion.FromToRotation(restFoot,(yaw*new Vector3(sign*.035f,-.65f,-.40f)).normalized)*world[ankle];
                float delayed=Mathf.Sin(phase-.65f-.20f);
                var delayedShin=Quaternion.FromToRotation(restShin,(yaw*new Vector3(sign*.08f,-.78f+.55f*delayed,-.42f-.30f*delayed)).normalized)*world[knee];
                // Follow the shin, with ~76 ms of soft ankle lag. Keep the
                // ankle position inherited from its parent; never slide it.
                var flexAxis=Quaternion.Inverse(neutralFoot)*(yaw*Vector3.right);
                ankle.localRotation=Quaternion.Inverse(knee.rotation)*delayedShin*
                    Quaternion.Inverse(neutralShin)*neutralFoot*
                    Quaternion.AngleAxis(4*Mathf.Sin(phase-.9f),flexAxis);
                var toeAxis=Quaternion.Inverse(world[toe])*Vector3.right;
                toe.localRotation=local[toe]*Quaternion.AngleAxis(-2+3.5f*Mathf.Sin(phase-1.2f),toeAxis);
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
