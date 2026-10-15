#nullable enable
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using VirooCaveTemplate.Networking;

namespace VirooCaveTemplate.Interactions
{
    /// <summary>
    /// Thin adapter: hides this object (via <see cref="NetworkedVisibilityToggle"/>) the moment
    /// any XR interactor selects it - grab, ray, poke, gaze, whichever the sibling interactable
    /// allows. Swap or duplicate this component to trigger a different action (Show, Toggle, a
    /// custom method, ...) from the same interaction without touching NetworkedVisibilityToggle.
    /// </summary>
    [RequireComponent(typeof(XRBaseInteractable))]
    [RequireComponent(typeof(NetworkedVisibilityToggle))]
    public class HideOnSelect : MonoBehaviour
    {
        private XRBaseInteractable interactable = default!;
        private NetworkedVisibilityToggle visibilityToggle = default!;

        protected void Awake()
        {
            interactable = GetComponent<XRBaseInteractable>();
            visibilityToggle = GetComponent<NetworkedVisibilityToggle>();
        }

        protected void OnEnable() => interactable.selectEntered.AddListener(OnSelectEntered);

        protected void OnDisable() => interactable.selectEntered.RemoveListener(OnSelectEntered);

        private void OnSelectEntered(SelectEnterEventArgs args) => visibilityToggle.Hide();
    }
}
