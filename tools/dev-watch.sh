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

# Kill a process and everything under it, children before parents, so a dying parent
# cannot re-orphan a child that then keeps the port. SIGTERM first, SIGKILL after a
# grace period — dotnet watch and its app routinely ignore SIGTERM.
kill_tree() {
  local pid="$1" child
  for child in $(pgrep -P "$pid" 2>/dev/null); do
    kill_tree "$child"
  done
  kill "$pid" 2>/dev/null || true
  local i=0
  while ps -p "$pid" >/dev/null 2>&1 && [ "$i" -lt 10 ]; do
    sleep 0.3
    i=$((i + 1))
  done
  if ps -p "$pid" >/dev/null 2>&1; then
    echo "[dev-watch]   $pid ignored SIGTERM — SIGKILL"
    kill -9 "$pid" 2>/dev/null || true
  fi
}

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
      # Descendants first, bottom-up. Killing the watcher alone re-orphans its app
      # process, which then survives as a fresh PID-1 child still holding the port.
      # Observed 2026-08-23: a watcher orphaned two days earlier was on iteration 56,
      # respawning the app every time a build touched a .cs file.
      kill_tree "$pid"
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
  # Reap the watcher's whole tree explicitly. A bare `kill -- -$$` is not enough:
  # dotnet watch and the app it spawns both ignore SIGTERM (verified 2026-08-23), so
  # the group signal exits claiming success while everything keeps running — the very
  # bug this script exists to prevent.
  [ -n "${WATCHER_PID:-}" ] && kill_tree "$WATCHER_PID"
  kill -- -$$ 2>/dev/null || true
  local i=0
  while [ "$i" -lt 10 ] && lsof -nP -tiTCP:"$PORT" -sTCP:LISTEN >/dev/null 2>&1; do
    sleep 0.3
    i=$((i + 1))
  done
  if lsof -nP -tiTCP:"$PORT" -sTCP:LISTEN >/dev/null 2>&1; then
    echo "[dev-watch] port $PORT still held after SIGTERM — SIGKILL"
    kill -9 -- -$$ 2>/dev/null || true
  fi
  exit $code
}

reap_orphans
trap cleanup EXIT INT TERM HUP

echo "[dev-watch] starting watcher (ctrl-c stops both watcher and app)"

# NOT exec: exec replaces this shell, which destroys the EXIT trap — the watcher would
# then be reparented to init on terminal close, producing exactly the orphan this script
# exists to prevent. Run it as a child and wait, so cleanup() still fires.
dotnet watch --project ProjectCeres --launch-profile https "$@" &
WATCHER_PID=$!
wait "$WATCHER_PID"
