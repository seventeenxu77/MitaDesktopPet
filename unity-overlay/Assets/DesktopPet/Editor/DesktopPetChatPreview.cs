using System;
using System.IO;
using System.Reflection;
using DesktopPet;
using UnityEditor;
using UnityEngine;

namespace DesktopPetEditor
{
    // Temporary isolated visual QA, not a permanent Tools menu or scene object.
    public sealed class DesktopPetChatPreview : EditorWindow
    {
        private GameObject runtime;
        private DesktopChatController controller;
        private Texture2D backdrop;
        private double captureAt;
        private Vector2 origin;
        private int phase;
        private bool drew;
        private const string Folder = "Library/DesktopPetChatReview";
        private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;

        public static void Run()
        {
            var window = CreateInstance<DesktopPetChatPreview>();
            window.titleContent = new GUIContent("Desktop Pet · Chat QA");
            window.minSize = window.maxSize = new Vector2(520,700);
            window.position = new Rect(180,80,520,700);
            window.runtime = new GameObject("Chat QA only") { hideFlags = HideFlags.HideAndDontSave };
            var backend = window.runtime.AddComponent<CodexSubscriptionChatBackend>();
            window.controller = window.runtime.AddComponent<DesktopChatController>();
            window.controller.Configure(null, null, null, backend);
            typeof(DesktopChatController).GetMethod("NewConversation", Flags).Invoke(window.controller, null);
            typeof(DesktopChatController).GetField("_open", Flags).SetValue(window.controller, true);
            var append = typeof(DesktopChatController).GetMethod("Append", Flags);
            append.Invoke(window.controller, new object[] {"你", "你好，我叫小海。"});
            append.Invoke(window.controller, new object[] {"米塔", "你好，小海。我就在这里陪着你。\n想聊聊天，还是一起看看今天的问题？"});
            window.backdrop = new Texture2D(2,2);
            window.backdrop.LoadImage(File.ReadAllBytes("Library/DesktopPetMouseLookReview/idle-0.png"));
            window.ShowUtility(); window.Focus();
            window.captureAt = EditorApplication.timeSinceStartup + 1;
            EditorApplication.update += window.Tick;
        }

        private void OnGUI()
        {
            if (controller == null) return;
            GUI.DrawTexture(new Rect(0,0,position.width,position.height), backdrop, ScaleMode.StretchToFill);
            typeof(DesktopChatController).GetMethod("DrawChatGui", Flags).Invoke(controller, new object[] { position.width, position.height });
            if (Event.current.type == EventType.Repaint) { origin = GUIUtility.GUIToScreenPoint(Vector2.zero); drew = true; }
        }

        private void Tick()
        {
            Repaint();
            if (!drew || EditorApplication.timeSinceStartup < captureAt) return;
            try
            {
                Directory.CreateDirectory(Folder);
                var parent = typeof(EditorWindow).GetField("m_Parent", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(this);
                var methods = parent.GetType().GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                File.WriteAllText(Folder + "/capture-methods.txt", string.Join("\n", Array.ConvertAll(methods, m => m.ToString())));
                var grab = parent.GetType().GetMethod("GrabPixels", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (grab == null) throw new NotSupportedException("No window-local capture method. Never capture desktop pixels.");
                int pixelWidth = Mathf.RoundToInt(position.width * EditorGUIUtility.pixelsPerPoint);
                int pixelHeight = Mathf.RoundToInt(position.height * EditorGUIUtility.pixelsPerPoint);
                var target = new RenderTexture(pixelWidth,pixelHeight,0);
                target.Create();
                grab.Invoke(parent, new object[] { target, new Rect(0,0,pixelWidth,pixelHeight) });
                var texture = new Texture2D(pixelWidth,pixelHeight,TextureFormat.RGB24,false);
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0,0,pixelWidth,pixelHeight),0,0);
                var pixels = texture.GetPixels(); var flipped = new Color[pixels.Length];
                for (int row = 0; row < pixelHeight; row++)
                    Array.Copy(pixels, row * pixelWidth, flipped, (pixelHeight - row - 1) * pixelWidth, pixelWidth);
                texture.SetPixels(flipped); texture.Apply();
                RenderTexture.active = previous;
                target.Release(); DestroyImmediate(target);
                File.WriteAllBytes(Folder + (phase == 0 ? "/chat-preview.png" : "/settings-preview.png"), texture.EncodeToPNG());
                DestroyImmediate(texture);
                if (phase++ == 0)
                {
                    typeof(DesktopChatController).GetMethod("OpenSettings", Flags).Invoke(controller, null);
                    drew = false; captureAt = EditorApplication.timeSinceStartup + 1;
                }
                else { File.WriteAllText(Folder + "/preview-status.txt", "Window-local chat/settings capture completed; inspect images before claiming visual verification."); Close(); }
            }
            catch (Exception ex) { File.WriteAllText(Folder + "/preview-status.txt", ex.ToString()); Close(); }
        }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            if (runtime != null) DestroyImmediate(runtime);
            if (backdrop != null) DestroyImmediate(backdrop);
        }
    }
}
