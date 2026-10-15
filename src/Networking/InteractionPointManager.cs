#nullable enable
using System;
using UnityEngine;
using Viroo.Api;
using Virtualware.Networking.Client.Variables;

namespace VirooCaveTemplate.Networking
{
    /// <summary>
    /// Scene-wide coordinator for <see cref="InteractionPoint"/> popups. Keeps a single "which
    /// point is currently open" id synchronized across every connected VIROO session client, so
    /// clicking a point of interest on any CAVE screen opens the same popup, with the same
    /// content, on every screen at once. Only one point can be open at a time - opening a new one
    /// closes whatever was open before, matching a guided walkthrough rather than N independent
    /// popups cluttering the room.
    ///
    /// One instance per scene. Any client can open or close a point - these are read-only
    /// observations with no scenario-state consequence (see InteractionPoint), so there's no
    /// leader-only gating here.
    /// </summary>
    public class InteractionPointManager : MonoBehaviour
    {
        private const string NetworkId = "InteractionPointManager-ActivePoint";

        public static InteractionPointManager? Instance { get; private set; }

        public event EventHandler<string>? ActivePointChanged;

        private NetworkVariable<string>? activePointId;

        protected void Awake()
        {
            Instance = this;
            VirooApi.Instance.OnInitialized += OnVirooInitialized;
        }

        protected void OnDestroy()
        {
            VirooApi.Instance.OnInitialized -= OnVirooInitialized;

            if (activePointId != null)
            {
                activePointId.OnInitialized -= OnActivePointChanged;
                activePointId.OnValueChanged -= OnActivePointChanged;
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnVirooInitialized(object sender, EventArgs e)
        {
            VirooApi.Instance.OnInitialized -= OnVirooInitialized;

            activePointId = VirooApi.Instance.Networking().CreateNetworkVariable(NetworkId, "");
            activePointId.OnInitialized += OnActivePointChanged;
            activePointId.OnValueChanged += OnActivePointChanged;
        }

        // Also invoked once, immediately, when the NetworkVariable finishes its initial sync.
        private void OnActivePointChanged(object sender, string value)
        {
            ActivePointChanged?.Invoke(this, value);
        }

        public bool TryGetPoint(string id, out InteractionPoint? point)
        {
            foreach (InteractionPoint candidate in FindObjectsByType<InteractionPoint>(FindObjectsSortMode.None))
            {
                if (candidate.PointId == id)
                {
                    point = candidate;
                    return true;
                }
            }

            point = null;
            return false;
        }

        /// <summary>Opens the given point for everyone; clicking the already-open point closes it.</summary>
        public void Open(string id)
        {
            if (activePointId == null)
            {
                return;
            }

            activePointId.Value = activePointId.Value == id ? "" : id;
        }

        /// <summary>Opens the given point for everyone, without the toggle-closed behaviour of <see cref="Open"/>.</summary>
        public void Show(string id)
        {
            if (activePointId != null)
            {
                activePointId.Value = id;
            }
        }

        public void Close()
        {
            if (activePointId != null)
            {
                activePointId.Value = "";
            }
        }
    }
}
