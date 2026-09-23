using System;
using System.IO;
using UnityEngine;

namespace DesktopPet
{
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public sealed class DesktopPetRuntimeSettings : MonoBehaviour
    {
        [Serializable]
        private sealed class SettingsData
        {
            public int version = 1;
            public bool alwaysOnTop = true;
            public bool voiceEnabled = true;
            public float voiceVolume = 0.85f;
            public bool quietMode;
            public float behaviorFrequency = 1f;
            public bool surfaceMovementEnabled = true;
            public int targetFrameRate = 60;
        }

        private SettingsData _data = new SettingsData();
        private DesktopWindowController _window;
        private GPTSoVitsSpeechController _speech;
        private PetBehaviorDirector _behavior;
        private PetSurfaceMotionController _surfaceMotion;
        private string _settingsPath;
        private bool _savePending;
        private float _saveAt;

        public static DesktopPetRuntimeSettings Active { get; private set; }
        public event Action Changed;

        public bool AlwaysOnTop { get => _data.alwaysOnTop; set { if (_data.alwaysOnTop != value) { _data.alwaysOnTop = value; ApplyAndSave(); } } }
        public bool VoiceEnabled { get => _data.voiceEnabled; set { if (_data.voiceEnabled != value) { _data.voiceEnabled = value; ApplyAndSave(); } } }
        public bool QuietMode { get => _data.quietMode; set { if (_data.quietMode != value) { _data.quietMode = value; ApplyAndSave(); } } }
        public bool SurfaceMovementEnabled { get => _data.surfaceMovementEnabled; set { if (_data.surfaceMovementEnabled != value) { _data.surfaceMovementEnabled = value; ApplyAndSave(); } } }

        public float VoiceVolume
        {
            get => _data.voiceVolume;
            set { value = Mathf.Clamp01(value); if (!Mathf.Approximately(_data.voiceVolume, value)) { _data.voiceVolume = value; ApplyAndSave(); } }
        }

        public float BehaviorFrequency
        {
            get => _data.behaviorFrequency;
            set { value = Mathf.Clamp(value, 0.25f, 2f); if (!Mathf.Approximately(_data.behaviorFrequency, value)) { _data.behaviorFrequency = value; ApplyAndSave(); } }
        }

        public int TargetFrameRate
        {
            get => _data.targetFrameRate;
            set { value = Mathf.Clamp(value, 30, 144); if (_data.targetFrameRate != value) { _data.targetFrameRate = value; ApplyAndSave(); } }
        }

        private void Awake()
        {
            if (Active != null && Active != this) { Destroy(this); return; }
            Active = this;
            _settingsPath = Path.Combine(Application.persistentDataPath, "desktop-pet-settings.json");
            Load();
            ResolveDependencies();
            Apply();
        }

        private void Start() { ResolveDependencies(); Apply(); }
        private void Update()
        {
            if (_savePending && Time.unscaledTime >= _saveAt) Save();
        }
        private void OnApplicationQuit() { Save(); }
        private void OnDestroy() { if (Active == this) Active = null; }

        public void RecallToPrimaryDisplay()
        {
            ResolveDependencies();
            if (_window != null) _window.RecallToPrimaryWorkArea();
        }

        public void ExitApplication()
        {
            Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public void Reload() { Load(); Apply(); }

        private void ResolveDependencies()
        {
            if (_window == null) _window = FindObjectOfType<DesktopWindowController>();
            if (_speech == null) _speech = FindObjectOfType<GPTSoVitsSpeechController>();
            if (_behavior == null) _behavior = FindObjectOfType<PetBehaviorDirector>();
            if (_surfaceMotion == null) _surfaceMotion = FindObjectOfType<PetSurfaceMotionController>();
        }

        private void ApplyAndSave()
        {
            ResolveDependencies(); Apply();
            _savePending = true;
            _saveAt = Time.unscaledTime + 0.25f;
            Changed?.Invoke();
        }

        private void Apply()
        {
            Application.targetFrameRate = Mathf.Clamp(_data.targetFrameRate, 30, 144);
            if (_window != null) _window.SetAlwaysOnTop(_data.alwaysOnTop);
            if (_speech != null) { _speech.Volume = _data.voiceVolume; _speech.VoiceEnabled = _data.voiceEnabled; }
            if (_behavior != null) { _behavior.QuietMode = _data.quietMode; _behavior.FrequencyScale = _data.behaviorFrequency; }
            if (_surfaceMotion != null) _surfaceMotion.MovementEnabled = _data.surfaceMovementEnabled;
        }

        private void Load()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var loaded = JsonUtility.FromJson<SettingsData>(File.ReadAllText(_settingsPath));
                    if (loaded != null) _data = loaded;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Desktop-pet settings could not be loaded: " + exception.Message);
                _data = new SettingsData();
            }
            _data.voiceVolume = Mathf.Clamp01(_data.voiceVolume);
            _data.behaviorFrequency = Mathf.Clamp(_data.behaviorFrequency, 0.25f, 2f);
            _data.targetFrameRate = Mathf.Clamp(_data.targetFrameRate, 30, 144);
        }

        private void Save()
        {
            try
            {
                var folder = Path.GetDirectoryName(_settingsPath);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                File.WriteAllText(_settingsPath, JsonUtility.ToJson(_data, true));
                _savePending = false;
            }
            catch (Exception exception)
            {
                _savePending = false;
                Debug.LogWarning("Desktop-pet settings could not be saved: " + exception.Message);
            }
        }
    }
}
