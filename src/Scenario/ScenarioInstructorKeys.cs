#nullable enable
using UnityEngine;
using UnityEngine.InputSystem;

namespace VirooCaveTemplate.Scenario
{
    /// <summary>
    /// Keyboard shortcuts for whoever runs the session (desktop, or the CAVE operator's PC):
    /// reset the scenario, and fast-forward the untreated deterioration to the next stage - so a
    /// demo can show both outcomes without waiting out the timers. Both write networked state, so
    /// every screen follows. Keys are chosen not to clash with VIROO's desktop bindings.
    /// </summary>
    public class ScenarioInstructorKeys : MonoBehaviour
    {
        [SerializeField] private Key resetKey = Key.F9;
        [SerializeField] private Key advanceKey = Key.F10;

        protected void Update()
        {
            Keyboard? keyboard = Keyboard.current;
            ScenarioState? state = ScenarioState.Instance;
            if (keyboard == null || state == null)
            {
                return;
            }

            if (keyboard[resetKey].wasPressedThisFrame)
            {
                state.ResetScenario();
            }
            else if (keyboard[advanceKey].wasPressedThisFrame)
            {
                state.AdvanceDeterioration();
            }
        }
    }
}
