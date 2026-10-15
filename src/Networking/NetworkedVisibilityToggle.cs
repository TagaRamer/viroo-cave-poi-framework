#nullable enable
using System;
using UnityEngine;
using Viroo.Api;
using Virtualware.Networking.Client.Variables;

namespace VirooCaveTemplate.Networking
{
    /// <summary>
    /// Generic, reusable building block: keeps a boolean "visible" state synchronized across
    /// every connected VIROO session client (every CAVE screen / user), including ones that
    /// join later. Call <see cref="Show"/>, <see cref="Hide"/> or <see cref="Toggle"/> from any
    /// interaction (button press, ray select, UI click, timer, ...) - no other networking code
    /// is required.
    ///
    /// Renderers and colliders under this GameObject are enabled/disabled rather than the
    /// GameObject itself, so this component keeps running - and stays subscribed to network
    /// updates - even while "hidden".
    /// </summary>
    [DisallowMultipleComponent]
    public class NetworkedVisibilityToggle : MonoBehaviour
    {
        [Tooltip("Unique id for this object's synced visibility state (must be unique across the whole session/scene). Leave blank to derive one from the object's name and instance id.")]
        [SerializeField]
        private string networkId = "";

        [Tooltip("Visibility used before the network value has synced, and the state new clients start from.")]
        [SerializeField]
        private bool startsVisible = true;

        [Tooltip("When true, only the session leader (first-connected client) is allowed to write a new value; other clients calling Show/Hide/Toggle are ignored. Prevents duplicate/conflicting writes when several clients could trigger the same event at once.")]
        [SerializeField]
        private bool leaderOnly = true;

        private NetworkVariable<bool>? isVisible;
        private Renderer[] renderers = Array.Empty<Renderer>();
        private Collider[] colliders = Array.Empty<Collider>();

        protected void Awake()
        {
            if (string.IsNullOrEmpty(networkId))
            {
                networkId = $"{nameof(NetworkedVisibilityToggle)}-{gameObject.name}-{gameObject.GetInstanceID()}";
            }

            renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
            colliders = GetComponentsInChildren<Collider>(includeInactive: true);

            VirooApi.Instance.OnInitialized += OnVirooInitialized;
        }

        protected void OnDestroy()
        {
            VirooApi.Instance.OnInitialized -= OnVirooInitialized;

            if (isVisible != null)
            {
                isVisible.OnInitialized -= OnVisibilityChanged;
                isVisible.OnValueChanged -= OnVisibilityChanged;
            }
        }

        private void OnVirooInitialized(object sender, EventArgs e)
        {
            VirooApi.Instance.OnInitialized -= OnVirooInitialized;

            isVisible = VirooApi.Instance.Networking().CreateNetworkVariable(networkId, startsVisible);
            isVisible.OnInitialized += OnVisibilityChanged;
            isVisible.OnValueChanged += OnVisibilityChanged;
        }

        // Also invoked once, immediately, when the NetworkVariable finishes its initial sync.
        private void OnVisibilityChanged(object sender, bool visible)
        {
            foreach (Renderer r in renderers)
            {
                r.enabled = visible;
            }

            foreach (Collider c in colliders)
            {
                c.enabled = visible;
            }
        }

        public void Show() => SetVisible(true);

        public void Hide() => SetVisible(false);

        public void Toggle() => SetVisible(!(isVisible?.Value ?? startsVisible));

        private void SetVisible(bool visible)
        {
            if (isVisible == null)
            {
                return;
            }

            if (leaderOnly)
            {
                VirooApi.Instance.Networking().ConditionalRunner.RunIfLeader(() => isVisible.Value = visible);
            }
            else
            {
                isVisible.Value = visible;
            }
        }
    }
}
