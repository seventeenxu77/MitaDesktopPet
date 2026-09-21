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
    public static class DesktopPetClickReactionReview
    {
        public const string Folder="Library/DesktopPetClickReactionReview";
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private static int checks;
        private static object Call(object instance,string method,params object[] args)
        { return instance.GetType().GetMethod(method,Private).Invoke(instance,args); }
        private static object Field(object instance,string name)
        { return instance.GetType().GetField(name,Private).GetValue(instance); }
        private static void Set(object instance,string name,object value)
        { instance.GetType().GetField(name,Private).SetValue(instance,value); }
        private static void Check(bool condition,string message)
        {
            if(!condition) throw new Exception("Click reaction review: "+message);
            checks++;
        }

        private sealed class Rig : IDisposable
        {
            public DesktopPetDragReview.ReviewScene View;
            public PetClickReactionController Click;
            public PetSleepyEyesController Eyes;
            public PetMouseLookController Look;
            public SkinnedMeshRenderer Face;
            public Animator Animator;
            public Transform Head;
            public float HeadMotionPixels;
            private Transform[] anchors;
            public Rig()
            {
                View=new DesktopPetDragReview.ReviewScene(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DesktopPet/Prefabs/DesktopMita.prefab"));
                Animator=View.Pet.GetComponent<Animator>();
                Click=View.Pet.AddComponent<PetClickReactionController>();
                Eyes=View.Pet.AddComponent<PetSleepyEyesController>();
                Look=View.Pet.AddComponent<PetMouseLookController>();
                Head=View.Pet.transform.Find("Armature/Hips/Spine/Chest/Neck2/Neck1/Head");
                Face=View.Pet.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(s=>s.sharedMesh!=null && s.sharedMesh.GetBlendShapeIndex("Blink")>=0);
                anchors=View.Pet.transform.Find("Armature").GetComponentsInChildren<Transform>().Where(b=>b.name=="Hips" ||
                    b.name.EndsWith(" leg") || b.name.EndsWith(" knee") || b.name.EndsWith(" ankle") || b.name.EndsWith(" toe")).ToArray();
                Reset();
            }
            public void Restore()
            {
                Call(Click,"RestoreOverlay"); Call(Look,"RestoreAnimationPose"); Call(Eyes,"RestoreAnimationWeight");
            }
            public void Reset()
            {
                Restore();
                Call(Click,"OnEnable"); Call(Eyes,"OnEnable"); Call(Look,"OnDisable");
                Set(Look,"head",Head); Set(Look,"neck",Head.parent); Set(Look,"petCamera",View.Camera); Set(Look,"_clickReaction",Click);
            }
            public bool Tap(bool head,float now) { return (bool)Call(Click,"RegisterClick",head,now); }
            public void Step(float dt,AnimationClip clip,float time,bool drag=false,bool mouse=false)
            {
                Restore();
                if(clip!=null) clip.SampleAnimation(View.Pet,time); else if(Animator.enabled) Animator.Update(dt);
                var positions=anchors.Select(b=>b.position).ToArray();
                var rotations=anchors.Select(b=>b.rotation).ToArray();
                var locals=anchors.Select(b=>b.localPosition).ToArray();
                var before=View.Camera.WorldToScreenPoint(Head.position+Head.up*.18f);
                Call(Click,"AdvanceReaction",dt,drag); Call(Click,"ApplyOverlay");
                var pointer=(Vector2)View.Camera.WorldToScreenPoint(Head.position)+new Vector2(120,60);
                Call(Look,"ApplyLook",pointer,mouse,dt); Call(Eyes,"AdvanceEyes",dt,drag);
                HeadMotionPixels=Vector2.Distance(before,View.Camera.WorldToScreenPoint(Head.position+Head.up*.18f));
                for(int i=0;i<anchors.Length;i++)
                {
                    Check(Vector3.Distance(anchors[i].position,positions[i])<.00001f,"Hip/leg world position changed: "+anchors[i].name);
                    Check(Quaternion.Angle(anchors[i].rotation,rotations[i])<.1f,"Hip/leg orientation changed");
                    Check(Vector3.Distance(anchors[i].localPosition,locals[i])<.00001f,"Bone length changed");
                }
                float blink=Face.GetBlendShapeWeight(Face.sharedMesh.GetBlendShapeIndex("Blink"));
                Check(blink>=27.9f && blink<=100.01f,"Eyelid layers conflict");
                var offset=(Vector3)Field(Click,"_headOffset");
                Check(offset.magnitude<26 && !float.IsNaN(offset.x),"Head overlay unbounded");
            }
            public void Dispose() { Restore(); View.Dispose(); }
        }

        public static void Run()
        {
            Directory.CreateDirectory(Folder); checks=0;
            var readability=new StringBuilder();
            var oldRandom=UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(42);
                var clips=new[] {"Assets/AnimationClip/MitaDreamer.anim","Assets/AnimationClip/Mita Sit Normal.anim",DesktopPetCrossLegAuthoring.LoopPath}
                    .Select(AssetDatabase.LoadAssetAtPath<AnimationClip>).ToArray();
                using(var rig=new Rig())
                {
                    // Same collider ray route as runtime; head zone follows the bone.
                    foreach(var clip in clips)
                    {
                        rig.Restore(); clip.SampleAnimation(rig.View.Pet,0);
                        Physics.SyncTransforms();
                        var centre=rig.Head.TransformPoint((Vector3)Field(rig.Click,"_headCentreLocal"));
                        float radius=rig.Head.TransformVector(Vector3.right*(float)Field(rig.Click,"_headRadiusLocal")).magnitude;
                        var crown=(Vector2)rig.View.Camera.WorldToScreenPoint(centre+rig.View.Camera.transform.up*radius*.65f);
                        object[] headArgs={rig.View.Camera,crown,false};
                        Check((bool)Call(rig.Click,"TryPick",headArgs) && (bool)headArgs[2],"Crown ray hit: "+clip.name);
                        var hips=rig.View.Pet.transform.Find("Armature/Hips");
                        object[] bodyArgs={rig.View.Camera,(Vector2)rig.View.Camera.WorldToScreenPoint(hips.position),false};
                        Check((bool)Call(rig.Click,"TryPick",bodyArgs) && !(bool)bodyArgs[2],"Body ray hit: "+clip.name);
                        object[] missArgs={rig.View.Camera,new Vector2(-20,20),false};
                        Check(!(bool)Call(rig.Click,"TryPick",missArgs),"Outside window must not react");
                        missArgs[1]=new Vector2(5,5);
                        Check(!(bool)Call(rig.Click,"TryPick",missArgs),"Background must not react");
                    }
                    rig.Reset();
                    Check(rig.Tap(true,0),"First head click");
                    Check(Field(rig.Click,"_reaction").ToString()=="Pet","Head selects petting");
                    Check(!rig.Tap(true,.08f),"Click cooldown");
                    Check(rig.Tap(false,.3f) && Field(rig.Click,"_reaction").ToString()=="Poke","Body selects poke");
                    Check(rig.Tap(false,.6f) && Field(rig.Click,"_reaction").ToString()=="Annoyed","Third rapid click escalates");
                    Check(rig.Tap(true,3f) && Field(rig.Click,"_reaction").ToString()=="Pet","Burst expires");
                    foreach(var clip in clips)
                        for(int kind=0;kind<3;kind++)
                        {
                            rig.Reset(); rig.Tap(kind==0,0);
                            float peakPixels=0;
                            for(int frame=0;frame<300;frame++)
                            {
                                float t=frame/60f;
                                if(kind==2 && (frame==18 || frame==36)) rig.Tap(false,t);
                                rig.Step(1f/60,clip,t%clip.length);
                                peakPixels=Mathf.Max(peakPixels,rig.HeadMotionPixels);
                                if(frame==60)
                                {
                                    float blink=rig.Face.GetBlendShapeWeight(rig.Face.sharedMesh.GetBlendShapeIndex("Blink"));
                                    Check(kind==0 ? blink>99 : blink<85,"Visible sleepy eye response");
                                    rig.View.Render(Folder+"/pose-"+Array.IndexOf(clips,clip)+"-"+kind+".png");
                                }
                            }
                            Check(((Vector3)Field(rig.Click,"_mix")).magnitude<.002f,"Expression must return to idle");
                            Check(((Vector3)Field(rig.Click,"_headOffset")).magnitude<.02f,"Head must settle back");
                            Check(((Vector3)Field(rig.Click,"_chestOffset")).magnitude<.02f,"Chest must settle back");
                            Check(peakPixels>4,"Feedback too small at full-body scale: "+clip.name+" "+kind+" "+peakPixels);
                            readability.AppendLine(clip.name+" reaction "+kind+": peak head landmark displacement="+peakPixels.ToString("F2")+" px at 520x700.");
                        }
                    // Holding a base pose without Animator keyframes must not accumulate offsets.
                    rig.Reset(); clips[2].SampleAnimation(rig.View.Pet,0);
                    var headRotation=rig.Head.localRotation;
                    var faceWeights=Enumerable.Range(0,rig.Face.sharedMesh.blendShapeCount).Select(rig.Face.GetBlendShapeWeight).ToArray();
                    rig.Tap(false,10);
                    for(int frame=0;frame<600;frame++) rig.Step(1f/60,null,0);
                    rig.Restore();
                    Check(Quaternion.Angle(headRotation,rig.Head.localRotation)<.1f,"Head drift on unkeyed pose");
                    for(int i=0;i<faceWeights.Length;i++) Check(Mathf.Abs(faceWeights[i]-rig.Face.GetBlendShapeWeight(i))<.001f,"Expression restoration");
                    rig.Reset(); rig.Tap(false,0);
                    for(int i=0;i<30;i++) rig.Step(1f/60,clips[2],0);
                    for(int i=0;i<60;i++) rig.Step(1f/60,clips[2],0,true);
                    Check(((Vector3)Field(rig.Click,"_mix")).magnitude<.001f,"Drag cancels reaction");
                    // Live Animator plus the same mouse-look/eye composition path.
                    rig.Animator.enabled=true;
                    rig.Animator.Rebind(); rig.Animator.Update(0);
                    foreach(string state in new[]{"Idle","SitLoop","SitCrossLegLoop","DragLoop"})
                    {
                        rig.Restore();
                        bool drag=state=="DragLoop";
                        rig.Animator.SetBool("IsDragging",drag); rig.Animator.SetBool("IsSitting",state.StartsWith("Sit"));
                        rig.Animator.Play(state,0,0); rig.Animator.Update(0); rig.Reset();
                        Check(rig.Tap(false,0)!=drag,"Cannot click while dragging");
                        for(int i=0;i<180;i++) rig.Step(1f/60,null,0,drag,true);
                        Check(rig.Animator.GetCurrentAnimatorStateInfo(0).IsName(state=="SitLoop" ? "SitCrossLeg" : state),"Click changed base state");
                    }
                    rig.Restore(); rig.Animator.enabled=false; rig.Animator.SetBool("IsDragging",false);
                    // Judge at the actual full-body framing, not a magnified face.
                    rig.Reset(); clips[0].SampleAnimation(rig.View.Pet,0);
                    for(int pose=0;pose<2;pose++)
                    for(int kind=0;kind<3;kind++)
                    {
                        rig.Reset(); rig.Tap(kind==0,0);
                        for(int frame=0;frame<300;frame++)
                        {
                            float t=frame/60f;
                            if(kind==2 && (frame==18 || frame==36)) rig.Tap(false,t);
                            rig.Step(1f/60,clips[pose==0?0:2],0);
                            rig.View.Render(Folder+"/readable-"+(pose==0?"standing-":"seated-")+(kind*300+frame).ToString("D4")+".png");
                        }
                    }
                }
                const string prefabPath="Assets/DesktopPet/Prefabs/DesktopMita.prefab";
                var pet=PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    if(pet.GetComponent<PetClickReactionController>()==null) pet.AddComponent<PetClickReactionController>();
                    PrefabUtility.SaveAsPrefabAsset(pet,prefabPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(pet); }
                AssetDatabase.SaveAssets();
                Check(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath).GetComponents<PetClickReactionController>().Length==1,"Installed once on prefab");
                File.WriteAllText(Folder+"/validation.txt","PASS "+checks+" checks: crown/body/background collider picking across standing/sitting/crossed poses, cooldown, burst escalation/reset, face and eyelid blending, fixed hips/legs, no pose accumulation, restoring expressions, drag cancellation, live Animator and mouse look, prefab installation.\n");
                File.WriteAllText(Folder+"/readability.txt",readability.ToString());
            }
            finally { UnityEngine.Random.state=oldRandom; }
        }
    }
}
