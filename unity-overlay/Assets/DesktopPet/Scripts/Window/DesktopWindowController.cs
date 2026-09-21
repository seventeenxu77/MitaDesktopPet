using System;
using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;

namespace DesktopPet
{
    [DefaultExecutionOrder(-100)]
    public sealed class DesktopWindowController : MonoBehaviour
    {
        [SerializeField, Min(200)] private int windowWidth = 520;
        [SerializeField, Min(200)] private int windowHeight = 700;
        [SerializeField] private bool alwaysOnTop = true;

        public bool IsDragging { get; private set; }
        public bool IsClickThrough { get; private set; }
        public Vector2 DragVelocityPixelsPerSecond { get; private set; }

        public event Action DragStarted;
        public event Action DragEnded;

        private IntPtr _windowHandle;
        private Vector2 _lastDragCursorPosition;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private POINT _dragStartCursor;
        private RECT _dragStartWindow;
#endif

        private void Awake()
        {
            Application.runInBackground = true;
        }

        private IEnumerator Start()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            Screen.SetResolution(windowWidth, windowHeight, FullScreenMode.Windowed);

            // The native window is created shortly after the first Unity frame.
            for (var frame = 0; frame < 120 && _windowHandle == IntPtr.Zero; frame++)
            {
                _windowHandle = FindCurrentProcessWindow();
                if (_windowHandle == IntPtr.Zero)
                {
                    yield return null;
                }
            }

            if (_windowHandle == IntPtr.Zero)
            {
                UnityEngine.Debug.LogError("Desktop Pet could not find the Unity player window.");
                yield break;
            }

            ApplyDesktopWindowStyle();
#else
            yield break;
#endif
        }

        private void Update()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (!IsDragging)
            {
                return;
            }

            if ((GetAsyncKeyState(VkRightButton) & 0x8000) == 0)
            {
                EndDrag();
                return;
            }

            if (!GetCursorPos(out var cursor))
            {
                return;
            }

            UpdateDragVelocity(new Vector2(cursor.X, -cursor.Y));

            var x = _dragStartWindow.Left + cursor.X - _dragStartCursor.X;
            var y = _dragStartWindow.Top + cursor.Y - _dragStartCursor.Y;
            SetWindowPos(
                _windowHandle,
                alwaysOnTop ? HwndTopmost : IntPtr.Zero,
                x,
                y,
                0,
                0,
                SwpNoSize | SwpNoActivate | (alwaysOnTop ? 0u : SwpNoZOrder));
#else
            if (IsDragging)
            {
                UpdateDragVelocity(Input.mousePosition);
            }
#endif
        }

        public bool BeginDrag()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (_windowHandle == IntPtr.Zero ||
                !GetCursorPos(out _dragStartCursor) ||
                !GetWindowRect(_windowHandle, out _dragStartWindow))
            {
                return false;
            }

            SetClickThrough(false);
            IsDragging = true;
            _lastDragCursorPosition = new Vector2(_dragStartCursor.X, -_dragStartCursor.Y);
            DragVelocityPixelsPerSecond = Vector2.zero;
            DragStarted?.Invoke();
            return true;
#else
            // Keep animation preview usable inside the Editor. The native Editor
            // window is deliberately never moved.
            IsDragging = true;
            _lastDragCursorPosition = Input.mousePosition;
            DragVelocityPixelsPerSecond = Vector2.zero;
            DragStarted?.Invoke();
            return true;
#endif
        }

        public void EndDrag()
        {
            if (!IsDragging)
            {
                return;
            }

            IsDragging = false;
            DragVelocityPixelsPerSecond = Vector2.zero;
            DragEnded?.Invoke();
        }

        private void UpdateDragVelocity(Vector2 cursorPosition)
        {
            var deltaTime = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            var measuredVelocity = (cursorPosition - _lastDragCursorPosition) / deltaTime;
            measuredVelocity = Vector2.ClampMagnitude(measuredVelocity, 2400f);
            var blend = 1f - Mathf.Exp(-18f * deltaTime);
            DragVelocityPixelsPerSecond = Vector2.Lerp(
                DragVelocityPixelsPerSecond,
                measuredVelocity,
                blend);
            _lastDragCursorPosition = cursorPosition;
        }

        public bool TryGetCursorClientPosition(out Vector2 screenPoint)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            screenPoint = default;
            if (_windowHandle == IntPtr.Zero || !GetCursorPos(out var cursor))
            {
                return false;
            }

            if (!ScreenToClient(_windowHandle, ref cursor))
            {
                return false;
            }

            screenPoint = new Vector2(cursor.X, Screen.height - cursor.Y);
            return true;
#else
            screenPoint = Input.mousePosition;
            return true;
#endif
        }

        public bool TryGetDesktopRect(out RectInt desktopRect)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            desktopRect = default;
            if (_windowHandle == IntPtr.Zero || !GetWindowRect(_windowHandle, out var rect))
            {
                return false;
            }

            desktopRect = new RectInt(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            return true;
#else
            desktopRect = default;
            return false;
#endif
        }

        public void MoveWindowTo(int desktopX, int desktopY)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (_windowHandle == IntPtr.Zero)
            {
                return;
            }

            SetWindowPos(
                _windowHandle,
                alwaysOnTop ? HwndTopmost : IntPtr.Zero,
                desktopX,
                desktopY,
                0,
                0,
                SwpNoSize | SwpNoActivate | (alwaysOnTop ? 0u : SwpNoZOrder));
#endif
        }

        public void SetClickThrough(bool clickThrough)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (_windowHandle == IntPtr.Zero || IsClickThrough == clickThrough)
            {
                return;
            }

            var extendedStyle = GetWindowLongPtrCompat(_windowHandle, GwlExtendedStyle).ToInt64();
            if (clickThrough)
            {
                extendedStyle |= WsExTransparent;
            }
            else
            {
                extendedStyle &= ~WsExTransparent;
            }

            SetWindowLongPtrCompat(_windowHandle, GwlExtendedStyle, new IntPtr(extendedStyle));
            SetWindowPos(
                _windowHandle,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
#endif
            IsClickThrough = clickThrough;
        }

        private void OnDisable()
        {
            EndDrag();
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private void ApplyDesktopWindowStyle()
        {
            var style = GetWindowLongPtrCompat(_windowHandle, GwlStyle).ToInt64();
            style &= ~(WsCaption | WsThickFrame | WsMinimizeBox | WsMaximizeBox | WsSystemMenu);
            style |= WsPopup | WsVisible;
            SetWindowLongPtrCompat(_windowHandle, GwlStyle, new IntPtr(style));

            var extendedStyle = GetWindowLongPtrCompat(_windowHandle, GwlExtendedStyle).ToInt64();
            extendedStyle |= WsExLayered;
            SetWindowLongPtrCompat(_windowHandle, GwlExtendedStyle, new IntPtr(extendedStyle));

            var margins = new Margins { Left = -1 };
            DwmExtendFrameIntoClientArea(_windowHandle, ref margins);

            // Unity 2021's D3D11 swap chain does not consistently expose the
            // camera alpha channel to DWM. Use exact black as a color key so
            // the camera clear color is reliably transparent on Windows.
            SetLayeredWindowAttributes(_windowHandle, 0x00000000, 0, LwaColorKey);

            GetWindowRect(_windowHandle, out var windowRect);
            SetWindowPos(
                _windowHandle,
                alwaysOnTop ? HwndTopmost : IntPtr.Zero,
                windowRect.Left,
                windowRect.Top,
                windowWidth,
                windowHeight,
                SwpFrameChanged | SwpShowWindow | (alwaysOnTop ? 0u : SwpNoZOrder));
        }

        private static IntPtr FindCurrentProcessWindow()
        {
            var processId = (uint)Process.GetCurrentProcess().Id;
            var result = IntPtr.Zero;

            EnumWindows((window, _) =>
            {
                GetWindowThreadProcessId(window, out var ownerProcessId);
                if (ownerProcessId != processId || !IsWindowVisible(window))
                {
                    return true;
                }

                if (!GetWindowRect(window, out var rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
                {
                    return true;
                }

                result = window;
                return false;
            }, IntPtr.Zero);

            return result;
        }

        private static IntPtr GetWindowLongPtrCompat(IntPtr window, int index)
        {
            return IntPtr.Size == 8 ? GetWindowLongPtr64(window, index) : new IntPtr(GetWindowLong32(window, index));
        }

        private static void SetWindowLongPtrCompat(IntPtr window, int index, IntPtr value)
        {
            if (IntPtr.Size == 8)
            {
                SetWindowLongPtr64(window, index, value);
            }
            else
            {
                SetWindowLong32(window, index, value.ToInt32());
            }
        }

        private const int GwlStyle = -16;
        private const int GwlExtendedStyle = -20;
        private const int VkRightButton = 0x02;

        private const long WsCaption = 0x00C00000L;
        private const long WsThickFrame = 0x00040000L;
        private const long WsMinimizeBox = 0x00020000L;
        private const long WsMaximizeBox = 0x00010000L;
        private const long WsSystemMenu = 0x00080000L;
        private const long WsPopup = unchecked((long)0x80000000);
        private const long WsVisible = 0x10000000L;
        private const long WsExLayered = 0x00080000L;
        private const long WsExTransparent = 0x00000020L;

        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpFrameChanged = 0x0020;
        private const uint SwpShowWindow = 0x0040;
        private const uint LwaColorKey = 0x00000001;

        private static readonly IntPtr HwndTopmost = new IntPtr(-1);

        private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Margins
        {
            public int Left;
            public int Right;
            public int Top;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr window, ref POINT point);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr window, out RECT rect);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr window, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr window, int index, int value);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr window, int index, IntPtr value);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(
            IntPtr window,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(
            IntPtr window,
            uint colorKey,
            byte alpha,
            uint flags);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref Margins margins);
#endif
    }
}
