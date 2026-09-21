using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using DesktopPet;
using UnityEditor;
using UnityEngine;

namespace DesktopPetEditor
{
    public static class DesktopPetSleepyEyesReview
    {
        public const string Folder = "Library/DesktopPetSleepyEyesReview";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly Type Eyes = typeof(PetSleepyEyesController);
        private static readonly MethodInfo Advance = Eyes.GetMethod("AdvanceEyes",Private);
        private static readonly MethodInfo Restore = Eyes.GetMethod("RestoreAnimationWeight",Private);
        private static int checks;

        private static void Check(bool value,string message)
        {
            if(!value) throw new Exception("Sleepy eyes review: "+message);
            checks++;
        }
        private static void Set(PetSleepyEyesController eyes,string field,object value)
        { Eyes.GetField(field,Private).SetValue(eyes,value); }
        private static float Get(PetSleepyEyesController eyes,string field)
        { return (float)Eyes.GetField(field,Private).GetValue(eyes); }
        private static void Step(PetSleepyEyesController eyes,float dt,bool drag=false)
        { Advance.Invoke(eyes,new object[]{dt,drag}); }
        private static void Begin(PetSleepyEyesController eyes)
        {
            Eyes.GetMethod("OnEnable",Private).Invoke(eyes,null);
            Set(eyes,"_wait",0f); Step(eyes,0);
            Set(eyes,"_tempo",1f); Set(eyes,"_strength",1f);
        }

        public static void Run()
        {
            Directory.CreateDirectory(Folder);
            checks = 0;
            var randomState = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(724);
                var evaluate = Eyes.GetMethod("EvaluateOpening",BindingFlags.Static|BindingFlags.NonPublic);
                Func<float,float> opening = t=>(float)evaluate.Invoke(null,new object[]{t});
                Check(opening(0)==0 && opening(6.8f)==0,"Start/end must sleep");
                Check(Mathf.Abs(opening(.95f)-.72f)<.0001f,"Slow first half-open");
                Check(opening(2.73f)>opening(3.77f) && opening(3.77f)>opening(4.79f),"Each effort weakens");
                Check(opening(2.33f)==0 && opening(3.39f)==0 && opening(4.38f)<.03f,"Three eyelid dips");
                using(var view = new DesktopPetDragReview.ReviewScene())
                {
                    var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/AnimationClip/MitaDreamer.anim");
                    idle.SampleAnimation(view.Pet,0);
                    var face = view.Pet.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                        .First(s=>s.sharedMesh!=null && s.sharedMesh.GetBlendShapeIndex("Blink")>=0);
                    int blink = face.sharedMesh.GetBlendShapeIndex("Blink");
                    Check(face.GetBlendShapeWeight(blink)==100,"Source closes eyes with Blink=100");
                    var weights = Enumerable.Range(0,face.sharedMesh.blendShapeCount).Select(face.GetBlendShapeWeight).ToArray();
                    var bones = view.Pet.transform.Find("Armature").GetComponentsInChildren<Transform>();
                    var rotations = bones.Select(b=>b.localRotation).ToArray();
                    var positions = bones.Select(b=>b.localPosition).ToArray();
                    var eyes = view.Pet.AddComponent<PetSleepyEyesController>();
                    Begin(eyes);
                    var pivot = face.bounds.center;
                    view.Camera.orthographicSize = .22f;
                    view.Camera.transform.position = pivot+Vector3.forward*5;
                    view.Camera.transform.LookAt(pivot);
                    float minimumBlink = 100;
                    // Isolate actual mesh deformation; no changing camera/head pose.
                    for(int frame=0;frame<=528;frame++)
                    {
                        float t=frame/60f;
                        if(frame>60 && t<=7.8f) Step(eyes,1f/60);
                        else if(frame>468) Step(eyes,0);
                        float weight=face.GetBlendShapeWeight(blink);
                        minimumBlink=Mathf.Min(minimumBlink,weight);
                        Check(weight>=34.99f && weight<=100.01f,"Never opens wide or exceeds valid range");
                        for(int i=0;i<weights.Length;i++)
                            if(i!=blink) Check(Mathf.Abs(face.GetBlendShapeWeight(i)-weights[i])<.001f,"Unrelated expression changed");
                        view.Render(Folder+"/sleepy-"+frame.ToString("D4")+".png");
                    }
                    Check(minimumBlink<36,"Effort should reveal pupils");
                    Check(face.GetBlendShapeWeight(blink)>99.99f,"Finally falls asleep");
                    Check(bones.Select((b,i)=>Quaternion.Angle(b.localRotation,rotations[i])).Max()<.1f,"Eye animation changed body rotation");
                    Check(bones.Select((b,i)=>Vector3.Distance(b.localPosition,positions[i])).Max()<.00001f,"Eye animation changed body position");
                    foreach(float fps in new[]{30f,60f,144f})
                    {
                        Begin(eyes);
                        for(int i=0;i<Mathf.RoundToInt(2.5f*fps);i++) Step(eyes,1/fps);
                        Check(Mathf.Abs(Get(eyes,"_opening")-opening(2.5f)*.65f)<.0005f,"Frame-rate independent eyelids");
                    }
                    Begin(eyes); Step(eyes,2.73f);
                    float previous=Get(eyes,"_opening");
                    for(int i=0;i<24;i++)
                    {
                        // Release partway through closing: it must finish softly, not snap.
                        Step(eyes,1f/60,i<4);
                        Check(Get(eyes,"_opening")<=previous+.00001f,"Drag interrupt opens eyes/snaps back");
                        previous=Get(eyes,"_opening");
                    }
                    Check(previous<.0001f && Get(eyes,"_attemptTime")<0,"Drag cancels sleepy effort");
                    Check(Get(eyes,"_wait")>20,"No immediate retry after pickup");
                    Begin(eyes); Step(eyes,7f);
                    float smallest=100,largest=0;
                    for(int trial=0;trial<20;trial++)
                    {
                        float wait=Get(eyes,"_wait");
                        Check(wait>=22 && wait<=38,"Rest interval bounds");
                        smallest=Mathf.Min(smallest,wait); largest=Mathf.Max(largest,wait);
                        Step(eyes,wait+.01f); Step(eyes,8f);
                        Check(Get(eyes,"_opening")==0,"Each effort ends asleep");
                    }
                    Check(largest-smallest>5,"Intervals must vary");
                    Restore.Invoke(eyes,null); face.SetBlendShapeWeight(blink,73);
                    Begin(eyes); Step(eyes,1);
                    Eyes.GetMethod("OnDisable",Private).Invoke(eyes,null);
                    Check(Mathf.Abs(face.GetBlendShapeWeight(blink)-73)<.001f,"Disabling must restore previous expression");
                    face.SetBlendShapeWeight(blink,100);
                    VerifyAnimator(view,eyes,face,blink);
                }
                var empty = new GameObject("Missing face regression");
                try
                {
                    var eyes=empty.AddComponent<PetSleepyEyesController>();
                    Begin(eyes); Step(eyes,1);
                    Check(Get(eyes,"_opening")==0,"Missing face safely ignored");
                }
                finally { UnityEngine.Object.DestroyImmediate(empty); }
                // Patch only the existing variant; keep the user's scene and source model intact.
                const string prefabPath="Assets/DesktopPet/Prefabs/DesktopMita.prefab";
                var pet=PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    if(pet.GetComponent<PetSleepyEyesController>()==null) pet.AddComponent<PetSleepyEyesController>();
                    Check(pet.GetComponents<PetSleepyEyesController>().Length==1,"Exactly one eye controller");
                    PrefabUtility.SaveAsPrefabAsset(pet,prefabPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(pet); }
                AssetDatabase.SaveAssets();
                var installed=AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Check(installed.GetComponents<PetSleepyEyesController>().Length==1,"Saved prefab reload retains eye controller");
                File.WriteAllText(Folder+"/validation.txt","PASS "+checks+" checks: actual Blink mesh, sleepy opening cap, three weakening attempts, final sleep, 30/60/144fps, random rest, drag interruption, disable restoration, unrelated expressions/body unchanged, idle/sit/cross/drag Animator and mouse-look coexistence, missing face, prefab installed.\n");
            }
            finally { UnityEngine.Random.state=randomState; }
        }

        private static void VerifyAnimator(DesktopPetDragReview.ReviewScene view,PetSleepyEyesController eyes,SkinnedMeshRenderer face,int blink)
        {
            var animator=view.Pet.GetComponent<Animator>();
            animator.enabled=true;
            animator.runtimeAnimatorController=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/DesktopPet/Animators/DesktopMita.controller");
            animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            animator.applyRootMotion=false;
            animator.Rebind(); animator.Update(0);
            var look=view.Pet.AddComponent<PetMouseLookController>();
            var lookType=typeof(PetMouseLookController);
            var head=view.Pet.transform.Find("Armature/Hips/Spine/Chest/Neck2/Neck1/Head");
            lookType.GetField("head",Private).SetValue(look,head);
            lookType.GetField("neck",Private).SetValue(look,head.parent);
            lookType.GetField("petCamera",Private).SetValue(look,view.Camera);
            foreach(string state in new[]{"Idle","SitLoop","SitCrossLeg","SitCrossLegLoop","DragLoop"})
            {
                animator.SetBool("IsDragging",state=="DragLoop");
                animator.SetBool("IsSitting",state.StartsWith("Sit"));
                animator.Play(state,0,0); animator.Update(0);
                Begin(eyes);
                Check((bool)Eyes.GetField("_hasDragParameter",Private).GetValue(eyes),"Runtime drag parameter resolved");
                for(int frame=0;frame<90;frame++)
                {
                    Restore.Invoke(eyes,null);
                    lookType.GetMethod("RestoreAnimationPose",Private).Invoke(look,null);
                    animator.Update(1f/60);
                    var pointer=(Vector2)view.Camera.WorldToScreenPoint(head.position)+new Vector2(100,50);
                    lookType.GetMethod("ApplyLook",Private).Invoke(look,new object[]{pointer,true,1f/60});
                    var headRotation=head.rotation;
                    // Deterministic time drives the same controller path as runtime.
                    Step(eyes,1f/60,state=="DragLoop");
                    Check(Quaternion.Angle(headRotation,head.rotation)<.1f,"Eyes interfered with mouse look");
                    Check(face.GetBlendShapeWeight(blink)>=34.99f && face.GetBlendShapeWeight(blink)<=100.01f,"Animator overwrote eyelids");
                }
                Check(state=="DragLoop" ? face.GetBlendShapeWeight(blink)>99.99f : face.GetBlendShapeWeight(blink)<95,
                    "Eye activity in "+state);
            }
        }

        public static void Inspect()
        {
            Directory.CreateDirectory(Folder);
            var report = new StringBuilder();
            using (var view = new DesktopPetDragReview.ReviewScene())
            {
                var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/AnimationClip/MitaDreamer.anim");
                idle.SampleAnimation(view.Pet,0);
                foreach (var skin in view.Pet.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var mesh = skin.sharedMesh;
                    if(mesh == null) continue;
                    for(int i=0;i<mesh.blendShapeCount;i++)
                        report.AppendLine(skin.name+" "+i+" "+mesh.GetBlendShapeName(i)+" = "+skin.GetBlendShapeWeight(i));
                    int blink = mesh.GetBlendShapeIndex("Blink");
                    if(blink<0) continue;
                    var pivot = skin.bounds.center;
                    view.Camera.orthographicSize = .22f;
                    view.Camera.transform.position = pivot + Vector3.forward*5;
                    view.Camera.transform.LookAt(pivot);
                    foreach(float weight in new[]{100f,80f,65f,50f,35f,0f})
                    {
                        skin.SetBlendShapeWeight(blink,weight);
                        view.Render(Folder+"/blink-"+weight.ToString("F0")+".png");
                    }
                    skin.SetBlendShapeWeight(blink,100);
                }
            }
            File.WriteAllText(Folder+"/rig.txt",report.ToString());
        }
    }
}
