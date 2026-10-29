VEHICLE COCKPIT + WARTHOG AUDIO - 0.4.6
For Project Reclaimer / MCC Halo 3 1.3528.0.0 on Windows x64.

PLAY
Extract the whole ZIP and double-click PLAY_FIRST_PERSON.cmd. Leave the helper
open and join normally. Its title should show 0.4.6. Keep the supplied audio
folder beside WarthogCamera.exe; no separate music or engine files are needed.
An already-running Reclaimer client is reused. Otherwise the launcher checks the
latest official stable client and verifies its checksum before starting it.
The exact Halo 3 engine layout is checked independently; an unknown engine is
refused. Client updates do not automatically guarantee engine compatibility.

CONTROLS
F8: first-person cameras on/off. Your saved camera shortcut is retained.
F9: radio on/off (starts off). F10: next station among four supplied songs.
Change the camera shortcut in the helper's Toggle key box, then Save key.
F9/F10 are reserved for radio; F12 is unavailable.
Untick Warthog engine recording to hear the original engine sound.
Some seats need exit/re-entry after toggling cameras.

BOOST ZOOM FIX IN 0.4.6
- Prevents accidental binocular zoom while driving supported boost vehicles in
  first person. Uses the native boost capability and power fields, not vehicle
  names or map lists, so compatible new/modded vehicles are detected automatically.
- Applies to supported driver seats, including driver/gunners and native first-
  person drivers. Passenger, turret-only, boarding and nonplayer seats are excluded.
  Restores binocular settings on exit, F8 OFF, or normal helper close.
- Applies only with one local player. Split-screen is excluded. Boost effects,
  guns, sounds, physics and existing camera positions are retained. Ordinary
  binoculars are unavailable while driving these vehicles with cockpit mode ON.
- Vehicles that lack usable first-person camera data or native boost capability
  are skipped; custom script-only boost cannot be inferred from these fields.

GHOSTING UPDATE RETAINED FROM 0.4.4
- Completely bypasses the native motion-blur pass, in addition to the earlier
  blur amplitude reductions. All camera positions and audio are unchanged.
- Falcon and Wombat positions were accepted by the owner and are preserved.

CAMERA FIXES RETAINED FROM 0.4.3
- Falcon: moves its behind-pilot camera forward to the pilot's head.
- Wombat: replaces a distant chase anchor with a pilot-head camera.
- Gauss turret: positions the view directly behind the actual scope screen and
  attaches it to the moving gun. It uses the existing model/screen shader;
  there is no new thermal overlay or gameplay targeting behavior.
- Converted vehicle seats: removes speed-driven camera displacement so the
  viewpoint stays at its marker instead of pulling back through the occupant.
- Native motion blur is suppressed while cockpit cameras are enabled in a
  loaded match. This also affects the on-foot view. F8 OFF or normal helper
  close restores the original renderer values. No saved graphics setting changes.

The aircraft and Gauss corrections require matching native marker geometry and
skeletons. They work independently of the map name. Other vehicles retain the
existing authored-camera / approximate-head selection. Unknown custom models
may still clip or lack a usable interior. Boarding and AI-only seats are skipped.
This cannot add missing cockpit geometry or make an AI-only vehicle driveable.

AUDIO
The accepted 0.4.2 engine mixer, native RPM telemetry, six engine textures, and
four radio tracks are unchanged. Engine pitch follows native RPM, including its
automatic gear changes. Engine power gates sound. Tire, collision and turret
sounds are preserved. This package contains no manual transmission.
Radio follows the seated or nearest supported Warthog: loudest inside, quieter
outside, silent beyond about 12 metres. Only the user's PC hears it. Leaving
range or switching off pauses playback. No shared radio state or wall occlusion.

RESTORE / COMPATIBILITY
Close the helper normally to restore loaded camera/render/audio data. If force
closed, restart Reclaimer. No MCC/Reclaimer game files are modified.
Friends need this complete package and their own MCC installation. Host-side
camera changes are not required. New maps are scanned automatically, but need
compatible seat/model data and a readable matching cache for named profiles.
No anti-cheat bypass, executable code patch, or server physics change is included.

TEST STATUS
Live HugeGrass Rebalance application and F8 restoration passed: all vehicle
physics bytes and protected seats preserved, targeted profiles found, native
all five motion-blur values restored byte-for-byte. 29 blur regression checks
and the exact-engine native pass-gate check passed. Earlier geometry/rejection
checks passed; camera positions are byte-identical to accepted 0.4.3.
Falcon and Wombat framing and the stock Valhalla Warthog were accepted by the
owner. High-speed custom-map ghosting remains unresolved; Gauss screen rendering
is visually unconfirmed. Ghost boost: owner-driven High Ground test passed;
547 seated samples held 70-degree FOV with no binocular zoom. Exact binocular
settings restored on exit and on-foot 3x zoom worked again. Owner accepted both.
0.4.6 expands that accepted Ghost mechanism: 32 boost eligibility and exact
apply/restore/guard checks passed. Offline cache inspection found 4 boost vehicles
on stock Valhalla and 28 each on installed HugeGrass and HugeGrass Rebalance.
The broader vehicle coverage has not been driving-tested; owner feedback pending.
Download the complete v0.4.6 release ZIP for the supplied engine and radio audio.
Built with Codex assistance. MIT source license; supplied audio remains bundled.
