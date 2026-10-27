# Vehicle Cockpit + Warthog Audio for Project Reclaimer

Client-local first-person vehicle cameras for Halo 3 in Project Reclaimer, plus a Warthog engine recording driven by native engine speed and a nearby radio with four songs. Driver, passenger and turret cameras work where the vehicle's camera data permits them.

**[Download v0.4.2](https://github.com/Rooster117stack/reclaimer-vehicle-cockpit/releases/tag/v0.4.2)** — choose **VehicleCockpitWarthogAudio-0.4.2.zip** under Assets. The ZIP includes all engine loops and radio tracks. The owner confirmed the corrected build working in game on **2026-10-27**.

The earlier [v0.4.0 preview](https://github.com/Rooster117stack/reclaimer-vehicle-cockpit/releases/tag/v0.4.0) and [v0.3.2 camera-only release](https://github.com/Rooster117stack/reclaimer-vehicle-cockpit/releases/tag/v0.3.2) remain available unchanged.

## Changes in 0.4.2

- Removes the repeated short rev-up/reset: steady source-derived loops now change pitch continuously with native engine RPM.
- Fixes audio staying on Halo's default engine sound when the helper starts before joining a match.
- Preserves the existing cockpit cameras and radio. The helper window title shows **0.4.2**.

## Play

Requires Windows x64 and your own MCC installation with Halo 3. Supported Halo 3 engine: **1.3528.0.0**; tested Reclaimer: **0.9.23**.

1. Extract the entire release ZIP into a writable folder, including `audio/`.
2. Double-click **PLAY_FIRST_PERSON.cmd**. It verifies and downloads the latest official stable Reclaimer, or reuses an already-running client. It can fall back to a verified cached client if the release lookup fails.
3. Leave the helper open, join normally and enter a Warthog.
4. **F8** toggles cockpit cameras, **F9** toggles the radio and **F10** changes station. Radio starts off. Shortcuts act while the game or helper is focused.

Untick **Warthog engine recording** to restore Halo's original engine sound. Close the helper normally to restore camera and sound changes. If it is force-killed, restart Reclaimer. To uninstall, close it and delete its folder.

Friends need the complete ZIP and their own game. No host-side mod is required for these local effects. The legacy filename `WarthogCamera.exe` is retained; it can run directly when Reclaimer is already open.

## Engine and radio

Six steady sound textures use only the first rev sweep of the supplied recording; its repeated second sweep is excluded. The captured rise in pitch and volume is removed before looping, then playback pitch continuously follows native Warthog engine speed. Genuine automatic gear shifts can still lower RPM during acceleration. Native engine power gates playback. It changes audio, not vehicle physics or gearing. The recording begins with the car already moving, so idle is a lowered-pitch approximation, and cabin/road noise remains in the samples.

The radio follows your seated Warthog, otherwise the nearest supported Warthog. It is louder in any seat and fades to silence at approximately **12 metres** outside. Each of the four songs is a looping station; switching off or leaving range pauses it. Only the person running the helper hears it. There is no synchronized server radio state, wall occlusion or directional stereo pan.

Supported Warthogs use a guarded engine-only sound-function edit. Shared sound assets used by civilian cars are preserved. Native sound functions can be shared across Warthog variants, so up to eight nearby running Warthog engines are rendered locally within approximately 60 metres while replacement is active. Tire, collision and turret sounds remain native.

## Cameras and compatibility

Loaded vehicle seats are scanned automatically, including on new maps. Native cockpit markers are preferred; optional approximate head cameras use suitable seat markers. Boarding, AI-only and cargo seats are excluded. The HugeGrass Sabre pilot correction from v0.3.2 remains included. Model geometry can cause clipping, missing interiors or unusable camera positions; every vehicle has not been visually tested.

Stock and the installed HugeGrass Warthog chassis were recognized. Audio on future maps requires a readable matching cache and compatible Warthog sound data. Unknown sound layouts retain the original engine sound. Split-screen audio is not supported.

The launcher follows official stable Reclaimer updates, but the exact Halo 3 engine hash and layouts remain guarded. A future engine change can require a helper update. This targets **Project Reclaimer**, not Steam MCC matchmaking, and is distributed separately from Reclaimer's automatic map downloads. No game files, executable-page patches, DLL injection, anti-cheat bypass or manual gearbox are included.

## Verification

The audio build passed 86 checks covering silent real-device playback, four stations, looping, pause/resume/next, distance gains and multiple-engine blending. Live stationary-player/Warthog telemetry passed. Native engine analysis established the RPM and engine-mute paths. Closing the helper restored all 20 tested vehicle definitions/cameras and the Warthog engine-sound function exactly. Cache checks covered stock Valhalla and HugeGrass.

The engine-loop repair passed **34 rendered-audio regression checks**, and the startup-detection fix passed **7 Windows thread-cache regression checks**. The owner then confirmed the corrected **0.4.2 build working in game**. This confirmation does not establish compatibility with every public server, map or custom vehicle.

If audio remains original, check that the helper title shows **0.4.2** and that **Warthog engine recording** is enabled. In a supported Warthog its status should say **Engine: recording / native RPM ...**. Keep the whole `audio/` folder alongside the executable. Logs are in `logs/` beside the helper.

## Source and credits

Run `build.cmd` on Windows to use the .NET Framework x64 C# compiler. Code is in `src/`; the build updates the helper checksum in `manifest.json`. Large WAV assets are distributed in the release ZIP rather than Git. For an audio-enabled source build, copy its `audio/` folder beside the compiled executable. GitHub's automatically generated source archives do not include those WAV files.

Code is [MIT licensed](LICENSE.txt). Supplied audio remains subject to its respective owners' rights; the code license does not grant redistribution rights to the recordings. Created with **OpenAI Codex assistance**. References: [XboxChaos/Assembly](https://github.com/XboxChaos/Assembly) and [Project Reclaimer](https://projectreclaimer.dev/). Independent of and not endorsed by Project Reclaimer's developers. See [README.txt](README.txt) for detailed behavior and earlier camera test results.
