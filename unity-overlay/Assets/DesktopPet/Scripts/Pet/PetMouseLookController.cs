using UnityEngine;

namespace DesktopPet
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public sealed class PetMouseLookController : MonoBehaviour
    {
        [SerializeField] private DesktopWindowController desktopWindow;
        [SerializeField] private Camera petCamera;
        [SerializeField] private Transform head;
        [SerializeField] private Transform neck;
        [SerializeField, Range(0f, 70f)] private float maximumYaw = 50f;
        [SerializeField, Range(0f, 45f)] private float maximumPitch = 25f;
        [SerializeField, Min(0.01f)] private float smoothingSeconds = 0.14f;
        [SerializeField, Min(0.05f)] private float gazeDepth = 0.65f;
        [SerializeField, Range(0f, 0.4f)] private float neckContribution = 0.2f;

        private Vector2 _angles;
        private Vector2 _angleVelocity;
        private Quaternion _headAnimationLocal;
        private Quaternion _neckAnimationLocal;
        private bool _poseApplied;

        private void Awake() { ResolveReferences(); }

        private void ResolveReferences()
        {
            if (desktopWindow == null) desktopWindow = FindObjectOfType<DesktopWindowController>();
            if (petCamera == null) petCamera = Camera.main;
            if (head == null) head = transform.Find("Armature/Hips/Spine/Chest/Neck2/Neck1/Head");
            if (neck == null) neck = transform.Find("Armature/Hips/Spine/Chest/Neck2/Neck1");
        }

        private void Update()
        {
            // Remove our previous offset before Animator evaluates the new frame.
            // This also prevents accumulated twisting when an animation omits a bone.
            RestoreAnimationPose();
        }

        private void LateUpdate()
        {
            if (petCamera == null || head == null || desktopWindow == null) ResolveReferences();
            Vector2 pointer = default;
            bool valid = desktopWindow != null && desktopWindow.TryGetCursorClientPosition(out pointer);
            ApplyLook(pointer, valid, Time.unscaledDeltaTime);
        }

        // Also used by the isolated editor review with synthetic cursor positions.
        internal void ApplyLook(Vector2 pointer, bool hasPointer, float deltaTime)
        {
            if (_poseApplied) RestoreAnimationPose();
            if (head == null || petCamera == null) return;

            _headAnimationLocal = head.localRotation;
            if (neck != null) _neckAnimationLocal = neck.localRotation;
            var animatedHeadWorld = head.rotation;
            var targetAngles = Vector2.zero;
            if (hasPointer)
                targetAngles = GetLookAngles(petCamera, head.position, animatedHeadWorld, pointer,
                    gazeDepth * Mathf.Abs(transform.lossyScale.y), maximumYaw, maximumPitch);

            if (deltaTime > 0)
                _angles = Vector2.SmoothDamp(_angles, targetAngles, ref _angleVelocity,
                    smoothingSeconds, Mathf.Infinity, deltaTime);
            var desiredHeadWorld = animatedHeadWorld * Quaternion.Euler(_angles.y, _angles.x, 0);
            var worldOffset = desiredHeadWorld * Quaternion.Inverse(animatedHeadWorld);
            if (neck != null)
                neck.rotation = Quaternion.Slerp(Quaternion.identity, worldOffset, neckContribution) * neck.rotation;
            head.rotation = desiredHeadWorld;
            _poseApplied = true;
        }

        internal static Vector2 GetLookAngles(Camera camera, Vector3 headPosition, Quaternion animatedRotation,
            Vector2 pointer, float depth, float yawLimit, float pitchLimit)
        {
            var projected = camera.WorldToScreenPoint(headPosition);
            if (projected.z <= 0) return Vector2.zero;
            // ScreenToWorldPoint deliberately accepts points outside the window.
            // The Windows backend supplies global cursor coordinates in client space.
            var target = camera.ScreenToWorldPoint(new Vector3(pointer.x, pointer.y, projected.z));
            var towardViewer = camera.orthographic ? -camera.transform.forward : (camera.transform.position - headPosition).normalized;
            target += towardViewer * Mathf.Max(0.05f, depth);
            var direction = Quaternion.Inverse(animatedRotation) * (target - headPosition);
            float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg;
            return new Vector2(Mathf.Clamp(yaw, -yawLimit, yawLimit), Mathf.Clamp(pitch, -pitchLimit, pitchLimit));
        }

        private void RestoreAnimationPose()
        {
            if (!_poseApplied) return;
            if (neck != null) neck.localRotation = _neckAnimationLocal;
            if (head != null) head.localRotation = _headAnimationLocal;
            _poseApplied = false;
        }

        private void OnDisable()
        {
            RestoreAnimationPose();
            _angles = Vector2.zero;
            _angleVelocity = Vector2.zero;
        }
    }
}
