using System;
using UnityEngine;

namespace DesktopPet
{
    [DefaultExecutionOrder(80)]
    [DisallowMultipleComponent]
    public sealed class PetExpressionController : MonoBehaviour
    {
        [Serializable]
        private sealed class Expression
        {
            public string stateName;
            [Min(0.1f)] public float duration = 2f;
            [Min(0f)] public float weight = 1f;
            [Range(0f, 1f)] public float eyeOpening = 0.2f;

            public Expression(string state, float seconds, float selectionWeight, float opening)
            {
                stateName = state;
                duration = seconds;
                weight = selectionWeight;
                eyeOpening = opening;
            }
        }

        [SerializeField] private Animator animator;
        [SerializeField] private string layerName = "Face";
        [SerializeField] private string neutralState = "FaceNeutral";
        [SerializeField] private Vector2 intervalSeconds = new Vector2(12f, 24f);
        [SerializeField] private Expression[] expressions =
        {
            new Expression("FaceHalfSleep", 3.8f, 4f, 0.12f),
            new Expression("FaceTry", 2.2f, 2.4f, 0.45f),
            new Expression("FaceSuspicion", 2.1f, 1.4f, 0.32f),
            new Expression("FaceShy", 1.8f, 1.2f, 0.22f),
            new Expression("FaceDiscontent", 2.1f, 1.1f, 0.22f),
            new Expression("FaceSurprise", 1.25f, 0.55f, 0.72f)
        };

        private DesktopWindowController _window;
        private PetClickReactionController _clickReaction;
        private PetBehaviorDirector _behavior;
        private int _layer = -1;
        private float _wait;
        private float _remaining;
        private float _influence;
        private float _eyeOpening;
        private string _state;
        private string _linkedAction;

        internal float BlendEyeOpening(float sleepyOpening)
        {
            return Mathf.Lerp(sleepyOpening, _eyeOpening, _influence);
        }

        private void Awake()
        {
            if (animator == null) animator = GetComponent<Animator>();
            _window = FindObjectOfType<DesktopWindowController>();
            _clickReaction = GetComponent<PetClickReactionController>();
            _behavior = GetComponent<PetBehaviorDirector>();
            if (animator != null) _layer = animator.GetLayerIndex(layerName);
            _wait = UnityEngine.Random.Range(6f, 10f);
        }

        private void Update()
        {
            if (_layer < 0 || animator == null) return;
            float dt = Mathf.Max(0f, Time.unscaledDeltaTime);
            bool interrupted = (_window != null && _window.IsDragging) ||
                (_clickReaction != null && _clickReaction.IsReacting);
            if (interrupted)
            {
                ReturnToNeutral();
                _wait = Mathf.Max(_wait, 4f);
                return;
            }

            string action = _behavior != null ? _behavior.CurrentActionId : string.Empty;
            if (!string.IsNullOrEmpty(action))
            {
                string linked = LinkedState(action, out float opening);
                if (!string.IsNullOrEmpty(linked))
                {
                    if (_linkedAction != action || _state != linked) Play(linked, 3600f, opening);
                    _linkedAction = action;
                    _influence = Mathf.MoveTowards(_influence, 1f, dt / 0.18f);
                    return;
                }
            }
            if (!string.IsNullOrEmpty(_linkedAction))
            {
                ReturnToNeutral();
                return;
            }

            if (_remaining > 0f)
            {
                _remaining -= dt;
                _influence = Mathf.MoveTowards(_influence, 1f, dt / 0.18f);
                if (_remaining <= 0f) ReturnToNeutral();
                return;
            }

            _influence = Mathf.MoveTowards(_influence, 0f, dt / 0.22f);
            _wait -= dt;
            if (_wait > 0f) return;
            PlayWeighted();
        }

        private void PlayWeighted()
        {
            float total = 0f;
            foreach (var expression in expressions)
                if (expression != null && HasState(expression.stateName)) total += Mathf.Max(0f, expression.weight);
            if (total <= 0f) { _wait = 5f; return; }
            float pick = UnityEngine.Random.value * total;
            Expression selected = null;
            foreach (var expression in expressions)
            {
                if (expression == null || !HasState(expression.stateName)) continue;
                pick -= Mathf.Max(0f, expression.weight);
                if (pick <= 0f) { selected = expression; break; }
            }
            if (selected == null) return;
            Play(selected.stateName, selected.duration, selected.eyeOpening);
        }

        private void Play(string state, float seconds, float opening)
        {
            if (!HasState(state)) return;
            _state = state;
            _remaining = Mathf.Max(0.1f, seconds);
            _eyeOpening = Mathf.Clamp01(opening);
            animator.CrossFadeInFixedTime(Animator.StringToHash("Face." + state), 0.18f, _layer);
        }

        private void ReturnToNeutral()
        {
            if (_remaining <= 0f && string.IsNullOrEmpty(_state)) return;
            if (HasState(neutralState))
                animator.CrossFadeInFixedTime(Animator.StringToHash("Face." + neutralState), 0.2f, _layer);
            _remaining = 0f;
            _state = string.Empty;
            _linkedAction = string.Empty;
            _wait = UnityEngine.Random.Range(Mathf.Max(2f, intervalSeconds.x),
                Mathf.Max(Mathf.Max(2f, intervalSeconds.x), intervalSeconds.y));
        }

        private bool HasState(string state)
        {
            return !string.IsNullOrWhiteSpace(state) &&
                animator.HasState(_layer, Animator.StringToHash("Face." + state));
        }

        private static string LinkedState(string action, out float opening)
        {
            opening = 0.18f;
            if (action.IndexOf("Sleep", StringComparison.OrdinalIgnoreCase) >= 0)
            { opening = 0f; return "FaceSleep"; }
            if (action.IndexOf("Tired", StringComparison.OrdinalIgnoreCase) >= 0 ||
                action.IndexOf("Yawn", StringComparison.OrdinalIgnoreCase) >= 0)
            { opening = 0.12f; return "FaceHalfSleep"; }
            if (action.IndexOf("Wall", StringComparison.OrdinalIgnoreCase) >= 0)
            { opening = 0.28f; return "FaceSuspicion"; }
            return string.Empty;
        }
    }
}
