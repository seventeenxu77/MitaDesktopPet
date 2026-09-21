using UnityEngine;

namespace DesktopPet
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(250)]
    public sealed class PetSleepyEyesController : MonoBehaviour
    {
        [SerializeField] private SkinnedMeshRenderer face;
        [SerializeField] private Vector2 sleepIntervalSeconds = new Vector2(22f,38f);
        [SerializeField, Range(.1f, .8f)] private float maximumOpening = .65f;

        // Openness, not Blink weight: 0 is asleep. Successive efforts lose strength.
        private static readonly float[] Times = {0f,.95f,1.5f,2.15f,2.33f,2.73f,3.22f,3.39f,3.77f,4.17f,4.38f,4.79f,5.25f,6.8f};
        private static readonly float[] Openings = {0f,.72f,.72f,.25f,0f,1f,.74f,0f,.83f,.40f,.02f,.64f,.34f,0f};
        internal const float AttemptDuration = 6.8f;
        private static readonly int DraggingHash = Animator.StringToHash("IsDragging");
        private Animator _animator;
        private bool _hasDragParameter;
        private int _blink = -1;
        private float _wait, _attemptTime = -1, _tempo = 1, _strength = 1;
        private float _opening, _closeTime = -1, _closeFrom;
        private bool _wasDragging, _applied;
        private float _animationBlink;
        private PetClickReactionController _clickReaction;

        private void OnEnable()
        {
            RestoreAnimationWeight();
            ResolveReferences();
            _wait = Random.Range(8f,14f);
            _attemptTime = _closeTime = -1;
            _opening = 0;
            _wasDragging = false;
        }

        private void ResolveReferences()
        {
            _clickReaction = GetComponent<PetClickReactionController>();
            _blink = -1;
            if(face == null)
            {
                foreach(var skin in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if(skin.sharedMesh != null && skin.sharedMesh.GetBlendShapeIndex("Blink") >= 0)
                    { face = skin; break; }
            }
            if(face != null && face.sharedMesh != null) _blink = face.sharedMesh.GetBlendShapeIndex("Blink");
            _animator = GetComponent<Animator>();
            _hasDragParameter = false;
            if(_animator != null && _animator.runtimeAnimatorController != null)
                foreach(var parameter in _animator.parameters)
                    if(parameter.nameHash == DraggingHash && parameter.type == AnimatorControllerParameterType.Bool)
                    { _hasDragParameter = true; break; }
        }

        // Remove last frame's override before Animator evaluates; never accumulate
        // offsets or change any mouth, brow, single-eye or other expression channel.
        private void Update() { RestoreAnimationWeight(); }
        private void LateUpdate()
        {
            bool dragging = _hasDragParameter && _animator != null && _animator.GetBool(DraggingHash);
            AdvanceEyes(Time.unscaledDeltaTime,dragging);
        }

        internal void AdvanceEyes(float deltaTime, bool dragging)
        {
            RestoreAnimationWeight();
            if(face == null || _blink < 0) return;
            float dt = Mathf.Max(0,deltaTime);
            if(dragging && !_wasDragging)
            {
                _attemptTime = -1;
                _wait = NextInterval();
                _closeFrom = _opening;
                _closeTime = 0;
            }
            _wasDragging = dragging;
            if(_closeTime >= 0)
            {
                _closeTime += dt;
                _opening = _closeFrom*(1-Ease(_closeTime/.3f));
                if(_closeTime >= .3f) _closeTime = -1;
            }
            else if(dragging) _opening = 0;
            else if(_clickReaction != null && _clickReaction.IsReacting)
            {
                _attemptTime = -1;
                _wait = Mathf.Max(_wait,6f);
                _opening = Mathf.MoveTowards(_opening,0,dt*.9f);
            }
            else if(_attemptTime >= 0)
            {
                _attemptTime += dt/_tempo;
                _opening = EvaluateOpening(_attemptTime)*maximumOpening*_strength;
                if(_attemptTime >= AttemptDuration)
                {
                    _attemptTime = -1;
                    _opening = 0;
                    _wait = NextInterval();
                }
            }
            else
            {
                _wait -= dt;
                _opening = 0;
                if(_wait <= 0)
                {
                    _attemptTime = 0;
                    _tempo = Random.Range(.93f,1.08f);
                    _strength = Random.Range(.92f,1f);
                }
            }
            _animationBlink = face.GetBlendShapeWeight(_blink);
            float opening = _clickReaction != null ? _clickReaction.BlendEyeOpening(_opening) : _opening;
            face.SetBlendShapeWeight(_blink,100f*(1-Mathf.Clamp01(opening)));
            _applied = true;
        }

        private float NextInterval()
        {
            float minimum = Mathf.Max(5,sleepIntervalSeconds.x);
            return Random.Range(minimum,Mathf.Max(minimum,sleepIntervalSeconds.y));
        }
        internal static float EvaluateOpening(float seconds)
        {
            if(seconds <= 0 || seconds >= AttemptDuration) return 0;
            for(int i=1;i<Times.Length;i++)
                if(seconds <= Times[i])
                    return Mathf.Lerp(Openings[i-1],Openings[i],Ease((seconds-Times[i-1])/(Times[i]-Times[i-1])));
            return 0;
        }
        private static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t*t*t*(t*(t*6-15)+10);
        }
        private void RestoreAnimationWeight()
        {
            if(_applied && face != null && _blink >= 0) face.SetBlendShapeWeight(_blink,_animationBlink);
            _applied = false;
        }
        private void OnDisable()
        {
            RestoreAnimationWeight();
            _attemptTime = _closeTime = -1;
            _opening = 0;
        }
    }
}
