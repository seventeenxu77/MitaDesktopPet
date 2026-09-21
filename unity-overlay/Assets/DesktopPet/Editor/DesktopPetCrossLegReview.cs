using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace DesktopPetEditor
{
    public static class DesktopPetCrossLegReview
    {
        public static void Run()
        {
            var report = new StringBuilder();
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(DesktopPetCrossLegAuthoring.ClipPath);
            var loop = AssetDatabase.LoadAssetAtPath<AnimationClip>(DesktopPetCrossLegAuthoring.LoopPath);
            var sit = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/AnimationClip/Mita Sit Normal.anim");
            var folder = DesktopPetCrossLegAuthoring.ReviewFolder;
            Directory.CreateDirectory(folder);
            using (var view = new DesktopPetDragReview.ReviewScene())
            {
                var bones = view.Pet.transform.Find("Armature").GetComponentsInChildren<Transform>();
                sit.SampleAnimation(view.Pet, 0);
                var rest = bones.Select(b => b.localRotation).ToArray();
                var lengths = bones.Select(b => b.localPosition).ToArray();
                var hip = bones.First(b => b.name == "Hips");
                var origin = hip.position;
                var toe = bones.First(b => b.name == "Left toe");
                var ankle = bones.First(b => b.name == "Left ankle");
                clip.SampleAnimation(view.Pet, 0);
                float seam = bones.Select((b,i)=>Quaternion.Angle(rest[i],b.localRotation)).Max();
                clip.SampleAnimation(view.Pet, clip.length);
                var crossKnee = bones.First(b=>b.name == "Left knee");
                Require(Vector3.Angle(ankle.position-crossKnee.position,new Vector3(0,-.445f,.29f)) < .05f,
                    "Approved forward-facing shin changed");
                Require(Vector3.Angle(toe.position-ankle.position,new Vector3(0,-.01f,.15f)) < .05f,
                    "Approved slightly raised forward-facing foot changed");
                var end = bones.Select(b=>b.localRotation).ToArray();
                var planted = bones.First(b=>b.name == "Right ankle");
                var plantedToe = bones.First(b=>b.name == "Right toe");
                var plantedPosition = planted.position; var plantedToePosition = plantedToe.position;
                loop.SampleAnimation(view.Pet, 0);
                seam = Mathf.Max(seam, bones.Select((b,i)=>Quaternion.Angle(end[i],b.localRotation)).Max());
                var loopStart = bones.Select(b=>b.localRotation).ToArray();
                var firstToe = toe.localRotation; var firstAnkle = ankle.localRotation;
                float hipDrift = 0, lengthDrift = 0, toeMotion = 0, ankleMotion = 0, footDrift = 0;
                loop.SampleAnimation(view.Pet, loop.length);
                seam = Mathf.Max(seam, bones.Select((b,i)=>Quaternion.Angle(loopStart[i],b.localRotation)).Max());
                // Full 60fps review: two seconds ordinary sitting, one entry,
                // then two seamless hold loops, with no automatic exit.
                int frameCount = Mathf.RoundToInt((2+clip.length+2*loop.length)*60);
                for (int frame = 0; frame <= frameCount; frame++)
                {
                    float t=frame/60f;
                    if(t<2) sit.SampleAnimation(view.Pet,0);
                    else if(t<=2+clip.length) clip.SampleAnimation(view.Pet,t-2);
                    else loop.SampleAnimation(view.Pet,(t-2-clip.length)%loop.length);
                    hipDrift = Mathf.Max(hipDrift, Vector3.Distance(origin, hip.position));
                    for (int b = 0; b < bones.Length; b++)
                    {
                        lengthDrift = Mathf.Max(lengthDrift, Vector3.Distance(lengths[b], bones[b].localPosition));
                    }
                    if (t >= 2+clip.length)
                    {
                        Require(Mathf.Abs((toe.position-ankle.position).normalized.x)<.001f,
                            "Foot turned sideways during hold");
                        toeMotion = Mathf.Max(toeMotion, Quaternion.Angle(firstToe, toe.localRotation));
                        ankleMotion = Mathf.Max(ankleMotion, Quaternion.Angle(firstAnkle, ankle.localRotation));
                        footDrift = Mathf.Max(footDrift, Vector3.Distance(planted.position,plantedPosition),
                            Vector3.Distance(plantedToe.position,plantedToePosition));
                    }
                    view.Render(folder + "/forward-originaltempo-" + frame.ToString("D4") + ".png");
                }
                Require(hipDrift < .0001f, "Hip anchor moved");
                Require(lengthDrift < .0001f, "Bone lengths changed");
                Require(seam < .1f, "Entry or loop seam mismatch");
                Require(footDrift < .0001f, "Planted support foot slides");
                Require(toeMotion > 4 && ankleMotion > 7, "Missing ankle/toe animation");
                Require(Mathf.Abs(clip.length-.95f)<.001f && Mathf.Abs(loop.length-DesktopPetCrossLegAuthoring.LoopDuration)<.001f, "Wrong clip duration");
                Require(AnimationUtility.GetAnimationClipSettings(loop).loopTime, "Hold clip must loop");
                report.AppendLine($"Pose PASS: hip drift={hipDrift:F6}, local position drift={lengthDrift:F6}, endpoint error={seam:F4}deg");
                report.AppendLine($"Original 0.95s entry restored; supporting foot drift during hold={footDrift:F6}m");
                report.AppendLine($"Independent local rotations: ankle range={ankleMotion:F2}deg, toe range={toeMotion:F2}deg");
                report.AppendLine("Approved forward-facing knee/shin and slightly raised foot PASS; original 0.95s timing, no staged foot planting.");
                CheckLegSurfaces(view, clip, sit, report);
                CheckLegSurfaces(view, loop, sit, report);
                VerifyAnimator(view, report);
                // Detail view isolates ankle/toe motion for visual review.
                view.Pet.GetComponent<Animator>().enabled = false;
                var pivot = new Vector3(.03f, .39f, -.4f);
                view.Camera.transform.position = pivot + Vector3.forward*5;
                view.Camera.transform.LookAt(pivot);
                view.Camera.orthographicSize = .32f;
                for (int i = 0; i < 6; i++)
                {
                    loop.SampleAnimation(view.Pet, i*.6f);
                    view.Render(folder + "/foot-" + i + ".png");
                }
            }
            File.WriteAllText(folder + "/validation.txt", report.ToString());
            DesktopPetSeatReview.Run();
        }

        // Sample actual skinned surfaces, excluding the shared pelvis/top-quarter
        // thigh seam. This is a diagnostic, not a general cloth collision solver.
        private static void CheckLegSurfaces(DesktopPetDragReview.ReviewScene view, AnimationClip clip, AnimationClip sit, StringBuilder report)
        {
            var skin = view.Pet.GetComponentsInChildren<SkinnedMeshRenderer>().First(s => s.name == "Body");
            var weights = skin.sharedMesh.boneWeights;
            var mesh = new Mesh();
            try
            {
                sit.SampleAnimation(view.Pet, 0); skin.BakeMesh(mesh);
                var original = mesh.vertices;
                var side = new int[original.Length];
                for (int v=0; v<side.Length; v++)
                {
                    var w = weights[v];
                    int b=w.boneIndex0; float best=w.weight0;
                    if(w.weight1>best) {b=w.boneIndex1;best=w.weight1;}
                    if(w.weight2>best) {b=w.boneIndex2;best=w.weight2;}
                    if(w.weight3>best) {b=w.boneIndex3;best=w.weight3;}
                    var bone=skin.bones[b];
                    bool leg=bone.name.EndsWith(" leg"), distal=bone.name.EndsWith(" knee") || bone.name.EndsWith(" ankle") || bone.name.EndsWith(" toe");
                    if(!leg && !distal) continue;
                    if(leg)
                    {
                        var knee=bone.GetComponentsInChildren<Transform>().First(t=>t.name.EndsWith(" knee"));
                        var axis=knee.position-bone.position;
                        if(Vector3.Dot(skin.transform.TransformPoint(original[v])-bone.position,axis)/axis.sqrMagnitude < .25f) continue;
                    }
                    side[v]=bone.name.StartsWith("Left")?1:2;
                }
                var ids=mesh.triangles;
                var left=new List<int>(); var right=new List<int>();
                for(int i=0;i<ids.Length;i+=3)
                    if(side[ids[i]]!=0 && side[ids[i]]==side[ids[i+1]] && side[ids[i]]==side[ids[i+2]])
                        (side[ids[i]]==1?left:right).Add(i);
                int sampleCount = Mathf.RoundToInt(clip.length * (clip.isLooping ? 4 : 60));
                foreach(float t in Enumerable.Range(0, sampleCount+1).Select(frame => clip.length*frame/sampleCount))
                {
                    clip.SampleAnimation(view.Pet,t); skin.BakeMesh(mesh);
                    var vertices=mesh.vertices;
                    var a=left.Select(i=>new SurfaceTriangle(vertices[ids[i]],vertices[ids[i+1]],vertices[ids[i+2]])).ToArray();
                    var b=right.Select(i=>new SurfaceTriangle(vertices[ids[i]],vertices[ids[i+1]],vertices[ids[i+2]])).ToArray();
                    int intersections=0; Vector3 centre=Vector3.zero;
                    foreach(var x in a) foreach(var y in b)
                        if(x.bounds.Intersects(y.bounds) && (Hits(x.a,x.b,y)||Hits(x.b,x.c,y)||Hits(x.c,x.a,y)||
                            Hits(y.a,y.b,x)||Hits(y.b,y.c,x)||Hits(y.c,y.a,x)))
                        {intersections++;centre+=skin.transform.TransformPoint(x.bounds.center);}
                    report.AppendLine($"{clip.name} surface sample {t:F2}s: intersecting triangle pairs={intersections}, region={(intersections>0?centre/intersections:Vector3.zero).ToString("F3")}");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }
        private struct SurfaceTriangle
        {
            public Vector3 a,b,c; public Bounds bounds;
            public SurfaceTriangle(Vector3 x,Vector3 y,Vector3 z) {a=x;b=y;c=z;bounds=new Bounds(x,Vector3.zero);bounds.Encapsulate(y);bounds.Encapsulate(z);}
        }
        private static bool Hits(Vector3 from,Vector3 to,SurfaceTriangle tri)
        {
            var dir=to-from;var e1=tri.b-tri.a;var e2=tri.c-tri.a;var p=Vector3.Cross(dir,e2);
            float det=Vector3.Dot(e1,p);if(Mathf.Abs(det)<1e-10f)return false;
            float inv=1/det;var s=from-tri.a;float u=Vector3.Dot(s,p)*inv;
            if(u<.00001f||u> .99999f)return false;
            var q=Vector3.Cross(s,e1);float v=Vector3.Dot(dir,q)*inv;
            if(v<.00001f||u+v> .99999f)return false;
            float t=Vector3.Dot(e2,q)*inv;return t>.00001f&&t<.99999f;
        }

        private static void VerifyAnimator(DesktopPetDragReview.ReviewScene view, StringBuilder report)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/DesktopPet/Animators/DesktopMita.controller");
            var machine = controller.layers[0].stateMachine;
            Require(machine.states.Count(s => s.state.name == "SitCrossLeg") == 1, "Duplicate cross state");
            Require(machine.states.First(s=>s.state.name == "SitLoop").state.transitions.Count(t=>t.destinationState != null &&
                t.destinationState.name == "SitCrossLeg") == 1, "Duplicate timed transition");
            var animator = view.Pet.GetComponent<Animator>();
            animator.enabled = true; animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind(); animator.Update(0);
            animator.SetBool("IsDragging", false); animator.SetBool("IsSitting", true);
            animator.Play("SitLoop", 0, 0); animator.Update(0);
            float began = -1, ended = -1;
            for (int frame = 1; frame <= 3600; frame++)
            {
                animator.Update(1f/60);
                if (animator.GetCurrentAnimatorStateInfo(0).IsName("SitCrossLeg") && began < 0) began = frame/60f;
                if (animator.GetCurrentAnimatorStateInfo(0).IsName("SitCrossLegLoop") && ended < 0) ended = frame/60f;
                if (ended > 0) Require(animator.GetCurrentAnimatorStateInfo(0).IsName("SitCrossLegLoop"), "Crossed pose exited without pickup");
            }
            Require(began >= 1.95f && began < 2.2f, "Cross leg must begin after about two seconds: " + began);
            Require(Mathf.Abs(ended - began - DesktopPetCrossLegAuthoring.EnterDuration) < .12f,
                "Wrong entry duration: " + (ended-began));
            Require(animator.GetCurrentAnimatorStateInfo(0).IsName("SitCrossLegLoop"), "Must keep crossed pose");
            Require(!machine.states.Any(s=>s.state.name == "SitRest"), "Obsolete automatic return state");
            report.AppendLine($"Animator PASS: starts {began:F3}s after SitLoop, entry={ended-began:F3}s; remains crossed through 60s without repeating entry.");
            foreach (string state in new[] {"SitCrossLeg", "SitCrossLegLoop"})
            {
                animator.SetBool("IsDragging", false); animator.SetBool("IsSitting", true);
                animator.Play(state, 0, .4f); animator.Update(0);
                animator.SetBool("IsDragging", true);
                for (int f=0; f<60; f++) animator.Update(1f/60);
                Require(animator.GetCurrentAnimatorStateInfo(0).IsName("DragLoop"), "Pickup interrupt: " + state);
                animator.SetBool("IsDragging", false); animator.SetBool("IsSitting", true);
                animator.Play(state, 0, .4f); animator.Update(0); animator.SetBool("IsSitting", false);
                for (int f=0; f<30; f++) animator.Update(1f/60);
                Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), "Detach interrupt: " + state);
            }
            animator.SetBool("IsSitting", true);
            bool reentered = false;
            for(int f=0;f<600;f++) { animator.Update(1f/60); reentered |= animator.GetCurrentAnimatorStateInfo(0).IsName("SitCrossLeg"); }
            Require(reentered, "Reseating must rearm animation");
            report.AppendLine("Interrupts PASS: pickup and detach from both new states; reseating rearms sequence.");
        }
        private static void Require(bool condition, string message) { if (!condition) throw new Exception("Cross-leg review: " + message); }
    }
}
