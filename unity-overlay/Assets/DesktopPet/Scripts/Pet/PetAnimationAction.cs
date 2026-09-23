using UnityEngine;

namespace DesktopPet
{
    public enum PetActionLocation
    {
        Any,
        Free,
        AttachedToWindowTop
    }

    [CreateAssetMenu(menuName = "Desktop Pet/Animation Action", fileName = "PetAction")]
    public sealed class PetAnimationAction : ScriptableObject
    {
        [SerializeField] private string actionId = "action";
        [SerializeField] private PetActionLocation location = PetActionLocation.Any;
        [SerializeField, Min(0f)] private float weight = 1f;
        [SerializeField, Min(0f)] private float cooldownSeconds = 30f;
        [SerializeField, Min(0f)] private float minimumIdleSeconds = 8f;
        [SerializeField] private bool allowedInQuietMode;
        [Header("Animator states (created after the clip is approved)")]
        [SerializeField] private string entryState;
        [SerializeField] private string loopState;
        [SerializeField] private string exitState;
        [SerializeField, Min(0f)] private float entrySeconds;
        [SerializeField] private Vector2 loopSeconds = new Vector2(2f, 5f);
        [SerializeField, Min(0f)] private float exitSeconds;
        [SerializeField, Min(0f)] private float crossFadeSeconds = 0.15f;
        [Header("Optional surface motion")]
        [SerializeField] private bool movesAlongSurface;
        [SerializeField] private float surfaceSpeedPixelsPerSecond = 45f;
        [SerializeField] private bool requiresSurfaceEdge;
        [SerializeField, Min(0)] private int surfaceEdgeMarginPixels = 90;

        public string ActionId => actionId;
        public PetActionLocation Location => location;
        public float Weight => Mathf.Max(0f, weight);
        public float CooldownSeconds => Mathf.Max(0f, cooldownSeconds);
        public float MinimumIdleSeconds => Mathf.Max(0f, minimumIdleSeconds);
        public bool AllowedInQuietMode => allowedInQuietMode;
        public string EntryState => entryState;
        public string LoopState => loopState;
        public string ExitState => exitState;
        public float EntrySeconds => Mathf.Max(0f, entrySeconds);
        public Vector2 LoopSeconds
        {
            get
            {
                float minimum = Mathf.Max(0f, loopSeconds.x);
                return new Vector2(minimum, Mathf.Max(minimum, loopSeconds.y));
            }
        }
        public float ExitSeconds => Mathf.Max(0f, exitSeconds);
        public float CrossFadeSeconds => Mathf.Max(0f, crossFadeSeconds);
        public bool MovesAlongSurface => movesAlongSurface;
        public float SurfaceSpeedPixelsPerSecond => surfaceSpeedPixelsPerSecond;
        public bool RequiresSurfaceEdge => requiresSurfaceEdge;
        public int SurfaceEdgeMarginPixels => Mathf.Max(0, surfaceEdgeMarginPixels);
    }
}
