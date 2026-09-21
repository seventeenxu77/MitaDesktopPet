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
        [SerializeField, Min(1)] private int snapDistance = 90;
        [SerializeField, Min(0)] private int horizontalTolerance = 40;
        [SerializeField] private int seatContactOffsetPixels = 24;
        [SerializeField] private string sittingParameter = "IsSitting";

        private int _sittingParameterHash;
        private IntPtr _targetWindow;
        private int _horizontalOffset;

        public bool IsAttached => _targetWindow != IntPtr.Zero;

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
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (_targetWindow == IntPtr.Zero || desktopWindow == null || desktopWindow.IsDragging)
            {
                return;
            }

            if (!IsWindow(_targetWindow) || !IsWindowVisible(_targetWindow) || IsIconic(_targetWindow) ||
                !TryGetVisibleWindowRect(_targetWindow, out var targetRect) ||
                !desktopWindow.TryGetDesktopRect(out var petRect) ||
                !TryGetSeatContactOffset(petRect, out var seatOffset))
            {
                Detach();
                return;
            }

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
            _horizontalOffset = seatDesktopPoint.x - bestRect.Left;
            SetSittingAnimation(true);
            desktopWindow.MoveWindowTo(
                bestRect.Left + _horizontalOffset - seatOffset.x,
                bestRect.Top - seatOffset.y);
#endif
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
                SetSittingAnimation(false);
                return;
            }

            _targetWindow = IntPtr.Zero;
            SetSittingAnimation(false);
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
