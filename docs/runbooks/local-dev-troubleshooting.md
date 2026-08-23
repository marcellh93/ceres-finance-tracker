# Local Dev Troubleshooting — stale artifacts

> **When to read this:** you changed code, the source file on disk is correct, and
> the running app still behaves the old way. No error, no warning, nothing in the
> logs. Both symptoms below are silent by nature — the app is faithfully serving
> something you built earlier.
>
> **The rule these share:** a change is not done when the file contains it. It is
> done when the running app is serving it. Confirm that before reporting.

## Symptom 1 — the backend runs old code (stale binary)

*First hit 2026-05-19.*

`dotnet watch` only restarts the running app when a `.cs` file changes **since its
own last scan**. If a separate session runs a manual `dotnet build`, watch does not
notice: the DLL on disk moves ahead of the process in memory.

What you see:

- Exception stack traces cite line numbers from the **old** source.
- "I restarted the server" produces no observable change.
- Behaviour matches a version of the code you no longer have open.

**Detect.** Compare the DLL's timestamp against the running process's start time:

```bash
ls -la ProjectCeres/bin/Debug/net10.0/ProjectCeres.dll
ps aux | grep "[b]in/Debug/net10.0/ProjectCeres"
```

If the DLL is newer than the process start, the process is stale.

**Fix.** Kill the process and force watch to rebuild and relaunch:

```bash
kill -9 <pid>
touch ProjectCeres/<any-source>.cs
```

## Symptom 2 — the frontend renders old React (stale SPA bundle)

*First hit 2026-08-22. Cost a full round-trip: a dashboard label was changed, the
file was correct, and the browser kept rendering the old text.*

In Development, `/dist/*` requests are routed to the Vite dev server —
`ProjectCeres/Program.cs:662`, the `MapWhen(IsViteRequest)` branch. **If no Vite
process is running, those requests fall through to the static files in
`ProjectCeres/wwwroot/dist/`** — whatever was last built there, possibly days old.

`dotnet watch` does **not** rebuild the SPA. Only `pnpm build` does. So the page
renders stale React with no error, no warning, and a correct-looking source file.

What you see:

- A copy or layout change simply doesn't appear.
- Hard-refreshing changes nothing (the stale bundle is what the server sends).
- The source file plainly contains your edit.

**Detect.** Compare the entry bundle the server advertises against what is on disk:

```bash
curl -sk https://localhost:7081/ | grep -oE 'razorAppEntry-[A-Za-z0-9_-]+\.js'
ls -la ProjectCeres/wwwroot/dist/assets/
```

Then check whether Vite is actually running:

```bash
pgrep -fl "node.*vite"
```

No match means the bundle is being served from disk. Use `pgrep`, **not**
`ps aux | grep vite` — the latter matches its own shell invocation and reports a
false positive. (That happened while this runbook was being verified.)

To confirm beyond doubt, grep the served asset for the string you changed:

```bash
curl -sk "https://localhost:7081/dist/assets/<entry>.js" | grep -c "<your string>"
```

**Fix — preferred.** Run Vite alongside the backend so it serves live modules with
hot reload. This is what the `MapWhen` branch exists for:

```bash
pnpm --dir ProjectCeres.Client dev
```

**Fix — rebuild.** If you are not running Vite, rebuild and re-stage after frontend
edits:

```bash
pnpm --dir ProjectCeres.Client build
rm -rf ProjectCeres/wwwroot/dist && mkdir -p ProjectCeres/wwwroot/dist
cp -R ProjectCeres.Client/dist/. ProjectCeres/wwwroot/dist/
```

The output filename is content-hashed, so once rebuilt a plain reload picks it up —
no cache-buster needed.

## Symptom 3 — build errors from a watcher you cannot see

`dotnet watch` reports build or file-write failures, but the source compiles fine
and nothing obvious is running.

**Cause.** `dotnet watch` does not exit when the terminal that started it closes.
It reparents to PID 1 and keeps file watches and MSBuild node handles on
`bin/Debug/net10.0/`. A second watcher started later then intermittently fails to
write the output DLL, because the dead one still holds those files.

**Why a port check misses it.** The orphan holds no port — only the *app* process
listens on 7081, and a watcher without a running app has nothing to find. Observed
2026-08-23: two orphaned watchers, both over two days old, one with 17 open handles
into the build output and zero CPU. It ignored `SIGTERM` and needed `kill -9`.

**Detect:**

```bash
# Any watcher whose parent is PID 1 is orphaned, whether or not it is functional.
pgrep -f "dotnet-watch.dll.*--project ProjectCeres" | while read -r p; do
  echo "pid=$p ppid=$(ps -o ppid= -p "$p" | tr -d ' ')"
done
```

**Fix — and prevent it recurring:** start the watcher through

```bash
tools/dev-watch.sh
```

It reaps orphaned watchers and port squatters before starting, and kills its own
process group on exit so neither the watcher nor the app can outlive the terminal.
Note it reaps *every* orphaned watcher for this project, including one that is
still working — if you have a watcher you want to keep, stop it yourself first.

## Related

- `dotnet watch` port conflicts (`AddressInUseException`) usually mean an orphaned
  app process outlived its watcher — the mirror image of Symptom 3. Check with
  `lsof -nP -iTCP:7081 -sTCP:LISTEN`, then `kill -9` the owner. Same family of
  problem: something from an earlier run is still in the way.
