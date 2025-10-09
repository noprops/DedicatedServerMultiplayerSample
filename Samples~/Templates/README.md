# Basic Scene Setup (Sample)

This sample is imported to `Assets/Samples/Dedicated Server Multiplayer Sample/<version>/Basic Scene Setup/`.

Contents:
- `Scenes/bootStrap.unity` – creates `NetworkManager`, `ClientSingleton`, and (for server builds) `ServerSingleton` at runtime.
- `Scenes/loading.unity` – demonstrates hooking into `LoadingScene` with `LoadingSceneSampleTask`.
- `Scenes/menu.unity` – placeholder scene; replace with your own menu UI wired to the matchmaking scripts.
- `Scenes/game.unity` – contains the `RockPaperScissorsGame` network behaviour and session controller.
- `Scripts/Client` & `Scripts/Shared` – sample behaviours referenced by the scenes.
- `Resources/Config/GameConfig.asset` – default configuration loaded by `GameConfig.Instance`.

After import:
1. Add the scenes to **Build Settings** (bootStrap → loading → menu → game).
2. Customise the menu UI and gameplay elements to fit your project.
3. Update `GameConfig.asset` and other serialized settings as needed.
