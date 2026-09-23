using System;
using System.Collections.Generic;
using UnityEngine;

namespace DesktopPet
{
    [DefaultExecutionOrder(120)]
    [DisallowMultipleComponent]
    public sealed class PetBehaviorDirector : MonoBehaviour
    {
        private enum Stage { None, Entry, Loop, Exit }

        [SerializeField] private Animator animator;
        [SerializeField] private DesktopWindowController desktopWindow;
        [SerializeField] private DesktopWindowAnchorController anchor;
        [SerializeField] private PetSurfaceMotionController surfaceMotion;
        [SerializeField] private List<PetAnimationAction> actions = new List<PetAnimationAction>();
        [SerializeField] private Vector2 decisionDelaySeconds = new Vector2(8f, 16f);
        [SerializeField] private string freeReturnState = "Idle";
        [SerializeField] private string seatedReturnState = "SitCrossLegLoop";

        private readonly Dictionary<PetAnimationAction, float> _lastPlayed = new Dictionary<PetAnimationAction, float>();
        private PetAnimationAction _active;
        private Stage _stage;
        private float _stageRemaining;
        private float _idleSince;
        private float _nextDecision;
        private bool _quietMode;
        private float _frequencyScale = 1f;
        private bool _reviewMode;
        private int _reviewIndex;

        public bool IsPlayingScheduledAction => _active != null;
        public string CurrentActionId => _active != null ? _active.ActionId : string.Empty;
        public bool ReviewMode => _reviewMode;

        public bool QuietMode
        {
            get => _quietMode;
            set => _quietMode = value;
        }

        public float FrequencyScale
        {
            get => _frequencyScale;
            set
            {
                _frequencyScale = Mathf.Clamp(value, 0.25f, 2f);
                ScheduleNextDecision();
            }
        }

        private void Awake()
        {
            if (animator == null) animator = GetComponent<Animator>();
            if (desktopWindow == null) desktopWindow = FindObjectOfType<DesktopWindowController>();
            if (anchor == null) anchor = FindObjectOfType<DesktopWindowAnchorController>();
            if (surfaceMotion == null) surfaceMotion = GetComponent<PetSurfaceMotionController>();
            _idleSince = Time.unscaledTime;
            ScheduleNextDecision();
        }

        private void OnEnable()
        {
            if (desktopWindow == null) desktopWindow = FindObjectOfType<DesktopWindowController>();
            if (desktopWindow != null)
            {
                desktopWindow.DragStarted += HandleDragStarted;
                desktopWindow.DragEnded += HandleDragEnded;
            }
            if (anchor != null) anchor.AttachmentChanged += HandleAttachmentChanged;
            if (surfaceMotion != null) surfaceMotion.ReachedEdge += HandleSurfaceEdge;
        }

        private void OnDisable()
        {
            if (desktopWindow != null)
            {
                desktopWindow.DragStarted -= HandleDragStarted;
                desktopWindow.DragEnded -= HandleDragEnded;
            }
            if (anchor != null) anchor.AttachmentChanged -= HandleAttachmentChanged;
            if (surfaceMotion != null) surfaceMotion.ReachedEdge -= HandleSurfaceEdge;
            StopScheduledAction(false);
        }

        private void Update()
        {
            if (animator == null) return;
            if (desktopWindow != null && desktopWindow.IsDragging)
            {
                if (_active != null) StopScheduledAction(false);
                return;
            }

            if (_active != null)
            {
                AdvanceAction(Time.unscaledDeltaTime);
                return;
            }

            if (Time.unscaledTime < _nextDecision) return;
            if (_reviewMode) TryStartReviewAction();
            else TryStartWeightedAction();
            ScheduleNextDecision();
        }

        public void NotifyInteraction()
        {
            ResetIdleTimer();
        }

        public void SetReviewMode(bool enabled)
        {
            _reviewMode = enabled;
            _reviewIndex = 0;
            if (_active != null) StopScheduledAction(false);
            _idleSince = enabled ? float.NegativeInfinity : Time.unscaledTime;
            _nextDecision = Time.unscaledTime + (enabled ? 0.2f : 1f);
        }

        public void StopScheduledAction(bool playExit)
        {
            if (_active == null) return;
            if (surfaceMotion != null) surfaceMotion.StopMoving();
            if (playExit && HasState(_active.ExitState) && _active.ExitSeconds > 0f)
            {
                _stage = Stage.Exit;
                _stageRemaining = _active.ExitSeconds;
                CrossFade(_active.ExitState, _active.CrossFadeSeconds);
                return;
            }
            FinishAndReturnToBase();
        }

        private void HandleDragStarted()
        {
            ResetIdleTimer();
            if (_active != null) StopScheduledAction(false);
        }

        private void HandleDragEnded() { ResetIdleTimer(); }

        private void HandleAttachmentChanged(bool attached)
        {
            ResetIdleTimer();
            if (_active == null) return;
            bool valid = _active.Location == PetActionLocation.Any ||
                (_active.Location == PetActionLocation.Free && !attached) ||
                (_active.Location == PetActionLocation.AttachedToWindowTop && attached);
            if (!valid) StopScheduledAction(false);
        }

        private void HandleSurfaceEdge(int edge)
        {
            if (_active != null && _active.MovesAlongSurface && surfaceMotion != null)
                surfaceMotion.BeginMoving(-edge, Mathf.Abs(_active.SurfaceSpeedPixelsPerSecond));
        }

        private void ResetIdleTimer()
        {
            _idleSince = _reviewMode ? float.NegativeInfinity : Time.unscaledTime;
            if (_reviewMode) _nextDecision = Time.unscaledTime + 0.25f;
            else ScheduleNextDecision();
        }

        private void TryStartWeightedAction()
        {
            var eligible = new List<PetAnimationAction>();
            float totalWeight = 0f;
            bool attached = anchor != null && anchor.IsAttached;
            float idleSeconds = Time.unscaledTime - _idleSince;
            foreach (var action in actions)
            {
                if (action == null || action.Weight <= 0f || idleSeconds < action.MinimumIdleSeconds) continue;
                if (_quietMode && !action.AllowedInQuietMode) continue;
                if (action.Location == PetActionLocation.Free && attached) continue;
                if (action.Location == PetActionLocation.AttachedToWindowTop && !attached) continue;
                if (action.RequiresSurfaceEdge && (anchor == null || !anchor.IsNearSurfaceEdge(action.SurfaceEdgeMarginPixels))) continue;
                if (_lastPlayed.TryGetValue(action, out var last) && Time.unscaledTime - last < action.CooldownSeconds) continue;
                if (!HasState(action.EntryState) && !HasState(action.LoopState)) continue;
                eligible.Add(action);
                totalWeight += action.Weight;
            }
            if (eligible.Count == 0 || totalWeight <= 0f) return;

            float pick = UnityEngine.Random.value * totalWeight;
            PetAnimationAction selected = eligible[eligible.Count - 1];
            foreach (var candidate in eligible)
            {
                pick -= candidate.Weight;
                if (pick <= 0f) { selected = candidate; break; }
            }
            StartAction(selected);
        }

        private void TryStartReviewAction()
        {
            if (actions.Count == 0) return;
            bool attached = anchor != null && anchor.IsAttached;
            for (int checkedCount = 0; checkedCount < actions.Count; checkedCount++)
            {
                int index = _reviewIndex++ % actions.Count;
                var action = actions[index];
                if (action == null || action.Weight <= 0f) continue;
                if (action.Location == PetActionLocation.Free && attached) continue;
                if (action.Location == PetActionLocation.AttachedToWindowTop && !attached) continue;
                if (!HasState(action.EntryState) && !HasState(action.LoopState)) continue;
                StartAction(action);
                return;
            }
        }

        private void StartAction(PetAnimationAction action)
        {
            _active = action;
            _lastPlayed[action] = Time.unscaledTime;
            if (HasState(action.EntryState) && action.EntrySeconds > 0f)
            {
                _stage = Stage.Entry;
                _stageRemaining = action.EntrySeconds;
                CrossFade(action.EntryState, action.CrossFadeSeconds);
            }
            else
            {
                EnterLoop();
            }
            if (action.MovesAlongSurface && surfaceMotion != null &&
                !surfaceMotion.BeginMoving(Mathf.Sign(action.SurfaceSpeedPixelsPerSecond),
                    Mathf.Abs(action.SurfaceSpeedPixelsPerSecond)))
                FinishAndReturnToBase();
        }

        private void AdvanceAction(float deltaTime)
        {
            _stageRemaining -= Mathf.Max(0f, deltaTime);
            if (_stageRemaining > 0f) return;
            if (_stage == Stage.Entry) EnterLoop();
            else if (_stage == Stage.Loop) StopScheduledAction(true);
            else if (_stage == Stage.Exit) FinishAndReturnToBase();
        }

        private void EnterLoop()
        {
            if (_active == null) return;
            _stage = Stage.Loop;
            var range = _active.LoopSeconds;
            _stageRemaining = UnityEngine.Random.Range(range.x, range.y);
            if (HasState(_active.LoopState)) CrossFade(_active.LoopState, _active.CrossFadeSeconds);
            else if (HasState(_active.EntryState)) _stageRemaining = Mathf.Max(0.1f, _stageRemaining);
            else FinishAndReturnToBase();
        }

        private void FinishAndReturnToBase()
        {
            if (surfaceMotion != null) surfaceMotion.StopMoving();
            _active = null;
            _stage = Stage.None;
            _stageRemaining = 0f;
            string state = anchor != null && anchor.IsAttached ? seatedReturnState : freeReturnState;
            if (HasState(state)) CrossFade(state, 0.15f);
            _idleSince = _reviewMode ? float.NegativeInfinity : Time.unscaledTime;
            if (_reviewMode) _nextDecision = Time.unscaledTime + 0.65f;
            else ScheduleNextDecision();
        }

        private bool HasState(string stateName)
        {
            return TryGetStateHash(stateName, out _);
        }

        private void CrossFade(string stateName, float duration)
        {
            if (TryGetStateHash(stateName, out var stateHash))
                animator.CrossFadeInFixedTime(stateHash, Mathf.Max(0f, duration), 0);
        }

        private bool TryGetStateHash(string stateName, out int stateHash)
        {
            stateHash = 0;
            if (animator == null || string.IsNullOrWhiteSpace(stateName)) return false;
            int direct = Animator.StringToHash(stateName);
            if (animator.HasState(0, direct)) { stateHash = direct; return true; }
            if (stateName.IndexOf('.') >= 0) return false;
            int baseLayer = Animator.StringToHash("Base Layer." + stateName);
            if (!animator.HasState(0, baseLayer)) return false;
            stateHash = baseLayer;
            return true;
        }

        private void ScheduleNextDecision()
        {
            float minimum = Mathf.Max(0.5f, decisionDelaySeconds.x);
            float maximum = Mathf.Max(minimum, decisionDelaySeconds.y);
            _nextDecision = Time.unscaledTime + UnityEngine.Random.Range(minimum, maximum) / Mathf.Max(0.25f, _frequencyScale);
        }
    }
}
