#nullable enable
using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace VirooCaveTemplate.Interactions
{
    /// <summary>
    /// VIROO's desktop mouse/keyboard control scheme spawns its NearFarInteractors with
    /// far/ray casting disabled (near/direct-grab only) even though VR controllers and the
    /// DTrack Flystick have it enabled by default - confirmed by reading the live
    /// NearFarInteractor.enableFarCasting value on the runtime-spawned rig
    /// (DesktopControllers/*Controller Desktop/Near-FarInteractor: false).
    ///
    /// The desktop rig is instantiated at runtime from a package prefab, so there is no scene
    /// object to fix this on directly. This waits for any NearFarInteractor to appear and turns
    /// far casting on, so ray-select/interact-at-a-distance behaves the same in desktop testing
    /// as it will on the CAVE with the DART controller. Safe to enable on VR/DTrack interactors
    /// too - it's already true there, so this is a no-op for them.
    /// </summary>
    public class EnableFarCasting : MonoBehaviour
    {
        protected void OnEnable() => StartCoroutine(EnableFarCastingRoutine());

        private IEnumerator EnableFarCastingRoutine()
        {
            NearFarInteractor[] interactors;
            int framesWaited = 0;

            do
            {
                yield return null;
                interactors = FindObjectsByType<NearFarInteractor>(FindObjectsSortMode.None);
            }
            while (interactors.Length == 0 && ++framesWaited < 600); // ~10s at 60fps, then give up

            foreach (NearFarInteractor interactor in interactors)
            {
                interactor.enableFarCasting = true;
            }
        }
    }
}
