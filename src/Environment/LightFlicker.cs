#nullable enable
using System;
using System.Collections;
using UnityEngine;
using Viroo.Api;
using Virtualware.Networking.Client.Variables;

namespace VirooCaveTemplate.Environment
{
    /// <summary>
    /// A light that occasionally dips in intensity for a moment, like a slightly imperfect
    /// fluorescent fixture. The session leader decides when each flicker happens and broadcasts
    /// it over the network, so every CAVE screen flickers the same light at the same moment
    /// instead of each screen rolling its own independent random timer and drifting apart.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Light))]
    public class LightFlicker : MonoBehaviour
    {
        [Tooltip("Unique id for this light's synced flicker trigger (must be unique across the whole session/scene). Leave blank to derive one from the object's name and instance id.")]
        [SerializeField]
        private string networkId = "";

        [SerializeField] private float baseIntensity = 4.2f;
        [SerializeField] private float flickerAmount = 0.35f;
        [SerializeField] private float minInterval = 2f;
        [SerializeField] private float maxInterval = 6f;
        [SerializeField] private float flickerDuration = 0.08f;

        private Light lightComponent = default!;
        private NetworkVariable<int>? flickerTrigger;
        private bool receivedFirstValue;

        protected void Awake()
        {
            if (string.IsNullOrEmpty(networkId))
            {
                networkId = $"{nameof(LightFlicker)}-{gameObject.name}-{gameObject.GetInstanceID()}";
            }

            lightComponent = GetComponent<Light>();
            VirooApi.Instance.OnInitialized += OnVirooInitialized;
        }

        protected void OnDestroy()
        {
            VirooApi.Instance.OnInitialized -= OnVirooInitialized;

            if (flickerTrigger != null)
            {
                flickerTrigger.OnInitialized -= OnFlickerTriggered;
                flickerTrigger.OnValueChanged -= OnFlickerTriggered;
            }
        }

        private void OnVirooInitialized(object sender, EventArgs e)
        {
            VirooApi.Instance.OnInitialized -= OnVirooInitialized;

            flickerTrigger = VirooApi.Instance.Networking().CreateNetworkVariable(networkId, 0);
            flickerTrigger.OnInitialized += OnFlickerTriggered;
            flickerTrigger.OnValueChanged += OnFlickerTriggered;

            VirooApi.Instance.Networking().ConditionalRunner.RunIfLeader(() => StartCoroutine(ScheduleLoop()));
        }

        // Also invoked once, immediately, when the NetworkVariable finishes its initial sync - that
        // first delivery is just the starting baseline (0), not an actual flicker, so it's skipped.
        private void OnFlickerTriggered(object sender, int value)
        {
            if (!receivedFirstValue)
            {
                receivedFirstValue = true;
                return;
            }

            StartCoroutine(FlickerOnce());
        }

        private IEnumerator ScheduleLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(UnityEngine.Random.Range(minInterval, maxInterval));

                if (flickerTrigger != null)
                {
                    flickerTrigger.Value += 1;
                }
            }
        }

        private IEnumerator FlickerOnce()
        {
            lightComponent.intensity = baseIntensity - flickerAmount;
            yield return new WaitForSeconds(flickerDuration);
            lightComponent.intensity = baseIntensity;
        }
    }
}
