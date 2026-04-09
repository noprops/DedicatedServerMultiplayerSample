# DSMS VM Import Guide

This guide is for downstream Unity projects that import `info.mygames888.dedicatedservermultiplayersample` through Package Manager and want to migrate from Unity Cloud hosting to VM-based dedicated servers.

## Target Architecture

1. Client enters Matchmaker
2. Matchmaker queue uses `CloudCode` hosting
3. Cloud Code `allocate` starts a per-match dedicated server process on a VM
4. Cloud Code `poll` waits until that server is ready
5. Matchmaker returns `ip:port`
6. Client connects directly to the VM-hosted dedicated server

Do not use `Multiplay Hosting (Deprecated)` for this migration.

## Current Single-VM Scope

The current reusable package flow is still centered on a single VM target.

Current local VM operations file:

- `project-root/dsms-vm.json`

That is the intended local source of truth for A/B rollout slots.

For the planned A/B rollout design, see the DSMS workspace documentation:

- `docs/DSMS_AB_VM_ROLLOUT_DESIGN.md`

## What The Package Already Includes

- runtime client/server code
- sample queue/environment templates
- Cloud Code allocator templates under `CloudCode~/matchmaker-vm-hosting-a/` and `CloudCode~/matchmaker-vm-hosting-b/`
- VM launcher template under `VmLauncher~/`
- downstream helper scripts under `Tools~/`
- these package-contained assets are the canonical downstream entry points

Important distinction:

- only the Unity sample content under `Samples~/Templates` is mirrored into `Assets/Samples/...` when the sample is imported
- package-only assets under `CloudCode~/`, `VmLauncher~/`, `Tools~/`, and `Documentation~/` are not mirrored into `Assets/Samples/...`
- that is intentional
- downstream projects should use those package-only assets directly from `Packages/info.mygames888.dedicatedservermultiplayersample/...`
- do not create a second copy of those shell scripts, launcher files, or Cloud Code scaffolds under `Assets/`

## What The Importing Project Must Do

Every importing project still needs to provide its own infrastructure and backend configuration:

1. Import the DSMS sample or wire the runtime into your own scenes
2. Create or choose a VM
3. Build and upload the Linux dedicated server build
4. Run the VM launcher service on that VM
5. Create Secret Manager entries for the launcher endpoint
6. Deploy the Cloud Code allocator module
7. Deploy Matchmaker queue/environment config

In practice, the minimum downstream checklist is not just `1-4`.
You also need:

- Linux dedicated server build upload
- launcher deployment
- firewall / port opening

## Service Account Requirement

If the downstream project uses UGS CLI for Cloud Code or Matchmaker deployment, its service account must have roles for that target project.

At minimum, the service account used by `ugs` must be able to deploy:

- Cloud Code modules
- Matchmaker config

Without project-specific roles, downstream deploy commands will fail even if the DSMS package is imported correctly.

## Secret Strategy

Use fixed secret names per slot across every project:

- `DSMS_VM_A_LAUNCHER_BASE_URL`
- `DSMS_VM_A_LAUNCHER_TOKEN`
- `DSMS_VM_B_LAUNCHER_BASE_URL`
- `DSMS_VM_B_LAUNCHER_TOKEN`

Important rule:

- keep the secret keys the same in every project and environment
- only the secret values change
- the Cloud Code module code does not need to change per project if the key names stay fixed

Recommended usage:

- if multiple projects share the same VM launcher, the secret values can also be the same
- if each project uses a different VM or token, keep the same keys and set different values for that project/environment

That avoids per-project source edits.

Current note:

- this package now assumes A/B rollout slots for downstream VM rollout
- `dsms-vm.json` is the canonical local slot config

## Cloud Code Module

Package paths:

- `CloudCode~/matchmaker-vm-hosting-a/`
- `CloudCode~/matchmaker-vm-hosting-b/`

This module:

- reads the launcher base URL and token from Secret Manager
- receives the Matchmaker players for a match
- extracts the expected auth IDs from the match players
- sends those expected auth IDs to the VM launcher
- returns `AllocateResponse` / `PollResponse` in the Unity Cloud Code Matchmaker allocator format

The server process then receives:

- `-matchId`
- `-expectedPlayers`
- `-expectedAuthIds`

This allows server-side connection approval to reject unexpected players.

## VM Launcher

Package path:

- `VmLauncher~/server_launcher.py`
- `VmLauncher~/config.example.json`
- `VmLauncher~/dsms-vm-launcher.service`

Behavior:

- receives `allocate`
- picks a free port in the configured range
- starts a Unity dedicated server process for that match only
- tracks state in `state.json`
- marks exited processes as `stopped`
- runs cleanly under `systemd`

## Helper Scripts Included In The Package

These scripts are intended to be run from the importing project's repo root, pointing at the package path in `Packages/`.

Cloud Code:

- `Packages/info.mygames888.dedicatedservermultiplayersample/Tools~/cloud/deploy_cloudcode_module.sh`

Matchmaker:

- `Packages/info.mygames888.dedicatedservermultiplayersample/Tools~/matchmaker/deploy_matchmaker_config.sh`

VM:

- `Packages/info.mygames888.dedicatedservermultiplayersample/Tools~/vm/create_lightsail_vm.sh`
- `Packages/info.mygames888.dedicatedservermultiplayersample/Tools~/vm/start_lightsail_vm.sh`
- `Packages/info.mygames888.dedicatedservermultiplayersample/Tools~/vm/stop_lightsail_vm.sh`
- `Packages/info.mygames888.dedicatedservermultiplayersample/Tools~/vm/set_current_work_slot.sh`
- `Packages/info.mygames888.dedicatedservermultiplayersample/Tools~/vm/open_lightsail_ports.sh`
- `Packages/info.mygames888.dedicatedservermultiplayersample/Tools~/vm/print_vm_secret_values.sh`
- `Packages/info.mygames888.dedicatedservermultiplayersample/Tools~/vm/upload_server_build.sh`
- `Packages/info.mygames888.dedicatedservermultiplayersample/Tools~/vm/deploy_vm_launcher.sh`
- `Packages/info.mygames888.dedicatedservermultiplayersample/Tools~/vm/install_vm_launcher_service.sh`

These are package-contained on purpose, so downstream users do not have to hunt through this DSMS repo.

## Why Shell Scripts And Not Editor Scripts

Use shell scripts for:

- UGS CLI deploy
- AWS / Lightsail operations
- SSH / SCP to the VM
- launcher installation

Reason:

- these tasks depend on external CLIs, credentials, SSH keys, and machine-local tooling
- that is a better fit for shell scripts than Unity Editor scripting

Use Editor scripts for:

- creating the Linux dedicated server build

The package now includes this editor menu item:

- `DSMS/VM/Build Linux Dedicated Server`

That menu item is implemented in:

- `Packages/info.mygames888.dedicatedservermultiplayersample/Editor/DsmsVmBuildTools.cs`

## Simplified Downstream Flow

1. Import DSMS
2. Import sample
3. Build Linux dedicated server
4. Create VM
5. Open launcher and game ports
6. Upload Linux dedicated server build
7. Deploy launcher files and install the `systemd` service
8. Set Unity project secrets
9. Deploy Cloud Code module
10. Deploy Matchmaker config

That is the real minimum flow.

## VM Creation And Secret Values

If you use AWS Lightsail, use:

- `Tools~/vm/create_lightsail_vm.sh`

It creates or updates `project-root/dsms-vm.json` automatically and prints the exact values you should store for the chosen slot.

For slot `A`:

- `DSMS_VM_A_LAUNCHER_BASE_URL`
- `DSMS_VM_A_LAUNCHER_TOKEN`

For slot `B`:

- `DSMS_VM_B_LAUNCHER_BASE_URL`
- `DSMS_VM_B_LAUNCHER_TOKEN`

That file becomes the canonical per-project local VM operations file.

Expected shape in `project-root/dsms-vm.json`:

- `projectId`
- `projectName`
- `environment`
- `currentWorkSlot`
- `slots.A.instanceName`
- `slots.A.host`
- `slots.A.publicIp`
- `slots.A.sshKeyPath`
- `slots.A.launcherToken`
- `slots.A.launcherBaseUrl`
- `slots.A.maxConcurrentMatches`
- `slots.B.instanceName`
- `slots.B.host`
- `slots.B.publicIp`
- `slots.B.sshKeyPath`
- `slots.B.launcherToken`
- `slots.B.launcherBaseUrl`
- `slots.B.maxConcurrentMatches`

Reference example:

- `Tools~/vm/dsms-vm.example.json`

Recommended usage:

- set `currentWorkSlot` to the slot you are actively deploying to
- if you are preparing the next release on `B`, keep `currentWorkSlot: "B"`
- scripts will use an explicit slot argument if you pass one
- otherwise they will fall back to `currentWorkSlot`
- `A` and `B` must point to different VMs
- do not copy `dsms-vm.json` between unrelated projects

So yes, the VM creation script drives both:

- Unity Secret values
- local deploy-time VM values
- slot-scoped launcher runtime config values such as `maxConcurrentMatches`

If you do not create the VM with the helper script, use:

- `Tools~/vm/print_vm_secret_values.sh <slot:A|B> [launcher-port]`

to generate the exact secret values to paste into Unity.

## Matchmaker Queue Requirements

Your queue must use:

- `type: "CloudCode"`
- `moduleName: "MatchmakerVmHostingA"` or `MatchmakerVmHostingB`
- `allocateFunctionName: "allocate"`
- `pollFunctionName: "poll"`

## Version Routing Note

The sample now uses `gameVersionInt` for Matchmaker-facing version routing.

Important clarification:

- `gameVersionInt` is derived from `Application.version`
- Matchmaker pool routing should use `Players.CustomData.gameVersionInt`
- session properties should not carry `gameVersionInt`

Recommended A/B rollout direction:

- route by Matchmaker pool rule
- let each rollout pool target its own Cloud Code module and VM slot
- keep Cloud Code modules static per slot

Important current limitation:

- in the current Unity Matchmaker authoring/deploy path used by `.mmq`, filtered pool rules for `gameVersionInt` are not deploying reliably
- numeric comparator filters and CEL filters both failed during live queue updates in this project
- therefore the package-delivered `.mmq` files currently ship only `poolA`
- if you need `poolB` for rollout, create it manually in the live Matchmaker configuration and maintain its version filter there

For the detailed design, see the DSMS workspace document:

- `docs/DSMS_AB_VM_ROLLOUT_DESIGN.md`

## Recommended Downstream Workflow

1. Import the package
2. Import the sample
3. Edit `.mmq` / `.mme`
4. Build Linux dedicated server
5. Create slot `A` and/or slot `B` and let the helper write `project-root/dsms-vm.json`
6. If needed, fill in `sshKeyPath` for that slot in that file
7. Copy the Linux server build to the target slot VM
8. Run the package VM launcher deploy/install scripts for that slot
9. Create the Unity project secrets for the slot values from `dsms-vm.json`
10. Deploy the Cloud Code modules from the package script
11. Deploy Matchmaker config from the package script
12. Run manual matchmaking tests

## Concrete Downstream Commands

Linux dedicated server build:

- Unity Editor menu:
  - `DSMS/VM/Build Linux Dedicated Server`

VM create:

```bash
Packages/info.mygames888.dedicatedservermultiplayersample/Tools~/vm/create_lightsail_vm.sh <slot:A|B> <instance-name> <availability-zone> <blueprint-id> [bundle-id]
```

VM start:

```bash
Packages/info.mygames888.dedicatedservermultiplayersample/Tools~/vm/start_lightsail_vm.sh <slot:A|B> [region]
```

VM stop:

```bash
Packages/info.mygames888.dedicatedservermultiplayersample/Tools~/vm/stop_lightsail_vm.sh <slot:A|B> [region]
```

Set current work slot:

```bash
Packages/info.mygames888.dedicatedservermultiplayersample/Tools~/vm/set_current_work_slot.sh <slot:A|B>
```

Open ports:

```bash
Packages/info.mygames888.dedicatedservermultiplayersample/Tools~/vm/open_lightsail_ports.sh <slot:A|B> [region]
```

Upload server build:

```bash
Packages/info.mygames888.dedicatedservermultiplayersample/Tools~/vm/upload_server_build.sh <slot:A|B> <server-build-dir> [remote-dir]
```

Deploy launcher:

```bash
Packages/info.mygames888.dedicatedservermultiplayersample/Tools~/vm/deploy_vm_launcher.sh <slot:A|B> [package-root]
Packages/info.mygames888.dedicatedservermultiplayersample/Tools~/vm/install_vm_launcher_service.sh <slot:A|B>
```

Deploy Cloud Code:

```bash
Packages/info.mygames888.dedicatedservermultiplayersample/Tools~/cloud/deploy_cloudcode_module.sh <project-id> <environment-name> [slot:A|B|ALL]
```

This script is the supported path.
It does not rely on `ugs deploy` for modules.
It builds the C# module, packages it into the Unity Cloud Code import format, and runs `ugs cc modules import`.

Deploy Matchmaker:

```bash
Packages/info.mygames888.dedicatedservermultiplayersample/Tools~/matchmaker/deploy_matchmaker_config.sh <project-id> <environment-name> <matchmaker-environment.mme> <queue.mmq> [more-config-paths...]
```

Canonical rule:

- for downstream usage, use the package-contained `CloudCode~/`, `VmLauncher~/`, and `Tools~/` assets
- do not depend on any root-level migration workspace scripts from this DSMS repository
- let `project-root/dsms-vm.json` be the canonical local VM operations file for both this DSMS repo and downstream repos
- let `project-root/dsms-vm.json` also be the source of truth for deployed launcher config values such as `maxConcurrentMatches`
- `deploy_vm_launcher.sh` must preserve and verify `maxConcurrentMatches` when pushing `config.json` to the VM
- `deploy_vm_launcher.sh` must also preserve and verify ownership metadata:
  - `projectId`
  - `projectName`
  - `environment`
  - `slot`
  - `instanceName`
- `upload_server_build.sh` now requires matching remote launcher ownership metadata before it will upload a build

## What Is Still Project-Specific

These are expected to differ per importing project:

- Unity project ID / environment
- VM instance name
- VM SSH host
- VM SSH key path
- Matchmaker queues and rules
- VM public IP or DNS
- launcher token value
- Linux server build contents
- firewall rules
- project service-account roles

These do not need source-code key renames:

- secret key names
- Cloud Code function names
- launcher endpoint JSON schema

## Per-Project VM Separation

Yes, different downstream projects are expected to use different VM instances.

Example:

- this DSMS migration workspace can use one VM
- `NIMA` should use a different VM
- another downstream project should use yet another VM

The separation point is not hardcoded in DSMS source.
It is controlled by per-project infrastructure values:

- VM public IP / DNS
- launcher token
- SSH host
- SSH key path
- Unity project secrets

As long as each project uses its own values, each project will deploy to and connect to its own VM.

## How A Downstream Project Chooses Its VM

The package scripts support a standard project-local VM operations file:

- `project-root/dsms-vm.json`

This is how a downstream project chooses and remembers its VM.

Standard workflow:

1. run `create_lightsail_vm.sh <slot:A|B> ...`
2. let it write or update `project-root/dsms-vm.json`
3. let later VM scripts read that file by slot

## Recommended Per-Project Operations File

To avoid retyping the wrong VM target, each downstream project should use:

- `project-root/dsms-vm.json`

This file is created automatically by the VM create script.

The only normal manual follow-up is:

- fill in `sshKeyPath` for the slot if the create script could not determine it automatically

Do not hardcode project VM identity into the DSMS package itself.

Reason:

- the package must stay reusable
- VM identity belongs to the importing project, not to the shared package

## Recommended Downstream VM Workflow

For a new downstream project:

1. create a new VM for that project
2. record that VM's host, public IP, SSH key path, and launcher token in the downstream project's own ops file
3. set that project's Unity secrets from those values
4. run all VM deploy scripts using that project's VM arguments

For the next deployment to the same downstream project:

1. reuse the same recorded VM values
2. rebuild the Linux server build if the game code changed
3. upload the new build to that same VM
4. redeploy launcher files only if launcher code changed

This is how the project continues to target the correct VM on later updates.

## Scaling Notes

Current included launcher is:

- single VM
- multi-port
- multi-process

It can run multiple matches in parallel on one VM as long as:

- free ports remain in the configured range
- CPU and memory remain sufficient

It does not yet do:

- auto-scale to additional VMs
- global scheduling across multiple VMs
- capacity-aware placement beyond one machine

Those are the next layer, not required for the base migration.
