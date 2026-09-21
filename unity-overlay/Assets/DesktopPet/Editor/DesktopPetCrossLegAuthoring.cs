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
        public const float EnterDuration = .9f, LoopDuration = 4f;
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
                        // Authored endpoints are at rest, including the loop seam.
                        var first = curve.keys[0]; first.inTangent = first.outTangent = 0; curve.MoveKey(0, first);
                        var lastKey = curve.keys[curve.length-1]; lastKey.inTangent = lastKey.outTangent = 0; curve.MoveKey(curve.length-1, lastKey);
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
                foreach (float t in new[] {0f, .1f, .2f, .25f, .3f, .4f, .5f, .6f, .7f, Duration})
                {
                    clip.SampleAnimation(view.Pet, t);
                    view.Render(ReviewFolder + "/front-" + t.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + ".png");
                }
                clip.SampleAnimation(view.Pet, 2);
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
            private static float Phase(float t, float a, float b) => Ease((t-a)/(b-a));
            private static readonly Vector3 SupportThigh = new Vector3(-.09f,-.10f,.423f);
            private static readonly Vector3 SupportShin = new Vector3(-.085f,-.46f,.035f);
            private static readonly Vector3 CrossThigh = new Vector3(.135f,.145f,.398f);
            private static readonly Vector3 CrossShin = new Vector3(0,-.445f,.29f);

            public void Pose(float seconds)
            {
                foreach (var bone in Bones) { bone.localRotation = rotations[bone]; bone.localPosition = positions[bone]; }
                if (seconds <= 0) return;
                // Support foot: 0-.20 lift, .20-.30 quicker downward plant.
                // An ankle target + two-bone IK keeps the planted foot immobile.
                float plant = Phase(seconds, 0, .30f);
                float supportLift = .06f * (Phase(seconds, 0, .20f) - Phase(seconds, .20f, .30f));
                var supportHip = points[Bone("Right leg")];
                float upperLength = Vector3.Distance(supportHip, points[Bone("Right knee")]);
                float lowerLength = Vector3.Distance(points[Bone("Right knee")], points[Bone("Right ankle")]);
                var finalKnee = supportHip + SupportThigh.normalized * upperLength;
                var finalAnkle = finalKnee + SupportShin.normalized * lowerLength;
                var target = Vector3.Lerp(points[Bone("Right ankle")], finalAnkle, plant) + Vector3.up * supportLift;
                var pole = Vector3.Lerp(points[Bone("Right knee")], finalKnee, plant);
                SolveChain("Right leg", "Right knee", "Right ankle", target, pole);
                Aim("Right ankle", "Right toe", Vector3.Slerp(points[Bone("Right toe")]-points[Bone("Right ankle")],
                    new Vector3(0,-.10f,.13f), plant));

                // Crossing starts late, accelerates through the middle, and brakes
                // into contact. sin^2(eased phase) has zero endpoint velocity.
                float crossing = Phase(seconds, .27f, .72f);
                float arc = Mathf.Pow(Mathf.Sin(Mathf.PI * crossing), 2);
                // A small preparatory clearance moves the free foot out of the
                // support foot's planting path; the actual crossing still waits.
                float clearance = Phase(seconds, .055f, .18f) * (1-crossing);
                float settle = .014f * Mathf.Pow(Mathf.Sin(Mathf.PI * Phase(seconds, .72f, .9f)), 2);
                var baseThigh = (points[Bone("Left knee")]-points[Bone("Left leg")]).normalized;
                var thigh = Vector3.Slerp(baseThigh, CrossThigh.normalized, crossing) + Vector3.up * (.28f*arc + settle)
                    + new Vector3(-.11f,.14f,.025f)*clearance;
                Aim("Left leg", "Left knee", thigh);
                var lower = Vector3.Slerp(points[Bone("Left ankle")]-points[Bone("Left knee")], CrossShin, crossing)
                    + Vector3.forward * (.24f*arc + .17f*clearance);
                Aim("Left knee", "Left ankle", lower);
                Aim("Left ankle", "Left toe", Vector3.Slerp(points[Bone("Left toe")]-points[Bone("Left ankle")],
                    new Vector3(0,-.01f,.15f), crossing));
                var toe = Bone("Left toe");
                toe.rotation = Quaternion.AngleAxis(-4f*crossing, Vector3.right) * toe.rotation;
                MoveHand("Left", Vector3.up * (.07f*crossing + .03f*arc));
                MoveHand("Right", Vector3.up * (.07f*crossing + .03f*arc));
            }

            public void PoseLoop(float seconds)
            {
                Pose(EnterDuration);
                float fast = 2*Mathf.PI*seconds/2f, slow = 2*Mathf.PI*seconds/LoopDuration;
                float ankleLift = 4f * (1-Mathf.Cos(fast));
                var direction = Quaternion.AngleAxis(-ankleLift, Vector3.right) * new Vector3(0,-.01f,.15f);
                Aim("Left ankle", "Left toe", direction);
                var toe = Bone("Left toe");
                toe.localRotation = rotations[toe];
                float toeLift = 4f + 2f*(1-Mathf.Cos(fast)) + 1.5f*(1-Mathf.Cos(slow));
                toe.rotation = Quaternion.AngleAxis(-toeLift, Vector3.right) * toe.rotation;
            }

            private void SolveChain(string upperName, string lowerName, string tipName, Vector3 target, Vector3 pole)
            {
                var upper = Bone(upperName); var lower = Bone(lowerName); var tip = Bone(tipName);
                float a = Vector3.Distance(points[upper], points[lower]), b = Vector3.Distance(points[lower], points[tip]);
                var axis = (target-upper.position).normalized;
                float distance = Mathf.Clamp(Vector3.Distance(upper.position,target), Mathf.Abs(a-b)+.0001f, a+b-.0001f);
                float x = (a*a-b*b+distance*distance)/(2*distance);
                var hint = pole-upper.position;
                var bend = (hint-axis*Vector3.Dot(hint,axis)).normalized;
                var elbow = upper.position + axis*x + bend*Mathf.Sqrt(Mathf.Max(0,a*a-x*x));
                Aim(upperName,lowerName,elbow-upper.position);
                Aim(lowerName,tipName,target-lower.position);
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
