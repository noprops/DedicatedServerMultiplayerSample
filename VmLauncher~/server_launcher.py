#!/usr/bin/env python3
import json
import os
import secrets
import signal
import socket
import subprocess
import threading
import time
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlparse


CONFIG_PATH = os.environ.get("DSMS_VM_LAUNCHER_CONFIG", str(Path(__file__).with_name("config.json")))
STATE_LOCK = threading.Lock()
RECENT_CAPACITY_REJECTION_LIMIT = 20


def load_config():
    with open(CONFIG_PATH, "r", encoding="utf-8") as f:
        config = json.load(f)

    config["logsDirectory"] = os.path.expanduser(config["logsDirectory"])
    config["stateFile"] = os.path.expanduser(config["stateFile"])
    config["serverWorkingDirectory"] = os.path.expanduser(config["serverWorkingDirectory"])
    config["serverExecutable"] = os.path.expanduser(config["serverExecutable"])
    return config


CONFIG = load_config()


def reconcile_and_save():
    with STATE_LOCK:
        state = load_state()
        reconcile_state(state)
        save_state(state)


def ensure_parent(path_str):
    path = Path(path_str)
    path.parent.mkdir(parents=True, exist_ok=True)
    return path


def load_state():
    state_path = ensure_parent(CONFIG["stateFile"])
    if not state_path.exists():
        return default_state()

    with state_path.open("r", encoding="utf-8") as f:
        return normalize_state(json.load(f))


def save_state(state):
    state = normalize_state(state)
    state_path = ensure_parent(CONFIG["stateFile"])
    temp_path = state_path.with_suffix(".tmp")
    with temp_path.open("w", encoding="utf-8") as f:
        json.dump(state, f, indent=2, sort_keys=True)
    temp_path.replace(state_path)


def default_state():
    return {
        "matches": {},
        "capacityRejectionsTotal": 0,
        "recentCapacityRejections": [],
    }


def normalize_state(state):
    if not isinstance(state, dict):
        return default_state()

    normalized = dict(state)
    matches = normalized.get("matches")
    normalized["matches"] = matches if isinstance(matches, dict) else {}

    total = normalized.get("capacityRejectionsTotal", 0)
    try:
        normalized["capacityRejectionsTotal"] = int(total)
    except (TypeError, ValueError):
        normalized["capacityRejectionsTotal"] = 0

    recent = normalized.get("recentCapacityRejections", [])
    normalized["recentCapacityRejections"] = recent if isinstance(recent, list) else []
    return normalized


def now_ts():
    return int(time.time())


def log_event(payload):
    try:
        print(json.dumps(payload, ensure_ascii=True), flush=True)
    except Exception:
        pass


def reap_children():
    while True:
        try:
            pid, _ = os.waitpid(-1, os.WNOHANG)
        except ChildProcessError:
            return
        except OSError:
            return

        if pid == 0:
            return


def process_zombie(pid):
    if not pid:
        return False

    try:
        stat_text = Path(f"/proc/{pid}/stat").read_text(encoding="utf-8", errors="ignore")
    except OSError:
        return False

    parts = stat_text.split()
    return len(parts) >= 3 and parts[2] == "Z"


def process_alive(pid):
    if not pid:
        return False
    if process_zombie(pid):
        return False
    try:
        os.kill(pid, 0)
        return True
    except OSError:
        return False


def port_open(host, port):
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
        sock.settimeout(0.2)
        return sock.connect_ex((host, port)) == 0


def log_contains_ready_markers(log_path):
    log_file = Path(log_path)
    if not log_file.exists():
        return False

    try:
        text = log_file.read_text(encoding="utf-8", errors="ignore")
    except OSError:
        return False

    for marker in CONFIG["readyMarkers"]:
        if marker not in text:
            return False
    return True


def reconcile_state(state):
    reap_children()

    for match_id, entry in list(state["matches"].items()):
        pid = entry.get("pid")
        status = entry.get("status")

        if status in ("failed", "stopped"):
            continue

        if not process_alive(pid):
            if status == "ready":
                entry["status"] = "stopped"
                entry["message"] = "server process exited"
            else:
                entry["status"] = "failed"
                entry["message"] = "server process exited before readiness"
            entry["updatedAt"] = now_ts()
            continue

        if status == "starting":
            if log_contains_ready_markers(entry["logPath"]):
                entry["status"] = "ready"
                entry["message"] = "server ready"
                entry["updatedAt"] = now_ts()
                continue

            if now_ts() - entry["startedAt"] > CONFIG["startupTimeoutSeconds"]:
                entry["status"] = "failed"
                entry["message"] = "server startup timed out"
                try:
                    os.killpg(pid, signal.SIGTERM)
                except OSError:
                    pass
                entry["updatedAt"] = now_ts()


def allocated_ports(state):
    ports = set()
    for entry in state["matches"].values():
        if entry.get("status") in ("starting", "ready"):
            ports.add(entry["port"])
    return ports


def active_match_count(state):
    count = 0
    for entry in state["matches"].values():
        if entry.get("status") in ("starting", "ready"):
            count += 1
    return count


def record_capacity_rejection(state, match_id, expected_players, expected_auth_ids):
    active_matches = active_match_count(state)
    max_concurrent_matches = int(CONFIG.get("maxConcurrentMatches", 0) or 0)
    timestamp = now_ts()
    event = {
        "event": "capacity_reached",
        "timestamp": timestamp,
        "matchId": match_id,
        "activeMatches": active_matches,
        "maxConcurrentMatches": max_concurrent_matches,
        "expectedPlayers": expected_players,
        "expectedAuthIdsCount": len(expected_auth_ids),
        "projectName": CONFIG.get("projectName"),
        "projectId": CONFIG.get("projectId"),
        "environment": CONFIG.get("environment"),
        "slot": CONFIG.get("slot"),
        "instanceName": CONFIG.get("instanceName"),
        "attemptCount": 1,
    }
    recent = state.get("recentCapacityRejections")
    if not isinstance(recent, list):
        recent = []
        state["recentCapacityRejections"] = recent

    existing = next((item for item in recent if item.get("matchId") == match_id), None)
    if existing is None:
        state["capacityRejectionsTotal"] = int(state.get("capacityRejectionsTotal", 0) or 0) + 1
        recent.append(event)
        if len(recent) > RECENT_CAPACITY_REJECTION_LIMIT:
            del recent[:-RECENT_CAPACITY_REJECTION_LIMIT]
    else:
        existing["timestamp"] = timestamp
        existing["activeMatches"] = active_matches
        existing["maxConcurrentMatches"] = max_concurrent_matches
        existing["expectedPlayers"] = expected_players
        existing["expectedAuthIdsCount"] = len(expected_auth_ids)
        existing["projectName"] = CONFIG.get("projectName")
        existing["projectId"] = CONFIG.get("projectId")
        existing["environment"] = CONFIG.get("environment")
        existing["slot"] = CONFIG.get("slot")
        existing["instanceName"] = CONFIG.get("instanceName")
        existing["attemptCount"] = int(existing.get("attemptCount", 1) or 1) + 1
        event["attemptCount"] = existing["attemptCount"]

    log_event(event)
    return {
        "matchId": match_id,
        "status": "failed",
        "message": f"capacity reached: activeMatches={active_matches} maxConcurrentMatches={max_concurrent_matches}",
        "activeMatches": active_matches,
        "maxConcurrentMatches": max_concurrent_matches,
        "updatedAt": now_ts(),
    }


def capacity_available(state):
    max_concurrent_matches = int(CONFIG.get("maxConcurrentMatches", 0) or 0)
    if max_concurrent_matches <= 0:
        return True
    return active_match_count(state) < max_concurrent_matches


def next_free_port(state):
    used = allocated_ports(state)
    for port in range(CONFIG["minPort"], CONFIG["maxPort"] + 1):
        if port in used:
            continue
        if port_open("127.0.0.1", port):
            continue
        return port
    raise RuntimeError("no free server port available")


def spawn_server(match_id, port, expected_players, expected_auth_ids):
    logs_dir = Path(CONFIG["logsDirectory"])
    logs_dir.mkdir(parents=True, exist_ok=True)
    log_path = logs_dir / f"{match_id}.log"
    working_directory = Path(CONFIG["serverWorkingDirectory"]).resolve()
    launch_env = os.environ.copy()
    existing_library_path = launch_env.get("LD_LIBRARY_PATH", "")
    if existing_library_path:
        launch_env["LD_LIBRARY_PATH"] = f"{working_directory}:{existing_library_path}"
    else:
        launch_env["LD_LIBRARY_PATH"] = str(working_directory)

    cmd = [
        CONFIG["serverExecutable"],
        "-batchmode",
        "-nographics",
        "-logFile",
        str(log_path),
        "-serverMode",
        "selfHosted",
        "-port",
        str(port),
        "-expectedPlayers",
        str(expected_players),
        "-matchId",
        match_id,
    ]

    if expected_auth_ids:
        cmd.extend(["-expectedAuthIds", ",".join(expected_auth_ids)])

    process = subprocess.Popen(
        cmd,
        cwd=working_directory,
        env=launch_env,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        preexec_fn=os.setsid,
    )

    return {
        "matchId": match_id,
        "status": "starting",
        "message": "server process starting",
        "ip": CONFIG["publicIp"],
        "port": port,
        "pid": process.pid,
        "logPath": str(log_path),
        "startedAt": now_ts(),
        "updatedAt": now_ts(),
        "expectedPlayers": expected_players,
        "expectedAuthIds": expected_auth_ids,
    }


def allocate_match(match_id, expected_players, expected_auth_ids):
    with STATE_LOCK:
        state = load_state()
        reconcile_state(state)

        existing = state["matches"].get(match_id)
        if existing and existing.get("status") in ("starting", "ready"):
            save_state(state)
            return existing

        if not capacity_available(state):
            rejection = record_capacity_rejection(state, match_id, expected_players, expected_auth_ids)
            save_state(state)
            return rejection

        port = next_free_port(state)
        entry = spawn_server(match_id, port, expected_players, expected_auth_ids)
        state["matches"][match_id] = entry
        save_state(state)
        return entry


def get_match(match_id):
    with STATE_LOCK:
        state = load_state()
        reconcile_state(state)
        save_state(state)
        return state["matches"].get(match_id)


def stop_match(match_id):
    with STATE_LOCK:
        state = load_state()
        reconcile_state(state)
        entry = state["matches"].get(match_id)
        if not entry:
            return None

        pid = entry.get("pid")
        if pid and process_alive(pid):
            try:
                os.killpg(pid, signal.SIGTERM)
            except OSError:
                pass

        entry["status"] = "stopped"
        entry["message"] = "stopped by launcher"
        entry["updatedAt"] = now_ts()
        save_state(state)
        return entry


class LauncherHandler(BaseHTTPRequestHandler):
    server_version = "DsmsVmLauncher/0.1"

    def do_GET(self):
        if not self._authorize():
            return

        path = urlparse(self.path).path
        self._log({"method": "GET", "path": path, "remote": self.client_address[0]})
        if path == "/healthz":
            self._write_json(HTTPStatus.OK, {"status": "ok"})
            return

        if path.startswith("/matches/"):
            match_id = path.rsplit("/", 1)[-1]
            match = get_match(match_id)
            if not match:
                self._write_json(HTTPStatus.NOT_FOUND, {"status": "missing", "matchId": match_id, "message": "match not found"})
                return
            self._write_json(HTTPStatus.OK, match)
            return

        self._write_json(HTTPStatus.NOT_FOUND, {"error": "not found"})

    def do_POST(self):
        if not self._authorize():
            return

        path = urlparse(self.path).path
        payload = self._read_json()
        self._log({"method": "POST", "path": path, "remote": self.client_address[0], "payload": payload})
        if path == "/matches/allocate":
            match_id = (payload.get("matchId") or payload.get("assignmentId") or "").strip()
            if not match_id:
                match_id = f"launcher-{secrets.token_hex(8)}"

            expected_players = int(payload.get("expectedPlayers") or CONFIG["defaultExpectedPlayers"])
            expected_auth_ids = payload.get("expectedAuthIds") or []
            if not isinstance(expected_auth_ids, list):
                expected_auth_ids = []
            expected_auth_ids = [str(item).strip() for item in expected_auth_ids if str(item).strip()]
            entry = allocate_match(match_id, expected_players, expected_auth_ids)
            if entry.get("status") == "failed" and "capacity reached" in (entry.get("message") or ""):
                self._write_json(HTTPStatus.SERVICE_UNAVAILABLE, entry)
                return
            self._write_json(HTTPStatus.OK, entry)
            return

        if path.startswith("/matches/") and path.endswith("/stop"):
            match_id = path.split("/")[-2]
            entry = stop_match(match_id)
            if not entry:
                self._write_json(HTTPStatus.NOT_FOUND, {"status": "missing", "matchId": match_id})
                return
            self._write_json(HTTPStatus.OK, entry)
            return

        self._write_json(HTTPStatus.NOT_FOUND, {"error": "not found"})

    def log_message(self, format, *args):
        return

    def _authorize(self):
        expected = f"Bearer {CONFIG['launcherToken']}"
        provided = self.headers.get("Authorization")
        if provided != expected:
            self._write_json(HTTPStatus.UNAUTHORIZED, {"error": "unauthorized"})
            return False
        return True

    def _read_json(self):
        length = int(self.headers.get("Content-Length", "0"))
        if length <= 0:
            return {}
        raw = self.rfile.read(length)
        if not raw:
            return {}
        try:
            return json.loads(raw.decode("utf-8"))
        except json.JSONDecodeError as ex:
            self._log({"method": "POST", "path": urlparse(self.path).path, "remote": self.client_address[0], "jsonError": str(ex), "raw": raw.decode("utf-8", errors="ignore")})
            return {}

    def _write_json(self, status, payload):
        body = json.dumps(payload).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def _log(self, payload):
        log_event(payload)


def main():
    ensure_parent(CONFIG["stateFile"])
    Path(CONFIG["logsDirectory"]).mkdir(parents=True, exist_ok=True)
    reconcile_interval = int(CONFIG.get("reconcileIntervalSeconds", 5))

    def reconcile_loop():
        while True:
            try:
                reconcile_and_save()
            except Exception:
                pass
            time.sleep(reconcile_interval)

    threading.Thread(target=reconcile_loop, name="state-reconcile", daemon=True).start()
    httpd = ThreadingHTTPServer((CONFIG["bindHost"], int(CONFIG["bindPort"])), LauncherHandler)
    httpd.serve_forever()


if __name__ == "__main__":
    main()
