using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DesktopPet
{
    [DefaultExecutionOrder(100)]
    public sealed class DesktopHitTestController : MonoBehaviour
    {
        [SerializeField] private DesktopWindowController desktopWindow;
        [SerializeField] private Camera interactionCamera;
        [SerializeField] private LayerMask interactiveLayers = ~0;
        [SerializeField, Min(1f)] private float rayDistance = 1000f;

        private GraphicRaycaster[] _graphicRaycasters;
        private readonly List<RaycastResult> _uiResults = new List<RaycastResult>();

        private void Awake()
        {
            if (desktopWindow == null)
            {
                desktopWindow = FindObjectOfType<DesktopWindowController>();
            }

            if (interactionCamera == null)
            {
                interactionCamera = Camera.main;
            }

            _graphicRaycasters = FindObjectsOfType<GraphicRaycaster>(true);
        }

        private void Update()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (desktopWindow == null || interactionCamera == null)
            {
                return;
            }

            if (desktopWindow.IsDragging)
            {
                desktopWindow.SetClickThrough(false);
                return;
            }

            var isOverInteractiveObject = false;
            if (desktopWindow.TryGetCursorClientPosition(out var pointer) &&
                pointer.x >= 0f && pointer.y >= 0f &&
                pointer.x < Screen.width && pointer.y < Screen.height)
            {
                var ray = interactionCamera.ScreenPointToRay(pointer);
                isOverInteractiveObject = Physics.Raycast(
                    ray,
                    rayDistance,
                    interactiveLayers,
                    QueryTriggerInteraction.Collide);

                if (!isOverInteractiveObject)
                {
                    isOverInteractiveObject = (DesktopChatController.Active != null && DesktopChatController.Active.IsPointerOverChat(pointer)) || IsOverInteractiveUi(pointer);
                }
            }

            desktopWindow.SetClickThrough(!isOverInteractiveObject);
#endif
        }

        private bool IsOverInteractiveUi(Vector2 pointer)
        {
            if (EventSystem.current == null || _graphicRaycasters == null)
            {
                return false;
            }

            var eventData = new PointerEventData(EventSystem.current) { position = pointer };
            foreach (var raycaster in _graphicRaycasters)
            {
                if (raycaster == null || !raycaster.isActiveAndEnabled)
                {
                    continue;
                }

                _uiResults.Clear();
                raycaster.Raycast(eventData, _uiResults);
                if (_uiResults.Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void OnDisable()
        {
            if (desktopWindow != null)
            {
                desktopWindow.SetClickThrough(false);
            }
        }
    }
}
