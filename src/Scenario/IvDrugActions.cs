#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using VirooCaveTemplate.Interactions.Visuals;
using VirooCaveTemplate.Networking;

namespace VirooCaveTemplate.Scenario
{
    /// <summary>
    /// Point G (IV line): a drug menu. Each drug can be given once and is logged in the popup
    /// with the patient's response. Methylene blue is the antidote and starts recovery (see
    /// <see cref="ScenarioState"/>); the other drugs - the crash-cart options from the scenario
    /// draft - change nothing, so a wrong choice never harms the patient.
    /// </summary>
    [RequireComponent(typeof(InteractionPoint))]
    public class IvDrugActions : MonoBehaviour, IPointActionProvider
    {
        private static readonly IvDrug[] Menu =
        {
            IvDrug.MethyleneBlue, IvDrug.Naloxone, IvDrug.Flumazenil,
            IvDrug.Hydrocortisone, IvDrug.Atropine,
        };

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
                IvDrug given = state.DrugsGiven;
                visual.SetStatus((given & IvDrug.MethyleneBlue) != 0 ? "METHYLENE BLUE"
                    : given != IvDrug.None ? $"IV · {Count(given)}" : "");
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

            var body = new StringBuilder(staticBody);
            IvDrug given = state.DrugsGiven;

            if (given != IvDrug.None)
            {
                body.Append("\n\n<b>Χορηγήθηκαν:</b>");
                foreach (IvDrug drug in Menu)
                {
                    if ((given & drug) != 0)
                    {
                        body.Append($"\n{ScenarioState.DrugName(drug)}  —  {Response(drug, state)}");
                    }
                }
            }

            if (state.IsArrest)
            {
                body.Append("\n\n<color=#FF5A4A><b>Καρδιακή ανακοπή.</b></color>");
            }

            return body.ToString();
        }

        public void GetActions(List<PointAction> actions)
        {
            ScenarioState? state = ScenarioState.Instance;
            if (state == null)
            {
                return;
            }

            for (int i = 0; i < Menu.Length; i++)
            {
                IvDrug drug = Menu[i];
                bool given = (state.DrugsGiven & drug) != 0;
                actions.Add(new PointAction(
                    ButtonLabel(drug),
                    () => state.GiveDrug(drug),
                    row: i < 3 ? 0 : 1,
                    interactable: state.IsReady && !state.IsArrest,
                    active: given));
            }
        }

        private static string Response(IvDrug drug, ScenarioState state)
        {
            if (drug != IvDrug.MethyleneBlue)
            {
                return "χωρίς μεταβολή";
            }

            return state.Course switch
            {
                ClinicalCourse.Recovered => "<color=#5CFF9E>βελτίωση οξυγόνωσης</color>",
                ClinicalCourse.Recovering => "σε εξέλιξη",
                _ => "",
            };
        }

        private static string ButtonLabel(IvDrug drug) => drug switch
        {
            IvDrug.MethyleneBlue => "Methylene blue\n1 mg/kg IV",
            IvDrug.Naloxone => "Naloxone\n0,4 mg IV",
            IvDrug.Flumazenil => "Flumazenil\n0,2 mg IV",
            IvDrug.Hydrocortisone => "Υδροκορτιζόνη\n200 mg IV",
            _ => "Ατροπίνη\n0,5 mg IV",
        };

        private static int Count(IvDrug drugs)
        {
            int n = 0;
            for (int bits = (int)drugs; bits != 0; bits &= bits - 1)
            {
                n++;
            }

            return n;
        }
    }
}
