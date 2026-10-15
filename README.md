# VIROO CAVE — Networked Point-of-Interest & Clinical Scenario Framework

A C# framework built for a multi-wall **CAVE** (4-screen immersive room) running on the
**VIROO** (Virtualware) platform, developed for a hospital staff training simulation around
**methaemoglobinaemia** — a condition where a patient's oxygen saturation stays stubbornly low
no matter how much oxygen is given, until the trainee identifies the cause and administers the
right antidote.

This repo contains the **code and architecture only** — the Unity scene, 3D models, and UI art
assets are licensed third-party content and are not included. See
[Why no scene/assets](#why-no-sceneassets) below.

## What it does

Every CAVE wall runs its own instance of the VIROO Player, all joined to the same session. A
trainee walks up to the patient and clicks small markers ("points of interest") placed around the
room — the patient's face, hands, chest, the monitor, the oxygen flow meter, the IV line, the
crash cart — each opening an info popup or an action menu. Whatever one trainee does, every
connected screen shows the same thing, instantly: the same popup opens, the same marker hides,
the same monitor numbers update.

Underneath that, a full networked clinical state machine drives the scenario: the patient's
vitals glide between stages (stable → worsening → severe → arrest) on a timer unless treated,
changing the oxygen device never fixes the saturation (the scenario's central paradox), and only
the correct antidote — delivered through an interactive IV drug menu — starts recovery. A
live-rendered bedside monitor draws real ECG/pleth/respiration traces from that shared state, an
in-scene instructor control point lets someone run the session from inside the CAVE with just a
tracked wand, and an outcome banner closes the loop when the patient recovers or arrests.

## Architecture

The full write-up is in [`docs/Interactions-And-Networking.md`](docs/Interactions-And-Networking.md):
how VIROO's leader-election and `NetworkVariable<T>` model works, the recipe for any new networked
interactable, and the reasoning behind each generic building block below.

**`src/Networking/`** — reusable sync primitives, independent of this specific scenario:
- `InteractionPoint` / `InteractionPointManager` — the point-of-interest system. One shared
  `NetworkVariable<string>` holds which point is open; clicking a point writes to it, every
  client's popup reacts the same way. Points hide themselves (via `NetworkedVisibilityToggle`,
  not `SetActive`, so they stay subscribed to network updates) while their own popup is open.
- `PointOfInterestPopupUI` — renders whichever point is active, with its content and any action
  buttons supplied by an `IPointActionProvider`.
- `NetworkedVisibilityToggle` — generic synced show/hide for any object.
- `NetworkedAnimatorSync` — keeps a looping Animator (e.g. breathing) in phase across screens.

**`src/Interactions/`** — thin, specific adapters and XR-selection plumbing, including the
workaround for VIROO's desktop mode not routing mouse clicks through `EventSystem` to world-space
UI (`PopupCloseOnSelect`, `PopupActionButton` — selected via collider + `XRSimpleInteractable`,
the same way every other point is selected, instead of relying on Unity's UI event system).

**`src/Scenario/`** — the methaemoglobinaemia case itself, built entirely on the generic layer
above: `ScenarioState` (the single source of truth — oxygen device/flow, blood gas stage, drugs
given, clinical course, all networked), `OxygenActions` / `IvDrugActions` / `BloodGasActions` /
`InstructorActions` (the interactive points, each an `IPointActionProvider`), and
`PatientMonitorDisplay` / `ScenarioOutcomeBanner` (runtime-built world-space displays driven
purely by the networked state, so every screen agrees).

## Tech stack

Unity 6000.0 (URP) · XR Interaction Toolkit + OpenXR · VIROO / Virtualware networking SDK ·
DTrack tracking for CAVE wands.

## Why no scene/assets

The working Unity project also includes third-party art and UI packages (a hospital room dressing
pack, a modular environment kit, and a sci-fi UI kit) under their own commercial licenses, which
don't permit redistribution. Rather than ship a project with missing or broken references, this
repo is the code and architecture on their own — everything here is original work.
