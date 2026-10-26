# Vehicle Cockpit for Project Reclaimer

A client-local first-person vehicle camera helper for Halo 3 in Project Reclaimer, including many stock and HugeGrass vehicles. Driver, passenger and turret seats are handled when their camera data permits it.

**[Download v0.3.2](https://github.com/Rooster117stack/reclaimer-vehicle-cockpit/releases/tag/v0.3.2)** - choose `VehicleCockpitPersonal-0.3.2.zip` under Assets.

## Play

Requires Windows x64 and your own MCC installation with Halo 3. The supported Halo 3 engine is **1.3528.0.0**; v0.3.2 was tested against Reclaimer **0.9.23**.

1. Extract the entire release ZIP into a writable folder.
2. Double-click **PLAY_FIRST_PERSON.cmd**. It downloads and checksum-verifies the latest official stable Reclaimer, or reuses an already-running client. A verified cached client is available if the release lookup fails.
3. Leave the helper open, join a Reclaimer game normally, and enter a vehicle. Wait for **ON**.
4. Press **F8** to toggle the cameras. To change the shortcut, click **Toggle key**, press your shortcut, then **Save key**. The game or helper must be focused. Some seats need an exit and re-entry after toggling.

Friends need the complete ZIP and their own game installation; no host-side camera mod is required. `WarthogCamera.exe` is the legacy filename and can also be started directly when Reclaimer is already running.

## Cameras and compatibility

The helper scans loaded vehicles automatically, including on new maps. It prefers native cockpit markers. The optional approximate-camera checkbox adds head-level cameras where usable seat markers exist. Boarding, AI-only and cargo seats are excluded; existing first-person seats stay native. Camera placement depends on the vehicle model: clipping, missing interiors or unusable markers can prevent a good view.

v0.3.2 corrects the **HugeGrass Sabre pilot camera**, whose original named camera sits behind the aircraft. This targeted correction also works with generic approximate cameras disabled. F8 or the main checkbox restores it along with the other camera changes.

Reclaimer client versions are not pinned. Future updates can work automatically while the verified Halo 3 engine and data layouts remain compatible; an engine/layout change may require a helper update. This is a separate helper for Reclaimer, shared independently of automatic map downloads. It does not target Steam MCC matchmaking. It changes loaded camera data, preserves vehicle physics, and adds no manual gearbox. No game assets are included.

## Test status

In a local HugeGrass Rebalance match, v0.3.2 changed only the Sabre driver's camera reference and added a pilot-head marker compared with v0.3.1. All **439 other seat definitions** matched, and F8 restored the original camera data exactly. The Sabre correction also passed with generic approximate cameras off. **This verifies live camera data; in-seat cockpit framing and sustained Sabre flight have not been visually validated.** Individual viewpoints and every public server/mod have not been tested.

## Removal and troubleshooting

Close the helper normally to restore its camera changes. If it is force-killed, restart the game. To uninstall, close it and delete its folder. Diagnostic logs are in `logs/`; shortcut settings are in `camera-settings.ini`. See [README.txt](README.txt) for detailed behavior and earlier test results.

## Source and credits

Run `build.cmd` on Windows to build the helper with the .NET Framework x64 C# compiler. Sources are in `src/`. The script also updates the helper checksum in `manifest.json`.

Code is provided under the [MIT license](LICENSE.txt). Created with **OpenAI Codex assistance**. Tag-layout references: [XboxChaos/Assembly](https://github.com/XboxChaos/Assembly). Requires [Project Reclaimer](https://projectreclaimer.dev/); independent of and not endorsed by its developers.
