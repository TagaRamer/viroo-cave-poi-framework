#nullable enable
using System;
using System.Collections;
using UnityEngine;
using Viroo.Api;
using Virtualware.Networking.Client.Variables;
using VirooCaveTemplate.Networking;

namespace VirooCaveTemplate.Scenario
{
    public enum OxygenDevice
    {
        NasalCannula = 0,
        SimpleMask = 1,
        NonRebreather = 2,
    }

    public enum BloodGasStage
    {
        NotTaken = 0,
        Drawing = 1,
        Resulted = 2,
    }

    /// <summary>The patient's clinical course. Untreated, it runs Stable -> Arrest over time.</summary>
    public enum ClinicalCourse
    {
        Stable = 0,
        Worsening = 1,
        Severe = 2,
        Arrest = 3,
        Recovering = 4,
        Recovered = 5,
    }

    [Flags]
    public enum IvDrug
    {
        None = 0,
        MethyleneBlue = 1,
        Naloxone = 2,
        Flumazenil = 4,
        Hydrocortisone = 8,
        Atropine = 16,
    }

    [Serializable]
    public struct VitalSigns
    {
        public int HeartRate;
        public int Systolic;
        public int Diastolic;
        public int RespiratoryRate;
        public int SpO2;

        public VitalSigns(int heartRate, int systolic, int diastolic, int respiratoryRate, int spo2)
        {
            HeartRate = heartRate;
            Systolic = systolic;
            Diastolic = diastolic;
            RespiratoryRate = respiratoryRate;
            SpO2 = spo2;
        }
    }

    /// <summary>
    /// The methaemoglobinaemia scenario's shared state, synchronized across every VIROO client so
    /// every CAVE screen shows the same monitor values, O2 setting and blood-gas progress.
    ///
    /// Only the trainee's actions and the clinical course are networked (O2 device + flow, blood gas
    /// sample, IV drugs given, course stage). Everything displayed - SpO2, FiO2, PaO2, the blood gas
    /// report - is derived from those on each client, so the screens can't disagree. Each section
    /// is a single packed int so a change (e.g. device + flow together) arrives as one update.
    ///
    /// Clinical course: untreated, the leader advances Stable -> Worsening -> Severe -> Arrest on a
    /// timer (deterioration can be switched off). Methylene blue moves it to Recovering, and after
    /// the onset delay the vitals trend back to normal and the course ends at Recovered. Monitor
    /// values glide between stages on every client rather than jumping.
    ///
    /// All clinical numbers below are PLACEHOLDERS chosen to illustrate the scenario's teaching
    /// point (SpO2 stuck around 85% whatever the O2, normal/high PaO2, raised MetHb). They have not
    /// been clinically reviewed and should be confirmed before use in training.
    /// </summary>
    public class ScenarioState : MonoBehaviour
    {
        private const string OxygenNetworkId = "Scenario-Oxygen";
        private const string BloodGasNetworkId = "Scenario-BloodGas";
        private const string CourseNetworkId = "Scenario-Course";
        private const string DrugsNetworkId = "Scenario-Drugs";
        private const string PausedNetworkId = "Scenario-Paused";

        public static ScenarioState? Instance { get; private set; }

        public event EventHandler? Changed;

        [Header("Vitals (PLACEHOLDERS - confirm clinically)")]
        [SerializeField] private int heartRate = 96;
        [SerializeField] private int systolic = 128;
        [SerializeField] private int diastolic = 78;
        [SerializeField] private int respiratoryRate = 16;
        [Tooltip("SpO2 on the monitor. Barely responds to oxygen - that's the scenario's paradox.")]
        [SerializeField] private int spo2 = 85;
        [SerializeField] private int spo2OnNonRebreather = 86;
        [Tooltip("Values below this trigger the monitor's SpO2 alarm.")]
        [SerializeField] private int spo2AlarmBelow = 90;

        [Header("Clinical course (PLACEHOLDERS - timing set for a short demo)")]
        [Tooltip("Off = the patient stays stable until treated, as in the original scenario draft.")]
        [SerializeField] private bool deteriorateIfUntreated = true;
        [SerializeField] private float stableSeconds = 150f;
        [SerializeField] private float worseningSeconds = 75f;
        [SerializeField] private float severeSeconds = 60f;
        [SerializeField] private VitalSigns worsening = new(114, 120, 74, 20, 81);
        [SerializeField] private VitalSigns severe = new(136, 98, 58, 27, 73);
        [SerializeField] private VitalSigns recovered = new(84, 122, 76, 14, 97);
        [Tooltip("Seconds after methylene blue before the vitals start to improve.")]
        [SerializeField] private float treatmentOnsetSeconds = 6f;
        [Tooltip("Seconds for the vitals to reach the recovered values (real life: tens of minutes).")]
        [SerializeField] private float recoverySeconds = 45f;

        [Header("Oxygen (starting setting)")]
        [SerializeField] private OxygenDevice startDevice = OxygenDevice.NasalCannula;
        [SerializeField] private int startFlow = 2;

        [Header("Arterial blood gas (PLACEHOLDERS - confirm clinically)")]
        [SerializeField] private float pH = 7.40f;
        [SerializeField] private int paCO2 = 38;
        [SerializeField] private int bicarbonate = 23;
        [SerializeField] private int metHbPercent = 28;
        [SerializeField] private int coHbPercent = 1;
        [SerializeField] private float lactate = 1.3f;
        [Tooltip("Alveolar-arterial gradient used to derive PaO2 from FiO2 (healthy lungs).")]
        [SerializeField] private int aaGradient = 10;

        [Header("Blood gas flow")]
        [Tooltip("Seconds between drawing the sample and the result coming back.")]
        [SerializeField] private float bloodGasSeconds = 8f;
        [Tooltip("Open this point's popup for everyone when the result comes back. Empty = don't.")]
        [SerializeField] private string resultPointId = "J";

        private NetworkVariable<int>? oxygen;
        private NetworkVariable<int>? bloodGas;
        private NetworkVariable<int>? course;
        private NetworkVariable<int>? drugs;
        private NetworkVariable<bool>? paused;
        private float drawStartedAt = -1f;
        private Coroutine? drawRoutine;

        // Displayed vitals glide from 'from' to the current stage's target.
        private Vector4 vitalsFrom;
        private float spo2From;
        private float transitionStart;
        private float transitionSeconds = 1f;
        private float courseEnteredAt;
        private bool advanceRequested;
        private ClinicalCourse shownCourse = ClinicalCourse.Stable;

        public bool IsReady => oxygen != null && bloodGas != null && course != null && drugs != null && paused != null;

        public ClinicalCourse Course => (ClinicalCourse)(course?.Value ?? 0);

        public IvDrug DrugsGiven => (IvDrug)(drugs?.Value ?? 0);

        public bool IsArrest => Course == ClinicalCourse.Arrest;

        /// <summary>How long (as seen by this client) the patient has been in the current stage.</summary>
        public float SecondsInCourse => Time.time - courseEnteredAt;

        /// <summary>Instructor pause: stage timers (deterioration, recovery) stop advancing.</summary>
        public bool IsPaused => paused?.Value ?? false;

        /// <summary>Seconds left before the course moves on by itself; infinity if it won't.</summary>
        public float SecondsToNextStage => StageLimit(Course) - SecondsInCourse;

        public OxygenDevice Device => (OxygenDevice)(OxygenValue / 100);

        public int Flow => OxygenValue % 100;

        public BloodGasStage BloodGas => (BloodGasStage)(BloodGasValue / 1000);

        /// <summary>FiO2 (as %) at the moment the sample was drawn - the report reflects that, not now.</summary>
        public int BloodGasFiO2Percent => BloodGasValue % 1000;

        /// <summary>0..1 while drawing, based on when this client saw the draw start.</summary>
        public float BloodGasProgress => BloodGas switch
        {
            BloodGasStage.Drawing when drawStartedAt >= 0f => Mathf.Clamp01((Time.time - drawStartedAt) / bloodGasSeconds),
            BloodGasStage.Resulted => 1f,
            _ => 0f,
        };

        public int HeartRate => Mathf.RoundToInt(Current().x);

        public int Systolic => Mathf.RoundToInt(Current().y);

        public int Diastolic => Mathf.RoundToInt(Current().z);

        public int RespiratoryRate => Mathf.RoundToInt(Current().w);

        public int SpO2 => Mathf.RoundToInt(CurrentSpO2());

        public bool SpO2Alarm => !IsArrest && SpO2 < spo2AlarmBelow;

        public float PH => pH;

        public int PaCO2 => paCO2;

        public int Bicarbonate => bicarbonate;

        public int MetHbPercent => metHbPercent;

        public int CoHbPercent => coHbPercent;

        public float Lactate => lactate;

        /// <summary>Oxyhaemoglobin fraction measured by co-oximetry.</summary>
        public int O2HbPercent => Mathf.Max(0, 100 - metHbPercent - coHbPercent - 1);

        private int OxygenValue => oxygen?.Value ?? Pack(startDevice, startFlow);

        private int BloodGasValue => bloodGas?.Value ?? 0;

        protected void Awake()
        {
            Instance = this;
            (vitalsFrom, spo2From) = Target(ClinicalCourse.Stable);
            VirooApi.Instance.OnInitialized += OnVirooInitialized;
        }

        protected void Update()
        {
            if (course == null)
            {
                return;
            }

            // Paused: hold the elapsed time still by moving the stage start along with the clock.
            if (IsPaused)
            {
                courseEnteredAt += Time.deltaTime;
                return;
            }

            ClinicalCourse current = Course;

            // Only the leader moves the course on, so it advances once for everyone.
            if (!advanceRequested && SecondsInCourse > StageLimit(current))
            {
                advanceRequested = true;
                VirooApi.Instance.Networking().ConditionalRunner.RunIfLeader(() =>
                {
                    if (Course == current)
                    {
                        course.Value = (int)Next(current);
                    }
                });
            }
        }

        protected void OnDestroy()
        {
            VirooApi.Instance.OnInitialized -= OnVirooInitialized;

            if (oxygen != null)
            {
                oxygen.OnInitialized -= OnOxygenChanged;
                oxygen.OnValueChanged -= OnOxygenChanged;
            }

            if (bloodGas != null)
            {
                bloodGas.OnInitialized -= OnBloodGasChanged;
                bloodGas.OnValueChanged -= OnBloodGasChanged;
            }

            if (course != null)
            {
                course.OnInitialized -= OnCourseChanged;
                course.OnValueChanged -= OnCourseChanged;
            }

            if (drugs != null)
            {
                drugs.OnInitialized -= OnDrugsChanged;
                drugs.OnValueChanged -= OnDrugsChanged;
            }

            if (paused != null)
            {
                paused.OnInitialized -= OnPausedChanged;
                paused.OnValueChanged -= OnPausedChanged;
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnVirooInitialized(object sender, EventArgs e)
        {
            VirooApi.Instance.OnInitialized -= OnVirooInitialized;

            oxygen = VirooApi.Instance.Networking().CreateNetworkVariable(OxygenNetworkId, Pack(startDevice, startFlow));
            oxygen.OnInitialized += OnOxygenChanged;
            oxygen.OnValueChanged += OnOxygenChanged;

            bloodGas = VirooApi.Instance.Networking().CreateNetworkVariable(BloodGasNetworkId, 0);
            bloodGas.OnInitialized += OnBloodGasChanged;
            bloodGas.OnValueChanged += OnBloodGasChanged;

            drugs = VirooApi.Instance.Networking().CreateNetworkVariable(DrugsNetworkId, 0);
            drugs.OnInitialized += OnDrugsChanged;
            drugs.OnValueChanged += OnDrugsChanged;

            course = VirooApi.Instance.Networking().CreateNetworkVariable(CourseNetworkId, 0);
            course.OnInitialized += OnCourseChanged;
            course.OnValueChanged += OnCourseChanged;

            paused = VirooApi.Instance.Networking().CreateNetworkVariable(PausedNetworkId, false);
            paused.OnInitialized += OnPausedChanged;
            paused.OnValueChanged += OnPausedChanged;
        }

        private void OnPausedChanged(object sender, bool value) => Changed?.Invoke(this, EventArgs.Empty);

        public void SetPaused(bool value)
        {
            if (paused != null && paused.Value != value)
            {
                paused.Value = value;
            }
        }

        private float StageLimit(ClinicalCourse stage) => stage switch
        {
            ClinicalCourse.Stable when deteriorateIfUntreated => stableSeconds,
            ClinicalCourse.Worsening when deteriorateIfUntreated => worseningSeconds,
            ClinicalCourse.Severe when deteriorateIfUntreated => severeSeconds,
            ClinicalCourse.Recovering => treatmentOnsetSeconds + recoverySeconds,
            _ => float.PositiveInfinity,
        };

        // Changing O2 nudges the SpO2 target (e.g. +1 on a non-rebreather) - quickly.
        private void OnOxygenChanged(object sender, int value)
        {
            BeginTransition(3f);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void OnDrugsChanged(object sender, int value) => Changed?.Invoke(this, EventArgs.Empty);

        private void OnCourseChanged(object sender, int value)
        {
            ClinicalCourse next = Course;
            if (next != shownCourse)
            {
                courseEnteredAt = Time.time;
                advanceRequested = false;

                switch (next)
                {
                    case ClinicalCourse.Recovering:
                        BeginTransition(recoverySeconds, treatmentOnsetSeconds, shownCourse);
                        break;
                    case ClinicalCourse.Arrest:
                        BeginTransition(1.5f, 0f, shownCourse);
                        break;
                    case ClinicalCourse.Recovered:
                        break; // same target as Recovering - keep gliding
                    case ClinicalCourse.Stable:
                        BeginTransition(0.01f, 0f, shownCourse); // a reset - snap back
                        break;
                    default:
                        BeginTransition(10f, 0f, shownCourse);
                        break;
                }

                shownCourse = next;
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }

        // Every client times the draw locally (for its progress ring); only the leader writes the
        // result, so it's written once however many clients are connected.
        private void OnBloodGasChanged(object sender, int value)
        {
            if (BloodGas == BloodGasStage.Drawing && drawRoutine == null)
            {
                drawStartedAt = Time.time;
                drawRoutine = StartCoroutine(CompleteDrawAfterDelay());
            }
            else if (BloodGas != BloodGasStage.Drawing && drawRoutine != null)
            {
                StopCoroutine(drawRoutine);
                drawRoutine = null;
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }

        private IEnumerator CompleteDrawAfterDelay()
        {
            yield return new WaitForSeconds(bloodGasSeconds);
            drawRoutine = null;

            VirooApi.Instance.Networking().ConditionalRunner.RunIfLeader(() =>
            {
                if (bloodGas == null || BloodGas != BloodGasStage.Drawing)
                {
                    return;
                }

                bloodGas.Value = ((int)BloodGasStage.Resulted * 1000) + BloodGasFiO2Percent;

                if (!string.IsNullOrEmpty(resultPointId))
                {
                    InteractionPointManager.Instance?.Show(resultPointId);
                }
            });
        }

        /// <summary>Changes the O2 device, picking that device's default flow.</summary>
        public void SetDevice(OxygenDevice device)
        {
            if (oxygen != null && device != Device)
            {
                oxygen.Value = Pack(device, DefaultFlow(device));
            }
        }

        /// <summary>Turns the flowmeter by <paramref name="delta"/> L/min, within the device's range.</summary>
        public void ChangeFlow(int delta)
        {
            (int min, int max) = FlowRange(Device);
            int flow = Mathf.Clamp(Flow + delta, min, max);
            if (oxygen != null && flow != Flow)
            {
                oxygen.Value = Pack(Device, flow);
            }
        }

        public void DrawBloodGas()
        {
            if (bloodGas != null && BloodGas == BloodGasStage.NotTaken)
            {
                bloodGas.Value = ((int)BloodGasStage.Drawing * 1000) + Mathf.RoundToInt(FiO2(Device, Flow) * 100f);
            }
        }

        /// <summary>Gives an IV drug once. Methylene blue starts recovery; the others change nothing.</summary>
        public void GiveDrug(IvDrug drug)
        {
            if (drugs == null || course == null || IsArrest || (DrugsGiven & drug) != 0)
            {
                return;
            }

            drugs.Value = (int)(DrugsGiven | drug);

            if (drug == IvDrug.MethyleneBlue && Course <= ClinicalCourse.Severe)
            {
                course.Value = (int)ClinicalCourse.Recovering;
            }
        }

        /// <summary>Instructor shortcut: jump to the next deterioration stage now.</summary>
        [ContextMenu("Advance deterioration (play mode)")]
        public void AdvanceDeterioration()
        {
            if (course != null && Course < ClinicalCourse.Arrest)
            {
                course.Value = (int)Next(Course);
            }
        }

        [ContextMenu("Reset scenario (play mode)")]
        public void ResetScenario()
        {
            if (oxygen != null)
            {
                oxygen.Value = Pack(startDevice, startFlow);
            }

            if (bloodGas != null)
            {
                bloodGas.Value = 0;
            }

            if (drugs != null)
            {
                drugs.Value = 0;
            }

            if (course != null)
            {
                course.Value = (int)ClinicalCourse.Stable;
            }

            InteractionPointManager.Instance?.Close();
        }

        private static ClinicalCourse Next(ClinicalCourse current) => current switch
        {
            ClinicalCourse.Stable => ClinicalCourse.Worsening,
            ClinicalCourse.Worsening => ClinicalCourse.Severe,
            ClinicalCourse.Severe => ClinicalCourse.Arrest,
            ClinicalCourse.Recovering => ClinicalCourse.Recovered,
            _ => current,
        };

        // Target vitals for a course stage, as (HR, systolic, diastolic, RR) + SpO2.
        private (Vector4 Vitals, float SpO2) Target(ClinicalCourse stage)
        {
            int o2Bonus = Device == OxygenDevice.NonRebreather ? spo2OnNonRebreather - spo2 : 0;
            return stage switch
            {
                ClinicalCourse.Worsening => (V(worsening), worsening.SpO2 + o2Bonus),
                ClinicalCourse.Severe => (V(severe), severe.SpO2 + o2Bonus),
                ClinicalCourse.Arrest => (Vector4.zero, 0f),
                ClinicalCourse.Recovering or ClinicalCourse.Recovered => (V(recovered), recovered.SpO2),
                _ => (new Vector4(heartRate, systolic, diastolic, respiratoryRate), spo2 + o2Bonus),
            };

            static Vector4 V(VitalSigns v) => new(v.HeartRate, v.Systolic, v.Diastolic, v.RespiratoryRate);
        }

        // Starts gliding from wherever the display is now (as seen under 'previous' stage).
        private void BeginTransition(float seconds, float delay = 0f, ClinicalCourse? previous = null)
        {
            ClinicalCourse from = previous ?? Course;
            float t = TransitionT();
            (Vector4 fromTarget, float fromSpO2) = Target(from);
            vitalsFrom = Vector4.Lerp(vitalsFrom, fromTarget, t);
            spo2From = Mathf.Lerp(spo2From, fromSpO2, t);
            transitionStart = Time.time + delay;
            transitionSeconds = Mathf.Max(0.01f, seconds);
        }

        private float TransitionT() => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Time.time - transitionStart) / transitionSeconds));

        private Vector4 Current() => Vector4.Lerp(vitalsFrom, Target(Course).Vitals, TransitionT());

        private float CurrentSpO2() => Mathf.Lerp(spo2From, Target(Course).SpO2, TransitionT());

        /// <summary>Usable flow range (L/min) for each device.</summary>
        public static (int Min, int Max) FlowRange(OxygenDevice device) => device switch
        {
            OxygenDevice.NasalCannula => (1, 6),
            OxygenDevice.SimpleMask => (5, 10),
            _ => (10, 15),
        };

        public static int DefaultFlow(OxygenDevice device) => device switch
        {
            OxygenDevice.NasalCannula => 2,
            OxygenDevice.SimpleMask => 6,
            _ => 15,
        };

        /// <summary>Rule-of-thumb delivered FiO2 (0..1) for a device and flow.</summary>
        public static float FiO2(OxygenDevice device, int flow) => device switch
        {
            OxygenDevice.NasalCannula => Mathf.Min(0.21f + (0.04f * flow), 0.45f),
            OxygenDevice.SimpleMask => Mathf.Clamp(0.40f + (0.05f * (flow - 6)), 0.35f, 0.60f),
            _ => Mathf.Clamp(0.60f + (0.06f * (flow - 10)), 0.60f, 0.90f),
        };

        /// <summary>PaO2 (mmHg) via the alveolar gas equation, for the FiO2 the sample was drawn on.</summary>
        public int PaO2ForFiO2(float fio2) => Mathf.RoundToInt((fio2 * 713f) - (paCO2 / 0.8f) - aaGradient);

        public static string DeviceName(OxygenDevice device) => device switch
        {
            OxygenDevice.NasalCannula => "Ρινικός καθετήρας",
            OxygenDevice.SimpleMask => "Απλή μάσκα",
            _ => "Μάσκα με ασκό (NRB)",
        };

        public static string DrugName(IvDrug drug) => drug switch
        {
            IvDrug.MethyleneBlue => "Methylene blue 1 mg/kg IV",
            IvDrug.Naloxone => "Naloxone 0,4 mg IV",
            IvDrug.Flumazenil => "Flumazenil 0,2 mg IV",
            IvDrug.Hydrocortisone => "Υδροκορτιζόνη 200 mg IV",
            IvDrug.Atropine => "Ατροπίνη 0,5 mg IV",
            _ => "",
        };

        public static string DeviceShortName(OxygenDevice device) => device switch
        {
            OxygenDevice.NasalCannula => "NC",
            OxygenDevice.SimpleMask => "MASK",
            _ => "NRB",
        };

        private static int Pack(OxygenDevice device, int flow) => ((int)device * 100) + flow;
    }
}
