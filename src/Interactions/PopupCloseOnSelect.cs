#nullable enable
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Viroo.Interactions.XRInteractionToolkit;
using VirooCaveTemplate.Networking;

namespace VirooCaveTemplate.Interactions
{
    /// <summary>
    /// Lets the point-of-interest popup's OK button be pressed with an XR ray (desktop
    /// controllers, CAVE wand), the same way the points themselves are selected - via a collider
    /// and an XRSimpleInteractable rather than the EventSystem.
    ///
    /// The UI Button alone isn't enough: in desktop mode VIROO runs the EventSystem with the
    /// mouse-only InputSystemUIInputModule (XRUIInputModule disabled), and VirooContextMock's
    /// full-screen "Desktop" UI Toolkit panel sits on top of every world-space canvas, so mouse
    /// clicks never reach the button. Closing is idempotent, so if the UI Button does also fire
    /// (e.g. on a rig where XRUIInputModule is active) nothing breaks.
    ///
    /// Put this on a child of the button sized to it, on a layer the interactors' ray hits.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [RequireComponent(typeof(XRSimpleInteractable))]
    [RequireComponent(typeof(VirooXRSimpleInteractable))]
    public class PopupCloseOnSelect : MonoBehaviour
    {
        [Tooltip("Button image to swap while the ray hovers it.")]
        [SerializeField] private Image? target;
        [SerializeField] private Sprite? hoverSprite;

        private XRSimpleInteractable interactable = default!;
        private Sprite? normalSprite;

        protected void Awake()
        {
            interactable = GetComponent<XRSimpleInteractable>();
            if (target != null)
            {
                normalSprite = target.sprite;
            }
        }

        protected void OnEnable()
        {
            interactable.selectEntered.AddListener(OnSelectEntered);
            interactable.hoverEntered.AddListener(OnHoverEntered);
            interactable.hoverExited.AddListener(OnHoverExited);
        }

        protected void OnDisable()
        {
            interactable.selectEntered.RemoveListener(OnSelectEntered);
            interactable.hoverEntered.RemoveListener(OnHoverEntered);
            interactable.hoverExited.RemoveListener(OnHoverExited);
            SetSprite(normalSprite);
        }

        private void OnSelectEntered(SelectEnterEventArgs args) => InteractionPointManager.Instance?.Close();

        private void OnHoverEntered(HoverEnterEventArgs args) => SetSprite(hoverSprite);

        private void OnHoverExited(HoverExitEventArgs args)
        {
            if (!interactable.isHovered)
            {
                SetSprite(normalSprite);
            }
        }

        private void SetSprite(Sprite? sprite)
        {
            if (target != null && sprite != null)
            {
                target.sprite = sprite;
            }
        }
    }
}
