# Quick Start

1. Install the package via Unity Package Manager (Add package from Git URL or local path).
2. In the Package Manager window select the package, expand **Samples**, and import **Basic Scene Setup**. The assets are placed under `Assets/Samples/Dedicated Server Multiplayer Sample/<version>/Basic Scene Setup/`.
3. Add the four sample scenes (`bootStrap`, `loading`, `menu`, `game`) to **Build Settings** in that order. Dedicated server builds only require `bootStrap` and `game`.
4. Open the imported scenes to review the templates. `NetworkManagerBootstrapper` will create a `NetworkManager` with `UnityTransport` at runtime, while `ClientSingleton` is paired with the sample `ClientSaveData`. Refine the menu UI and gameplay objects as needed for your project.
5. Tweak `Resources/Config/GameConfig.asset` (imported with the sample) to match your player count and session requirements.
6. Configure Unity Services (Authentication, Matchmaker, Multiplay) and update any environment-specific settings referenced by the scripts.
7. Build the client and server targets, then deploy the server build to Unity Game Server Hosting.
