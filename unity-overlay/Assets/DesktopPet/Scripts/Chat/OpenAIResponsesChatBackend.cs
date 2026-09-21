using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace DesktopPet
{
    // Private local prototype only. Never distribute a developer API key in a player.
    public sealed class OpenAIResponsesChatBackend : MonoBehaviour, IChatBackend
    {
        [SerializeField] private string endpoint = "https://api.openai.com/v1/responses";
        [SerializeField] private string model = "gpt-5.3-codex";
        [SerializeField] private string apiKeyEnvironmentVariable = "OPENAI_API_KEY";
        [SerializeField] private string modelEnvironmentVariable = "OPENAI_MODEL";
        [SerializeField, Min(256)] private int maxOutputTokens = 2048;
        [SerializeField, Min(5)] private int timeoutSeconds = 90;
        [SerializeField] private DesktopPetPersona persona;
        [SerializeField, TextArea(8, 18)] private string personaInstructions =
            "你是住在用户桌面上的米塔。你会友善、简洁地回答用户，通常不超过三句话。" +
            "你没有查看屏幕、文件或操作电脑的能力，不要声称自己执行了这些操作。";

        // Never serialized, persisted in PlayerPrefs, or logged.
        private string _sessionKey;
        private string _sessionModel;
        private string _sessionPersona;
        private string _previousResponseId;
        private UnityWebRequest _activeRequest;
        private Action<string> _pendingError;
        private int _generation;

        public bool IsBusy { get; private set; }
        public bool HasApiKey => !string.IsNullOrWhiteSpace(ResolveApiKey());
        public string CurrentModel => !string.IsNullOrWhiteSpace(_sessionModel) ? _sessionModel :
            Environment.GetEnvironmentVariable(modelEnvironmentVariable) is string value && !string.IsNullOrWhiteSpace(value) ? value : model;
        public string CurrentPersona => _sessionPersona ??
            (persona != null && !string.IsNullOrWhiteSpace(persona.instructions) ? persona.instructions : personaInstructions);

        public void ConfigurePersona(DesktopPetPersona value) { persona = value; }

        // Blank key keeps the existing session/environment key; UI never reads it back.
        public void ConfigureSession(string key, string modelName, string instructions)
        {
            ResetConversation();
            if (!string.IsNullOrWhiteSpace(key)) _sessionKey = key.Trim();
            _sessionModel = string.IsNullOrWhiteSpace(modelName) ? null : modelName.Trim();
            _sessionPersona = string.IsNullOrWhiteSpace(instructions) ? null : instructions.Trim();
        }

        private string ResolveApiKey()
        {
            return !string.IsNullOrWhiteSpace(_sessionKey) ? _sessionKey : Environment.GetEnvironmentVariable(apiKeyEnvironmentVariable);
        }

        public void Send(string userText, Action<string> onCompleted, Action<string> onError)
        {
            if (IsBusy) { onError?.Invoke("还在等待上一条回复。"); return; }
            if (!isActiveAndEnabled) { onError?.Invoke("对话服务尚未启用。"); return; }
            if (string.IsNullOrWhiteSpace(userText)) { onError?.Invoke("请先输入内容。"); return; }
            if (userText.Length > 2000) { onError?.Invoke("每条消息最多 2000 个字符。"); return; }
            var key = ResolveApiKey();
            if (string.IsNullOrWhiteSpace(key))
            {
                onError?.Invoke("还没有 API Key。请打开“设置”输入，或设置 OPENAI_API_KEY 后重新启动桌宠。");
                return;
            }
            if (!IsOfficialEndpoint(endpoint))
            {
                onError?.Invoke("此版本仅向 https://api.openai.com/v1/responses 发送密钥，请检查接口地址。");
                return;
            }
            IsBusy = true;
            _pendingError = onError;
            int generation = ++_generation;
            StartCoroutine(SendRequest(BuildRequestJson(userText.Trim()), key.Trim(), generation, onCompleted));
        }

        internal static bool IsOfficialEndpoint(string address)
        {
            return Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
                uri.Host == "api.openai.com" && uri.Port == 443 && uri.AbsolutePath == "/v1/responses" &&
                string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
        }

        internal string BuildRequestJson(string userText)
        {
            ResponsesRequest body = string.IsNullOrWhiteSpace(_previousResponseId)
                ? new ResponsesRequest()
                : new FollowupRequest { previous_response_id = _previousResponseId };
            body.model = CurrentModel;
            // Instructions are supplied every turn, even with previous_response_id.
            body.instructions = CurrentPersona;
            body.input = userText;
            body.max_output_tokens = Mathf.Max(2048, maxOutputTokens);
            body.store = true;
            body.reasoning = new ReasoningOptions { effort = "low" };
            return JsonUtility.ToJson(body);
        }

        public void Cancel()
        {
            ++_generation;
            var error = _pendingError;
            _pendingError = null;
            IsBusy = false;
            if (_activeRequest != null) _activeRequest.Abort();
            StopAllCoroutines();
            if (_activeRequest != null) { _activeRequest.Dispose(); _activeRequest = null; }
            error?.Invoke("已取消等待；输入内容已保留。已发送的请求仍可能产生费用。");
        }

        public void ResetConversation()
        {
            Cancel();
            _previousResponseId = null;
        }

        private IEnumerator SendRequest(string json, string key, int generation, Action<string> onCompleted)
        {
            UnityWebRequest request = null;
            UnityWebRequestAsyncOperation operation = null;
            try
            {
                request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST);
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = Mathf.Max(90, timeoutSeconds);
                request.redirectLimit = 0; // Never forward bearer credentials to another host.
                request.SetRequestHeader("Authorization", "Bearer " + key);
                request.SetRequestHeader("Content-Type", "application/json");
                _activeRequest = request;
                operation = request.SendWebRequest();
            }
            catch (Exception)
            {
                request?.Dispose();
                _activeRequest = null;
                CompleteError("无法启动请求，请检查网络或接口配置。", generation);
            }
            if (operation == null) yield break;
            string reply = null, responseId = null, failure = null;
            try
            {
                yield return operation;
                if (generation != _generation) yield break;
                if (request.result != UnityWebRequest.Result.Success)
                    failure = ExplainHttpError(request.responseCode);
                else
                    TryParseReply(request.downloadHandler.text, out reply, out responseId, out failure);
            }
            finally
            {
                if (_activeRequest == request) _activeRequest = null;
                request.Dispose();
            }
            if (generation != _generation) yield break;
            if (failure != null) { CompleteError(failure, generation); yield break; }
            _previousResponseId = responseId;
            IsBusy = false;
            _pendingError = null;
            onCompleted?.Invoke(reply);
        }

        private void CompleteError(string message, int generation)
        {
            if (generation != _generation) return;
            IsBusy = false;
            var error = _pendingError;
            _pendingError = null;
            error?.Invoke(message);
        }

        internal static string ExplainHttpError(long code)
        {
            if (code == 401) return "API Key 无效或已失效，请在设置中更换。";
            if (code == 403) return "当前账号无权访问此模型或服务，请检查项目权限。";
            if (code == 429) return "额度不足或请求过于频繁，请检查 API 余额和速率限制后重试。";
            if (code == 400 || code == 404) return "模型、参数或会话不可用，请检查模型名称；也可新建对话后重试。";
            if (code >= 500) return "服务暂时不可用，请稍后重试。";
            if (code == 0) return "网络连接失败或等待超时，请检查网络后重试。";
            return "请求未完成（HTTP " + code + "），请检查配置后重试。";
        }

        internal static bool TryParseReply(string json, out string reply, out string responseId, out string error)
        {
            reply = null; responseId = null; error = null;
            ResponsesResponse response;
            try { response = JsonUtility.FromJson<ResponsesResponse>(json); }
            catch (Exception) { error = "服务返回了无法识别的数据，请稍后重试。"; return false; }
            if (response == null) { error = "服务返回了空数据。"; return false; }
            if (response.status != "completed")
            {
                error = response.status == "incomplete" ? "回复未生成完整，可能达到输出限制；请缩短问题后重试。" : "服务未完成这次回复，请重试。";
                return false;
            }
            var builder = new StringBuilder();
            if (response.output != null)
                foreach (var item in response.output)
                {
                    if (item == null || item.type != "message" || item.role != "assistant" ||
                        item.phase == "commentary" || item.content == null) continue;
                    foreach (var content in item.content)
                    {
                        if (content == null) continue;
                        var text = content.type == "output_text" ? content.text : content.type == "refusal" ? content.refusal : null;
                        if (string.IsNullOrWhiteSpace(text)) continue;
                        if (builder.Length > 0) builder.AppendLine();
                        builder.Append(text);
                    }
                }
            if (builder.Length == 0 || string.IsNullOrWhiteSpace(response.id))
            { error = "没有收到可显示的回复，请重试。"; return false; }
            reply = builder.ToString().Trim(); responseId = response.id;
            return true;
        }

        private void OnDisable() { Cancel(); }
        private void OnDestroy() { _sessionKey = null; }

        [Serializable] private class ResponsesRequest
        {
            public string model;
            public string instructions;
            public string input;
            public int max_output_tokens;
            public bool store;
            public ReasoningOptions reasoning;
        }
        [Serializable] private sealed class FollowupRequest : ResponsesRequest { public string previous_response_id; }
        [Serializable] private sealed class ReasoningOptions { public string effort; }
        [Serializable] private sealed class ResponsesResponse
        {
            public string id = null;
            public string status = null;
            public OutputItem[] output = null;
        }
        [Serializable] private sealed class OutputItem
        {
            public string type = null;
            public string role = null;
            public string phase = null;
            public OutputContent[] content = null;
        }
        [Serializable] private sealed class OutputContent
        {
            public string type = null;
            public string text = null;
            public string refusal = null;
        }
    }
}
