#nullable enable
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Viroo.Interactions.XRInteractionToolkit;

namespace VirooCaveTemplate.Networking
{
    /// <summary>
    /// A clickable point of interest - e.g. a small sphere placed on the patient's face, chest,
    /// hand, or on a piece of equipment. On select, opens a synchronized info popup (see
    /// <see cref="InteractionPointManager"/> and <see cref="PointOfInterestPopupUI"/>) showing
    /// this point's title and body text on every connected CAVE screen at once - selecting it on
    /// any one screen opens it on all of them. While its popup is open the point stays visible
    /// (InteractionPointVisual shows it highlighted) and selecting it again closes the popup;
    /// enable Hide While Open to hide it (also synchronized) instead.
    ///
    /// Uses VirooXRSimpleInteractable rather than the GrabbableCube/RaySelectCube's
    /// VirooXRGrabInteractable - same Viroo networked-interaction family (same
    /// AllowDirectInteraction / AllowRayInteraction / AllowDistanceSelectInteraction flags,
    /// requires a sibling stock interactable the same way), but built on XRSimpleInteractable
    /// instead of XRGrabInteractable. XRGrabInteractable is built around a persistent "held"
    /// select state meant for picking something up - even with tracking disabled so it doesn't
    /// visually move, the ray still latches onto it until explicitly released, requiring a second
    /// press to let go before it can be clicked again. A point of interest just needs a single
    /// momentary select per click, which is what XRSimpleInteractable (and its Viroo wrapper) is
    /// for - no grab/attach/hold behaviour at all, while still supporting ray/distance selection
    /// from any connected screen.
    ///
    /// Cross-screen sync doesn't come from the interactable component at all - it comes from
    /// InteractionPointManager's own NetworkVariable (which point is open) and this object's own
    /// NetworkedVisibilityToggle (whether this point is currently visible), both independent of
    /// which interactable type is used here.
    ///
    /// Deliberately has no NetworkObject: that component exists to give a grabbed, moving object a
    /// network identity and authority for position sync. This point never moves, so there's
    /// nothing to give it authority over.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(XRSimpleInteractable))]
    [RequireComponent(typeof(VirooXRSimpleInteractable))]
    [RequireComponent(typeof(NetworkedVisibilityToggle))]
    public class InteractionPoint : MonoBehaviour
    {
        [Tooltip("Unique id for this point (e.g. \"A\"). Must not collide with any other point in the scene.")]
        [SerializeField]
        private string pointId = "";

        [SerializeField]
        private string title = "";

        [TextArea(3, 10)]
        [SerializeField]
        private string body = "";

        [Tooltip("Hide this point while its own popup is showing. Off: it stays visible (highlighted by InteractionPointVisual) and selecting it again closes the popup.")]
        [SerializeField]
        private bool hideWhileOpen;

        public string PointId => pointId;

        public string Title => title;

        public string Body => body;

        private XRSimpleInteractable interactable = default!;
        private NetworkedVisibilityToggle visibility = default!;

        protected void Awake()
        {
            interactable = GetComponent<XRSimpleInteractable>();
            visibility = GetComponent<NetworkedVisibilityToggle>();
        }

        // Subscribing in Start (not OnEnable/Awake) guarantees InteractionPointManager.Instance is
        // already set, since every Awake in the scene runs before the first Start.
        protected void Start()
        {
            if (InteractionPointManager.Instance != null)
            {
                InteractionPointManager.Instance.ActivePointChanged += OnActivePointChanged;
            }
        }

        protected void OnEnable() => interactable.selectEntered.AddListener(OnSelectEntered);

        protected void OnDisable() => interactable.selectEntered.RemoveListener(OnSelectEntered);

        protected void OnDestroy()
        {
            if (InteractionPointManager.Instance != null)
            {
                InteractionPointManager.Instance.ActivePointChanged -= OnActivePointChanged;
            }
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            InteractionPointManager.Instance?.Open(pointId);
        }

        // With hideWhileOpen, hides this point while its own popup is showing, and shows it again
        // as soon as any other point (including "none") becomes active - covers both the OK/close
        // button and another point being opened instead, since both change the active point id.
        // Without it, always shows - which also restores a point left hidden in the synced state.
        private void OnActivePointChanged(object sender, string activeId)
        {
            if (hideWhileOpen && activeId == pointId)
            {
                visibility.Hide();
            }
            else
            {
                visibility.Show();
            }
        }
    }
}
