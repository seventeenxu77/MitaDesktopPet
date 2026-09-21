using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DesktopPetEditor
{
    public static class DesktopPetCrossLegAuthoring
    {
        public const string ClipPath = "Assets/DesktopPet/Animations/SitCrossLeg.anim";
        public const string LoopPath = "Assets/DesktopPet/Animations/SitCrossLegLoop.anim";
        public const string ReviewFolder = "Library/DesktopPetCrossLegReview";
        // Keep the approved crossing tempo; only the free leg keeps settling afterwards.
        public const float CrossingDuration = .95f, EnterDuration = 2.3f, LoopDuration = 12f;
        public const float Duration = EnterDuration;
        private const string SitPath = "Assets/AnimationClip/Mita Sit Normal.anim";

        public static void Inspect()
        {
            Directory.CreateDirectory(ReviewFolder);
            using (var view = new DesktopPetDragReview.ReviewScene())
            {
                var sit = AssetDatabase.LoadAssetAtPath<AnimationClip>(SitPath);
                sit.SampleAnimation(view.Pet, 0);
                var output = new StringBuilder("Sit clip length: " + sit.length + "\n");
                foreach (var bone in view.Pet.GetComponentsInChildren<Transform>())
                    if (bone.name == "Hips" || bone.name.Contains(" leg") || bone.name.Contains(" knee") ||
                        bone.name.Contains(" ankle") || bone.name.Contains(" toe") || bone.name.Contains("wrist"))
                        output.AppendLine(bone.name + " pos=" + bone.position.ToString("F5") + " rot=" + bone.rotation.ToString("F5"));
                foreach (var skin in view.Pet.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var weights = skin.sharedMesh.boneWeights;
                    for (int i = 0; i < skin.bones.Length; i++)
                    {
                        if (skin.bones[i] == null || !skin.bones[i].name.Contains(" toe")) continue;
                        int count = weights.Count(w => (w.boneIndex0 == i && w.weight0 > .01f) ||
                            (w.boneIndex1 == i && w.weight1 > .01f) || (w.boneIndex2 == i && w.weight2 > .01f) ||
                            (w.boneIndex3 == i && w.weight3 > .01f));
                        output.AppendLine(skin.name + " " + skin.bones[i].name + " weighted vertices=" + count);
                    }
                }
                File.WriteAllText(ReviewFolder + "/rig.txt", output.ToString());
                view.Render(ReviewFolder + "/normal.png");
            }
        }

        public static void GenerateAndReview()
        {
            Generate();
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/DesktopPet/Animators/DesktopMita.controller");
            ConfigureStates(controller);
            AssetDatabase.SaveAssets();
            RenderReview();
            DesktopPetCrossLegReview.Run();
        }

        public static void Generate()
        {
            BakeClip(ClipPath, EnterDuration, false);
            BakeClip(LoopPath, LoopDuration, true);
        }

        private static void BakeClip(string path, float duration, bool loop)
        {
            var sit = AssetDatabase.LoadAssetAtPath<AnimationClip>(SitPath);
            using (var view = new DesktopPetDragReview.ReviewScene())
            {
                var rig = new SeatedRig(view.Pet, sit);
                var curves = new Dictionary<Transform, AnimationCurve[]>();
                var previous = new Dictionary<Transform, Quaternion>();
                foreach (var bone in rig.Bones) curves[bone] = Enumerable.Range(0, 7).Select(_ => new AnimationCurve()).ToArray();
                int frames = Mathf.RoundToInt(duration * 60);
                for (int frame = 0; frame <= frames; frame++)
                {
                    float time = duration * frame / frames;
                    if (loop) rig.PoseLoop(time); else rig.Pose(time);
                    foreach (var bone in rig.Bones)
                    {
                        Quaternion q = bone.localRotation;
                        if (previous.TryGetValue(bone, out var last) && Quaternion.Dot(q, last) < 0)
                            q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                        previous[bone] = q;
                        Vector3 p = bone.localPosition;
                        float[] values = { q.x, q.y, q.z, q.w, p.x, p.y, p.z };
                        for (int k = 0; k < 7; k++) curves[bone][k].AddKey(time, values[k]);
                    }
                }
                var clip = new AnimationClip { name = loop ? "SitCrossLegLoop" : "SitCrossLeg", frameRate = 60 };
                string[] properties = { "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w",
                    "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z" };
                var bindings = new List<EditorCurveBinding>();
                var data = new List<AnimationCurve>();
                foreach (var pair in curves)
                    for (int k = 0; k < 7; k++)
                    {
                        var curve = pair.Value[k];
                        if (curve.keys.All(key => Mathf.Abs(key.value - curve.keys[0].value) < 0.000001f))
                            curve = new AnimationCurve(new Keyframe(0, curve.keys[0].value), new Keyframe(duration, curve.keys[0].value));
                        for (int j = 0; j < curve.length; j++)
                        {
                            AnimationUtility.SetKeyLeftTangentMode(curve, j, AnimationUtility.TangentMode.ClampedAuto);
                            AnimationUtility.SetKeyRightTangentMode(curve, j, AnimationUtility.TangentMode.ClampedAuto);
                        }
                        // Match velocity as well as position across the hold loop.
                        float seamTangent = loop && curve.length>2 ?
                            (curve.keys[1].value-curve.keys[curve.length-2].value)/(2*duration/frames) : 0;
                        AnimationUtility.SetKeyLeftTangentMode(curve,0,AnimationUtility.TangentMode.Free);
                        AnimationUtility.SetKeyRightTangentMode(curve,0,AnimationUtility.TangentMode.Free);
                        AnimationUtility.SetKeyLeftTangentMode(curve,curve.length-1,AnimationUtility.TangentMode.Free);
                        AnimationUtility.SetKeyRightTangentMode(curve,curve.length-1,AnimationUtility.TangentMode.Free);
                        var first = curve.keys[0]; first.inTangent = first.outTangent = seamTangent; curve.MoveKey(0, first);
                        var lastKey = curve.keys[curve.length-1]; lastKey.inTangent = lastKey.outTangent = seamTangent; curve.MoveKey(curve.length-1, lastKey);
                        bindings.Add(EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(pair.Key, view.Pet.transform),
                            typeof(Transform), properties[k]));
                        data.Add(curve);
                    }
                AnimationUtility.SetEditorCurves(clip, bindings.ToArray(), data.ToArray());
                clip.EnsureQuaternionContinuity();
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.startTime = 0; settings.stopTime = duration; settings.loopTime = loop;
                settings.keepOriginalOrientation = settings.keepOriginalPositionXZ = settings.keepOriginalPositionY = true;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (existing == null) AssetDatabase.CreateAsset(clip, path);
                else { EditorUtility.CopySerialized(clip, existing); EditorUtility.SetDirty(existing); Object.DestroyImmediate(clip); }
            }
        }

        public static void ConfigureStates(AnimatorController controller)
        {
            var machine = controller.layers[0].stateMachine;
            var sit = machine.states.First(s => s.state.name == "SitLoop").state;
            var idle = machine.states.First(s => s.state.name == "Idle").state;
            var drag = machine.states.First(s => s.state.name == "DragStart").state;
            foreach (var transition in sit.transitions.Where(t => t.destinationState != null &&
                (t.destinationState.name == "SitCrossLeg" || t.destinationState.name == "SitCrossLegLoop" || t.destinationState.name == "SitRest")).ToArray())
                sit.RemoveTransition(transition);
            foreach (var state in machine.states.Where(s => s.state.name == "SitCrossLeg" || s.state.name == "SitCrossLegLoop" || s.state.name == "SitRest").ToArray())
                machine.RemoveState(state.state);
            var cross = machine.AddState("SitCrossLeg", new Vector3(1000, 180));
            var rest = machine.AddState("SitCrossLegLoop", new Vector3(1250, 180));
            cross.motion = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            rest.motion = AssetDatabase.LoadAssetAtPath<AnimationClip>(LoopPath);
            var start = sit.AddTransition(cross);
            start.hasExitTime = true; start.exitTime = 2f / ((AnimationClip)sit.motion).length;
            start.hasFixedDuration = true; start.duration = .04f;
            start.AddCondition(AnimatorConditionMode.If, 0, "IsSitting");
            start.AddCondition(AnimatorConditionMode.IfNot, 0, "IsDragging");
            foreach (var state in new[] {cross, rest})
            {
                var pickup = state.AddTransition(drag);
                pickup.hasExitTime = false; pickup.hasFixedDuration = true; pickup.duration = .12f;
                pickup.AddCondition(AnimatorConditionMode.If, 0, "IsDragging");
                var detach = state.AddTransition(idle);
                detach.hasExitTime = false; detach.hasFixedDuration = true; detach.duration = .15f;
                detach.AddCondition(AnimatorConditionMode.IfNot, 0, "IsSitting");
                detach.AddCondition(AnimatorConditionMode.IfNot, 0, "IsDragging");
            }
            var finish = cross.AddTransition(rest);
            finish.hasExitTime = true; finish.exitTime = 1; finish.hasFixedDuration = true; finish.duration = .04f;
            finish.AddCondition(AnimatorConditionMode.If, 0, "IsSitting");
            finish.AddCondition(AnimatorConditionMode.IfNot, 0, "IsDragging");
            EditorUtility.SetDirty(machine); EditorUtility.SetDirty(controller);
        }

        public static void RenderReview()
        {
            Directory.CreateDirectory(ReviewFolder);
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            using (var view = new DesktopPetDragReview.ReviewScene())
            {
                foreach (float t in new[] {0f, .15f, .3f, .45f, .6f, .75f, .85f, Duration})
                {
                    clip.SampleAnimation(view.Pet, t);
                    view.Render(ReviewFolder + "/front-" + t.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + ".png");
                }
                clip.SampleAnimation(view.Pet, EnterDuration);
                var pivot = new Vector3(0, .72f, -.85f);
                view.Camera.transform.position = pivot + new Vector3(3, .3f, 4);
                view.Camera.transform.LookAt(pivot);
                view.Render(ReviewFolder + "/three-quarter.png");
                view.Camera.transform.position = pivot + new Vector3(5, 0, 0);
                view.Camera.transform.LookAt(pivot);
                view.Render(ReviewFolder + "/side.png");
            }
        }

        public sealed class SeatedRig
        {
            public readonly Transform[] Bones;
            private readonly Dictionary<string, Transform> names;
            private readonly Dictionary<Transform, Quaternion> rotations;
            private readonly Dictionary<Transform, Vector3> positions;
            private readonly Dictionary<Transform, Quaternion> world;
            private readonly Dictionary<Transform, Vector3> points;
            public SeatedRig(GameObject pet, AnimationClip sit)
            {
                sit.SampleAnimation(pet, 0);
                Bones = pet.transform.Find("Armature").GetComponentsInChildren<Transform>();
                names = Bones.GroupBy(b => b.name).ToDictionary(g => g.Key, g => g.First());
                rotations = Bones.ToDictionary(b => b, b => b.localRotation);
                positions = Bones.ToDictionary(b => b, b => b.localPosition);
                world = Bones.ToDictionary(b => b, b => b.rotation);
                points = Bones.ToDictionary(b => b, b => b.position);
            }
            public Transform Bone(string name) => names[name];
            private void Aim(string boneName, string childName, Vector3 direction)
            {
                var bone = Bone(boneName);
                var rest = points[Bone(childName)] - points[bone];
                bone.rotation = Quaternion.FromToRotation(rest.normalized, direction.normalized) * world[bone];
            }
            private static float Ease(float t) { t = Mathf.Clamp01(t); return t*t*t*(t*(t*6-15)+10); }
            private static float Pulse(float t, float start, float rise, float fall)
            {
                if (t <= start || t >= start+rise+fall) return 0;
                return t < start+rise ? Ease((t-start)/rise) : 1-Ease((t-start-rise)/fall);
            }
            private static float Clearance(float progress)
            {
                // Preserve the original early arc, then match its value/velocity
                // into a soft landing. sin(pi*t) alone hits the end at full speed.
                const float landing = .7f;
                if (progress <= landing) return Mathf.Sin(Mathf.PI*progress);
                float u = (progress-landing)/(1-landing);
                float value = Mathf.Sin(Mathf.PI*landing);
                float slope = Mathf.PI*Mathf.Cos(Mathf.PI*landing)*(1-landing);
                return (2*u*u*u-3*u*u+1)*value + (u*u*u-2*u*u+u)*slope;
            }
            public void Pose(float seconds)
            {
                foreach (var bone in Bones) { bone.localRotation = rotations[bone]; bone.localPosition = positions[bone]; }
                float progress = Mathf.Clamp01(seconds / CrossingDuration);
                float blend = Ease(progress);
                if (blend <= 0) return;
                float crossing = Ease((progress-.22f)/.78f);
                float lift = Clearance(progress);
                // Original timing/clearance arc, with the approved forward-facing
                // target pose. Timing must not imply restoring the sideways pose.
                Aim("Right leg", "Right knee", Vector3.Slerp(points[Bone("Right knee")]-points[Bone("Right leg")],
                    new Vector3(-.09f, -.10f, .423f), blend));
                Aim("Right knee", "Right ankle", Vector3.Slerp(points[Bone("Right ankle")]-points[Bone("Right knee")],
                    new Vector3(-.085f,-.46f,.035f), blend));
                Aim("Right ankle", "Right toe", Vector3.Slerp(points[Bone("Right toe")]-points[Bone("Right ankle")],
                    new Vector3(0,-.10f,.13f), blend));
                var baseThigh = (points[Bone("Left knee")]-points[Bone("Left leg")]).normalized;
                var crossedThigh = new Vector3(.135f, .145f, .398f).normalized;
                var thigh = Vector3.Slerp(baseThigh, crossedThigh, crossing) + Vector3.up * (.55f*lift);
                Aim("Left leg", "Left knee", thigh);
                var lower = new Vector3(0, -.445f, .29f + .38f*lift);
                Aim("Left knee", "Left ankle", Vector3.Slerp(points[Bone("Left ankle")]-points[Bone("Left knee")], lower, blend));
                var footDirection = new Vector3(0,-.01f,.15f);
                Aim("Left ankle", "Left toe", Vector3.Slerp(points[Bone("Left toe")]-points[Bone("Left ankle")], footDirection, blend));
                float toeLift = 4f;
                var toe = Bone("Left toe");
                var toeHinge = Vector3.Cross(Vector3.up, toe.position - Bone("Left ankle").position).normalized;
                toe.rotation = Quaternion.AngleAxis(-toeLift*blend, toeHinge) * toe.rotation;
                // Hands make room for the crossing thigh without moving the torso
                // or the screen-space seating anchor.
                MoveHand("Left", Vector3.up * (.07f*blend + .05f*lift));
                MoveHand("Right", Vector3.up * (.07f*blend + .05f*lift));
                // The thigh is already seated while shin, ankle and toes finish
                // successively smaller, delayed arcs. Every pulse has zero end velocity.
                // Stay on the clear, forward side of the support thigh while
                // settling; the ankle can overshoot without pushing the shin through it.
                float shin = -2.5f*Pulse(seconds,.72f,.31f,.39f)-.65f*Pulse(seconds,1.18f,.25f,.37f);
                float foot = 8f*Pulse(seconds,.79f,.36f,.52f)-2f*Pulse(seconds,1.43f,.24f,.40f);
                float toes = 2f*Pulse(seconds,.88f,.38f,.5f)-.7f*Pulse(seconds,1.56f,.25f,.44f);
                FollowThrough(shin,foot,toes);
            }
            public void PoseLoop(float seconds)
            {
                Pose(EnterDuration);
                // Two unhurried gestures separated by actual rests. Sagittal-only
                // swing keeps the knee and sole facing the screen. The ankle follows
                // the calf 180 ms later, with an additional soft dorsiflexion.
                float foot = .65f*CalfSwing(seconds-.18f)
                    -15f*Pulse(seconds,1.25f,1.3f,1.4f)+3f*Pulse(seconds,3.6f,.8f,.95f)
                    -17f*Pulse(seconds,7.2f,1.2f,1.45f)+3f*Pulse(seconds,9.2f,.9f,1f);
                float toes = -5f*Pulse(seconds,1.48f,1.2f,1.4f)
                    -6f*Pulse(seconds,7.45f,1.15f,1.4f);
                FollowThrough(CalfSwing(seconds),foot,toes);
            }
            private static float CalfSwing(float t)
            {
                // Forward swing and unhurried return, never behind the approved
                // resting shin (that region overlaps the supporting thigh).
                return -7f*Pulse(t,1f,1.25f,1.25f)
                    -5.5f*Pulse(t,7f,1.05f,1.4f);
            }
            private void FollowThrough(float shin, float foot, float toes)
            {
                var ankle = Bone("Left ankle");
                var originalFoot = ankle.rotation;
                var knee = Bone("Left knee");
                knee.rotation = Quaternion.AngleAxis(shin,Vector3.right)*knee.rotation;
                // Set the foot's world pitch independently so it lags the calf,
                // instead of being rigidly carried through the exact same angle.
                ankle.rotation = Quaternion.AngleAxis(foot,Vector3.right)*originalFoot;
                var toe = Bone("Left toe");
                toe.rotation = Quaternion.AngleAxis(toes,Vector3.right)*toe.rotation;
            }

            private void MoveHand(string side, Vector3 offset)
            {
                var arm=Bone(side+" arm"); var elbow=Bone(side+" elbow"); var wrist=Bone(side+" wrist");
                var start=arm.position; var target=points[wrist]+offset;
                float upper=Vector3.Distance(points[arm],points[elbow]);
                float lower=Vector3.Distance(points[elbow],points[wrist]);
                var axis=(target-start).normalized;
                float d=Mathf.Clamp(Vector3.Distance(start,target),Mathf.Abs(upper-lower)+.001f,upper+lower-.001f);
                float along=(upper*upper-lower*lower+d*d)/(2*d);
                var hint=points[elbow]-start; var bend=(hint-axis*Vector3.Dot(hint,axis)).normalized;
                var middle=start+axis*along+bend*Mathf.Sqrt(Mathf.Max(0,upper*upper-along*along));
                Aim(side+" arm",side+" elbow",middle-start);
                Aim(side+" elbow",side+" wrist",target-elbow.position);
                wrist.rotation=world[wrist];
            }
        }
    }
}
