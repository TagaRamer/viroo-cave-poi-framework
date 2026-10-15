#nullable enable
using System;
using System.Collections;
using UnityEngine;
using Viroo.Api;
using Virtualware.Networking.Client.Variables;

namespace VirooCaveTemplate.Networking
{
    /// <summary>
    /// Generic, reusable building block: keeps an Animator's playback position synchronized
    /// across every connected VIROO session client (every CAVE screen / user), so a looping idle
    /// animation (breathing, a spinning fan, ...) reads as one shared object instead of drifting
    /// out of phase between screens. Works with any Animator that is already sitting in the state
    /// it should loop (its default state) - no other networking code is required.
    ///
    /// The session leader periodically broadcasts its local normalized playback position; every
    /// client (leader included) re-syncs its own Animator to that position on receipt. Re-syncing
    /// on an interval, rather than once at startup, also corrects the slow drift that comes from
    /// each client running at its own frame rate over a long play session.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public class NetworkedAnimatorSync : MonoBehaviour
    {
        [Tooltip("Unique id for this object's synced animation state (must be unique across the whole session/scene). Leave blank to derive one from the object's name and instance id.")]
        [SerializeField]
        private string networkId = "";

        [Tooltip("Animator layer to sync (almost always 0 unless this Animator has multiple layers).")]
        [SerializeField]
        private int layerIndex = 0;

        [Tooltip("How often (seconds) the leader rebroadcasts its playback position to correct drift.")]
        [SerializeField]
        private float resyncInterval = 5f;

        private Animator animatorComponent = default!;
        private int stateHash;
        private NetworkVariable<float>? normalizedTime;

        protected void Awake()
        {
            if (string.IsNullOrEmpty(networkId))
            {
                networkId = $"{nameof(NetworkedAnimatorSync)}-{gameObject.name}-{gameObject.GetInstanceID()}";
            }

            animatorComponent = GetComponent<Animator>();
            VirooApi.Instance.OnInitialized += OnVirooInitialized;
        }

        protected void OnDestroy()
        {
            VirooApi.Instance.OnInitialized -= OnVirooInitialized;

            if (normalizedTime != null)
            {
                normalizedTime.OnInitialized -= OnNormalizedTimeChanged;
                normalizedTime.OnValueChanged -= OnNormalizedTimeChanged;
            }
        }

        private void OnVirooInitialized(object sender, EventArgs e)
        {
            VirooApi.Instance.OnInitialized -= OnVirooInitialized;

            AnimatorStateInfo state = animatorComponent.GetCurrentAnimatorStateInfo(layerIndex);
            stateHash = state.fullPathHash;

            normalizedTime = VirooApi.Instance.Networking().CreateNetworkVariable(networkId, state.normalizedTime % 1f);
            normalizedTime.OnInitialized += OnNormalizedTimeChanged;
            normalizedTime.OnValueChanged += OnNormalizedTimeChanged;

            VirooApi.Instance.Networking().ConditionalRunner.RunIfLeader(() => StartCoroutine(BroadcastLoop()));
        }

        // Also invoked once, immediately, when the NetworkVariable finishes its initial sync.
        private void OnNormalizedTimeChanged(object sender, float value)
        {
            animatorComponent.Play(stateHash, layerIndex, value);
        }

        private IEnumerator BroadcastLoop()
        {
            var wait = new WaitForSeconds(resyncInterval);
            while (true)
            {
                yield return wait;

                if (normalizedTime != null)
                {
                    normalizedTime.Value = animatorComponent.GetCurrentAnimatorStateInfo(layerIndex).normalizedTime % 1f;
                }
            }
        }
    }
}
