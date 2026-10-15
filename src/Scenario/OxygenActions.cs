#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using VirooCaveTemplate.Interactions.Visuals;
using VirooCaveTemplate.Networking;

namespace VirooCaveTemplate.Scenario
{
    /// <summary>
    /// Point F (O2 flow meter): lets the trainee change the oxygen delivery device and flow from
    /// the popup. Writes only to <see cref="ScenarioState"/>; the monitor's SpO2 barely moves
    /// whatever they choose, which is the scenario's central paradox. The marker's status chip
    /// shows the current setting on every screen.
    /// </summary>
    [RequireComponent(typeof(InteractionPoint))]
    public class OxygenActions : MonoBehaviour, IPointActionProvider
    {
        private static readonly OxygenDevice[] Devices = { OxygenDevice.NasalCannula, OxygenDevice.SimpleMask, OxygenDevice.NonRebreather };

        public event EventHandler? Changed;

        private InteractionPointVisual? visual;

        protected void Awake() => visual = GetComponent<InteractionPointVisual>();

        // Subscribing in Start guarantees ScenarioState.Instance is set (it's assigned in Awake).
        protected void Start()
        {
            if (ScenarioState.Instance != null)
            {
                ScenarioState.Instance.Changed += OnStateChanged;
            }

            OnStateChanged(this, EventArgs.Empty);
        }

        protected void OnDestroy()
        {
            if (ScenarioState.Instance != null)
            {
                ScenarioState.Instance.Changed -= OnStateChanged;
            }
        }

        private void OnStateChanged(object sender, EventArgs e)
        {
            ScenarioState? state = ScenarioState.Instance;
            if (state != null && visual != null)
            {
                visual.SetStatus($"{state.Flow} L/min · {ScenarioState.DeviceShortName(state.Device)}");
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }

        public string ComposeBody(string staticBody)
        {
            ScenarioState? state = ScenarioState.Instance;
            if (state == null)
            {
                return staticBody;
            }

            (int min, int max) = ScenarioState.FlowRange(state.Device);
            int fio2 = Mathf.RoundToInt(ScenarioState.FiO2(state.Device, state.Flow) * 100f);

            return $"{staticBody}\n\n" +
                $"<b>Χορήγηση O₂:</b> {ScenarioState.DeviceName(state.Device)}, <b>{state.Flow} L/min</b> " +
                $"(εύρος {min}–{max})  ·  εκτ. FiO₂ ~{fio2}%";
        }

        public void GetActions(List<PointAction> actions)
        {
            ScenarioState? state = ScenarioState.Instance;
            if (state == null)
            {
                return;
            }

            bool ready = state.IsReady;

            foreach (OxygenDevice device in Devices)
            {
                OxygenDevice target = device;
                actions.Add(new PointAction(DeviceButtonLabel(device), () => state.SetDevice(target), row: 0, interactable: ready, active: state.Device == device));
            }

            // Flowmeter knob: down / current reading / up, within the device's range.
            (int min, int max) = ScenarioState.FlowRange(state.Device);
            actions.Add(new PointAction("− 1 L/min", () => state.ChangeFlow(-1), row: 1, interactable: ready && state.Flow > min));
            actions.Add(new PointAction($"{state.Flow} L/min", () => { }, row: 1, active: true));
            actions.Add(new PointAction("+ 1 L/min", () => state.ChangeFlow(1), row: 1, interactable: ready && state.Flow < max));
        }

        private static string DeviceButtonLabel(OxygenDevice device) => device switch
        {
            OxygenDevice.NasalCannula => "Ρινικός καθετήρας",
            OxygenDevice.SimpleMask => "Απλή μάσκα",
            _ => "Μάσκα με ασκό\n(NRB)",
        };
    }
}
