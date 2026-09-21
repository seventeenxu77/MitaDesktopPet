using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace DesktopPet
{
    [DisallowMultipleComponent]
    public sealed class GPTSoVitsSpeechController : MonoBehaviour
    {
        [Header("GPT-SoVITS")]
        [SerializeField] private bool voiceEnabled = true;
        [SerializeField] private bool startServiceOnLaunch = true;
        [SerializeField] private string repositoryPath = @"D:\GPT-SoVITS-main";
        [SerializeField] private string pythonExecutable = "";
        [SerializeField] private string configRelativePath = "model/desktop_pet_tts.yaml";
        [SerializeField] private string endpoint = "http://127.0.0.1:9880";
        [SerializeField] private string referenceAudioRelativePath = "model/mitadreamer-reference-ja.wav";
        [SerializeField, TextArea] private string referenceText =
            "このコーヒー、ひどい味なんだもん…私はぜんぜん…元気だよ…";
        [SerializeField, Range(0.6f, 1.4f)] private float speed = 0.95f;
        [SerializeField, Min(15)] private int startupTimeoutSeconds = 180;
        [SerializeField, Min(15)] private int synthesisTimeoutSeconds = 180;

        private AudioSource _audioSource;
        private Process _serviceProcess;
        private Coroutine _speakRoutine;
        private bool _serviceReady;
        private bool _ownsProcess;
        private string _lastServiceError;

        public bool VoiceEnabled
        {
            get => voiceEnabled;
            set
            {
                voiceEnabled = value;
                if (!voiceEnabled)
                {
                    if (_speakRoutine != null) StopCoroutine(_speakRoutine);
                    _speakRoutine = null;
                    if (_audioSource != null) _audioSource.Stop();
                    StatusText = "已关闭";
                }
                else
                {
                    StatusText = _serviceReady ? "已就绪" : "等待启动";
                    if (isActiveAndEnabled && startServiceOnLaunch) StartCoroutine(EnsureService());
                }
            }
        }

        public string StatusText { get; private set; } = "等待启动";

        private void Awake()
        {
            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null) _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.loop = false;
            _audioSource.spatialBlend = 0f;
        }

        private void Start()
        {
            if (voiceEnabled && startServiceOnLaunch) StartCoroutine(EnsureService());
            else if (!voiceEnabled) StatusText = "已关闭";
        }

        public void Speak(string japaneseText)
        {
            if (!voiceEnabled || !isActiveAndEnabled || string.IsNullOrWhiteSpace(japaneseText)) return;
            if (_speakRoutine != null) StopCoroutine(_speakRoutine);
            _audioSource.Stop();
            _speakRoutine = StartCoroutine(SynthesizeAndPlay(japaneseText.Trim()));
        }

        private IEnumerator SynthesizeAndPlay(string text)
        {
            yield return EnsureService();
            if (!_serviceReady)
            {
                _speakRoutine = null;
                yield break;
            }

            StatusText = "正在合成…";
            var requestData = new TtsRequest
            {
                text = text,
                text_lang = "ja",
                ref_audio_path = Path.GetFullPath(Path.Combine(ResolveRepositoryPath(), referenceAudioRelativePath)),
                prompt_text = referenceText,
                prompt_lang = "ja",
                text_split_method = "cut5",
                batch_size = 1,
                media_type = "wav",
                streaming_mode = false,
                speed_factor = speed
            };
            var body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(requestData));
            var address = endpoint.TrimEnd('/') + "/tts";
            using (var request = new UnityWebRequest(address, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerAudioClip(address, AudioType.WAV);
                request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
                request.timeout = Mathf.Max(15, synthesisTimeoutSeconds);
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    StatusText = "合成失败";
                    UnityEngine.Debug.LogWarning("GPT-SoVITS synthesis failed: " + request.error + "\n" +
                        (request.downloadHandler != null ? request.downloadHandler.text : ""));
                    _speakRoutine = null;
                    yield break;
                }

                var clip = DownloadHandlerAudioClip.GetContent(request);
                if (clip == null)
                {
                    StatusText = "音频解码失败";
                    _speakRoutine = null;
                    yield break;
                }
                clip.name = "Mita GPT-SoVITS Reply";
                if (_audioSource.clip != null) Destroy(_audioSource.clip);
                _audioSource.clip = clip;
                _audioSource.Play();
                StatusText = "正在说话";
            }

            while (_audioSource != null && _audioSource.isPlaying) yield return null;
            StatusText = "已就绪";
            _speakRoutine = null;
        }

        private IEnumerator EnsureService()
        {
            if (_serviceReady) yield break;
            StatusText = "正在连接语音服务…";

            yield return ProbeService();
            if (_serviceReady) yield break;

            if (!IsOwnedProcessAlive() && !TryStartService()) yield break;
            StatusText = "正在载入米塔语音模型…";
            float deadline = Time.realtimeSinceStartup + Mathf.Max(15, startupTimeoutSeconds);
            while (Time.realtimeSinceStartup < deadline)
            {
                if (_ownsProcess && !IsOwnedProcessAlive())
                {
                    StatusText = "语音服务启动失败";
                    if (!string.IsNullOrWhiteSpace(_lastServiceError))
                        UnityEngine.Debug.LogError("GPT-SoVITS stopped during startup: " + _lastServiceError);
                    yield break;
                }
                yield return ProbeService();
                if (_serviceReady) yield break;
                yield return new WaitForSecondsRealtime(1f);
            }
            StatusText = "语音服务启动超时";
        }

        private IEnumerator ProbeService()
        {
            using (var request = UnityWebRequest.Get(endpoint.TrimEnd('/') + "/docs"))
            {
                request.timeout = 2;
                yield return request.SendWebRequest();
                _serviceReady = request.result == UnityWebRequest.Result.Success;
                if (_serviceReady) StatusText = "已就绪";
            }
        }

        private bool TryStartService()
        {
            try
            {
                var repository = ResolveRepositoryPath();
                var python = ResolvePythonExecutable();
                var config = Path.GetFullPath(Path.Combine(repository, configRelativePath));
                var reference = Path.GetFullPath(Path.Combine(repository, referenceAudioRelativePath));
                if (!Directory.Exists(repository)) throw new DirectoryNotFoundException("未找到 GPT-SoVITS 文件夹：" + repository);
                if (!File.Exists(python)) throw new FileNotFoundException("未找到 GPT-SoVITS Python 环境", python);
                if (!File.Exists(config)) throw new FileNotFoundException("未找到语音配置", config);
                if (!File.Exists(reference)) throw new FileNotFoundException("未找到参考音频", reference);

                var info = new ProcessStartInfo
                {
                    FileName = python,
                    Arguments = QuoteArgument(Path.Combine(repository, "api_v2.py")) + " -c " + QuoteArgument(config) +
                        " -a 127.0.0.1 -p 9880",
                    WorkingDirectory = repository,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = new UTF8Encoding(false),
                    StandardErrorEncoding = new UTF8Encoding(false)
                };
                var environmentRoot = Path.GetDirectoryName(python);
                var environmentPath = string.Join(";", new[]
                {
                    environmentRoot,
                    Path.Combine(environmentRoot, "Scripts"),
                    Path.Combine(environmentRoot, "Library", "bin"),
                    Environment.GetEnvironmentVariable("PATH") ?? string.Empty
                });
                info.EnvironmentVariables["PATH"] = environmentPath;
                info.EnvironmentVariables["PYTHONUTF8"] = "1";
                _serviceProcess = new Process { StartInfo = info, EnableRaisingEvents = true };
                _serviceProcess.OutputDataReceived += (_, args) =>
                {
                    if (!string.IsNullOrWhiteSpace(args.Data)) _lastServiceError = args.Data;
                };
                _serviceProcess.ErrorDataReceived += (_, args) =>
                {
                    if (!string.IsNullOrWhiteSpace(args.Data)) _lastServiceError = args.Data;
                };
                if (!_serviceProcess.Start()) throw new InvalidOperationException("Python 进程没有启动");
                _serviceProcess.BeginOutputReadLine();
                _serviceProcess.BeginErrorReadLine();
                _ownsProcess = true;
                return true;
            }
            catch (Exception ex)
            {
                StatusText = "语音服务无法启动";
                UnityEngine.Debug.LogError("Cannot start GPT-SoVITS: " + ex.Message);
                ShutdownOwnedProcess();
                return false;
            }
        }

        private string ResolveRepositoryPath()
        {
            var configured = Environment.GetEnvironmentVariable("GPT_SOVITS_HOME");
            return Path.GetFullPath(string.IsNullOrWhiteSpace(configured) ? repositoryPath : configured);
        }

        private string ResolvePythonExecutable()
        {
            var configured = Environment.GetEnvironmentVariable("GPT_SOVITS_PYTHON");
            if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
            if (!string.IsNullOrWhiteSpace(pythonExecutable)) return Path.GetFullPath(pythonExecutable);
            return Path.Combine(ResolveRepositoryPath(), "runtime", "GPTSoVits", "python.exe");
        }

        private bool IsOwnedProcessAlive()
        {
            try { return _serviceProcess != null && !_serviceProcess.HasExited; }
            catch { return false; }
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
        }

        private void OnDestroy()
        {
            ShutdownOwnedProcess();
            if (_audioSource != null && _audioSource.clip != null) Destroy(_audioSource.clip);
        }

        private void ShutdownOwnedProcess()
        {
            if (!_ownsProcess || _serviceProcess == null) return;
            try { if (!_serviceProcess.HasExited) _serviceProcess.Kill(); } catch { }
            try { _serviceProcess.Dispose(); } catch { }
            _serviceProcess = null;
            _ownsProcess = false;
            _serviceReady = false;
        }

        [Serializable]
        private sealed class TtsRequest
        {
            public string text;
            public string text_lang;
            public string ref_audio_path;
            public string prompt_text;
            public string prompt_lang;
            public string text_split_method;
            public int batch_size;
            public string media_type;
            public bool streaming_mode;
            public float speed_factor;
        }
    }
}
