# Dedicated Server Multiplayer Sample

Reusable client/server scaffolding for Unity Multiplayer Services dedicated server projects. Includes bootstrapping scripts, matchmaking helpers, and configuration templates so new projects can get online quickly.

This README is the complete manual for importing and running the sample in another project.

## What This Package Includes
- Sample scenes: `bootStrap`, `loading`, `menu`, `game`
- Client and server runtime helpers
- Matchmaker and Multiplay configuration templates (`*.mmq`, `*.mme`, `*.gsh`)
- Multiplayer Play Mode support for editor testing

## Install (Add Package by Git URL)
In Unity:
1. `Window > Package Manager`
2. Click `+` > `Add package from git URL...`
3. Enter: `https://github.com/noprops/DedicatedServerMultiplayerSample.git`

This adds the package to `Packages/manifest.json`.

## Import the Sample
1. In Package Manager, select **Dedicated Server Multiplayer Sample**
2. Open **Samples**
3. Import **Basic Scene Setup**

The sample is copied into:
`Assets/Samples/Dedicated Server Multiplayer Sample/<version>/Basic Scene Setup/`

## Scene Setup
Add scenes to **Build Settings** in this order:
1. `bootStrap.unity`
2. `loading.unity`
3. `menu.unity`
4. `game.unity`

Dedicated Server build should include only:
- `bootStrap`
- `game`

## Unity Services Project Linking
1. Open `Project Settings > Services`
2. Link the project to your Unity Cloud project
3. Ensure **Authentication**, **Matchmaker**, and **Multiplay Hosting** are enabled

Note: open the Multiplay Hosting page at least once in the Dashboard to initialize the project.

## Configure Matchmaking and Multiplay
Edit the files under:
`Assets/Samples/Dedicated Server Multiplayer Sample/<version>/Basic Scene Setup/Configurations/`

Required edits:
- `MatchmakerQueue.mmq` (queue name, pool, rank rules, etc.)
- `MatchmakerEnvironment.mme`
- `MultiplayConfiguration.gsh` (build name, executable, build output path, fleet)
- `DefaultNetworkPrefabs.asset` (if you add/remove networked prefabs)

## Deployment (Editor)
Open `Services/Deployment` and deploy in this order:
1. Build (`*.build`)
2. Build Configuration (`*.buildConfig`)
3. Fleet (`*.fleet`)
4. Matchmaker Queue (`*.mmq`)
5. Matchmaker Environment (`*.mme`)

## Build
Server build (Linux Server):
1. Switch platform to **Linux Server**
2. Build with only `bootStrap` and `game`
3. Output path should match `MultiplayConfiguration.gsh`

Client build:
- Build with `bootStrap`, `loading`, `menu`, `game`

## Editor Test (Multiplayer Play Mode)
1. `Window > Multiplayer > Multiplayer Play Mode`
2. Enable `Main Editor` and at least one `Virtual Player`
3. Set all player roles to **Client**
4. Enter Play Mode and test matchmaking

## Customization Workflow
All editable content is under:
`Assets/Samples/Dedicated Server Multiplayer Sample/<version>/Basic Scene Setup/`

You can:
- Edit directly in that folder, or
- Copy the scenes/scripts to your own `Assets/` folder

If you copy, update references and ensure the Build Settings use your copied scenes.

## Troubleshooting
- Authentication errors: confirm project linking and services enabled.
- Matchmaking stuck: confirm queue/pool/fleet names match the config files.
- Connection failures: check Multiplay fleet ports, QoS, and firewall rules.
