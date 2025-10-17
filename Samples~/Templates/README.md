# Basic Scene Setup (Sample)

This sample is imported to `Assets/Samples/Dedicated Server Multiplayer Sample/<version>/Basic Scene Setup/`.

Contents:
- `Scenes/bootStrap.unity` – creates `NetworkManager`, `ClientSingleton`, and (for server builds) `ServerSingleton` at runtime.
- `Scenes/loading.unity` – demonstrates hooking into `LoadingScene` with `LoadingSceneSampleTask`.
- `Scenes/menu.unity` – basic menu scene ready to hook up to your matchmaking UI.
- `Scenes/game.unity` – contains the `RockPaperScissorsGame` network behaviour and session controller.
- `Scripts/Client` & `Scripts/Shared` – sample behaviours referenced by the scenes.
- `Configurations/` – deployment templates for Matchmaker & Multiplay.

After import:
1. Add the scenes to **Build Settings** (bootStrap → loading → menu → game).
2. Customise the menu UI and gameplay elements to fit your project.
3. Edit the configuration templates under `Configurations/` to match your project (queue, map, rank rules, etc.).
