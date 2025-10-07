# Quick Start

1. Install the package via Unity Package Manager (Add package from Git URL or local path).
2. Import the **Basic Scene Setup** sample from the Package Manager window. This creates copies of the bootStrap, loading, menu and game scenes plus required prefabs and configurations under your project `Assets/` folder.
3. Add the four scenes to **Build Settings** in the order: bootStrap, loading, menu, game. For dedicated server builds only include bootStrap and game.
4. Open the bootStrap scene and ensure the NetworkManager, ClientSingleton and ServerSingleton prefabs are present. Adjust `GameConfig` if you need different team/player counts.
5. Configure Unity Services (Authentication, Matchmaker, Multiplay) and update the configuration files under `Assets/Configurations/` for your environment.
6. Build the client and server targets, then deploy the server build to Unity Game Server Hosting.
