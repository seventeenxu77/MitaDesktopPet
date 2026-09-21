using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

namespace DesktopPet
{
    // Uses the local Codex App Server and its ChatGPT login. No API key is stored in Unity.
    public sealed class CodexSubscriptionChatBackend : MonoBehaviour, IChatBackend, ILocalizedChatBackend
    {
        [SerializeField] private string model = "";
        [SerializeField, Min(15)] private int timeoutSeconds = 120;
        [SerializeField] private DesktopPetPersona persona;
        [SerializeField, TextArea(8, 18)] private string personaInstructions =
            DesktopPetPersona.SleepyMitaDefault;
        [SerializeField] private string codexExecutable = "";

        private readonly Queue<string> _stdout = new Queue<string>();
        private readonly object _stdoutLock = new object();
        private readonly Dictionary<int, string> _requests = new Dictionary<int, string>();
        private Process _process;
        private StreamWriter _input;
        private int _nextRequestId;
        private bool _initialized, _accountChecked, _signedIn, _loginRequested, _shuttingDown;
        private string _sessionModel, _sessionPersona, _threadId, _turnId, _finalReply, _lastProcessError;
        private string _pendingText;
        private Action<string> _pendingCompleted, _pendingError;
        private float _requestStartedAt;

        public bool IsBusy { get; private set; }
        public string LastSpeechText { get; private set; }
        public bool IsSignedIn => _signedIn;
        public string CurrentModel => string.IsNullOrWhiteSpace(_sessionModel) ?
            (string.IsNullOrWhiteSpace(model) ? "Codex 默认模型" : model) : _sessionModel;
        public string CurrentPersona => _sessionPersona ??
            (persona != null && !string.IsNullOrWhiteSpace(persona.instructions) ? persona.instructions : personaInstructions);
        public string StatusText { get; private set; } = "正在检查 ChatGPT 登录状态…";

        public void ConfigurePersona(DesktopPetPersona value) { persona = value; }

        public void ConfigureSession(string modelName, string instructions)
        {
            ResetConversation();
            _sessionModel = string.IsNullOrWhiteSpace(modelName) || modelName.Trim() == "Codex 默认模型" ? null : modelName.Trim();
            _sessionPersona = string.IsNullOrWhiteSpace(instructions) ? null : instructions.Trim();
        }

        private void Start() { RefreshAccountStatus(); }

        public void RefreshAccountStatus()
        {
            if (!EnsureServer()) return;
            if (_initialized) RequestAccount();
        }

        public void BeginChatGptLogin()
        {
            if (_signedIn)
            {
                RefreshAccountStatus();
                return;
            }
            _loginRequested = true;
            StatusText = "正在打开 ChatGPT 登录…";
            if (!EnsureServer()) return;
            if (!_initialized) return;
            SendLoginRequest();
        }

        public void Send(string userText, Action<string> onCompleted, Action<string> onError)
        {
            if (IsBusy) { onError?.Invoke("还在等待上一条回复。"); return; }
            if (!isActiveAndEnabled) { onError?.Invoke("对话服务尚未启用。"); return; }
            if (string.IsNullOrWhiteSpace(userText)) { onError?.Invoke("请先输入内容。"); return; }
            if (userText.Length > 2000) { onError?.Invoke("每条消息最多 2000 个字符。"); return; }

            IsBusy = true;
            _pendingText = userText.Trim();
            _pendingCompleted = onCompleted;
            _pendingError = onError;
            _requestStartedAt = Time.realtimeSinceStartup;
            _finalReply = null;
            LastSpeechText = null;
            if (!EnsureServer()) return;
            if (!_initialized) return;
            if (!_accountChecked) { RequestAccount(); return; }
            if (!_signedIn) { Fail("Codex 尚未使用 ChatGPT 登录。请在“设置”中点击“登录 ChatGPT”。"); return; }
            StartPendingTurn();
        }

        public void Cancel()
        {
            InterruptActiveTurn();
            if (IsBusy) Fail("已取消，输入内容已保留。");
        }

        public void ResetConversation()
        {
            if (IsBusy) Cancel();
            _threadId = null;
            _turnId = null;
            _finalReply = null;
        }

        private void Update()
        {
            while (true)
            {
                string line;
                lock (_stdoutLock)
                {
                    if (_stdout.Count == 0) break;
                    line = _stdout.Dequeue();
                }
                HandleLine(line);
            }

            if (_process != null && !_shuttingDown && _process.HasExited)
            {
                string details = string.IsNullOrWhiteSpace(_lastProcessError) ? "" : "\n" + _lastProcessError;
                ShutdownProcess();
                StatusText = "Codex 服务已停止";
                if (IsBusy) Fail("本机 Codex 服务意外停止，请确认 Codex CLI 可用后重试。" + details);
            }
            if (IsBusy && Time.realtimeSinceStartup - _requestStartedAt > Mathf.Max(15, timeoutSeconds))
            {
                InterruptActiveTurn();
                Fail("等待回复超时，请重试。");
            }
        }

        private bool EnsureServer()
        {
            if (IsProcessAlive()) return true;
            try
            {
                var executable = ResolveCodexExecutable();
                if (string.IsNullOrEmpty(executable))
                {
                    StatusText = "未找到 Codex CLI";
                    if (IsBusy) Fail("未找到 Codex CLI。请先安装 Codex，并执行 codex login 使用 ChatGPT 登录。");
                    return false;
                }
                var info = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = "app-server --stdio",
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = new UTF8Encoding(false),
                    StandardErrorEncoding = new UTF8Encoding(false)
                };
                _process = new Process { StartInfo = info, EnableRaisingEvents = true };
                _process.OutputDataReceived += (_, args) => Enqueue(args.Data);
                _process.ErrorDataReceived += (_, args) => { if (!string.IsNullOrWhiteSpace(args.Data)) _lastProcessError = args.Data; };
                if (!_process.Start()) throw new InvalidOperationException("Codex process did not start.");
                _input = _process.StandardInput;
                _input.AutoFlush = true;
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
                _initialized = _accountChecked = _signedIn = false;
                _requests.Clear();
                StatusText = "正在连接本机 Codex…";
                SendRequest("initialize", "{\"clientInfo\":{\"name\":\"desktop-pet\",\"title\":\"Desktop Mita\",\"version\":\"1.0.0\"}}");
                return true;
            }
            catch (Exception ex)
            {
                ShutdownProcess();
                StatusText = "无法启动 Codex";
                if (IsBusy) Fail("无法启动本机 Codex：" + ex.Message);
                return false;
            }
        }

        private void HandleLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            RpcEnvelope message;
            try { message = JsonUtility.FromJson<RpcEnvelope>(line); }
            catch { return; }
            if (message == null) return;

            if (message.id != 0 && _requests.TryGetValue(message.id, out var method))
            {
                _requests.Remove(message.id);
                if (message.error != null && !string.IsNullOrWhiteSpace(message.error.message))
                {
                    HandleRequestError(method, message.error.message);
                    return;
                }
                HandleResponse(method, message.result);
                return;
            }

            var parameters = message.@params;
            if (parameters == null) return;
            if (message.method == "account/login/completed")
            {
                _loginRequested = false;
                RequestAccount();
            }
            else if (message.method == "item/completed" && IsBusy && parameters.item != null &&
                parameters.item.type == "agentMessage" &&
                (string.IsNullOrEmpty(parameters.item.phase) || parameters.item.phase == "final_answer"))
            {
                _finalReply = parameters.item.text;
            }
            else if (message.method == "turn/completed" && IsBusy && parameters.turn != null &&
                (string.IsNullOrEmpty(parameters.threadId) || parameters.threadId == _threadId))
            {
                if (parameters.turn.status == "completed" && !string.IsNullOrWhiteSpace(_finalReply)) Complete(_finalReply.Trim());
                else if (parameters.turn.status == "interrupted") Fail("回复已取消，输入内容已保留。");
                else Fail(parameters.turn.error != null && !string.IsNullOrWhiteSpace(parameters.turn.error.message) ?
                    "Codex 无法完成回复：" + parameters.turn.error.message : "Codex 没有返回可显示的回复，请重试。");
            }
        }

        private void HandleResponse(string method, RpcResult result)
        {
            if (method == "initialize")
            {
                _initialized = true;
                SendNotification("initialized", "{}");
                RequestAccount();
            }
            else if (method == "account/read")
            {
                _accountChecked = true;
                _signedIn = result != null && result.account != null && result.account.type == "chatgpt";
                if (_signedIn)
                {
                    var plan = DisplayPlan(result.account.planType);
                    StatusText = "已登录 · " + plan;
                    _loginRequested = false;
                    if (IsBusy) StartPendingTurn();
                }
                else if (_loginRequested) SendLoginRequest();
                else
                {
                    StatusText = "尚未使用 ChatGPT 登录";
                    if (IsBusy) Fail("Codex 尚未使用 ChatGPT 登录。请在“设置”中点击“登录 ChatGPT”。");
                }
            }
            else if (method == "account/login/start")
            {
                if (result != null && !string.IsNullOrWhiteSpace(result.authUrl))
                {
                    StatusText = "浏览器登录完成后会自动更新";
                    Application.OpenURL(result.authUrl);
                }
                else StatusText = "没有取得 ChatGPT 登录地址";
            }
            else if (method == "thread/start")
            {
                if (result == null || result.thread == null || string.IsNullOrWhiteSpace(result.thread.id))
                { Fail("Codex 未能建立对话线程。"); return; }
                _threadId = result.thread.id;
                if (IsBusy && !string.IsNullOrEmpty(_pendingText)) StartTurn();
            }
            else if (method == "turn/start")
            {
                if (IsBusy) _turnId = result != null && result.turn != null ? result.turn.id : null;
            }
        }

        private void HandleRequestError(string method, string error)
        {
            if (method == "account/read" || method == "account/login/start")
            {
                StatusText = "ChatGPT 登录检查失败";
                _accountChecked = true;
            }
            if (IsBusy) Fail("Codex 请求失败：" + error);
        }

        private void RequestAccount()
        {
            if (!_initialized || !IsProcessAlive() || HasPendingRequest("account/read")) return;
            StatusText = "正在检查 ChatGPT 登录状态…";
            SendRequest("account/read", "{\"refreshToken\":false}");
        }

        private void SendLoginRequest()
        {
            if (!_initialized || HasPendingRequest("account/login/start")) return;
            SendRequest("account/login/start", "{\"type\":\"chatgpt\"}");
        }

        private void StartPendingTurn()
        {
            if (!IsBusy || string.IsNullOrEmpty(_pendingText)) return;
            if (string.IsNullOrEmpty(_threadId)) StartThread(); else StartTurn();
        }

        private void StartThread()
        {
            var runtimeDirectory = Path.Combine(Application.temporaryCachePath, "DesktopPetCodexChat");
            Directory.CreateDirectory(runtimeDirectory);
            var instructions = CurrentPersona + "\n\n" +
                "你只进行普通文字对话。绝对不要调用任何工具、命令、联网功能，不要读取或修改任何文件，" +
                "不要声称看到了用户屏幕。不要输出工作过程或状态播报。" +
                "每次回答都同时准备：给用户阅读的简体中文，以及意思相同、符合瞌睡米塔语气的自然日语口语，用于语音合成。";
            var selectedModel = string.IsNullOrWhiteSpace(_sessionModel) ? model : _sessionModel;
            var modelJson = string.IsNullOrWhiteSpace(selectedModel) ? "" : ",\"model\":" + Quote(selectedModel.Trim());
            SendRequest("thread/start", "{\"cwd\":" + Quote(runtimeDirectory) +
                ",\"approvalPolicy\":\"never\",\"sandbox\":\"read-only\",\"ephemeral\":true" + modelJson +
                ",\"config\":{\"mcp_servers\":{},\"features\":{\"plugins\":false}}" +
                ",\"baseInstructions\":" + Quote(instructions) + "}");
        }

        private void InterruptActiveTurn()
        {
            if (!string.IsNullOrEmpty(_threadId) && !string.IsNullOrEmpty(_turnId) && IsProcessAlive())
                SendRequest("turn/interrupt", "{\"threadId\":" + Quote(_threadId) + ",\"turnId\":" + Quote(_turnId) + "}");
        }

        private void StartTurn()
        {
            _finalReply = null;
            _turnId = null;
            SendRequest("turn/start", "{\"threadId\":" + Quote(_threadId) +
                ",\"input\":[{\"type\":\"text\",\"text\":" + Quote(_pendingText) + "}],\"effort\":\"low\"" +
                ",\"outputSchema\":{\"type\":\"object\",\"properties\":{" +
                "\"display_text_zh\":{\"type\":\"string\"},\"speech_text_ja\":{\"type\":\"string\"}}," +
                "\"required\":[\"display_text_zh\",\"speech_text_ja\"],\"additionalProperties\":false}}");
        }

        private int SendRequest(string method, string parameters)
        {
            int id = ++_nextRequestId;
            _requests[id] = method;
            Write("{\"method\":" + Quote(method) + ",\"id\":" + id + ",\"params\":" + parameters + "}");
            return id;
        }

        private void SendNotification(string method, string parameters)
        { Write("{\"method\":" + Quote(method) + ",\"params\":" + parameters + "}"); }

        private void Write(string json)
        {
            if (_input == null) throw new InvalidOperationException("Codex input is unavailable.");
            _input.WriteLine(json);
        }

        private bool HasPendingRequest(string method)
        {
            foreach (var value in _requests.Values) if (value == method) return true;
            return false;
        }

        private void Complete(string reply)
        {
            var visibleReply = reply;
            LastSpeechText = null;
            try
            {
                var localized = JsonUtility.FromJson<LocalizedReply>(StripCodeFence(reply));
                if (localized != null && !string.IsNullOrWhiteSpace(localized.display_text_zh))
                {
                    visibleReply = localized.display_text_zh.Trim();
                    if (!string.IsNullOrWhiteSpace(localized.speech_text_ja))
                        LastSpeechText = localized.speech_text_ja.Trim();
                }
            }
            catch { }
            var callback = _pendingCompleted;
            ClearPending();
            callback?.Invoke(visibleReply);
        }

        private static string StripCodeFence(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            var text = value.Trim();
            if (!text.StartsWith("```", StringComparison.Ordinal)) return text;
            int firstLine = text.IndexOf('\n');
            int lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            return firstLine >= 0 && lastFence > firstLine ?
                text.Substring(firstLine + 1, lastFence - firstLine - 1).Trim() : text;
        }

        private void Fail(string error)
        {
            var callback = _pendingError;
            ClearPending();
            callback?.Invoke(error);
        }

        private void ClearPending()
        {
            IsBusy = false;
            _pendingText = _turnId = _finalReply = null;
            _pendingCompleted = null;
            _pendingError = null;
        }

        private void Enqueue(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (_stdoutLock) _stdout.Enqueue(line);
        }

        private bool IsProcessAlive()
        { return _process != null && !_process.HasExited && _input != null; }

        private string ResolveCodexExecutable()
        {
            var configured = string.IsNullOrWhiteSpace(codexExecutable) ?
                Environment.GetEnvironmentVariable("DESKTOP_PET_CODEX_PATH") : codexExecutable;
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return Path.GetFullPath(configured);

            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var folder in path.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(folder)) continue;
                try
                {
                    var candidate = Path.Combine(folder.Trim().Trim('"'), "codex.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }

            var npmPackage = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".npm_global", "node_modules", "@openai", "codex");
            if (Directory.Exists(npmPackage))
            {
                try
                {
                    var matches = Directory.GetFiles(npmPackage, "codex.exe", SearchOption.AllDirectories);
                    if (matches.Length > 0) return matches[0];
                }
                catch { }
            }
            return null;
        }

        internal static string Quote(string value)
        {
            if (value == null) return "null";
            var result = new StringBuilder(value.Length + 2).Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': result.Append("\\\""); break;
                    case '\\': result.Append("\\\\"); break;
                    case '\b': result.Append("\\b"); break;
                    case '\f': result.Append("\\f"); break;
                    case '\n': result.Append("\\n"); break;
                    case '\r': result.Append("\\r"); break;
                    case '\t': result.Append("\\t"); break;
                    default:
                        if (c < 32) result.Append("\\u").Append(((int)c).ToString("x4"));
                        else result.Append(c);
                        break;
                }
            }
            return result.Append('"').ToString();
        }

        private static string DisplayPlan(string planType)
        {
            if (string.IsNullOrWhiteSpace(planType)) return "ChatGPT";
            if (string.Equals(planType, "pro", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(planType, "prolite", StringComparison.OrdinalIgnoreCase)) return "ChatGPT Pro";
            if (string.Equals(planType, "plus", StringComparison.OrdinalIgnoreCase)) return "ChatGPT Plus";
            return "ChatGPT " + planType;
        }

        private void OnDestroy() { ShutdownProcess(); }

        private void ShutdownProcess()
        {
            _shuttingDown = true;
            try { _input?.Close(); } catch { }
            try { if (_process != null && !_process.HasExited) _process.Kill(); } catch { }
            try { _process?.Dispose(); } catch { }
            _input = null;
            _process = null;
            _initialized = _accountChecked = _signedIn = false;
            _shuttingDown = false;
        }

        [Serializable] private sealed class RpcEnvelope
        {
            public int id = 0;
            public string method = null;
            public RpcResult result = null;
            public RpcParams @params = null;
            public RpcError error = null;
        }

        [Serializable] private sealed class RpcResult
        {
            public RpcAccount account = null;
            public RpcThread thread = null;
            public RpcTurn turn = null;
            public string authUrl = null;
        }

        [Serializable] private sealed class RpcParams
        {
            public string threadId = null;
            public RpcTurn turn = null;
            public RpcItem item = null;
        }

        [Serializable] private sealed class RpcAccount { public string type = null; public string planType = null; }
        [Serializable] private sealed class RpcThread { public string id = null; }
        [Serializable] private sealed class RpcItem { public string type = null; public string phase = null; public string text = null; }
        [Serializable] private sealed class RpcTurn { public string id = null; public string status = null; public RpcError error = null; }
        [Serializable] private sealed class RpcError { public string message = null; }
        [Serializable] private sealed class LocalizedReply
        {
            public string display_text_zh = null;
            public string speech_text_ja = null;
        }
    }
}
