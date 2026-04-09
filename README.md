# Dedicated Server Multiplayer Sample

Reusable client/server scaffolding for Unity Multiplayer Services dedicated server projects. Includes bootstrapping scripts, matchmaking helpers, and configuration templates so new projects can get online quickly.

This README is the complete manual for importing and running the sample in another project.

VM migration support is now included in-package:
- Cloud Code allocator templates:
  - `CloudCode~/matchmaker-vm-hosting-a/`
  - `CloudCode~/matchmaker-vm-hosting-b/`
- VM launcher template: `VmLauncher~/`
- Import/migration guide: `Documentation~/VM_IMPORT_GUIDE.md`
- helper scripts: `Tools~/`
- Linux server build menu: `DSMS/VM/Build Linux Dedicated Server`

VM helper scripts include slot-based:
- create
- start
- stop
- open ports
- upload server build
- deploy launcher
- install launcher service

Per-project VM operations are standardized through:
- `project-root/dsms-vm.json`
- generated automatically by `Tools~/vm/create_lightsail_vm.sh`
- includes top-level project ownership fields:
  - `projectId`
  - `projectName`
  - `environment`
- includes top-level create defaults:
  - `defaultAvailabilityZone`
  - `defaultBlueprintId`
  - `defaultBundleId`
- includes `currentWorkSlot`
- includes slot-scoped launcher config values such as `maxConcurrentMatches`
- scripts use an explicit slot argument if provided, otherwise they use `currentWorkSlot`
- slot `A` and slot `B` must point to different VMs
- DSMS Editor operational menus now read their required project and slot values directly from `project-root/dsms-vm.json`
- there is no separate persisted DSMS operations settings asset used as operational state
- `DSMS/VM/Create Lightsail VM` reads create-time values directly from `project-root/dsms-vm.json`
- the selected slot's `instanceName` is used as the desired Lightsail instance name
- create `project-root/dsms-vm.json` first, typically by copying `Tools~/vm/dsms-vm.example.json` and filling the required keys

For downstream projects, these package-contained assets are the canonical integration entry points.
Do not depend on root-level migration workspace `modules/` or `scripts/` paths from this repository.

## What This Package Includes
- Sample scenes: `bootStrap`, `loading`, `menu`, `game`
- Client and server runtime helpers
- Matchmaker and VM-hosting configuration templates (`*.mmq`, `*.mme`, `*.gsh`)
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
3. Ensure **Authentication**, **Matchmaker**, and **Cloud Code** are enabled

## Configure Matchmaking and VM Hosting
Edit the files under:
`Assets/Samples/Dedicated Server Multiplayer Sample/<version>/Basic Scene Setup/Configurations/`

Required edits:
- `CompetitiveQueue.mmq` / `CasualQueue.mmq` (queue name, pool, rules, etc.)
- `MatchmakerEnvironment.mme`
- Cloud Code module name / allocate / poll function names inside the `.mmq` files
- `DefaultNetworkPrefabs.asset` (if you add/remove networked prefabs)

Current package-deliverable queue templates only include `poolA`.

Important current limitation:
- filtered pool rollout rules for `gameVersionInt` are not deploying reliably from `.mmq` authoring in this Unity package/version combination
- if you need A/B rollout pools, create and maintain `poolB` manually in the live Matchmaker configuration
- use `poolA -> MatchmakerVmHostingA` as the deployable baseline from this package
- treat `poolB -> MatchmakerVmHostingB` as a manual backend rollout step for now

Do not migrate by falling back to `Multiplay Hosting (Deprecated)`.
Use `CloudCode` hosting only.

## Deployment (Editor / CLI)
Open `Services/Deployment` and deploy:
1. Matchmaker Queue (`*.mmq`)
2. Matchmaker Environment (`*.mme`)

Deploy your Cloud Code modules with:

```bash
Packages/info.mygames888.dedicatedservermultiplayersample/Tools~/cloud/deploy_cloudcode_module.sh <project-id> <environment-name> [slot:A|B|ALL]
```

This is the supported path.
It builds the C# modules and imports them with `ugs cc modules import`.
Do not rely on `ugs deploy` for these modules.

For the full VM path, follow:
`Documentation~/VM_IMPORT_GUIDE.md`

Important:
- Unity Secret Manager stores Cloud Code secrets
- `project-root/dsms-vm.json` stores local deploy/SSH values for VM slot `A` and `B`
- `project-root/dsms-vm.json` is also the source of truth for launcher config values such as `maxConcurrentMatches`
- `project-root/dsms-vm.json` now also stores project ownership values used for VM ownership checks
- DSMS operational menus read `projectId`, `projectName`, `environment`, `currentWorkSlot`, and slot-scoped VM values directly from `project-root/dsms-vm.json`
- launcher deploy rebuilds remote `config.json` from package defaults, then overrides slot values from `dsms-vm.json`
- launcher deploy refuses to overwrite a VM that reports a different `projectId` or `slot`
- launcher deploy verifies `projectId`, `projectName`, `environment`, `slot`, `instanceName`, `publicIp`, `launcherToken`, `bindPort`, and `maxConcurrentMatches` after upload
- server build upload refuses to run unless the remote launcher ownership metadata matches the local project and slot
- both this DSMS repo and downstream repos use the same `dsms-vm.json` convention

## Build
Server build (Linux Server):
1. Switch platform to **Linux Server**
2. Build with only `bootStrap` and `game`
3. Use your VM deployment process to upload the Linux server build and launch it with the required runtime arguments

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
- Matchmaking stuck: confirm queue names and Cloud Code module/function names match the config files.
- Connection failures: check the endpoint returned by Cloud Code, VM firewall rules, and server launch arguments.
