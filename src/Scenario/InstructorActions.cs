#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using VirooCaveTemplate.Interactions.Visuals;
using VirooCaveTemplate.Networking;

namespace VirooCaveTemplate.Scenario
{
    /// <summary>
    /// The instructor's in-scene control point ("Χ"), for running a session from inside the CAVE
    /// with only the tracked wand - no keyboard: reset the scenario, push the untreated
    /// deterioration to its next stage, and pause/resume the stage timers. Shows the current stage
    /// and time to the next one, both in its popup and in the marker's status chip. Everything it
    /// does goes through the networked <see cref="ScenarioState"/>, so all screens follow.
    /// </summary>
    [RequireComponent(typeof(InteractionPoint))]
    public class InstructorActions : MonoBehaviour, IPointActionProvider
    {
        public event EventHandler? Changed;

        private InteractionPointVisual? visual;
        private int shownSecond = -1;

        protected void Awake() => visual = GetComponent<InteractionPointVisual>();

        // Subscribing in Start guarantees ScenarioState.Instance is set (it's assigned in Awake).
        protected void Start()
        {
            if (ScenarioState.Instance != null)
            {
                ScenarioState.Instance.Changed += OnStateChanged;
            }
        }

        protected void OnDestroy()
        {
            if (ScenarioState.Instance != null)
            {
                ScenarioState.Instance.Changed -= OnStateChanged;
            }
        }

        // The countdown changes every second, so refresh the chip (and an open popup) then.
        protected void Update()
        {
            ScenarioState? state = ScenarioState.Instance;
            if (state == null)
            {
                return;
            }

            int second = Mathf.FloorToInt(Time.time);
            if (second != shownSecond)
            {
                shownSecond = second;
                visual?.SetStatus($"{StageName(state.Course).ToUpperInvariant()} · {Countdown(state)}");
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        private void OnStateChanged(object sender, EventArgs e) => shownSecond = -1;

        public string ComposeBody(string staticBody)
        {
            ScenarioState? state = ScenarioState.Instance;
            if (state == null)
            {
                return staticBody;
            }

            string timer = state.IsPaused ? "<color=#FFC233><b>σε παύση</b></color>" : "ενεργό";
            return $"{staticBody}\n\n" +
                $"<b>Κατάσταση ασθενούς:</b> {StageName(state.Course)}   ·   <b>Επόμενο στάδιο σε:</b> {Countdown(state)}\n" +
                $"<b>Χρονόμετρο:</b> {timer}   ·   SpO2 {state.SpO2}%   ·   HR {state.HeartRate}";
        }

        public void GetActions(List<PointAction> actions)
        {
            ScenarioState? state = ScenarioState.Instance;
            if (state == null)
            {
                return;
            }

            bool ready = state.IsReady;
            bool canAdvance = state.Course < ClinicalCourse.Arrest;

            actions.Add(new PointAction("Επανεκκίνηση\nσεναρίου", state.ResetScenario, interactable: ready));
            actions.Add(new PointAction("Επόμενο στάδιο\nεπιδείνωσης", state.AdvanceDeterioration, interactable: ready && canAdvance));
            actions.Add(state.IsPaused
                ? new PointAction("Συνέχεια\nχρόνου", () => state.SetPaused(false), interactable: ready)
                : new PointAction("Παύση\nχρόνου", () => state.SetPaused(true), interactable: ready));
        }

        private static string Countdown(ScenarioState state)
        {
            float seconds = state.SecondsToNextStage;
            if (float.IsInfinity(seconds))
            {
                return "—";
            }

            int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{s / 60}:{s % 60:00}" + (state.IsPaused ? " (παύση)" : "");
        }

        private static string StageName(ClinicalCourse course) => course switch
        {
            ClinicalCourse.Stable => "Σταθερή",
            ClinicalCourse.Worsening => "Επιδείνωση",
            ClinicalCourse.Severe => "Βαριά",
            ClinicalCourse.Arrest => "Ανακοπή",
            ClinicalCourse.Recovering => "Ανάκαμψη",
            _ => "Σταθεροποιήθηκε",
        };
    }
}
