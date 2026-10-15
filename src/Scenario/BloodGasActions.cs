#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using VirooCaveTemplate.Interactions.Visuals;
using VirooCaveTemplate.Networking;

namespace VirooCaveTemplate.Scenario
{
    /// <summary>
    /// Point J (arterial blood draw): one action, "draw sample". While the sample is processed a
    /// progress ring fills around the marker on every screen; when the result is back (see
    /// <see cref="ScenarioState"/>) the syringe of dark chocolate-brown blood appears floating by
    /// the wrist, turned side-on to the viewer and gently rocking so the colour reads clearly, and
    /// the popup body becomes the blood gas report - normal/high PaO2 and calculated
    /// SaO2 against an SpO2 of ~85%, with a raised MetHb on co-oximetry: the diagnostic clue.
    /// </summary>
    [RequireComponent(typeof(InteractionPoint))]
    public class BloodGasActions : MonoBehaviour, IPointActionProvider
    {
        [Tooltip("Syringe shown once the result is back. Keep it outside this point's hierarchy - NetworkedVisibilityToggle would otherwise switch its renderers on and off with the marker.")]
        [SerializeField] private Transform? syringe;
        [SerializeField] private float syringePopSeconds = 0.6f;
        [SerializeField] private float syringeRockDegrees = 20f;
        [SerializeField] private float syringeBobMetres = 0.008f;

        public event EventHandler? Changed;

        private InteractionPointVisual? visual;
        private BloodGasStage shownStage = (BloodGasStage)(-1);
        private float syringeShownAt = -1f;
        private Vector3 syringeScale = Vector3.one;
        private Vector3 syringePosition;

        protected void Awake()
        {
            visual = GetComponent<InteractionPointVisual>();
            if (syringe != null)
            {
                syringeScale = syringe.localScale;
                syringePosition = syringe.position;
                syringe.gameObject.SetActive(false);
            }
        }

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

        protected void Update()
        {
            ScenarioState? state = ScenarioState.Instance;
            if (state == null)
            {
                return;
            }

            visual?.SetProgress(state.BloodGas == BloodGasStage.Drawing ? state.BloodGasProgress : -1f);

            if (syringe != null && syringe.gameObject.activeSelf)
            {
                PresentSyringe();
            }

            // Syringe "pops" in with a little overshoot.
            if (syringe != null && syringeShownAt >= 0f)
            {
                float t = Mathf.Clamp01((Time.time - syringeShownAt) / syringePopSeconds);
                float back = 1f + (2.2f * Mathf.Pow(t - 1f, 3f)) + (1.2f * Mathf.Pow(t - 1f, 2f));
                syringe.localScale = syringeScale * back;
                if (t >= 1f)
                {
                    syringeShownAt = -1f;
                }
            }
        }

        // Long axis horizontal and across the viewer's line of sight (each screen's own camera),
        // tip slightly down, rocking a little.
        private void PresentSyringe()
        {
            if (syringe == null)
            {
                return;
            }

            syringe.position = syringePosition + (Vector3.up * (Mathf.Sin(Time.time * 1.6f) * syringeBobMetres));

            Camera? cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            Vector3 toViewer = Vector3.ProjectOnPlane(cam.transform.position - syringe.position, Vector3.up);
            if (toViewer.sqrMagnitude < 1e-4f)
            {
                return;
            }

            float rock = Mathf.Sin(Time.time * 0.9f) * syringeRockDegrees;
            syringe.rotation = Quaternion.AngleAxis(rock, Vector3.up)
                * Quaternion.LookRotation(toViewer.normalized, Vector3.up)
                * Quaternion.Euler(0f, 0f, 100f);
        }

        private void OnStateChanged(object sender, EventArgs e)
        {
            ScenarioState? state = ScenarioState.Instance;
            if (state == null)
            {
                return;
            }

            BloodGasStage stage = state.BloodGas;
            if (stage != shownStage)
            {
                bool wasKnown = (int)shownStage >= 0;
                shownStage = stage;

                visual?.SetStatus(stage switch
                {
                    BloodGasStage.Drawing => "ΑΝΑΛΥΣΗ · ANALYSING",
                    BloodGasStage.Resulted => "ΑΠΟΤΕΛΕΣΜΑ · RESULT",
                    _ => "",
                });

                if (syringe != null)
                {
                    bool show = stage == BloodGasStage.Resulted;
                    syringe.gameObject.SetActive(show);
                    syringe.localScale = syringeScale;

                    // Animate only when it happens live, not when a late joiner syncs in.
                    syringeShownAt = show && wasKnown ? Time.time : -1f;
                }
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

            return state.BloodGas switch
            {
                BloodGasStage.Drawing => $"{staticBody}\n\n<i>Το δείγμα αναλύεται… / Sample being analysed…</i>",
                BloodGasStage.Resulted => Report(state),
                _ => staticBody,
            };
        }

        public void GetActions(List<PointAction> actions)
        {
            ScenarioState? state = ScenarioState.Instance;
            if (state == null)
            {
                return;
            }

            switch (state.BloodGas)
            {
                case BloodGasStage.NotTaken:
                    actions.Add(new PointAction("Λήψη δείγματος\nDraw ABG sample", state.DrawBloodGas, interactable: state.IsReady));
                    break;
                case BloodGasStage.Drawing:
                    actions.Add(new PointAction("Σε εξέλιξη…\nIn progress…", () => { }, interactable: false));
                    break;
            }
        }

        private static string Report(ScenarioState state)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            float fio2 = state.BloodGasFiO2Percent / 100f;
            int paO2 = state.PaO2ForFiO2(fio2);
            const string high = "<color=#FF5A4A><b>▲</b></color>";

            return
                $"<b>Αέρια αρτηριακού αίματος / Arterial blood gas</b>  (FiO2 {fio2.ToString("0.00", c)})\n" +
                $"pH {state.PH.ToString("0.00", c)}   ·   PaCO2 {state.PaCO2} mmHg   ·   PaO2 {paO2} mmHg   ·   HCO3 {state.Bicarbonate} mmol/L\n" +
                $"SaO2 (υπολογ. / calc.) 99 %   ·   Lactate {state.Lactate.ToString("0.0", c)} mmol/L\n\n" +
                $"<b>Συν-οξυμετρία / Co-oximetry</b>\n" +
                $"O2Hb {state.O2HbPercent} %   ·   <b>MetHb {state.MetHbPercent} %</b> {high}   ·   COHb {state.CoHbPercent} %\n\n" +
                $"Δείγμα: σκούρο καφέ χρώμα / Sample: dark chocolate-brown";
        }
    }
}
