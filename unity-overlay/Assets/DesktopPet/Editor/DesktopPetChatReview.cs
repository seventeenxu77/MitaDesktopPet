using System;
using System.IO;
using System.Reflection;
using DesktopPet;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace DesktopPetEditor
{
    public static class DesktopPetChatReview
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string Folder = "Library/DesktopPetChatReview";
        private static int passed;

        public static void Run()
        {
            passed = 0;
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Folder + "/ui-assembly.txt", typeof(Button).Assembly.Location + "\n" +
                "Button.onClick getter IL: " + BitConverter.ToString(typeof(Button).GetProperty("onClick").GetGetMethod().GetMethodBody().GetILAsByteArray()) + "\n" +
                "Text.text setter IL: " + BitConverter.ToString(typeof(Text).GetProperty("text").GetSetMethod().GetMethodBody().GetILAsByteArray()) + "\n");
            CheckProtocol();
            CheckSubscriptionBackend();
            CheckInterface();
            File.WriteAllText(Folder + "/validation.txt", "PASS " + passed +
                " checks: Responses fallback, Codex subscription backend, persona/context, key isolation, errors, UI send/reply/retry/cancel/reset, long history, scaled native UI hit tests, settings, collapse. No live model call.\n");
        }

        private static void CheckSubscriptionBackend()
        {
            var go = new GameObject("Codex subscription review");
            try
            {
                var backend = go.AddComponent<CodexSubscriptionChatBackend>();
                var persona = AssetDatabase.LoadAssetAtPath<DesktopPetPersona>("Assets/DesktopPet/Config/DesktopMitaPersona.asset");
                Check(persona != null && persona.instructions.Contains("瞌睡米塔") &&
                    persona.instructions.Contains("咖啡") && persona.instructions.Contains("不要每句话都打哈欠"),
                    "Sleepy Mita persona asset keeps source-grounded traits and restrained speech style");
                backend.ConfigureSession("", "我是米塔，只做文字对话。");
                Check(backend.CurrentModel == "Codex 默认模型" && backend.CurrentPersona.Contains("米塔"),
                    "Subscription settings use default model and editable persona");
                var quote = typeof(CodexSubscriptionChatBackend).GetMethod("Quote", BindingFlags.Static | BindingFlags.NonPublic);
                var encoded = (string)quote.Invoke(null, new object[] { "中文、引号\"和\n换行" });
                var decoded = JsonUtility.FromJson<QuotedCheck>("{\"value\":" + encoded + "}");
                Check(decoded.value == "中文、引号\"和\n换行", "App Server JSON escapes Unicode input");
                var resolve = typeof(CodexSubscriptionChatBackend).GetMethod("ResolveCodexExecutable", Hidden);
                var executable = (string)resolve.Invoke(backend, null);
                Check(!string.IsNullOrWhiteSpace(executable) && File.Exists(executable), "Installed Codex CLI is discoverable");
                Check(!backend.IsBusy && !backend.IsSignedIn, "Backend stays offline until runtime start");
            }
            finally { Object.DestroyImmediate(go); }
        }

        private static void CheckProtocol()
        {
            var go = new GameObject("Chat protocol review");
            try
            {
                var backend = go.AddComponent<OpenAIResponsesChatBackend>();
                Set(backend, "apiKeyEnvironmentVariable", "DESKTOP_PET_NONEXISTENT_TEST_KEY");
                string failure = null;
                backend.Send("你好", text => throw new Exception("Unexpected response"), e => failure = e);
                Check(failure != null && failure.Contains("API Key") && !backend.IsBusy, "Missing key fails cleanly");
                backend.ConfigureSession("test-only-not-a-real-key", "gpt-5.3-codex", "我是米塔，保持简洁。");
                var first = (string)Call(backend, "BuildRequestJson", "中文、引号\"和\n换行");
                Check(!first.Contains("previous_response_id"), "First request omits previous ID, not empty string");
                Check(!first.Contains("test-only"), "Key not in API JSON");
                Check(!JsonUtility.ToJson(backend).Contains("test-only"), "Key not in Unity serialization");
                var wire = JsonUtility.FromJson<RequestCheck>(first);
                Check(wire.input == "中文、引号\"和\n换行" && wire.model == "gpt-5.3-codex", "Unicode JSON round trip");
                Check(wire.instructions.Contains("米塔") && wire.max_output_tokens >= 2048, "Persona and output budget");
                Set(backend, "_previousResponseId", "resp_test_first");
                var second = JsonUtility.FromJson<RequestCheck>((string)Call(backend, "BuildRequestJson", "我刚才说了什么？"));
                Check(second.previous_response_id == "resp_test_first" && second.instructions == wire.instructions,
                    "Followup contains inherited fields and repeated persona");
                backend.ConfigureSession("", "gpt-5.3-codex", "新的人设");
                Check(backend.HasApiKey && backend.CurrentPersona == "新的人设", "Empty key keeps configured secret");
                Check(!((string)Call(backend, "BuildRequestJson", "你好")).Contains("previous_response_id"), "Settings start new context");
                foreach (var address in new[] { "http://api.openai.com/v1/responses", "https://api.openai.com.evil.test/v1/responses",
                    "https://example.com/v1/responses", "https://key@api.openai.com/v1/responses", "https://api.openai.com/v1/responses?key=abc" })
                    Check(!(bool)Static("IsOfficialEndpoint", address), "Reject untrusted endpoint");
                Check((bool)Static("IsOfficialEndpoint", "https://api.openai.com/v1/responses"), "Accept official endpoint");
                Set(backend, "endpoint", "http://invalid.test"); failure = null;
                backend.Send("hello", text => throw new Exception("Unexpected request"), e => failure = e);
                Check(failure != null && !backend.IsBusy, "Unsafe endpoint stops before transport");
                var parse = typeof(OpenAIResponsesChatBackend).GetMethod("TryParseReply", BindingFlags.Static | BindingFlags.NonPublic);
                var json = "{\"id\":\"resp_ok\",\"status\":\"completed\",\"output\":[" +
                    "{\"type\":\"reasoning\",\"content\":[{\"type\":\"output_text\",\"text\":\"hidden\"}]}," +
                    "{\"type\":\"message\",\"role\":\"assistant\",\"phase\":\"commentary\",\"content\":[{\"type\":\"output_text\",\"text\":\"preamble\"}]}," +
                    "{\"type\":\"message\",\"role\":\"assistant\",\"phase\":\"final_answer\",\"content\":[{\"type\":\"output_text\",\"text\":\"你好，\"},{\"type\":\"output_text\",\"text\":\"我在呢。\"}]}]}";
                var args = new object[] { json, null, null, null };
                Check((bool)parse.Invoke(null, args) && ((string)args[1]).Contains("你好") &&
                    !((string)args[1]).Contains("hidden") && !((string)args[1]).Contains("preamble") && (string)args[2] == "resp_ok",
                    "Only final assistant output is displayed");
                var refusal = "{\"id\":\"resp_refusal\",\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"refusal\",\"refusal\":\"无法协助这件事。\"}]}]}";
                args = new object[] { refusal, null, null, null };
                Check((bool)parse.Invoke(null, args) && (string)args[1] == "无法协助这件事。", "Refusal is displayed");
                foreach (var invalid in new[] { "", "<html>bad gateway</html>", "{}", "{\"status\":\"incomplete\"}",
                    "{\"status\":\"failed\"}", "{\"status\":\"completed\",\"id\":\"resp_empty\",\"output\":[]}" })
                {
                    args = new object[] { invalid, null, null, null };
                    Check(!(bool)parse.Invoke(null, args) && args[3] != null && args[2] == null, "Invalid response cannot commit context");
                }
                foreach (long code in new long[] { 0, 400, 401, 403, 404, 429, 500, 502 })
                    Check(!string.IsNullOrWhiteSpace((string)Static("ExplainHttpError", code)), "Actionable HTTP error " + code);
                backend.ResetConversation();
                Check(!backend.IsBusy, "Reset is idempotent");
            }
            finally { Object.DestroyImmediate(go); }
        }

        private static void CheckInterface()
        {
            var runtime = new GameObject("Chat UI state review");
            try
            {
                var backend = runtime.AddComponent<DesktopPetChatReviewBackend>();
                var controller = runtime.AddComponent<DesktopChatController>();
                controller.Configure(null, null, null, backend);
                Call(controller, "Start");
                Check(!(bool)Get(controller, "_open"), "Starts collapsed");
                Call(controller, "TogglePanel");
                Check((bool)Get(controller, "_open"), "Toggle opens chat");
                Set(controller, "_draft", "你好，我叫小海。"); ClickSend(controller);
                Check(backend.IsBusy && backend.LastInput.Contains("小海"), "Send starts request");
                Call(controller, "SendCurrentText"); Check(backend.Sends == 1, "No duplicate request");
                backend.Complete("你好，小海。我会在桌面陪着你。");
                Check(!backend.IsBusy && Transcript(controller).Contains("小海"), "Reply keeps transcript");
                Set(controller, "_draft", "我刚才说我叫什么？"); ClickSend(controller);
                backend.Fail("本地模拟网络错误");
                Check((string)Get(controller, "_draft") == "我刚才说我叫什么？", "Failure restores draft");
                ClickSend(controller); backend.Cancel();
                Check(!backend.IsBusy && ((string)Get(controller, "_draft")).Length > 0, "Cancel restores draft");
                ClickSend(controller);
                var stale = backend.PendingCompletion;
                Call(controller, "NewConversation"); stale?.Invoke("过期回复");
                Check(!Transcript(controller).Contains("过期回复") && (string)Get(controller, "_draft") == "" && !backend.IsBusy,
                    "Reset rejects stale response");
                for (int i = 0; i < 50; i++) Call(controller, "Append", "米塔", "长回复滚动检查 " + i);
                Check(((System.Collections.ICollection)Get(controller, "_messages")).Count == 40, "Bounded visible transcript");
                var api = runtime.AddComponent<OpenAIResponsesChatBackend>();
                Set(controller, "_openAI", api); Call(controller, "OpenSettings");
                Check((string)Get(controller, "_keyDraft") == "" && (bool)Get(controller, "_settings"), "Secret never prefilled");
                Set(controller, "_keyDraft", "test-only-not-a-real-key");
                Set(controller, "_personaDraft", "我是米塔，这是测试人设。");
                Set(controller, "_modelDraft", "gpt-5.3-codex");
                Call(controller, "ApplySettings");
                Check((string)Get(controller, "_keyDraft") == "" && api.HasApiKey && api.CurrentPersona.Contains("测试"),
                    "Apply clears key field and keeps session settings");
                Call(controller, "TogglePanel");
                Check(!(bool)Get(controller, "_open"), "Hide closes panel");
                Set(controller, "_draft", "我等你回复"); ClickSend(controller); backend.Complete("我回来了。");
                Check((bool)Get(controller, "_unread"), "Hidden reply sets unread marker");
                var type = typeof(DesktopChatController);
                foreach (var size in new[] { new Vector2(520,700), new Vector2(1040,1400), new Vector2(720,700), new Vector2(260,350) })
                {
                    var args = new object[] { size.x, size.y, 0f, new Rect(), new Rect() };
                    type.GetMethod("Layout", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);
                    float scale = (float)args[2];
                    var toggle = (Rect)args[3]; var panel = (Rect)args[4];
                    var t = new Vector2(toggle.center.x * scale, size.y - toggle.center.y * scale);
                    var p = new Vector2(panel.center.x * scale, size.y - panel.center.y * scale);
                    var hit = type.GetMethod("HitTest", BindingFlags.NonPublic | BindingFlags.Static);
                    Check((bool)hit.Invoke(null, new object[] { t, size.x, size.y, false }), "Collapsed toggle remains clickable");
                    Check(!(bool)hit.Invoke(null, new object[] { p, size.x, size.y, false }), "Hidden panel clicks pass through");
                    Check((bool)hit.Invoke(null, new object[] { p, size.x, size.y, true }), "Visible panel blocks drag/click-through");
                    Check(!(bool)hit.Invoke(null, new object[] { Vector2.zero, size.x, size.y, true }), "Outside UI remains transparent");
                }
            }
            finally { Object.DestroyImmediate(runtime); }
        }

        private static string Transcript(object controller)
        { return string.Join("\n", (System.Collections.Generic.List<string>)Get(controller, "_messages")); }
        private static void ClickSend(object controller)
        { Set(controller, "_lastSubmitFrame", -1); Call(controller, "SendCurrentText"); }
        private static object Static(string name, params object[] args) => typeof(OpenAIResponsesChatBackend).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);
        private static object Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Hidden).Invoke(obj, args);
        private static object Get(object obj, string name) => obj.GetType().GetField(name, Hidden).GetValue(obj);
        private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Hidden).SetValue(obj, value);
        private static void Check(bool ok, string name) { if (!ok) throw new Exception("Chat review: " + name); passed++; }
        [Serializable] private class RequestCheck
        {
            public string input = null, model = null, instructions = null, previous_response_id = null;
            public int max_output_tokens = 0;
        }
        [Serializable] private class QuotedCheck { public string value = null; }
    }

    public sealed class DesktopPetChatReviewBackend : MonoBehaviour, IChatBackend
    {
        public bool IsBusy { get; private set; }
        public string LastInput;
        public int Sends;
        public Action<string> PendingCompletion;
        private Action<string> error;
        public void Send(string text, Action<string> complete, Action<string> failure)
        { IsBusy = true; LastInput = text; Sends++; PendingCompletion = complete; error = failure; }
        public void Complete(string text) { IsBusy = false; var c = PendingCompletion; PendingCompletion = null; error = null; c?.Invoke(text); }
        public void Fail(string text) { IsBusy = false; var e = error; error = null; PendingCompletion = null; e?.Invoke(text); }
        public void Cancel() { Fail("已取消测试请求。"); }
        public void ResetConversation() { Cancel(); }
    }
}
