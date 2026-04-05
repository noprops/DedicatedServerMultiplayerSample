# VM Launcher

This directory contains the minimum VM-side process launcher for the DSMS Matchmaker migration.

## Purpose

Replace the temporary resident dedicated-server approach with:

1. Matchmaker queue calls Cloud Code `allocate`
2. Cloud Code calls this launcher on the VM
3. The launcher starts a per-match Unity dedicated server process on a free port
4. Cloud Code `poll` checks launcher status until the process is ready
5. Matchmaker returns the resulting `ip:port` to the clients
6. The launched Unity dedicated server only approves the expected auth IDs for that match

## Files

- `server_launcher.py`
  - HTTP service for allocate / poll / stop
- `config.example.json`
  - template for VM-local launcher configuration
- `dsms-vm-launcher.service`
  - systemd unit for stable launcher operation on the VM

## Endpoints

- `GET /healthz`
- `POST /matches/allocate`
- `GET /matches/<matchId>`
- `POST /matches/<matchId>/stop`

All endpoints require:

```text
Authorization: Bearer <launcherToken>
```

## Expected Server Launch Shape

The launcher starts DSMS like this:

```text
-batchmode -nographics -serverMode selfHosted -port <allocated-port> -expectedPlayers 2 -matchId <match-id> -expectedAuthIds <auth1,auth2,...>
```

## Notes

- This is a single-VM PoC launcher, not production orchestration.
- It uses a local port pool and local JSON state.
- It can enforce a configurable `maxConcurrentMatches` limit on one VM.
- It marks readiness from Unity server log markers.
- It now reconciles process exit into `state.json` on a timer, not only on incoming HTTP requests.
- It reaps exited child server processes so zombie entries do not accumulate and stale `ready` state does not linger.
- In production for this repo, the launcher should be installed as `systemd` service `dsms-vm-launcher.service`.
- The VM should remain stopped when not actively testing to avoid unnecessary cost.
