using UnityEngine;
using UnityEngine.EventSystems;

namespace DesktopPet
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class PetDragController : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private DesktopWindowController desktopWindow;
        [SerializeField] private string draggingParameter = "IsDragging";

        private int _draggingParameterHash;
        private bool _ownsDrag;

        private void Awake()
        {
            if (animator == null)
            {
                animator = GetComponent<Animator>();
            }

            if (desktopWindow == null)
            {
                desktopWindow = FindObjectOfType<DesktopWindowController>();
            }

            _draggingParameterHash = Animator.StringToHash(draggingParameter);
        }

        private void TryStartDragging()
        {
            if (desktopWindow == null || !desktopWindow.TryGetCursorClientPosition(out var pointer)) return;
            if (DesktopChatController.Active != null && DesktopChatController.Active.IsPointerOverChat(pointer)) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            var camera = Camera.main;
            if (camera == null || !camera.pixelRect.Contains(pointer)) return;
            if (!Physics.Raycast(camera.ScreenPointToRay(pointer), out var hit, 1000f, ~0,
                QueryTriggerInteraction.Collide) || hit.collider.GetComponentInParent<PetDragController>() != this) return;
            if (desktopWindow == null || !desktopWindow.BeginDrag())
            {
                return;
            }

            _ownsDrag = true;
            SetDraggingAnimation(true);
        }

        private void Update()
        {
            if (!_ownsDrag && Input.GetMouseButtonDown(1)) TryStartDragging();
            if (!_ownsDrag)
            {
                return;
            }

#if UNITY_EDITOR
            if (!Input.GetMouseButton(1))
            {
                StopDragging();
            }
#else
            if (desktopWindow == null || !desktopWindow.IsDragging)
            {
                StopDragging();
            }
#endif
        }

        private void OnDisable()
        {
            StopDragging();
        }

        private void StopDragging()
        {
            if (!_ownsDrag)
            {
                return;
            }

            _ownsDrag = false;
            if (desktopWindow != null)
            {
                desktopWindow.EndDrag();
            }

            SetDraggingAnimation(false);
        }

        private void SetDraggingAnimation(bool value)
        {
            if (animator != null && HasParameter(animator, _draggingParameterHash))
            {
                animator.SetBool(_draggingParameterHash, value);
            }
        }

        private static bool HasParameter(Animator targetAnimator, int parameterHash)
        {
            foreach (var parameter in targetAnimator.parameters)
            {
                if (parameter.nameHash == parameterHash)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
