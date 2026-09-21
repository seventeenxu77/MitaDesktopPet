using UnityEngine;

namespace DesktopPet
{
    /// <summary>
    /// Adds a small, velocity-driven hanging motion after the Animator has
    /// evaluated the authored pickup/flailing clips. The clip owns the pose;
    /// this component only supplies subtle desktop-drag inertia.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class ProceduralDragPoseController : MonoBehaviour
    {
        [SerializeField] private DesktopWindowController desktopWindow;
        [SerializeField, Range(0f, 25f)] private float maximumBodyTilt = 5f;
        [SerializeField, Range(0f, 15f)] private float idleSwingAngle = 1.25f;
        [SerializeField, Range(0.1f, 15f)] private float idleSwingSpeed = 5.5f;
        [SerializeField, Min(0.01f)] private float blendInSeconds = 0.16f;
        [SerializeField, Min(0.01f)] private float blendOutSeconds = 0.18f;

        private Transform _hips;
        private Transform _spine;
        private Transform _chest;

        private float _weight;
        private Vector2 _filteredVelocity;
        private Vector2 _velocitySmoothing;

        private void Awake()
        {
            if (desktopWindow == null)
            {
                desktopWindow = FindObjectOfType<DesktopWindowController>();
            }

            _hips = transform.Find("Armature/Hips");
            _spine = transform.Find("Armature/Hips/Spine");
            _chest = transform.Find("Armature/Hips/Spine/Chest");

            if (_hips == null)
            {
                Debug.LogWarning("Desktop Pet drag pose could not find Armature/Hips.", this);
            }
        }

        private void LateUpdate()
        {
            var isDragging = desktopWindow != null && desktopWindow.IsDragging;
            var blendSeconds = isDragging ? blendInSeconds : blendOutSeconds;
            _weight = Mathf.MoveTowards(
                _weight,
                isDragging ? 1f : 0f,
                Time.unscaledDeltaTime / Mathf.Max(0.01f, blendSeconds));

            if (_weight <= 0f || _hips == null)
            {
                return;
            }

            var targetVelocity = isDragging
                ? desktopWindow.DragVelocityPixelsPerSecond / 900f
                : Vector2.zero;
            targetVelocity = Vector2.ClampMagnitude(targetVelocity, 1f);
            _filteredVelocity = Vector2.SmoothDamp(
                _filteredVelocity,
                targetVelocity,
                ref _velocitySmoothing,
                0.09f,
                Mathf.Infinity,
                Time.unscaledDeltaTime);

            var phase = Time.unscaledTime * idleSwingSpeed;
            var passiveSwing = Mathf.Sin(phase) * idleSwingAngle;
            var bodyRoll = (-_filteredVelocity.x * maximumBodyTilt + passiveSwing) * _weight;
            var forwardLag = (-_filteredVelocity.y * 7f) * _weight;
            AddLocalRotation(_hips, new Vector3(forwardLag, 0f, bodyRoll));
            AddLocalRotation(_spine, new Vector3(-forwardLag * 0.25f, 0f, -bodyRoll * 0.3f));
            AddLocalRotation(_chest, new Vector3(-forwardLag * 0.2f, 0f, -bodyRoll * 0.2f));
        }

        private static void AddLocalRotation(Transform bone, Vector3 eulerOffset)
        {
            if (bone != null)
            {
                bone.localRotation *= Quaternion.Euler(eulerOffset);
            }
        }
    }
}
