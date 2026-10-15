# VIROO CAVE: networked points of interest and a clinical scenario

C# code for a hospital training scenario that runs in a four-wall CAVE (an immersive projection room) on Virtualware's VIROO platform. I built it at the Medical Physics & Digital Innovation Lab of Aristotle University of Thessaloniki.

[![Short clip of the scenario running in the CAVE](https://i.ytimg.com/vi/FzQWA8A3l2s/hqdefault.jpg)](https://youtube.com/shorts/FzQWA8A3l2s)

Short clip: https://youtube.com/shorts/FzQWA8A3l2s

## The scenario

A 32-year-old woman sits on a hospital bed with blue lips. She is calm and breathing easily, but her oxygen saturation sits around 85% and does not go up, whatever oxygen the trainees give her. The cause is methaemoglobinaemia from a drug she had during an exam earlier that day.

Trainees walk around her in the CAVE and click small markers on her face, hands and chest, the monitor, the oxygen flow meter, the IV line and the crash cart. Each marker opens an info popup or an action menu. They can change the oxygen device and flow, take a blood gas (the blood comes out brown) and give drugs through the IV. If nobody treats her, she gets worse on a timer: stable, worsening, severe, cardiac arrest. Only methylene blue starts her recovery.

Every wall runs its own VIROO player, all joined to the same session, and they all show the same thing: the same popup, the same hidden marker, the same numbers on the monitor.

The clinical scenario (room layout, interaction points and the case itself) was designed together with a student I supervise. I wrote the framework and the code in this repo.

## How it is built

The full write-up is in [`docs/Interactions-And-Networking.md`](docs/Interactions-And-Networking.md). It covers how VIROO picks a leader among the screens, how `NetworkVariable<T>` works, and the recipe for adding a new networked interactable.

`src/Networking/` has the reusable parts, which know nothing about this scenario:
- `InteractionPoint` and `InteractionPointManager`: the point-of-interest system. One shared `NetworkVariable<string>` holds which point is open. A click writes to it, and every screen's popup reacts to the change. A point hides itself while its popup is open, through `NetworkedVisibilityToggle` rather than `SetActive`, so it stays subscribed to network updates.
- `PointOfInterestPopupUI`: shows whichever point is open, with its text and any action buttons that an `IPointActionProvider` supplies.
- `NetworkedVisibilityToggle`: synced show and hide for any object.
- `NetworkedAnimatorSync`: keeps a looping animation, like the patient's breathing, in step on every screen.

`src/Interactions/` has small adapters and the XR selection plumbing. In VIROO's desktop mode, mouse clicks don't reach world-space UI through the `EventSystem`, so `PopupCloseOnSelect` and `PopupActionButton` are selected the same way as every other point, through a collider and an `XRSimpleInteractable`.

`src/Scenario/` is the methaemoglobinaemia case, built only on the layer above:
- `ScenarioState` holds everything the scenario knows (oxygen device and flow, blood gas stage, drugs given, clinical course), all of it networked.
- `OxygenActions`, `IvDrugActions`, `BloodGasActions` and `InstructorActions` are the interactive points, each an `IPointActionProvider`.
- `PatientMonitorDisplay` draws the ECG, pleth and respiration traces at runtime from the shared state, and `ScenarioOutcomeBanner` shows the result when the patient recovers or arrests.
- An instructor point inside the room lets someone restart, pause or skip stages with just a tracked wand.

## Tech

Unity 6000.0 (URP), XR Interaction Toolkit and OpenXR, the VIROO / Virtualware networking SDK, ART DTrack for the CAVE wands.

## What is not here

The working project also uses commercial art and UI packs (hospital room dressing, a modular environment kit and a sci-fi UI kit) whose licences don't allow redistribution. So this repo has the code and the docs only, not the Unity scene. You also need a VIROO licence to run it.

## Licence

The code in this repo is MIT licensed (see `LICENSE`). The VIROO SDK and the art packs are not included and keep their own licences.
