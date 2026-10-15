#nullable enable
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace VirooCaveTemplate.Interactions
{
    /// <summary>
    /// Desktop testing/demo aid: points VIROO's desktop right-hand controller at the mouse cursor,
    /// so markers and popup buttons can be aimed with the mouse and selected with the desktop
    /// select key (H) - instead of turning the whole view until the fixed forward ray lines up.
    ///
    /// VIROO's desktop rig parents its controllers to the camera with rays pointing straight ahead.
    /// This only touches that runtime-spawned "RightController Desktop"; on the CAVE / VR rigs it
    /// doesn't exist and this does nothing. While the right mouse button is held (looking around)
    /// the cursor is locked, so the ray is left pointing ahead.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class DesktopMouseAim : MonoBehaviour
    {
        [SerializeField] private string controllerName = "RightController Desktop";
        [SerializeField] private float maxDistance = 30f;

        private Transform? controller;
        private Quaternion restRotation;
        private float nextSearch;

        protected void LateUpdate()
        {
            if (controller == null)
            {
                FindController();
                return;
            }

            Mouse? mouse = Mouse.current;
            Camera? cam = Camera.main;
            if (mouse == null || cam == null || mouse.rightButton.isPressed)
            {
                controller.localRotation = restRotation;
                return;
            }

            Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            Vector3 target = Physics.Raycast(ray, out RaycastHit hit, maxDistance, ~0, QueryTriggerInteraction.Collide)
                ? hit.point
                : ray.GetPoint(maxDistance);

            Vector3 direction = target - controller.position;
            if (direction.sqrMagnitude > 1e-6f)
            {
                controller.rotation = Quaternion.LookRotation(direction, cam.transform.up);
            }
        }

        private void FindController()
        {
            if (Time.unscaledTime < nextSearch)
            {
                return;
            }

            nextSearch = Time.unscaledTime + 1f;
            foreach (NearFarInteractor interactor in FindObjectsByType<NearFarInteractor>(FindObjectsSortMode.None))
            {
                Transform parent = interactor.transform.parent;
                if (parent != null && parent.name == controllerName)
                {
                    controller = parent;
                    restRotation = parent.localRotation;
                    return;
                }
            }
        }
    }
}
