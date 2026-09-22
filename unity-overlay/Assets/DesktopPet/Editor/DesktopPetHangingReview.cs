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
                ValidateArms(view,clip,bone,report);
                clip.SampleAnimation(view.Pet,0);
                float downDrop=bone("Left arm").position.y-bone("Left wrist").position.y;
                Check(downDrop>.40f,"Both arms should begin hanging down, not folded at the chest");
                clip.SampleAnimation(view.Pet,DesktopPetDragAuthoring.ArmPeriod*.34f);
                float levelDrop=Mathf.Abs(bone("Left arm").position.y-bone("Left wrist").position.y);
                var frame=Quaternion.Inverse(Quaternion.Euler(0,30,0));
                float levelSide=(frame*(bone("Left wrist").position-chest.position)).x;
                Check(levelDrop>.25f,"Open the arms diagonally downward along the hanging torso, not horizontally");
                clip.SampleAnimation(view.Pet,DesktopPetDragAuthoring.ArmPeriod*.49f);
                float hugSide=(frame*(bone("Left wrist").position-chest.position)).x;
                Check(hugSide>levelSide+.12f,"Forearms should close inward from the raised pose");
                report.AppendLine($"Arm stages: hanging drop={downDrop:F3}m, open-arm drop={levelDrop:F3}m, inward scoop={hugSide-levelSide:F3}m.");
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

        private static void ValidateArms(DesktopPetDragReview.ReviewScene view,AnimationClip clip,
            Func<string,Transform> bone,StringBuilder report)
        {
            var joints=new[]{"Left arm","Left elbow","Left wrist","Right arm","Right elbow","Right wrist"}.Select(bone).ToArray();
            var restRotations=new[]{bone("Left arm").rotation,bone("Right arm").rotation};
            var restNormals=new[]{"Left","Right"}.Select(side=>Vector3.Cross(
                bone(side+" elbow").position-bone(side+" arm").position,
                bone(side+" wrist").position-bone(side+" elbow").position).normalized).ToArray();
            var palmSigns=new[]{"_L","_R"}.Select(suffix=>Vector3.Dot(Vector3.Cross(
                bone("IndexFinger1"+suffix).position-bone("LittleFinger1"+suffix).position,
                bone("MiddleFinger1"+suffix).position-bone(suffix=="_L"?"Left wrist":"Right wrist").position),Vector3.down)>=0?1f:-1f).ToArray();
            var previous=new Quaternion[joints.Length]; var previousDelta=new Quaternion[joints.Length];
            clip.SampleAnimation(view.Pet,0);
            var wrists=new[]{bone("Left wrist").localRotation,bone("Right wrist").localRotation};
            float maxStep=0,maxAcceleration=0,maxWrist=0,minElbow=180,maxElbow=0,minPalmInward=1,maxShoulderDeparture=0;
            string peakJoint=""; float peakTime=0;
            for(int frame=0;frame<=Mathf.RoundToInt(clip.length*120);frame++)
            {
                clip.SampleAnimation(view.Pet,frame/120f);
                for(int j=0;j<joints.Length;j++)
                {
                    var rotation=joints[j].localRotation;
                    var delta=(rotation*Quaternion.Inverse(previous[j])).normalized;
                    if(frame>0 && Quaternion.Angle(rotation,previous[j])>maxStep)
                    { maxStep=Quaternion.Angle(rotation,previous[j]); peakJoint=joints[j].name; peakTime=frame/120f; }
                    if(frame>1)
                    {
                        var change=(delta*Quaternion.Inverse(previousDelta[j])).normalized;
                        float accurateAngle=2*Mathf.Atan2(new Vector3(change.x,change.y,change.z).magnitude,Mathf.Abs(change.w))*Mathf.Rad2Deg;
                        maxAcceleration=Mathf.Max(maxAcceleration,accurateAngle);
                    }
                    previous[j]=rotation; previousDelta[j]=delta;
                }
                foreach(string side in new[]{"Left","Right"})
                {
                    float flex=ArmAngle(bone,side); minElbow=Mathf.Min(minElbow,flex); maxElbow=Mathf.Max(maxElbow,flex);
                    maxWrist=Mathf.Max(maxWrist,Quaternion.Angle(wrists[side=="Left"?0:1],bone(side+" wrist").localRotation));
                    int index=side=="Left"?0:1;
                    var upper=bone(side+" elbow").position-bone(side+" arm").position;
                    var lower=bone(side+" wrist").position-bone(side+" elbow").position;
                    var expected=bone(side+" arm").rotation*Quaternion.Inverse(restRotations[index])*restNormals[index];
                    Check(Vector3.Dot(expected,Vector3.Cross(upper,lower).normalized)>.98f,"Elbow bent opposite the rig's anatomical direction");
                    var forward=Quaternion.Euler(0,30,0)*Vector3.forward;
                    Check(Vector3.Dot(upper,forward)>0 && Vector3.Dot(lower,forward)>0,"Arm travelled behind the hanging body");
                    var torso=bone("Neck2").position-bone("Chest").position;
                    float departure=Vector3.Angle(upper,torso);
                    maxShoulderDeparture=Mathf.Max(maxShoulderDeparture,departure);
                    Check(departure<55,"Upper arm sharply opposes the hanging torso");
                    Check(bone(side+" arm").position.y-bone(side+" wrist").position.y>.25f,"Hands raised horizontally instead of embracing downward");
                    string suffix=index==0?"_L":"_R";
                    var palmNormal=palmSigns[index]*Vector3.Cross(bone("IndexFinger1"+suffix).position-bone("LittleFinger1"+suffix).position,
                        bone("MiddleFinger1"+suffix).position-bone(side+" wrist").position).normalized;
                    var inward=Quaternion.Euler(0,30,0)*(index==0?Vector3.right:Vector3.left);
                    float facing=Vector3.Dot(palmNormal,inward); minPalmInward=Mathf.Min(minPalmInward,facing);
                    Check(facing>.65f,$"Palm must face inward, with the back of the hand facing outward: {side} at {frame/120f:F3}s dot={facing:F3}");
                }
            }
            string metrics=$"Arm continuity at 120fps: max joint step={maxStep:F3}deg ({peakJoint} at {peakTime:F3}s), delta change={maxAcceleration:F3}deg; elbow flexion={minElbow:F1}..{maxElbow:F1}deg; wrist deviation={maxWrist:F3}deg.";
            File.WriteAllText(Folder+"/arm-validation.txt",metrics);
            Check(maxStep<4,"Arm joint discontinuity: "+metrics);
            Check(maxAcceleration<.8f,"Arm angular speed changes abruptly: "+metrics);
            Check(minElbow>5 && maxElbow<90,"Elbow collapse or lockout: "+metrics);
            Check(maxWrist<.2f,"Wrist should follow forearm without independent folding: "+metrics);
            report.AppendLine(metrics);
            report.AppendLine($"Embrace orientation: minimum palm-inward dot={minPalmInward:F3}; maximum upper-arm/torso departure={maxShoulderDeparture:F1}deg.");
        }

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
            var pivot=new Vector3(0,.75f,.08f);
            view.Camera.transform.position=pivot+Vector3.right*5; view.Camera.transform.LookAt(pivot);
            view.Camera.orthographicSize=1.2f;
            for(int frame=0;frame<Mathf.RoundToInt(clip.length*60);frame++)
            {
                clip.SampleAnimation(view.Pet,frame/60f);
                view.Render(Folder+"/arm-side-"+frame.ToString("D3")+".png");
            }
            clip.SampleAnimation(view.Pet,0); view.Render(Folder+"/side-down.png");
            clip.SampleAnimation(view.Pet,DesktopPetDragAuthoring.ArmPeriod*.34f); view.Render(Folder+"/side-level.png");
            clip.SampleAnimation(view.Pet,DesktopPetDragAuthoring.ArmPeriod*.53f); view.Render(Folder+"/side-reaching.png");
            view.Camera.transform.position=new Vector3(0,.45f,-5); view.Camera.transform.LookAt(new Vector3(0,.75f,0));
            clip.SampleAnimation(view.Pet,0); view.Render(Folder+"/chest-down.png");
            clip.SampleAnimation(view.Pet,DesktopPetDragAuthoring.ArmPeriod*.53f); view.Render(Folder+"/chest-reaching.png");
            Call(spring,"OnDisable");
        }
    }
}
