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
    public static class DesktopPetHangingReview
    {
        public const string Folder="Library/DesktopPetHangingReview";
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private static int checks;
        private static object Call(object instance,string method,params object[] args)
        { return instance.GetType().GetMethod(method,Private).Invoke(instance,args); }
        private static void Set(object instance,string field,object value)
        { instance.GetType().GetField(field,Private).SetValue(instance,value); }
        private static Vector2 Angles(ProceduralDragPoseController spring)
        { return (Vector2)spring.GetType().GetProperty("SwingAngles",Private).GetValue(spring); }
        private static void Check(bool value,string message)
        {
            if(!value) throw new Exception("Hanging review: "+message);
            checks++;
        }
        private static void Step(ProceduralDragPoseController spring,Vector2 velocity,float dt,bool drag=true)
        { Call(spring,"StepInertia",velocity,drag,dt); }
        private static void Reset(ProceduralDragPoseController spring,Camera camera)
        { Call(spring,"OnEnable"); Set(spring,"_camera",camera); }
        public static void Run()
        {
            Directory.CreateDirectory(Folder); checks=0;
            var report=new StringBuilder();
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(DesktopPetDragAuthoring.Folder+"/DragFlailLoop.anim");
            Check(Mathf.Abs(clip.length-7.2f)<.001f,"Slow composite cycle duration");
            using(var view=new DesktopPetDragReview.ReviewScene())
            {
                var bones=view.Pet.transform.Find("Armature").GetComponentsInChildren<Transform>();
                Func<string,Transform> bone=name=>bones.First(b=>b.name==name);
                var hips=bone("Hips"); var head=bone("Head"); var chest=bone("Chest");
                clip.SampleAnimation(view.Pet,0);
                float foldedAngle=ArmAngle(bone,"Left");
                float foldedDistance=Vector3.Distance(bone("Left wrist").position,chest.position);
                clip.SampleAnimation(view.Pet,1.9f);
                float reachAngle=ArmAngle(bone,"Left");
                float reachedDistance=Vector3.Distance(bone("Left wrist").position,chest.position);
                Check(foldedAngle>85 && reachAngle<55,"Gathered arms must unfold to reach");
                Check(reachedDistance>foldedDistance+.12f,"Hands must visibly extend away from chest");
                report.AppendLine($"Arm flexion: gathered={foldedAngle:F1}deg, reaching={reachAngle:F1}deg; wrist/chest distance={foldedDistance:F3}->{reachedDistance:F3}m.");
                clip.SampleAnimation(view.Pet,.6f);
                float leftHigh=bone("Left ankle").position.y-bone("Right ankle").position.y;
                clip.SampleAnimation(view.Pet,1.8f);
                float rightHigh=bone("Left ankle").position.y-bone("Right ankle").position.y;
                Check(leftHigh>.08f && rightHigh<-.08f,"Legs must kick alternately");
                clip.SampleAnimation(view.Pet,.37f);
                var legDirection=bone("Left ankle").position-bone("Left knee").position;
                var armDirection=bone("Left wrist").position-bone("Left elbow").position;
                clip.SampleAnimation(view.Pet,.37f+2.4f);
                Check(Vector3.Angle(legDirection,bone("Left ankle").position-bone("Left knee").position)<.1f,"Leg period");
                Check(Vector3.Angle(armDirection,bone("Left wrist").position-bone("Left elbow").position)>5,"Arms must not share leg cycle");
                var spring=view.Pet.AddComponent<ProceduralDragPoseController>();
                Reset(spring,view.Camera);
                for(int i=0;i<360;i++) Step(spring,Vector2.zero,1f/60);
                Check(Angles(spring).magnitude<.00001f,"No artificial sway while holding still");
                for(int i=0;i<12;i++) Step(spring,new Vector2(1000,0),1f/60);
                float rightPull=Angles(spring).y;
                Check(rightPull<-3,"Rightward acceleration should lag left");
                Step(spring,new Vector2(-1000,0),1f/60);
                Check(Angles(spring).y<0,"Reversing drag must not instantly flip the body");
                for(int i=0;i<30;i++) Step(spring,new Vector2(-1000,0),1f/60);
                Check(Angles(spring).y>3,"Opposite pull should reverse the swing");
                float peakAfterStop=0;
                for(int i=0;i<240;i++)
                {
                    Step(spring,Vector2.zero,1f/60);
                    if(i<60) peakAfterStop=Mathf.Max(peakAfterStop,Mathf.Abs(Angles(spring).y));
                }
                Check(peakAfterStop>2 && Angles(spring).magnitude<.1f,"Swing must carry on after stop then decay");
                report.AppendLine($"Inertia: initial lag={rightPull:F2}deg, carry after stop={peakAfterStop:F2}deg, settled={Angles(spring).magnitude:F4}deg.");
                var rates=new[]{30,60,120}; var samples=new Vector2[rates.Length];
                for(int rateIndex=0;rateIndex<rates.Length;rateIndex++)
                {
                    Reset(spring,view.Camera); int fps=rates[rateIndex];
                    for(int i=1;i<=fps*2;i++)
                    {
                        float time=(float)i/fps;
                        var velocity=time<.5f || time>1.5f ? Vector2.zero : new Vector2(900*Mathf.Sin((time-.5f)*Mathf.PI*2),0);
                        Step(spring,velocity,1f/fps);
                    }
                    samples[rateIndex]=Angles(spring);
                }
                Check(Vector2.Distance(samples[0],samples[2])<1.2f && Vector2.Distance(samples[1],samples[2])<.6f,"Frame-rate-sensitive pendulum");
                Reset(spring,view.Camera);
                for(int i=0;i<120;i++) Step(spring,new Vector2(i%2==0?10000:-10000,10000),1f/60);
                Check(Mathf.Abs(Angles(spring).y)<=20.01f && Mathf.Abs(Angles(spring).x)<=9.01f,"Wild dragging exceeds limits");
                for(int i=0;i<30;i++) Step(spring,Vector2.zero,1f/60,false);
                Check(Angles(spring).magnitude==0,"Release must remove offsets for seating");
                RenderSequence(view,spring,clip,bones,report);
            }
            DesktopPetSeatReview.Run();
            report.Insert(0,"PASS "+checks+" checks: independent slow reach/kick cycles, gravity-hanging silhouette, acceleration lag/reversal/coasting, damping, 30/60/120fps, extreme input, fixed hip anchor, no accumulated rotations, mouse-look composition, release cleanup.\n");
            File.WriteAllText(Folder+"/validation.txt",report.ToString());
        }
        private static float ArmAngle(Func<string,Transform> bone,string side)
        { return Vector3.Angle(bone(side+" elbow").position-bone(side+" arm").position,bone(side+" wrist").position-bone(side+" elbow").position); }

        private static void RenderSequence(DesktopPetDragReview.ReviewScene view,ProceduralDragPoseController spring,
            AnimationClip clip,Transform[] bones,StringBuilder report)
        {
            var hips=bones.First(b=>b.name=="Hips"); var head=bones.First(b=>b.name=="Head");
            var look=view.Pet.AddComponent<PetMouseLookController>();
            Set(look,"head",head); Set(look,"neck",head.parent); Set(look,"petCamera",view.Camera); Set(look,"_dragInertia",spring);
            Reset(spring,view.Camera);
            float maxSwing=0,minDrop=100;
            for(int frame=0;frame<720;frame++)
            {
                float time=frame/60f;
                Call(spring,"RestorePose"); Call(look,"RestoreAnimationPose");
                clip.SampleAnimation(view.Pet,time%clip.length);
                var anchor=hips.position; var baseRotation=hips.localRotation;
                var localPositions=bones.Select(b=>b.localPosition).ToArray();
                float speed=time>=4 && time<8 ? 950*Mathf.Sin((time-4)*Mathf.PI*2*.8f) : 0;
                Step(spring,new Vector2(speed,0),1f/60); Call(spring,"ApplyPose");
                var pointer=(Vector2)view.Camera.WorldToScreenPoint(hips.position)+new Vector2(80,20);
                Call(look,"ApplyLook",pointer,true,1f/60);
                Check(Vector3.Distance(anchor,hips.position)<.00001f,"Suspension pivot moved");
                Check(bones.Select((b,i)=>Vector3.Distance(localPositions[i],b.localPosition)).Max()<.00001f,"Inertia stretched a bone");
                minDrop=Mathf.Min(minDrop,hips.position.y-head.position.y);
                maxSwing=Mathf.Max(maxSwing,Mathf.Abs(Angles(spring).y));
                view.Render(Folder+"/hanging-"+frame.ToString("D4")+".png");
                Call(spring,"RestorePose"); Call(look,"RestoreAnimationPose");
                Check(Quaternion.Angle(baseRotation,hips.localRotation)<.1f,"Pendulum did not restore authored pose");
            }
            Check(minDrop>.30f,"Head should hang substantially below the hips");
            Check(maxSwing>7,"Sway not readable during drag");
            report.AppendLine($"Combined: maximum swing={maxSwing:F2}deg; head remains at least {minDrop:F3}m below hips; hip pivot unchanged.");
            var pivot=new Vector3(0,.75f,0);
            view.Camera.transform.position=pivot+Vector3.right*5; view.Camera.transform.LookAt(pivot);
            clip.SampleAnimation(view.Pet,0); view.Render(Folder+"/side-gathered.png");
            clip.SampleAnimation(view.Pet,1.9f); view.Render(Folder+"/side-reaching.png");
            Call(spring,"OnDisable");
        }
    }
}
