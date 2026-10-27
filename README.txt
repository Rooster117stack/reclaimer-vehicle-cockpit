VEHICLE COCKPIT + WARTHOG AUDIO - 0.4.0 PREVIEW
For Project Reclaimer / MCC Halo 3 1.3528.0.0 on Windows x64.
WARTHOG ENGINE + RADIO
Launch PLAY_FIRST_PERSON.cmd as usual. Keep the whole audio folder beside the EXE.
- F8: cockpit cameras on/off (your saved camera shortcut still works unless F9/F10).
- F9: radio on/off. It starts off.
- F10: next station, cycling through the four supplied songs.
- Untick "Warthog engine recording" in the helper to restore Halo's engine sound.

The engine uses six crossfaded loops cut only from the first rev sweep in the
supplied recording. Native Warthog engine speed selects the sound bands; road
speed does not stand in for RPM. Engine power gates playback, so an empty,
switched-off Warthog stays silent. The recording starts already moving; its idle
loop is a lowered-pitch approximation. This changes sound, not the transmission.

The radio follows your seated Warthog, otherwise the nearest supported Warthog.
It is loudest in any seat, quieter outside, and silent beyond about 12 metres.
Each song is a looping station; leaving range or switching off pauses it.
Only your PC hears it. Friends do not need this for your audio to work.
There is no shared server radio state, wall occlusion, or directional stereo pan.

Stock and the installed HugeGrass Warthog chassis are recognized. Future maps
need a readable matching cache and the same supported Warthog sound layout.
Unknown layouts retain original engine sounds; the camera feature is independent.
The native engine function is shared across Warthog variants: while replacement
is active it is muted for those variants, and up to eight nearby running engines
are rendered locally within about 60 metres. Tire, collision and turret sounds
and non-Warthog cars are preserved. A future engine DLL change still needs review.

Close the helper normally to restore camera and engine-sound data. If it is force
closed, restart Reclaimer. No MCC/Reclaimer game files are changed. This preview
includes the supplied music. Download the complete ZIP from the v0.4.0 GitHub release.

FIRST CHECK
Enter a Warthog, accelerate, coast and reverse. Compare the engine checkbox off/on.
Press F9, then F10; exit the vehicle and walk away to check the radio fade.
The driving/listening check remains pending; see TEST STATUS below.

PLAY
1. Extract the entire ZIP into a writable folder. Install your own MCC with Halo 3.
2. Double-click PLAY_FIRST_PERSON.cmd. It checks the latest official stable
   Reclaimer release, downloads it if needed and verifies SHA-256 checksums.
   If the lookup is unavailable, it uses the last verified cached client. An
   already-open Reclaimer client is reused without updating or restarting it.
3. Leave Vehicle Cockpit open, join a Reclaimer game normally and enter a vehicle.
   Wait for ON. Exit and re-enter the seat if a camera toggle needs to take effect.
4. Press F8 to switch cameras on/off. Untick the first-person checkbox or close
   the helper normally to restore cameras.

The launcher and WarthogCamera.exe retain their old filenames for compatibility.
If Reclaimer is already running, WarthogCamera.exe can also run by itself.
Friends need this entire ZIP and their own MCC installation. No host-side camera
mod is required; this affects only the client running the helper.

TOGGLE KEY
F8 is the default. To change it, click the box beside "Toggle key:", press your
shortcut, then click "Save key". Settings are saved in camera-settings.ini beside
the helper. Use F1-F11 except F9/F10, or a modified shortcut; F9/F10 control the radio and F12 is reserved.
The shortcut acts only while the game or helper is focused. Some seats need an
exit and re-entry before a changed camera takes effect. Leave the helper open.

CAMERAS
The helper scans loaded vehicle seats, preferring existing configured or dedicated
cockpit camera markers. It covers many stock and modded vehicle types, including
HugeGrass vehicles, rather than relying on a maintained vehicle-name list.
Boarding, AI-only and cargo seats are excluded; existing first-person seats stay.
The second checkbox enables approximate head cameras where no usable cockpit
marker exists. It clones the seat attachment's marker records and raises their
Z position by 0.25 in the original node's coordinate system. Original attachment
markers are preserved, so this does not reposition occupants. Untick that checkbox
for existing native camera markers only. The Warthog gunner adjustment is retained.
The HugeGrass Sabre has a targeted pilot-head correction: its named driver camera
is actually anchored far behind the aircraft. This correction stays enabled with
the main first-person checkbox even when generic approximate cameras are off.
Approximate views are not individually tuned. Model scale, rotated nodes, enclosed
hulls or missing interior surfaces can cause clipping or poor visibility. Some
seats lack usable markers and are skipped. This cannot make every vehicle a
finished cockpit, or make an AI-only vehicle driveable. No manual gearbox is added.

NEW MAPS AND IMPORTED VEHICLES
New maps are scanned automatically. Configured camera IDs can be matched directly
against a loaded render model even when map strings are unavailable. Approximate
clones can also work when a valid unused marker ID and seat attachment are available.
Names used to find additional semantic cameras require the matching readable cache
from stock MCC, an installed Steam Workshop library or Reclaimer's standard Mods
folders. Empty virtual placeholders are skipped. Tag-table changes trigger a
restore and rebuild so newly imported vehicles can be discovered.

COMPATIBILITY AND REMOVAL
There is no Reclaimer client-version pin. Ordinary future Reclaimer updates can
work automatically while the Halo 3 engine remains compatible. The exact Halo 3
1.3528.0.0 engine hash and loaded data layouts are still checked before writes.
If a future engine or layout changes, the helper stops safely and may need an
update. Automatic downloads cannot guarantee compatibility with every future
engine change. This targets Reclaimer, not Steam MCC matchmaking, and
is shared separately from Reclaimer's automatic map/mod downloads.
Writes are limited to loaded camera data and guarded Warthog engine-sound object functions, with verified readback and restoration.
Approximate cameras use read/write data allocations only: no executable-page patch,
DLL injection, input injection, physics edits or anti-cheat bypass is included.
Normal close restores matching original fields. Allocations with uncertain live
references stay until game exit. If force-killed, restart the game to clear changes.
To uninstall, close the helper and delete its folder. Older packages are preserved.

TEST STATUS
0.3.2: In a local HugeGrass Rebalance match, comparison against 0.3.1 confirmed only
the Sabre driver's camera reference changed and one pilot-head marker was added.
All 439 other seat definitions and unrelated marker groups matched. Original
vehicle fields/markers stayed intact; F8 restored all original data exactly.
The Sabre correction also passed with generic approximate cameras disabled.
This verifies live camera data; cockpit framing and sustained flight remain for
an in-seat check. Exit and re-enter the Sabre after enabling the helper.
0.3.1 fixes the Windows PowerShell "path is not of a legal form" error when
refreshing the launcher's existing verified release cache. Camera behavior is
unchanged from 0.3.0.
0.3.0: Live F8 OFF/ON and changing/saving F9 worked. Independent camera restoration
checks passed. All 12 launcher tests passed, including verified offline fallback
and rejection of invalid release metadata and a tampered cached executable copy.
Historical 0.2.0 coverage: HugeGrass Rebalance exposed 214 usable seats, including
91 already first person and 47 approximate cameras; two seats were skipped. All
260 changed fields passed independent apply/restore checks, with native vehicle
fields and 224 protected seats preserved.
Individual viewpoints and every public server/mod have not been visually tested.

SOURCE AND CREDITS
MIT license: LICENSE.txt. Build: build.cmd; sources: src\WarthogCamera.cs and
src\SeatCameraData.cs, src\WarthogAudio.cs, src\VehicleAudioTelemetry.cs and src\WarthogEngineMute.cs. Created with OpenAI Codex assistance; no game assets included. Supplied audio is separate from the code license.
Layouts: https://github.com/XboxChaos/Assembly | https://projectreclaimer.dev/
Official releases: https://github.com/ProjectReclaimer/project-reclaimer-releases/releases/latest
Independent of, and not endorsed by, the Project Reclaimer developers.
