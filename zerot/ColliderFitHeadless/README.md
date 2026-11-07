# ColliderFit Headless

A BepInEx plugin (no scene plugin to add) that keeps the fixed colliders on every **female Person** snug to the skin when the body morphs change.
It is an independent re-implementation of the idea of **mutoe's ColliderFit** (CC BY, https://hub.virtamate.com/members/mutoe.99301/); none of
mutoe's code is used, and nothing of mutoe.ColliderFit / mutoe.OverlayUI has to be installed.

## What it does

VaM rescales its AutoColliders (torso, breasts, belly, hips, thighs) with the body morphs, but the fixed capsules on the upper arms, forearms, hands,
palms, fingers, feet, toes, neck and jaw (and, with *Use Advanced Colliders* off, the standard torso / thigh / calf / hip ones) keep the size stored in
the character prefab. A slimmed forearm still collides as if it were thick.

For every female atom, a couple of seconds after the scene settles:

1. the colliders to manage are collected (the set that is active: advanced or standard) and every skin vertex is handed to the nearest collider;
2. the skin vertex positions are computed from the non-pose morphs (base mesh + sum of `delta * appliedValue`);
3. each collider's radius and centre are fitted to the skin (16-bin cross-section; skin on one side only slides the collider along the normal);
4. the GPU colliders hair and cloth use are updated, and rigidbody centres of mass that Unity moved are pulled back.

It refits about 0.4 s after the morphs stop changing. A fit that would change a radius by more than 50% or move a centre by more than 15 mm is not written.
Switching the plugin off (or removing the atom / leaving the scene) puts every value back.

## Working with Collider Editor

| Case | Behaviour |
|---|---|
| **Collider Editor (Headless)** (`ColliderEditor.Headless.dll`) | Its loaded preset is read (by reflection, guid `acidbubbles.collidereditor.headless`). Colliders in it are left alone, and handed back / taken over by themselves when the preset or its *Enabled* switch changes. |
| **Collider Editor scene plugin** on a Person | The colliders listed in its `editables` are left alone; a collider reset there is taken over again. |
| **mutoe.ColliderFit scene plugin** on a Person | That atom is skipped entirely (`Targets.SkipAtomsWithColliderFitPlugin`), so the two never fight. |
| Any other writer | A collider whose values no longer match what this plugin wrote is marked *changed by others* and left alone. |

The built-in preset of Collider Editor (Headless) only covers breast and tongue colliders, none of which are managed here.

## Window

Same window as the other zerot plugins (drag, resize, S- / S+, collapse, On / Off, DPI aware; config section `Window`).
Shows the female atoms, Collider Editor status, per-atom counts by body part and exceptions. Buttons: **Fit now**, **Rebind all**, **Log details**,
the margin stepper (+/- 0.5 mm), and **Skip this atom** / **Fit this atom** (writes `Targets.ExcludedAtoms`).

The **HUD Dock** lists it as *ColliderFit* (show / collapse / On-Off through `General.Active`).

## Config

`Fit.MarginMm` (-10..10), `Targets.ExcludedAtoms`, `Targets.IncludeMales` (off: only the female body was tested by the original),
`Targets.SkipAtomsWithColliderFitPlugin`, `Timing.ScanInterval / SettleSeconds / QuietSeconds`, `General.LogEvents`, `General.Active`.

## Limits

Only the fixed colliders are managed and only for the non-pose body morphs: skin bulging as the character moves, and clothing, are not followed.
Not run in VaM yet: tested in the VamSim simulator (3 scenarios: fit / morph / margin / foreign change / switch-off, Collider Editor preset hand-over,
scene plugin skip) and against the real `Assembly-CSharp.dll` at compile time.
