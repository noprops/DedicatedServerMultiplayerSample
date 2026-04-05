# matchmaker-vm-hosting

Legacy single-slot module note:

- the current recommended rollout model uses:
  - `matchmaker-vm-hosting-a`
  - `matchmaker-vm-hosting-b`
- with Matchmaker module names:
  - `MatchmakerVmHostingA`
  - `MatchmakerVmHostingB`
- use this folder only if you intentionally want a single-slot VM allocator

Cloud Code module for routing Unity Matchmaker tickets to a VM-side launcher that starts dedicated server processes on demand.

Current behavior:

- `allocate` calls the VM launcher and requests a per-match dedicated server process
- `poll` queries the launcher until the dedicated server is ready
- once the launcher reports `ready`, the module returns an `IpPortAssignment`
- the module reads launcher endpoint credentials from Unity Secret Manager
- the module forwards match player auth IDs to the server as `expectedAuthIds`
- the module no longer assumes a resident always-on server process

This is the migration path away from the temporary resident-server validation stage.

## Files

- `matchmaker-vm-hosting.ccmr`
- `Module~/matchmaker-vm-hosting.sln`
- `Module~/Project/MatchmakerVmHosting.csproj`
- `Module~/Project/FixedVmAllocator.cs`

## Deploy

1. Deploy the VM launcher from `VmLauncher~/`
2. Create these Secret Manager entries:
   - `DSMS_VM_LAUNCHER_BASE_URL`
   - `DSMS_VM_LAUNCHER_TOKEN`
3. Keep those secret names fixed across projects. Only the values should differ.
4. Deploy with the package helper script from the importing project root:

```bash
Packages/info.mygames888.dedicatedservermultiplayersample/Tools~/cloud/deploy_cloudcode_module.sh <project-id> <environment-name>
```

5. Set your Matchmaker `.mmq` pools to:
   - `type: "CloudCode"`
   - `moduleName: "MatchmakerVmHosting"`
   - `allocateFunctionName: "allocate"`
   - `pollFunctionName: "poll"`

## Scope

This is still a PoC allocator.

Known limits:

- single VM only
- local port-pool only
- no HTTPS termination yet
- no multi-VM scheduling yet

Canonical source of truth:

- downstream projects should use the package-contained assets under `CloudCode~/`, `VmLauncher~/`, and `Tools~/`
- root-level `modules/` and `scripts/` from the migration workspace are not part of the reusable package contract
