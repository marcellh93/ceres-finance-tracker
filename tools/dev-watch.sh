#!/usr/bin/env bash
# Start dotnet watch with orphan cleanup and guaranteed teardown.
#
# Two failure modes this exists to prevent, both observed on this machine:
#
#   1. A watcher outliving its terminal. `dotnet watch` does not exit when its
#      parent shell dies, so it reparents to PID 1 and keeps file watches and
#      MSBuild handles on bin/Debug/. A later watcher then intermittently fails
#      to write the DLL. The orphan holds no port, so a port check does not find
#      it. Two of these had been running for over two days.
#
#   2. An app process outliving its watcher, which DOES hold the port and shows
#      up as AddressInUseException.
#
# Usage: tools/dev-watch.sh [extra dotnet watch args]

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

PORT=7081

reap_orphans() {
  local found=0

  # Watchers for THIS project that are no longer attached to a live shell.
  # Match on the project name so a watcher for a different repo is never touched,
  # and require the executable to actually be dotnet — a shell or grep whose command
  # line merely contains the pattern must not be killed.
  while read -r pid; do
    [ -z "$pid" ] && continue
    [ "$pid" = "$$" ] && continue
    local comm ppid
    comm="$(ps -o comm= -p "$pid" 2>/dev/null | tr -d ' ')"
    case "$comm" in *dotnet) ;; *) continue ;; esac
    ppid="$(ps -o ppid= -p "$pid" 2>/dev/null | tr -d ' ')"
    if [ "$ppid" = "1" ]; then
      echo "[dev-watch] reaping orphaned watcher $pid (reparented to init)"
      kill "$pid" 2>/dev/null || true
      sleep 1
      ps -p "$pid" >/dev/null 2>&1 && kill -9 "$pid" 2>/dev/null || true
      found=1
    fi
  done < <(pgrep -f "dotnet-watch.dll.*--project ProjectCeres" 2>/dev/null || true)

  # Anything still squatting on the app port.
  while read -r pid; do
    [ -z "$pid" ] && continue
    echo "[dev-watch] reaping process $pid still holding port $PORT"
    kill -9 "$pid" 2>/dev/null || true
    found=1
  done < <(lsof -nP -tiTCP:"$PORT" -sTCP:LISTEN 2>/dev/null || true)

  [ "$found" = "1" ] && sleep 1
  return 0
}

# Kill the whole process group on exit, so the app process cannot outlive us.
cleanup() {
  local code=$?
  trap - EXIT INT TERM HUP
  echo "[dev-watch] shutting down watcher and app"
  kill -- -$$ 2>/dev/null || true
  exit $code
}

reap_orphans
trap cleanup EXIT INT TERM HUP

echo "[dev-watch] starting watcher (ctrl-c stops both watcher and app)"
exec dotnet watch --project ProjectCeres --launch-profile https "$@"
