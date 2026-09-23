using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;

namespace DesktopPet
{
    [DefaultExecutionOrder(50)]
    public sealed class DesktopWindowAnchorController : MonoBehaviour
    {
        [SerializeField] private DesktopWindowController desktopWindow;
        [SerializeField] private Animator petAnimator;
        [SerializeField] private Camera petCamera;
        [SerializeField] private Transform seatAnchor;
        [SerializeField] private Transform leftSurfaceAnchor;
        [SerializeField] private Transform rightSurfaceAnchor;
        [SerializeField, Min(1)] private int snapDistance = 90;
        [SerializeField, Min(0)] private int horizontalTolerance = 40;
        [SerializeField] private int seatContactOffsetPixels = 24;
        [SerializeField] private string sittingParameter = "IsSitting";

        private int _sittingParameterHash;
        private IntPtr _targetWindow;
        private int _horizontalOffset;
        private RectInt _targetDesktopRect;
        private bool _useSurfaceContact;
        private float _surfaceContactBlend;
        private bool _surfaceVerticalCaptured;
        private float _surfaceVerticalDistance;

        public bool IsAttached => _targetWindow != IntPtr.Zero;
        public RectInt TargetDesktopRect => _targetDesktopRect;
        public int SurfaceHorizontalOffset => _horizontalOffset;
        public float SurfaceNormalizedPosition => _targetDesktopRect.width > 0 ?
            Mathf.Clamp01((float)_horizontalOffset / _targetDesktopRect.width) : 0.5f;
        public event Action<bool> AttachmentChanged;

        private void Awake()
        {
            if (desktopWindow == null)
            {
                desktopWindow = FindObjectOfType<DesktopWindowController>();
            }

            if (petAnimator == null)
            {
                petAnimator = FindObjectOfType<Animator>();
            }

            if (petCamera == null)
            {
                petCamera = Camera.main;
            }

            if (seatAnchor == null && petAnimator != null)
            {
                seatAnchor = FindChildByName(petAnimator.transform, "Hips");
            }
            if (petAnimator != null)
            {
                if (leftSurfaceAnchor == null) leftSurfaceAnchor = FindChildByName(petAnimator.transform, "Left toe");
                if (rightSurfaceAnchor == null) rightSurfaceAnchor = FindChildByName(petAnimator.transform, "Right toe");
            }

            _sittingParameterHash = Animator.StringToHash(sittingParameter);
        }

        private void OnEnable()
        {
            if (desktopWindow == null)
            {
                desktopWindow = FindObjectOfType<DesktopWindowController>();
            }

            if (desktopWindow != null)
            {
                desktopWindow.DragStarted += HandleDragStarted;
                desktopWindow.DragEnded += HandleDragEnded;
            }
        }

        private void OnDisable()
        {
            if (desktopWindow != null)
            {
                desktopWindow.DragStarted -= HandleDragStarted;
                desktopWindow.DragEnded -= HandleDragEnded;
            }

            Detach();
        }

        private void LateUpdate()
        {
            _surfaceContactBlend = Mathf.MoveTowards(_surfaceContactBlend,
                _useSurfaceContact ? 1f : 0f, Time.unscaledDeltaTime / 0.22f);
            if (!_useSurfaceContact && _surfaceContactBlend <= 0f)
            {
                _surfaceVerticalCaptured = false;
                _surfaceVerticalDistance = 0f;
            }
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (_targetWindow == IntPtr.Zero || desktopWindow == null || desktopWindow.IsDragging)
            {
                return;
            }

            if (!IsWindow(_targetWindow) || !IsWindowVisible(_targetWindow) || IsIconic(_targetWindow) ||
                !TryGetVisibleWindowRect(_targetWindow, out var targetRect) ||
                !desktopWindow.TryGetDesktopRect(out var petRect) ||
                !TryGetFollowContactOffset(petRect, out var seatOffset))
            {
                Detach();
                return;
            }

            _targetDesktopRect = new RectInt(targetRect.Left, targetRect.Top,
                targetRect.Right - targetRect.Left, targetRect.Bottom - targetRect.Top);

            desktopWindow.MoveWindowTo(
                targetRect.Left + _horizontalOffset - seatOffset.x,
                targetRect.Top - seatOffset.y);
#endif
        }

        private void HandleDragStarted()
        {
            Detach();
        }

        private void HandleDragEnded()
        {
            TryAttachToNearestWindow();
        }

        private void TryAttachToNearestWindow()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (desktopWindow == null || !desktopWindow.TryGetDesktopRect(out var petRect) ||
                !TryGetSeatContactOffset(petRect, out var seatOffset))
            {
                return;
            }

            var currentProcessId = (uint)Process.GetCurrentProcess().Id;
            // Capture the visible hip contact point before switching to SitEnter.
            // The same point is used to test the edge and to follow it afterwards.
            var seatDesktopPoint = petRect.position + seatOffset;
            var bestScore = int.MaxValue;
            var bestWindow = IntPtr.Zero;
            var bestRect = default(NativeRect);

            EnumWindows((window, _) =>
            {
                GetWindowThreadProcessId(window, out var ownerProcessId);
                if (ownerProcessId == currentProcessId ||
                    !IsWindowVisible(window) ||
                    IsIconic(window) ||
                    !TryGetVisibleWindowRect(window, out var candidate))
                {
                    return true;
                }

                var width = candidate.Right - candidate.Left;
                var height = candidate.Bottom - candidate.Top;
                if (width < 160 || height < 100)
                {
                    return true;
                }

                if (!TryScoreWindowTop(seatDesktopPoint,
                    new RectInt(candidate.Left, candidate.Top, width, height),
                    snapDistance, horizontalTolerance, out var score))
                {
                    return true;
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    bestWindow = window;
                    bestRect = candidate;
                }

                return true;
            }, IntPtr.Zero);

            if (bestWindow == IntPtr.Zero)
            {
                Detach();
                return;
            }

            _targetWindow = bestWindow;
            _targetDesktopRect = new RectInt(bestRect.Left, bestRect.Top,
                bestRect.Right - bestRect.Left, bestRect.Bottom - bestRect.Top);
            _horizontalOffset = seatDesktopPoint.x - bestRect.Left;
            SetSittingAnimation(true);
            AttachmentChanged?.Invoke(true);
            desktopWindow.MoveWindowTo(
                bestRect.Left + _horizontalOffset - seatOffset.x,
                bestRect.Top - seatOffset.y);
#endif
        }

        public bool TryMoveAlongSurface(float deltaPixels, int edgeMarginPixels, out bool hitEdge)
        {
            hitEdge = false;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (_targetWindow == IntPtr.Zero ||
                !TryGetVisibleWindowRect(_targetWindow, out var targetRect)) return false;
            int width = targetRect.Right - targetRect.Left;
            int margin = Mathf.Clamp(edgeMarginPixels, 0, Mathf.Max(0, width / 2));
            int minimum = margin;
            int maximum = Mathf.Max(minimum, width - margin);
            int requested = _horizontalOffset + Mathf.RoundToInt(deltaPixels);
            int clamped = Mathf.Clamp(requested, minimum, maximum);
            hitEdge = clamped != requested || clamped == minimum || clamped == maximum;
            _horizontalOffset = clamped;
            _targetDesktopRect = new RectInt(targetRect.Left, targetRect.Top, width,
                targetRect.Bottom - targetRect.Top);
            return true;
#else
            return false;
#endif
        }

        public void SetSurfaceContact(bool enabled)
        {
            if (enabled && !_useSurfaceContact)
            {
                _surfaceVerticalCaptured = false;
                _surfaceVerticalDistance = 0f;
            }
            _useSurfaceContact = enabled;
        }

        public bool IsNearSurfaceEdge(int marginPixels)
        {
            if (!IsAttached || _targetDesktopRect.width <= 0) return false;
            int margin = Mathf.Clamp(marginPixels, 0, _targetDesktopRect.width / 2);
            return _horizontalOffset <= margin || _horizontalOffset >= _targetDesktopRect.width - margin;
        }

        private bool TryGetFollowContactOffset(RectInt petRect, out Vector2Int contactOffset)
        {
            if (!TryGetSeatContactOffset(petRect, out var seatOffset))
            {
                contactOffset = default;
                return false;
            }
            if (_surfaceContactBlend <= 0f ||
                !TryGetSurfaceContactOffset(petRect, out var surfaceOffset))
            {
                contactOffset = seatOffset;
                return true;
            }
            float measuredVerticalDistance = surfaceOffset.y - seatOffset.y;
            if (!_surfaceVerticalCaptured)
            {
                _surfaceVerticalDistance = measuredVerticalDistance;
                if (_surfaceContactBlend >= 0.85f) _surfaceVerticalCaptured = true;
            }
            contactOffset = ComposeStableSurfaceContact(seatOffset,
                _surfaceVerticalDistance, _surfaceContactBlend);
            return true;
        }

        internal static Vector2Int ComposeStableSurfaceContact(Vector2Int coreOffset,
            float fixedVerticalDistance, float blend)
        {
            return new Vector2Int(coreOffset.x,
                coreOffset.y + Mathf.RoundToInt(fixedVerticalDistance * Mathf.Clamp01(blend)));
        }

        private bool TryGetSurfaceContactOffset(RectInt petRect, out Vector2Int surfaceOffset)
        {
            surfaceOffset = default;
            if (leftSurfaceAnchor == null || rightSurfaceAnchor == null || petCamera == null ||
                petRect.width <= 0 || petRect.height <= 0) return false;
            var left = petCamera.WorldToViewportPoint(leftSurfaceAnchor.position);
            var right = petCamera.WorldToViewportPoint(rightSurfaceAnchor.position);
            if (left.z <= 0f || right.z <= 0f) return false;
            var point = (left + right) * 0.5f;
            var viewportRect = petCamera.rect;
            surfaceOffset = new Vector2Int(
                Mathf.RoundToInt((viewportRect.x + point.x * viewportRect.width) * petRect.width),
                Mathf.RoundToInt((1f - viewportRect.y - point.y * viewportRect.height) * petRect.height));
            return true;
        }

        private bool TryGetSeatContactOffset(RectInt petRect, out Vector2Int seatOffset)
        {
            seatOffset = default;
            if (seatAnchor == null || petCamera == null || petRect.width <= 0 || petRect.height <= 0)
            {
                return false;
            }

            var viewportPoint = petCamera.WorldToViewportPoint(seatAnchor.position);
            if (viewportPoint.z <= 0f)
            {
                return false;
            }

            // Unity viewport Y points up; Windows desktop Y points down. Map
            // through the actual window size, including a non-fullscreen camera rect.
            var viewportRect = petCamera.rect;
            seatOffset = new Vector2Int(
                Mathf.RoundToInt((viewportRect.x + viewportPoint.x * viewportRect.width) * petRect.width),
                Mathf.RoundToInt((1f - viewportRect.y - viewportPoint.y * viewportRect.height) * petRect.height)
                    + seatContactOffsetPixels);
            return true;
        }

        internal static bool TryScoreWindowTop(Vector2Int seatPoint, RectInt target,
            int verticalTolerance, int horizontalTolerance, out int score)
        {
            score = int.MaxValue;
            var verticalDistance = Mathf.Abs(seatPoint.y - target.yMin);
            var horizontalDistance = Mathf.Max(target.xMin - seatPoint.x, seatPoint.x - target.xMax, 0);
            if (verticalDistance > verticalTolerance || horizontalDistance > horizontalTolerance)
                return false;

            score = verticalDistance * 4 + horizontalDistance;
            return true;
        }

        private static Transform FindChildByName(Transform root, string childName)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == childName)
                {
                    return child;
                }
            }

            return null;
        }

        private void Detach()
        {
            if (_targetWindow == IntPtr.Zero)
            {
                ResetSurfaceContact();
                SetSittingAnimation(false);
                return;
            }

            _targetWindow = IntPtr.Zero;
            _targetDesktopRect = default;
            ResetSurfaceContact();
            SetSittingAnimation(false);
            AttachmentChanged?.Invoke(false);
        }

        private void ResetSurfaceContact()
        {
            _useSurfaceContact = false;
            _surfaceContactBlend = 0f;
            _surfaceVerticalCaptured = false;
            _surfaceVerticalDistance = 0f;
        }

        private void SetSittingAnimation(bool sitting)
        {
            if (petAnimator == null)
            {
                return;
            }

            foreach (var parameter in petAnimator.parameters)
            {
                if (parameter.nameHash == _sittingParameterHash)
                {
                    petAnimator.SetBool(_sittingParameterHash, sitting);
                    return;
                }
            }
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private static bool TryGetVisibleWindowRect(IntPtr window, out NativeRect rect)
        {
            if (DwmGetWindowAttribute(
                    window,
                    DwmwaExtendedFrameBounds,
                    out rect,
                    Marshal.SizeOf(typeof(NativeRect))) == 0)
            {
                return true;
            }

            return GetWindowRect(window, out rect);
        }

        private const int DwmwaExtendedFrameBounds = 9;

        private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr window);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(
            IntPtr window,
            int attribute,
            out NativeRect value,
            int valueSize);
#endif
    }
}
