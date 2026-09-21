using System;
using System.IO;
using System.Reflection;
using DesktopPet;
using UnityEditor;
using UnityEngine;

namespace DesktopPetEditor
{
    // Regression checks run in the isolated model preview, with no menu entry.
    public static class DesktopPetSeatReview
    {
        public static void Run()
        {
            int passed = 0;
            var type = typeof(DesktopWindowAnchorController);
            var project = type.GetMethod("TryGetSeatContactOffset", BindingFlags.NonPublic | BindingFlags.Instance);
            var score = type.GetMethod("TryScoreWindowTop", BindingFlags.NonPublic | BindingFlags.Static);
            using (var view = new DesktopPetDragReview.ReviewScene())
            {
                var controller = view.Pet.AddComponent<DesktopWindowAnchorController>();
                var hips = view.Pet.transform.Find("Armature/Hips");
                type.GetField("seatAnchor", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(controller, hips);
                type.GetField("petCamera", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(controller, view.Camera);
                var petRect = new RectInt(-700, 140, 520, 700);
                foreach (var clipPath in new[] {"Assets/DesktopPet/Animations/DragFlailLoop.anim", "Assets/AnimationClip/Mita Sit Normal.anim",
                    DesktopPetCrossLegAuthoring.ClipPath, DesktopPetCrossLegAuthoring.LoopPath})
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                    foreach (float phase in new[] {0f, 0.25f, 0.5f, 0.75f})
                    {
                        clip.SampleAnimation(view.Pet, phase * clip.length);
                        var args = new object[] {petRect, Vector2Int.zero};
                        Check((bool)project.Invoke(controller, args), "Hip must project", ref passed);
                        var local = (Vector2Int)args[1];
                        var contact = petRect.position + local;
                        Check(Matches(score, contact, new RectInt(contact.x - 100, contact.y, 400, 300)), "Hip-on-edge must attach", ref passed);
                        Check(!Matches(score, contact, new RectInt(contact.x - 100, petRect.yMax - 28, 400, 300)), "Window-bottom-only must not attach", ref passed);
                        Check(Matches(score, contact, new RectInt(contact.x - 100, contact.y + 90, 400, 300)), "Tolerance boundary", ref passed);
                        Check(!Matches(score, contact, new RectInt(contact.x - 100, contact.y + 91, 400, 300)), "Beyond tolerance", ref passed);
                        Check(!Matches(score, contact, new RectInt(contact.x + 41, contact.y, 400, 300)), "Hip horizontally outside edge", ref passed);
                        var attachedOrigin = new Vector2Int(contact.x - local.x, contact.y - local.y);
                        Check(attachedOrigin + local == contact, "Contact remains pinned in both axes", ref passed);
                    }
                }
                // Shift the rig away from the window centre: hit testing must follow the hip.
                hips.position += Vector3.right * 0.65f;
                var shifted = new object[] {petRect, Vector2Int.zero};
                Check((bool)project.Invoke(controller, shifted), "Off-centre hip projects", ref passed);
                var shiftedSeat = petRect.position + (Vector2Int)shifted[1];
                var centreOnlyWindow = new RectInt(petRect.x + petRect.width / 2, shiftedSeat.y, 200, 300);
                Check(!Matches(score, shiftedSeat, centreOnlyWindow), "Window-centre-only must not attach", ref passed);
                // Same viewport, different desktop sizes / negative monitor coordinates.
                var larger = new object[] {new RectInt(-1500, -900, 1040, 1400), Vector2Int.zero};
                Check((bool)project.Invoke(controller, larger), "Scaled window projects", ref passed);
                var a = (Vector2Int)shifted[1]; var b = (Vector2Int)larger[1];
                Check(Mathf.Abs(b.x - 2*a.x) <= 1 && Mathf.Abs((b.y - 24) - 2*(a.y - 24)) <= 1,
                    "Projection scales with actual desktop window", ref passed);
                type.GetField("seatAnchor", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(controller, null);
                Check(!(bool)project.Invoke(controller, new object[] {petRect, Vector2Int.zero}), "Missing hips never falls back to feet", ref passed);
                type.GetField("seatAnchor", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(controller, hips);
                hips.position = view.Camera.transform.position - view.Camera.transform.forward;
                Check(!(bool)project.Invoke(controller, new object[] {petRect, Vector2Int.zero}), "Behind camera is invalid", ref passed);
            }
            Directory.CreateDirectory(DesktopPetDragReview.ReviewFolder);
            File.WriteAllText(DesktopPetDragReview.ReviewFolder + "/seat-validation.txt",
                "PASS " + passed + " checks: actual drag/sit hips, edge distance, horizontal hip position, negative desktop coordinates, scaled windows, missing/behind-camera anchors.\n");
        }

        private static bool Matches(MethodInfo score, Vector2Int point, RectInt window)
        {
            return (bool)score.Invoke(null, new object[] {point, window, 90, 40, 0});
        }

        private static void Check(bool condition, string description, ref int passed)
        {
            if (!condition) throw new Exception("Seat regression: " + description);
            passed++;
        }
    }
}
