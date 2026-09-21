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
        private const string Folder = "Library/DesktopPetCrossLegReview/ForwardPoseFootLift";

        public static void Render()
        {
            Directory.CreateDirectory(Folder);
            using (var view = new DesktopPetDragReview.ReviewScene())
            {
                var sit = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/AnimationClip/Mita Sit Normal.anim");
                var rig = new DesktopPetCrossLegAuthoring.SeatedRig(view.Pet, sit);
                var bones = rig.Bones.GroupBy(b => b.name).ToDictionary(g => g.Key, g => g.First());
                var originalPoints = bones.ToDictionary(p => p.Key, p => p.Value.position);
                var originalRotations = bones.ToDictionary(p => p.Key, p => p.Value.rotation);
                var restingToeRotation = bones["Left toe"].localRotation;
                rig.Pose(DesktopPetCrossLegAuthoring.EnterDuration);

                Aim(bones, originalPoints, originalRotations, "Right leg", "Right knee", new Vector3(-.09f,-.10f,.423f));
                Aim(bones, originalPoints, originalRotations, "Right knee", "Right ankle", new Vector3(-.085f,-.46f,.035f));
                Aim(bones, originalPoints, originalRotations, "Right ankle", "Right toe", new Vector3(0,-.10f,.13f));
                Aim(bones, originalPoints, originalRotations, "Left leg", "Left knee", new Vector3(.135f,.145f,.398f));
                Aim(bones, originalPoints, originalRotations, "Left knee", "Left ankle", new Vector3(0,-.445f,.29f));
                Aim(bones, originalPoints, originalRotations, "Left ankle", "Left toe", new Vector3(0,-.01f,.15f));
                bones["Left toe"].localRotation = restingToeRotation;
                bones["Left toe"].rotation = Quaternion.AngleAxis(-4f, Vector3.right) * bones["Left toe"].rotation;
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

        private static void Aim(Dictionary<string,Transform> bones, Dictionary<string,Vector3> points,
            Dictionary<string,Quaternion> rotations, string bone, string child, Vector3 direction)
        {
            var rest = points[child] - points[bone];
            bones[bone].rotation = Quaternion.FromToRotation(rest.normalized, direction.normalized) * rotations[bone];
        }
    }
}
