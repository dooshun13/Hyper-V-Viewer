# Eviewer: status, changes and to-do

Last updated: 2026-11-12. Branch `main` = `build/v1` (commit `00608f2`). Not tagged or released yet.

Source of truth for the design is `docs/superpowers/specs/2026-11-11-eviewer-v1-design.md` (see §4 and §4a for the owner's later decisions). This file records what has been built, what changed after first use, and what is left.

## 1. What exists

A personal, read-only Windows event viewer for the owner's own PC (.NET 10, WPF + WPF-UI, MVVM).

| Screen | What it does |
|---|---|
| **Home** | One-screen dashboard (scales to the window, no scroll bar): this PC (OS, uptime, last boot, CPU, RAM), events by severity, five health checks, errors and warnings over time, "Needs attention" tiles (10 troubleshooter groups), "New & unusual", "Before the last restart" (only after an unexpected restart). Range: 1 h / 24 h / 7 d / Custom (calendar + time picker, then Apply). |
| **Events** | Search panel (find text with Aa / ab / .\* / é=e options, date presets and From/To pickers, event IDs, source, log category tree, task category, level, one-line query, Save view, Export CSV/HTML). Results list with its own scroll bar, histogram, grouping, detail view with explanation, raw XML and "around this event". |
| **Saved** | Saved searches. |
| **Settings** | Theme (Auto / Night / Day), time zone (this PC / UTC), default range, rules loaded, about. |

Top bar (all screens): machine name, search box (Ctrl+K), Night / Day / Auto switch, access badge, Read-only badge, and "Restart as administrator" (standard user only).

Projects: `Eviewer.Core` (engine), `Eviewer.System` (namespace `Eviewer.Machine`; OS, roles, health, hardware via read-only WMI), `Eviewer.Rules` (11 embedded JSON troubleshooters), `Eviewer.App` (screens), tests in `tests/Eviewer.Tests` (387) and `tests/Eviewer.App.Tests` (200).

Rules kept from `CLAUDE.md`: viewer only (never clear or write logs, registry, services, tasks, drivers or settings), no `Win32_Product`, every query has a time range, the only files written are `%AppData%\Eviewer\Eviewer.settings.json` and exports saved by the user.

## 2. Changes made after the first build (all done)

1. **Guardrails dropped** (the app only runs on the owner's PC): safety test, resource limits, "leave no trace" and `--no-save` removed; spec §4, CLAUDE.md and the plan amended.
2. **Xdisk look:** Night (default dark) and Day palettes ported from `dooshun13/Xdisk`; accent cyan, severity colours, sidebar accent bar, 10 px cards.
3. **Night / Day / Auto switch:** first on Home, now in the top bar on every screen (same setting as Settings).
4. **Home redesign:** fits one screen; certificates check removed; Hyper-V / roles line removed; CPU model, cores and RAM in the PC card; lists capped at 3 rows with "+N more in Events"; restart card renamed "Before the last restart".
5. **Top bar:** Local/UTC switch removed (stays in Settings).
6. **Fix-it page removed.** The troubleshooter rules still drive the Home tiles and the "By troubleshooter" folder in the log picker.
7. **Events search panel:** fits with no scroll bar (rows merged, labels moved to tooltips); Event IDs box wraps so the whole list shows; "Clear filters" button inside the panel.
8. **Date and time picker** (`DateTimeField`): click From / To for a calendar plus hour / minute / second lists; used on Events and on Home's Custom range. Boxes are wide enough for the full timestamp.
9. **Log category dropdown:** "Select all" and "Clear all" (Select all respects the filter box and skips Security for a standard user).
10. **Task category** dropdown: visible scroll bar, tooltip explains what a task is.
11. **Skipped logs** shown as one summary line with the full list in a tooltip (analytic logs that cannot be read no longer fill the screen); the results list scroll bar is always visible.
12. **Restart as administrator** button (relaunches elevated after the Windows prompt; declining the prompt changes nothing).
13. **Performance:** search over about 42 000 events went from 15.2 s to 3.2 s (single format call into a reusable buffer, per-batch parallel formatting, event-data text for message-less events).
14. **Error window** for unhandled exceptions (copyable details, nothing written to disk).

## 3. Known issues and limits

- **Security log and logon events** need administrator rights (or membership of the Event Log Readers group). A standard user sees "Access denied: run as administrator to include". The Logons tile shows "—".
- **Analytic and Debug logs:** Windows cannot read most of them through the normal API. They are hidden by default; ticking "Analytic & Debug logs" and Select all produces hundreds of "skipped" logs. This is expected.
- **Select all** over about 1 200 logs gives a very long one-line query; searching that many logs is slow. Prefer ticking folders.
- Single-record lookups (detail message, XML, event data) query by `EventRecordID` without a time range (deliberately deferred).
- Home's "previous 7 days" comparison query omits rule IDs for logs with more than 15 IDs (harmless).
- A leftover elevated `Eviewer.exe` process cannot be ended from a standard session; end it from an administrator PowerShell with `Stop-Process -Id <pid> -Force` (it blocks republishing to `out\Eviewer`; `out\Eviewer-new` was used as a workaround and is git-ignored).
- Dead code: `HomeViewModel.IsThemeNight/IsThemeDay/IsThemeAuto` are no longer shown on Home (the switch moved to the top bar). Remove with their tests when convenient.

## 4. To do and to decide

**Owner decisions**
- [ ] Tag `v1.0.0-beta.1`? (Pushing a tag makes CI build and publish a GitHub release.)
- [ ] é=e button: flip to "match accents" so all four option buttons mean "more exact when lit" (changes the default: accents would be ignored unless turned on; query keyword `noaccent` would change), or only reword the tooltip.
- [ ] Review Ntfs event 98 severity (set to info) and all rule explanation texts (written by Claude, not copied from Microsoft).
- [ ] Should "Clear filters" also reset the date range and log selection?
- [ ] Delete the `build/v1` branch on GitHub (it is identical to `main`).

**Testing still open**
- [ ] Export the sample logs listed in `tests/samples/README.md` (only `perf.evtx` exists; other sample-log tests skip until the files are added, including a French Windows System log).
- [ ] Run `docs/release-checklist.md` (other Windows versions, standard user vs administrator, long scan while watching Task Manager).
- [ ] Try "Restart as administrator" and confirm Security can then be ticked and the Logons tile fills in (the button builds and is unit-tested only for the view-model, not clicked).
- [ ] Add tests for `CanRestartAsAdmin` and the top-bar theme properties.

**Possible next work**
- Events page polish (the owner has been reviewing it piece by piece).
- Optional fresh-agent code review of the whole branch (`/code-review`).
- Clean up the dead Home theme properties (see §3).

## 5. How to build and run

```
dotnet build Eviewer.sln -c Release
dotnet test  Eviewer.sln -c Release
dotnet publish src/Eviewer.App -c Release -r win-x64 --self-contained false -o out/Eviewer
out\Eviewer\Eviewer.exe
```
