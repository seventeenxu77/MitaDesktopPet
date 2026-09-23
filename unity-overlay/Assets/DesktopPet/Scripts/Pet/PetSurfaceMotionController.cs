using UnityEngine;

namespace DesktopPet
{
    [DefaultExecutionOrder(40)]
    [DisallowMultipleComponent]
    public sealed class PetSurfaceMotionController : MonoBehaviour
    {
        [SerializeField] private DesktopWindowAnchorController anchor;
        [SerializeField, Min(0)] private int edgeMarginPixels = 36;
        [SerializeField] private string surfaceSpeedParameter = "SurfaceSpeed";
        [SerializeField, Min(0.02f)] private float turnSeconds = 0.2f;
        [SerializeField, Min(0f)] private float turnPauseSeconds = 0.3f;
        [SerializeField, Min(0.02f)] private float accelerationSeconds = 0.14f;

        private Animator _animator;
        private Camera _camera;
        private int _surfaceSpeedHash;
        private bool _hasSpeedParameter;
        private float _direction;
        private float _speed;
        private float _pixelRemainder;
        private float _turnRemaining;
        private float _movementBlend;
        private Quaternion _neutralRotation;
        private Quaternion _targetRotation;

        public bool MovementEnabled { get; set; } = true;
        public bool IsMoving { get; private set; }
        public event System.Action<int> ReachedEdge;

        private void Awake()
        {
            if (anchor == null) anchor = FindObjectOfType<DesktopWindowAnchorController>();
            _camera = Camera.main;
            _neutralRotation = transform.rotation;
            _targetRotation = _neutralRotation;
            _animator = GetComponent<Animator>();
            _surfaceSpeedHash = Animator.StringToHash(surfaceSpeedParameter);
            if (_animator != null)
                foreach (var parameter in _animator.parameters)
                    if (parameter.nameHash == _surfaceSpeedHash && parameter.type == AnimatorControllerParameterType.Float)
                    { _hasSpeedParameter = true; break; }
        }

        private void Update()
        {
            if (!MovementEnabled || !IsMoving || anchor == null || !anchor.IsAttached)
            {
                if (IsMoving) StopMoving();
                return;
            }
            if (_turnRemaining > 0f)
            {
                _turnRemaining = Mathf.Max(0f, _turnRemaining - Time.unscaledDeltaTime);
                SetMovementBlend(0f, Time.unscaledDeltaTime);
                if (_turnRemaining > 0f) return;
            }
            SetMovementBlend(1f, Time.unscaledDeltaTime);
            _pixelRemainder += _direction * _speed * _movementBlend * Time.unscaledDeltaTime;
            float wholePixels = Mathf.Sign(_pixelRemainder) * Mathf.Floor(Mathf.Abs(_pixelRemainder));
            if (Mathf.Approximately(wholePixels, 0f)) return;
            _pixelRemainder -= wholePixels;
            if (!anchor.TryMoveAlongSurface(wholePixels,
                    edgeMarginPixels, out var hitEdge))
            {
                StopMoving();
                return;
            }
            if (hitEdge)
            {
                int edge = _direction < 0f ? -1 : 1;
                StopMoving();
                ReachedEdge?.Invoke(edge);
            }
        }

        public bool BeginMoving(float direction, float pixelsPerSecond)
        {
            if (!MovementEnabled || anchor == null || !anchor.IsAttached || Mathf.Approximately(direction, 0f)) return false;
            _direction = Mathf.Sign(direction);
            _speed = Mathf.Max(1f, pixelsPerSecond);
            _pixelRemainder = 0f;
            _movementBlend = 0f;
            IsMoving = true;
            anchor.SetSurfaceContact(true);
            ResolveFacing(_direction);
            SetAnimatorPlayback(0f);
            return true;
        }

        public bool ReverseDirection()
        {
            return IsMoving && BeginMoving(-_direction, _speed);
        }

        public void StopMoving()
        {
            IsMoving = false;
            _speed = 0f;
            _pixelRemainder = 0f;
            _turnRemaining = 0f;
            _movementBlend = 0f;
            if (anchor != null) anchor.SetSurfaceContact(false);
            _targetRotation = _neutralRotation;
            SetAnimatorPlayback(0f);
        }

        private void LateUpdate()
        {
            float blend = 1f - Mathf.Exp(-Time.unscaledDeltaTime / Mathf.Max(0.02f, turnSeconds));
            transform.rotation = Quaternion.Slerp(transform.rotation, _targetRotation, blend);
        }

        private void ResolveFacing(float direction)
        {
            if (_camera == null) _camera = Camera.main;
            Vector3 screenRight = _camera != null ? _camera.transform.right : Vector3.right;
            var next = CalculateFacingRotation(screenRight, direction);
            if (Quaternion.Angle(transform.rotation, next) > 8f)
                _turnRemaining = Mathf.Max(_turnRemaining, turnPauseSeconds);
            _targetRotation = next;
        }

        internal static Quaternion CalculateFacingRotation(Vector3 screenRight, float direction)
        {
            screenRight = Vector3.ProjectOnPlane(screenRight, Vector3.up);
            if (screenRight.sqrMagnitude < 0.0001f) screenRight = Vector3.right;
            var forward = (direction >= 0f ? screenRight : -screenRight).normalized;
            return Quaternion.LookRotation(forward, Vector3.up);
        }

        private void SetMovementBlend(float target, float deltaTime)
        {
            _movementBlend = Mathf.MoveTowards(_movementBlend, target,
                Mathf.Max(0f, deltaTime) / Mathf.Max(0.02f, accelerationSeconds));
            SetAnimatorPlayback(_movementBlend);
        }

        private void SetAnimatorPlayback(float normalizedSpeed)
        {
            if (_animator != null && _hasSpeedParameter)
                _animator.SetFloat(_surfaceSpeedHash, Mathf.Clamp01(normalizedSpeed));
        }

        private void OnDisable()
        {
            StopMoving();
            transform.rotation = _neutralRotation;
        }
    }
}
