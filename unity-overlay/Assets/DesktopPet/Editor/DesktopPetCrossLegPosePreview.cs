using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DesktopPetEditor
{
    // Approval-only preview. Does not bake clips, change controllers or save scenes.
    public static class DesktopPetCrossLegPosePreview
    {
        private const string Folder = "Library/DesktopPetCrossLegReview/CurrentPose";

        public static void Render()
        {
            Directory.CreateDirectory(Folder);
            using (var view = new DesktopPetDragReview.ReviewScene())
            {
                var sit = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/AnimationClip/Mita Sit Normal.anim");
                var rig = new DesktopPetCrossLegAuthoring.SeatedRig(view.Pet, sit);
                var bones = rig.Bones.GroupBy(b => b.name).ToDictionary(g => g.Key, g => g.First());
                rig.Pose(DesktopPetCrossLegAuthoring.EnterDuration);
                view.Render(Folder + "/front.png");

                var pivot = new Vector3(0,.71f,-.82f);
                view.Camera.orthographicSize = .72f;
                view.Camera.transform.position = pivot + Vector3.forward * 5;
                view.Camera.transform.LookAt(pivot);
                view.Render(Folder + "/front-detail.png");
                view.Camera.transform.position = pivot + new Vector3(3,.2f,4);
                view.Camera.transform.LookAt(pivot);
                view.Render(Folder + "/three-quarter.png");

                var footPivot = bones["Left ankle"].position + new Vector3(0,-.015f,.04f);
                view.Camera.orthographicSize = .22f;
                view.Camera.transform.position = footPivot + Vector3.forward * 5;
                view.Camera.transform.LookAt(footPivot);
                view.Render(Folder + "/foot-detail.png");

                File.WriteAllText(Folder + "/status.txt", "Preview only. Runtime clips and controller unchanged.\n" +
                    "Left knee=" + bones["Left knee"].position.ToString("F4") + "\nLeft ankle=" + bones["Left ankle"].position.ToString("F4") +
                    "\nLeft toe=" + bones["Left toe"].position.ToString("F4"));
            }
        }

    }
}
