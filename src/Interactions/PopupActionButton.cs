#nullable enable
using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Viroo.Interactions.XRInteractionToolkit;
using VirooCaveTemplate.Networking;

namespace VirooCaveTemplate.Interactions
{
    /// <summary>
    /// One pre-built, XR-ray-selectable button slot in the point-of-interest popup, filled in at
    /// runtime by PointOfInterestPopupUI from the open point's <see cref="IPointActionProvider"/>.
    /// Selected through a collider + XRSimpleInteractable for the same reason as
    /// <see cref="PopupCloseOnSelect"/>: mouse/EventSystem clicks don't reach world-space UI in
    /// VIROO's desktop mode. The slots are built in the scene (not at runtime) so the Viroo
    /// interactable is set up like every other interactable in the scene.
    ///
    /// Lives on the button's XRHitArea child; <see cref="Root"/> is the visible button.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [RequireComponent(typeof(XRSimpleInteractable))]
    [RequireComponent(typeof(VirooXRSimpleInteractable))]
    public class PopupActionButton : MonoBehaviour
    {
        [SerializeField] private RectTransform root = default!;
        [SerializeField] private Image background = default!;
        [SerializeField] private Text label = default!;
        [SerializeField] private Sprite? normalSprite;
        [SerializeField] private Sprite? hoverSprite;
        [Tooltip("Sprite for the currently chosen option (e.g. the O2 flow in use).")]
        [SerializeField] private Sprite? activeSprite;
        [SerializeField] private Color labelColor = new(0f, 0.97f, 1f, 1f);
        [SerializeField] private Color activeLabelColor = new(1f, 0.76f, 0.2f, 1f);

        private XRSimpleInteractable interactable = default!;
        private Collider hitArea = default!;
        private CanvasGroup group = default!;
        private Action? onPress;
        private bool isInteractable;
        private bool isActive;

        public RectTransform Root => root;

        protected void Awake()
        {
            interactable = GetComponent<XRSimpleInteractable>();
            hitArea = GetComponent<Collider>();
            group = root.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = root.gameObject.AddComponent<CanvasGroup>();
            }
        }

        protected void OnEnable()
        {
            interactable.selectEntered.AddListener(OnSelectEntered);
            interactable.hoverEntered.AddListener(OnHoverChanged);
            interactable.hoverExited.AddListener(OnHoverChanged);
        }

        protected void OnDisable()
        {
            interactable.selectEntered.RemoveListener(OnSelectEntered);
            interactable.hoverEntered.RemoveListener(OnHoverChanged);
            interactable.hoverExited.RemoveListener(OnHoverChanged);
        }

        public void Configure(PointAction action, Action press)
        {
            root.gameObject.SetActive(true);
            label.text = action.Label;
            onPress = press;
            isInteractable = action.Interactable;
            isActive = action.Active;
            hitArea.enabled = isInteractable && !isActive;
            Apply();
        }

        public void Hide()
        {
            onPress = null;
            root.gameObject.SetActive(false);
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (isInteractable && !isActive)
            {
                onPress?.Invoke();
            }
        }

        private void OnHoverChanged(BaseInteractionEventArgs args) => Apply();

        private void Apply()
        {
            bool hovered = interactable.isHovered && isInteractable && !isActive;
            Sprite? sprite = isActive ? activeSprite : hovered ? hoverSprite : normalSprite;
            if (sprite != null)
            {
                background.sprite = sprite;
            }

            label.color = isActive ? activeLabelColor : labelColor;
            group.alpha = isInteractable ? 1f : 0.4f;
        }
    }
}
