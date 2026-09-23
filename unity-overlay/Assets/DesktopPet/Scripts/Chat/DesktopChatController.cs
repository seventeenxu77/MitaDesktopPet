using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DesktopPet
{
    // IMGUI deliberately avoids the exported project's nonfunctional uGUI DLL.
    public sealed class DesktopChatController : MonoBehaviour
    {
        // Keep old scene/setup bindings so existing scenes upgrade without regeneration.
        [SerializeField] private InputField inputField;
        [SerializeField] private Text responseText;
        [SerializeField] private Button sendButton;
        [SerializeField] private MonoBehaviour backendComponent;
        private IChatBackend _backend;
        private OpenAIResponsesChatBackend _openAI;
        private CodexSubscriptionChatBackend _codex;
        private GPTSoVitsSpeechController _speech;
        private DesktopPetRuntimeSettings _runtimeSettings;
        private readonly List<string> _messages = new List<string>();
        private string _draft = "", _pendingText, _notice = "";
        private string _keyDraft = "", _modelDraft = "", _personaDraft = "";
        private bool _open, _settings, _unread, _wasComposing;
        private int _lastSubmitFrame = -1, _turnVersion;
        private float _sentAt;
        private Vector2 _scroll, _personaScroll, _settingsScroll;
        private GUIStyle _panelStyle, _labelStyle, _mutedStyle, _buttonStyle, _fieldStyle, _historyStyle;
        private Font _font;
        private Texture2D _panelTexture, _buttonTexture, _fieldTexture;
        public static DesktopChatController Active { get; private set; }

        public void Configure(InputField input, Text response, Button send, MonoBehaviour backend)
        {
            inputField = input; responseText = response; sendButton = send; backendComponent = backend;
            _backend = backend as IChatBackend; _openAI = backend as OpenAIResponsesChatBackend;
            _codex = backend as CodexSubscriptionChatBackend;
        }

        private void Awake()
        {
            _backend = backendComponent as IChatBackend;
            if (_backend == null)
                foreach (var component in GetComponents<MonoBehaviour>())
                    if (component is IChatBackend candidate)
                    { _backend = candidate; backendComponent = component; break; }
            _openAI = backendComponent as OpenAIResponsesChatBackend;
            _codex = backendComponent as CodexSubscriptionChatBackend;
            _speech = GetComponent<GPTSoVitsSpeechController>();
            if (_speech == null) _speech = gameObject.AddComponent<GPTSoVitsSpeechController>();
            _runtimeSettings = FindObjectOfType<DesktopPetRuntimeSettings>();
        }

        private void Start()
        {
            if (inputField != null) inputField.transform.parent.gameObject.SetActive(false);
            NewConversation();
        }
        private void OnEnable() { Active = this; }
        private void OnDisable()
        {
            _backend?.Cancel();
            _keyDraft = "";
            if (Active == this) Active = null;
        }
        private void OnDestroy()
        {
            Release(_font); Release(_panelTexture); Release(_buttonTexture); Release(_fieldTexture);
        }
        private static void Release(Object value)
        { if (value == null) return; if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }

        private void LateUpdate() { _wasComposing = !string.IsNullOrEmpty(Input.compositionString); }
        private void OnGUI() { DrawChatGui(Screen.width, Screen.height); }

        internal static void Layout(float width, float height, out float scale, out Rect toggle, out Rect panel)
        {
            scale = Mathf.Max(0.1f, Mathf.Min(width / 520f, height / 700f));
            toggle = new Rect(width / scale - 108, 12, 96, 34);
            panel = new Rect((width / scale - 496) / 2, height / scale - 348, 496, 336);
        }

        public bool IsPointerOverChat(Vector2 screenPoint)
        {
            if (!isActiveAndEnabled) return false;
            return HitTest(screenPoint, Screen.width, Screen.height, _open);
        }

        internal static bool HitTest(Vector2 point, float width, float height, bool open)
        {
            Layout(width, height, out var scale, out var toggle, out var panel);
            var guiPoint = new Vector2(point.x, height - point.y) / scale;
            return toggle.Contains(guiPoint) || (open && panel.Contains(guiPoint));
        }

        internal void DrawChatGui(float width, float height)
        {
            EnsureStyles();
            Layout(width, height, out var scale, out var toggle, out var panel);
            var oldMatrix = GUI.matrix;
            var oldColor = GUI.color;
            int oldDepth = GUI.depth;
            bool oldEnabled = GUI.enabled;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            GUI.color = Color.white;
            GUI.depth = -10;
            if (GUI.Button(toggle, _open ? "收起对话" : _unread ? "新回复" : "对话", _buttonStyle)) TogglePanel();
            if (_open)
            {
                var ev = Event.current;
                bool enter = ev.type == EventType.KeyDown && (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter);
                bool submit = enter && GUI.GetNameOfFocusedControl() == "DesktopPetMessage" &&
                    !_wasComposing && string.IsNullOrEmpty(Input.compositionString);
                if (ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Escape)
                {
                    if (_settings) { _settings = false; _keyDraft = ""; }
                    else TogglePanel();
                    ev.Use();
                }
                GUILayout.BeginArea(panel, _panelStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label("米塔 · 桌面对话", _labelStyle, GUILayout.Height(28));
                if (GUILayout.Button("新对话", _buttonStyle, GUILayout.Width(74), GUILayout.Height(28))) NewConversation();
                GUI.enabled = !IsBusy && (_openAI != null || _codex != null || _runtimeSettings != null);
                if (GUILayout.Button("设置", _buttonStyle, GUILayout.Width(60), GUILayout.Height(28))) OpenSettings();
                GUI.enabled = oldEnabled;
                if (GUILayout.Button("收起", _buttonStyle, GUILayout.Width(60), GUILayout.Height(28))) TogglePanel();
                GUILayout.EndHorizontal();
                GUILayout.Space(5);
                if (_settings) DrawSettings();
                else
                {
                    var status = IsBusy ? "米塔正在思考… " + Mathf.FloorToInt(Time.unscaledTime - _sentAt) + " 秒" :
                        string.IsNullOrEmpty(_notice) ? ReadyStatus() : _notice;
                    GUILayout.Label(status, _mutedStyle, GUILayout.Height(32));
                    _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(192));
                    GUILayout.Label(string.Join("\n\n", _messages), _historyStyle);
                    GUILayout.EndScrollView();
                    GUILayout.Space(7);
                    GUILayout.BeginHorizontal();
                    GUI.enabled = !IsBusy;
                    GUI.SetNextControlName("DesktopPetMessage");
                    _draft = GUILayout.TextField(_draft, 2000, _fieldStyle, GUILayout.Height(36));
                    GUI.enabled = oldEnabled;
                    if (IsBusy)
                    {
                        if (GUILayout.Button("取消", _buttonStyle, GUILayout.Width(74), GUILayout.Height(36))) _backend?.Cancel();
                    }
                    else if (GUILayout.Button("发送", _buttonStyle, GUILayout.Width(74), GUILayout.Height(36))) SendCurrentText();
                    GUILayout.EndHorizontal();
                    if (submit && !IsBusy) { SendCurrentText(); ev.Use(); }
                }
                GUILayout.EndArea();
            }
            GUI.enabled = oldEnabled; GUI.color = oldColor; GUI.matrix = oldMatrix; GUI.depth = oldDepth;
        }

        private bool IsBusy => _backend != null && _backend.IsBusy;

        private void DrawSettings()
        {
            _settingsScroll = GUILayout.BeginScrollView(_settingsScroll, GUILayout.Height(244));
            if (_codex != null)
            {
                GUILayout.Label("ChatGPT 订阅 · " + _codex.StatusText, _mutedStyle);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("检查登录", _buttonStyle, GUILayout.Height(28))) _codex.RefreshAccountStatus();
                if (GUILayout.Button("登录 ChatGPT", _buttonStyle, GUILayout.Height(28))) _codex.BeginChatGptLogin();
                GUILayout.EndHorizontal();
            }
            else if (_openAI != null)
            {
                GUILayout.Label("API Key · 仅本次运行保存，留空保留已有密钥", _mutedStyle);
                _keyDraft = GUILayout.PasswordField(_keyDraft, '*', 1024, _fieldStyle, GUILayout.Height(26));
            }
            else GUILayout.Label("本地测试后端", _mutedStyle);
            if (_speech != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("日语语音 · " + _speech.StatusText, _mutedStyle);
                if (GUILayout.Button("试听", _buttonStyle, GUILayout.Width(60), GUILayout.Height(24)))
                    _speech.Speak("もう眠いよ…少しだけ、そばにいてくれる？");
                if (GUILayout.Button(_speech.VoiceEnabled ? "关闭" : "开启", _buttonStyle, GUILayout.Width(60), GUILayout.Height(24)))
                {
                    if (_runtimeSettings != null) _runtimeSettings.VoiceEnabled = !_runtimeSettings.VoiceEnabled;
                    else _speech.VoiceEnabled = !_speech.VoiceEnabled;
                }
                GUILayout.EndHorizontal();
            }
            if (_runtimeSettings != null)
            {
                GUILayout.Space(4);
                GUILayout.Label("桌宠基础设置", _mutedStyle);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(_runtimeSettings.AlwaysOnTop ? "置顶：开" : "置顶：关", _buttonStyle, GUILayout.Height(26)))
                    _runtimeSettings.AlwaysOnTop = !_runtimeSettings.AlwaysOnTop;
                if (GUILayout.Button(_runtimeSettings.QuietMode ? "安静模式：开" : "安静模式：关", _buttonStyle, GUILayout.Height(26)))
                    _runtimeSettings.QuietMode = !_runtimeSettings.QuietMode;
                if (GUILayout.Button(_runtimeSettings.SurfaceMovementEnabled ? "窗口移动：开" : "窗口移动：关", _buttonStyle, GUILayout.Height(26)))
                    _runtimeSettings.SurfaceMovementEnabled = !_runtimeSettings.SurfaceMovementEnabled;
                GUILayout.EndHorizontal();
                GUILayout.Label("语音音量 " + Mathf.RoundToInt(_runtimeSettings.VoiceVolume * 100f) + "%", _mutedStyle);
                float volume = GUILayout.HorizontalSlider(_runtimeSettings.VoiceVolume, 0f, 1f);
                if (!Mathf.Approximately(volume, _runtimeSettings.VoiceVolume)) _runtimeSettings.VoiceVolume = volume;
                GUILayout.Label("自主动作频率 " + _runtimeSettings.BehaviorFrequency.ToString("0.00") + "×", _mutedStyle);
                float frequency = GUILayout.HorizontalSlider(_runtimeSettings.BehaviorFrequency, 0.25f, 2f);
                if (!Mathf.Approximately(frequency, _runtimeSettings.BehaviorFrequency)) _runtimeSettings.BehaviorFrequency = frequency;
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("召回主屏", _buttonStyle, GUILayout.Height(26))) _runtimeSettings.RecallToPrimaryDisplay();
                if (GUILayout.Button("帧率 " + _runtimeSettings.TargetFrameRate, _buttonStyle, GUILayout.Height(26)))
                    _runtimeSettings.TargetFrameRate = _runtimeSettings.TargetFrameRate < 60 ? 60 :
                        _runtimeSettings.TargetFrameRate < 90 ? 90 : 30;
                GUILayout.EndHorizontal();
                var behavior = FindObjectOfType<PetBehaviorDirector>();
                if (behavior != null)
                {
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button(behavior.ReviewMode ? "动作巡检：开" : "动作巡检：关",
                        _buttonStyle, GUILayout.Height(26))) behavior.SetReviewMode(!behavior.ReviewMode);
                    GUILayout.Label(behavior.IsPlayingScheduledAction ?
                        "当前：" + behavior.CurrentActionId : "当前：等待动作", _mutedStyle, GUILayout.Height(26));
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.Label("模型", _mutedStyle);
            _modelDraft = GUILayout.TextField(_modelDraft, 100, _fieldStyle, GUILayout.Height(26));
            GUILayout.Label("人设 · 应用后开始新对话", _mutedStyle);
            _personaScroll = GUILayout.BeginScrollView(_personaScroll, GUILayout.Height(76));
            _personaDraft = GUILayout.TextArea(_personaDraft, 4000, _fieldStyle, GUILayout.MinHeight(70));
            GUILayout.EndScrollView();
            GUILayout.Label(_codex != null ?
                "通过本机 Codex 使用 ChatGPT 登录态，不需要 API Key。\n对话受 ChatGPT 方案用量限制；桌宠不会调用电脑工具。" :
                "发送内容将交给 OpenAI，连续对话使用服务端记录。\n本地不保存聊天。API 调用可能产生费用。", _mutedStyle, GUILayout.Height(34));
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            if ((_codex != null || _openAI != null) && GUILayout.Button("应用并新对话", _buttonStyle, GUILayout.Height(28))) ApplySettings();
            if (GUILayout.Button("返回", _buttonStyle, GUILayout.Height(28))) { _settings = false; _keyDraft = ""; }
            if (_runtimeSettings != null && GUILayout.Button("退出", _buttonStyle, GUILayout.Width(54), GUILayout.Height(28)))
                _runtimeSettings.ExitApplication();
            GUILayout.EndHorizontal();
        }

        private void TogglePanel()
        {
            _open = !_open;
            _keyDraft = "";
            if (_open) { _settings = false; _unread = false; _scroll.y = float.MaxValue; }
            if (Event.current != null) GUI.FocusControl(null);
        }

        private void SendCurrentText()
        {
            if (_backend == null || IsBusy || _lastSubmitFrame == Time.frameCount) return;
            var text = _draft.Trim();
            if (text.Length == 0) return;
            _lastSubmitFrame = Time.frameCount;
            int version = ++_turnVersion;
            _pendingText = text;
            Append("你", text);
            _draft = ""; _notice = ""; _sentAt = Time.unscaledTime;
            _backend.Send(text,
                reply =>
                {
                    if (this == null || version != _turnVersion) return;
                    _pendingText = null; Append("米塔", reply);
                    var localized = _backend as ILocalizedChatBackend;
                    if (_speech != null && localized != null && !string.IsNullOrWhiteSpace(localized.LastSpeechText))
                        _speech.Speak(localized.LastSpeechText);
                    _notice = ""; _unread = !_open;
                },
                error =>
                {
                    if (this == null || version != _turnVersion) return;
                    if (_messages.Count > 0) _messages.RemoveAt(_messages.Count - 1);
                    Append("提示", error);
                    _draft = _pendingText ?? text; _pendingText = null;
                    _notice = "未完成 · 内容已保留，可重试";
                });
        }

        private string ReadyStatus()
        {
            if (_codex != null) return _codex.StatusText + " · " + _codex.CurrentModel;
            if (_openAI == null) return "本地测试模式 · 不调用 API";
            return _openAI.HasApiKey ? "已配置 · " + _openAI.CurrentModel : "尚未配置 API Key · 点击“设置”";
        }

        private void Append(string speaker, string text)
        {
            _messages.Add(speaker + "：" + text);
            while (_messages.Count > 40) _messages.RemoveAt(0);
            _scroll.y = float.MaxValue;
        }

        private void NewConversation()
        {
            ++_turnVersion; _backend?.ResetConversation();
            _pendingText = null; _messages.Clear(); _draft = ""; _notice = ""; _unread = false;
            Append("米塔", "我在呢，想和我聊些什么？");
        }

        private void OpenSettings()
        {
            if (IsBusy || (_openAI == null && _codex == null && _runtimeSettings == null)) return;
            _keyDraft = "";
            if (_codex != null)
            {
                _modelDraft = _codex.CurrentModel;
                _personaDraft = _codex.CurrentPersona;
            }
            else if (_openAI != null)
            {
                _modelDraft = _openAI.CurrentModel;
                _personaDraft = _openAI.CurrentPersona;
            }
            _settings = true;
        }

        private void ApplySettings()
        {
            if (_codex != null) _codex.ConfigureSession(_modelDraft, _personaDraft);
            else if (_openAI != null) _openAI.ConfigureSession(_keyDraft, _modelDraft, _personaDraft);
            else { _settings = false; return; }
            _keyDraft = ""; _settings = false; NewConversation();
        }

        private void EnsureStyles()
        {
            if (_panelStyle != null) return;
            _font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 16);
            _panelTexture = Texture(new Color(0.055f, 0.07f, 0.11f, 1));
            _buttonTexture = Texture(new Color(0.22f, 0.34f, 0.55f, 1));
            _fieldTexture = Texture(new Color(0.92f, 0.94f, 0.98f, 1));
            _labelStyle = new GUIStyle(GUI.skin.label) { font = _font, fontSize = 17, richText = false, wordWrap = true };
            _labelStyle.normal.textColor = new Color(0.94f, 0.95f, 1);
            _mutedStyle = new GUIStyle(_labelStyle) { fontSize = 12 };
            _mutedStyle.normal.textColor = new Color(0.7f, 0.77f, 0.87f);
            _historyStyle = new GUIStyle(_labelStyle) { fontSize = 16, padding = new RectOffset(4, 10, 4, 8) };
            _panelStyle = new GUIStyle { padding = new RectOffset(12, 12, 10, 10) };
            _panelStyle.normal.background = _panelTexture;
            _buttonStyle = new GUIStyle { font = _font, fontSize = 14, richText = false,
                alignment = TextAnchor.MiddleCenter, stretchWidth = true, margin = new RectOffset(2,2,2,2) };
            _buttonStyle.normal.background = _buttonTexture; _buttonStyle.normal.textColor = Color.white;
            _buttonStyle.hover.background = _buttonTexture; _buttonStyle.hover.textColor = new Color(0.8f,0.9f,1);
            _buttonStyle.active.background = _buttonTexture; _buttonStyle.active.textColor = Color.white;
            _buttonStyle.focused.background = _buttonTexture; _buttonStyle.focused.textColor = Color.white;
            _fieldStyle = new GUIStyle { font = _font, fontSize = 15, richText = false, wordWrap = true,
                padding = new RectOffset(7, 7, 4, 4), margin = new RectOffset(2,2,2,2), stretchWidth = true };
            _fieldStyle.normal.background = _fieldTexture; _fieldStyle.normal.textColor = new Color(0.06f, 0.08f, 0.12f);
            _fieldStyle.focused.background = _fieldTexture; _fieldStyle.focused.textColor = _fieldStyle.normal.textColor;
        }

        private static Texture2D Texture(Color color)
        {
            var texture = new Texture2D(1, 1); texture.SetPixel(0, 0, color); texture.Apply(); return texture;
        }
    }
}
