# Eviewer v1 — Design Spec

- **Date:** 2026-11-11
- **Status:** Approved (2026-11-11)
- **Repo:** `dooshun13/Eviewer`
- **UI mockups:** "Eviewer – UI mockups" design canvas (Home dashboard, Events search). The Compare screen on the canvas is **out of scope** for v1.

## 1. Purpose

Windows Event Viewer is bulky and hard to read. Eviewer is a portable, read-only Windows event viewer that lets an everyday user or an admin answer, within about 30 seconds of opening it: *did something go wrong recently, and where do I look?*

**Users:** everyday Windows users doing basic troubleshooting, and system admins doing deeper troubleshooting on PCs and servers, including production machines.

**Success criteria**

1. Copy the `Eviewer` folder to a machine, run `Eviewer.exe`, and see a useful 24-hour summary with no installation.
2. Find any event by text and by an exact from/to date and time.
3. Every important event comes with a plain-English explanation and a next step.
4. Running Eviewer has no noticeable impact on the machine and changes nothing on it.

## 2. Scope

### In scope (v1)

- **Home dashboard (last 24 h, also 1 h / 7 d / custom):** system card, health checks, events by severity with comparison to the previous period, errors and warnings per hour chart, "Needs attention" tiles, "What happened before the last restart", "New & unusual" (not seen in the previous 7 days).
- **Events search:**
  - Find text with match case, whole word and regex options; search in message, all fields, or raw XML.
  - Date and time: presets (last 15 min, last hour, last 24 h, today, yesterday, since last boot) and a custom range with from/to date and time to the second.
  - Filters: event IDs (a leading `-` excludes an ID), source, log category dropdown (Windows Logs / Applications and Services Logs tree / By troubleshooter; Analytic and Debug logs hidden behind a toggle), level, task category.
  - The search shown as one line of query text that can be copied or typed directly.
  - Results: date and time per row, a mini chart of when matches happened, grouping of identical events (×N), matched text highlighted.
  - Export results to CSV and HTML.
- **Event detail:** tabs Explained / Message / XML / Raw data, "Around this event" (all logs, ±60 s), copy for ticket.
- **Troubleshooters (11):** Boot & shutdown, Crashes & hangs, Disk & storage, Logons & accounts, Windows Update, Services, Memory & performance, Network & DNS, Group Policy, RDP / Remote Desktop, Printing.
- **System detection:** Windows version, edition, build, client or server, Server Core, VM or physical, domain membership, basic installed roles.
- **Health checks:** pending reboot, uptime and last boot type, last installed update, low disk space, time-sync status, certificates expiring soon.
- **Saved views:** stored with settings next to the exe.
- **Time zone switch:** this PC / the server / UTC.
- **Themes:** dark and light, plus follow Windows.
- **Admin and non-admin modes.**

### Out of scope

These are not part of Eviewer. Any of them would be a separate app built on the Eviewer engine.

- Compare (server vs server, period vs period)
- Opening `.evtx` files, live tail
- Remote servers and multi-server views
- Modules (Hyper-V, Failover Clustering, SCVMM, WSUS, etc.)
- Automatic fixes, background monitoring, alerts, installer
- Translated UI (the UI is English only)

## 3. Platform and packaging

- **Supported OS:** Windows 10 and 11, Windows Server 2016, 2019, 2022, 2025, and Windows Server 2012 R2. Server Core is not supported for the UI (it has no desktop shell).
- **Stack:** .NET 10 (LTS), WPF, WPF-UI (Fluent / Windows 11 style). x64.
- **Packaging:** self-contained **portable folder** (`Eviewer.exe` plus its DLLs), distributed as a zip. Built with ReadyToRun for fast start-up. Not a single-file exe, so nothing is unpacked to the temp folder.
- **Build:** GitHub Actions on a Windows runner builds, tests and attaches the zip to each release.
- **Language:** English UI. Must work correctly on non-English Windows, French in particular (see §8).

## 4. Scope of use (amended 2026-11-11)

Eviewer is a personal tool that runs only on the owner's own PC. It is **not** meant for production servers or other people's machines, so the original "no impact on the machine" guardrails are dropped:

- No forbidden-API safety test, no process-priority or background-mode rules, no parallel-reader cap, no event or memory budget, no "leave no trace" rule, and no `--no-save` mode.
- Settings are saved to `%AppData%\Eviewer\Eviewer.settings.json`. Temp files, caches and network access are allowed where they help.
- **Kept:** Eviewer is a viewer. It reads and searches event logs and never clears, rewrites or reconfigures them, never changes the registry, services or system settings, and never uses `Win32_Product`. Every query still has a time range.

## 4a. Changes after first use (2026-11-12)

Decided by the owner after trying the first build. They override anything below that says otherwise.

- **Home fits one screen.** No scroll bar: the page is laid out at a fixed size and scaled to the window. Lists on Home show the first three rows (the rest are one click away in Events). "New & unusual" takes the full width when the restart card is hidden.
- **Health checks: five, not six.** The certificates check is removed (pending reboot, uptime, last update, free disk space and time sync stay).
- **System card shows hardware:** processor model, cores, threads and clock speed; memory size, memory in use and the module layout. No installed-roles line on Home (role detection stays in the library).
- **Time zone switch** (this PC / UTC) lives in Settings only, not in the top bar. The Night / Day / Auto switch is on Home (and in Settings).
- The restart card is named "Before the last restart".
- **Night / Day / Auto switch is in the top bar on every screen** (not on Home only). The top bar also has "Restart as administrator" for a standard user.
- **The Fix-it (Troubleshooters) page is removed.** The troubleshooter rules still drive the Home tiles and the "By troubleshooter" folder in the log picker.
- **Events search panel** fits with no scroll bar; From / To open a calendar and time picker (also used for Home's Custom range); the log dropdown has Select all / Clear all; the panel has Clear filters; skipped logs show as one summary line with details in a tooltip.
- See `docs/status.md` for the full list of changes, known issues and open items.

## 5. Architecture

```
Eviewer.sln
├─ src/Eviewer.Core      engine: read, filter, search events (no UI)
├─ src/Eviewer.System    machine info: OS, roles, health checks (read-only)
├─ src/Eviewer.Rules     troubleshooters + event explanations (JSON rule files)
├─ src/Eviewer.App       WPF + WPF-UI screens only, no business logic
└─ tests/Eviewer.Tests   unit, sample-log, rule, safety and performance tests
```

Dependencies go one way: **App → Rules, System → Core.** Core, System and Rules have no UI dependency, so other apps can reuse them.

### 5.1 Eviewer.Core

- **`IEventSource`:** one shared interface for reading events, with two implementations:
  - **`ManagedEventSource`** uses .NET's `System.Diagnostics.Eventing.Reader`. It is used for single events, detail views and less frequent queries.
  - **`NativeEventSource`** calls Windows' event log API (`wevtapi.dll`) directly: `EvtQuery`, batched `EvtNext`, `EvtRender` with a system-values render context that pulls only basic fields, and `EvtFormatMessage` only on demand. It is used for the dashboard scan and search.
- **`EventRecordLite`:** the basic fields every event carries: time (UTC), event ID, level (as a number), source name, log name, computer, record ID, task, keywords, and a handle or bookmark used to fetch the full event later.
- **`EventQuery`:** text, text options, target (message / all fields / XML), from/to time, IDs to include and exclude, sources, logs, levels and task categories. It turns into:
  1. a **native XPath filter** for time, ID, level and source, sent to Windows; and
  2. a **text filter** applied afterwards, in-process.
- **`QueryText`:** reads and writes the one-line query text (e.g. `"Disk 3" word from:2026-11-10 22:00:00 to:2026-11-11 04:00:00 id:153,-7036 level:>=warning`).
- **`MessageCache`:** builds an event's message only when needed; in memory only, cleared on close.
- **`ResourceGuard`:** process priority and background mode, the limit on parallel readers, the event-count and memory limit, and cancellation.
- **`Grouping`:** groups identical events (same log, source, ID and message template) within a result set.

### 5.2 Eviewer.System

- **`SystemInfo`:** OS product name, edition, version, build number with UBR, installation type (Client / Server / Server Core), VM detection (from the computer model and manufacturer), domain membership, last boot time, uptime.
- **`RoleDetection`:** basic roles from installed server features (on servers) and well-known services (IIS, File Server, Print Server, AD DS, DNS, DHCP and others). Detection only; v1 has no role-specific troubleshooting.
- **`HealthChecks`:** each check returns OK / Warning / Problem plus a short text:
  - Pending reboot (Component Based Servicing, Windows Update and pending file-rename registry keys)
  - Uptime and whether the last boot was clean (from Kernel-Power 41 / EventLog 6008)
  - Last installed update
  - Free disk space per fixed volume (warning below 15%, problem below 10%)
  - Time sync (last successful sync from the Time-Service events and registry)
  - Certificates in LocalMachine\My expiring within 30 days

### 5.3 Eviewer.Rules

- Each troubleshooter is a JSON file embedded in the app. Example of the shape (texts shortened):
  ```json
  {
    "id": "boot-shutdown",
    "title": "Boot & shutdown",
    "logs": ["System"],
    "events": [
      {
        "provider": "Microsoft-Windows-Kernel-Power",
        "id": 41,
        "severity": "critical",
        "tile": "unexpected-restarts",
        "title": "System rebooted without shutting down cleanly",
        "meaning": "…",
        "causes": "…",
        "next": "…",
        "docs": "https://learn.microsoft.com/…"
      }
    ]
  }
  ```
- Rules match on **provider name and event ID** (and the log's internal name), never on message text or translated names.
- The rule format is versioned (`"schema": 1`) so other apps can use it later.
- v1 ships eleven rule files: `boot-shutdown`, `crashes-hangs`, `disk-storage`, `logons-accounts`, `windows-update`, `services`, `memory-performance`, `network-dns`, `group-policy`, `rdp`, `printing`. The exact event lists are decided in the implementation plan and reviewed by Dooshun.
- Explanations are written for Eviewer, never copied from Microsoft, and each links to the official documentation.

### 5.4 Eviewer.App

- MVVM; screens contain no business logic.
- **Shell:** icon bar on the left (Home, Events, Troubleshooters, Saved, Settings), top bar with the machine name, search box (Ctrl+K), time-zone switch, Read-only badge, Admin or Standard-user badge, and Eviewer's own CPU and memory in the status bar.
- **Screens:** Home, Events (search, results, detail panel), Troubleshooters (list; opening one runs its search), Saved views, Settings (theme, time zone, limits, settings location, about).
- **States:** loading with progress, no results (with suggestions to widen the range), standard user (admin-only logs greyed out, with "Run as administrator to include"), logs skipped, error screen with copyable details.
- Results list drawn only for rows on screen (UI virtualisation).

## 6. Data flow

### 6.1 Start-up and dashboard

1. The window opens; the system card fills from `SystemInfo` (registry reads only).
2. The rules decide the logs and event IDs the dashboard needs (the union of all eleven troubleshooters; roughly 15–20 logs).
3. `NativeEventSource` reads each log for the selected period, one log at a time, pulling basic fields only and formatting no messages.
4. Each event is matched against the rules; tiles update as each log finishes.
5. The previous period is counted the same way, for the comparison figures and "New & unusual" (previous 7 days, counts by provider and ID only).
6. "What happened before the last restart": if a Kernel-Power 41 or EventLog 6008 falls in the period, events of level warning and above from all scanned logs in the 15 minutes before it are listed in time order.
7. Health checks run alongside the scan.

### 6.2 Search

1. UI input (or typed query text) becomes an `EventQuery`.
2. The XPath part runs natively, per selected log.
3. If text is set, messages (or XML, for the XML and all-fields targets) are built in batches of 256 and matched; results stream to the UI with a progress line and Cancel.
4. Results are grouped and the matched text is highlighted.

### 6.3 Event detail

- The message comes from `MessageCache`; the explanation from the rules (by provider and ID).
- "Around this event" runs a ±60 s query across all logs readable by the user.
- If formatting the message fails, the Raw data tab (event data fields and XML) opens instead.

## 7. Performance targets

Measured on a typical server (4 vCPU, 8 GB RAM, about 1M events across logs):

| Target | Goal |
|---|---|
| Window open and usable | < 2 s |
| 24 h dashboard complete | < 10 s, tiles appear progressively |
| ID, level or time filter on loaded data | < 200 ms |
| Text search over ~50,000 events | < 5 s, cancellable |
| Memory | < 300 MB typical, enforced limit |
| CPU | below-normal priority, at most 2 parallel readers |

## 8. Non-English Windows (French)

- Rules use provider name, event ID and the log's internal name, none of which change with language.
- Level is read as a number (1 Critical, 2 Error, 3 Warning, 4 Information, 5 Verbose); the UI shows English labels.
- Messages are shown as Windows formats them (French on French Windows); Eviewer's explanations stay English.
- Text search runs on the original text as is. Matching is accent-sensitive by default, with an option to ignore accents.
- Dates use ISO format (`2026-11-10 22:00:00`) for input and query text; display follows the Windows region settings.

## 9. Settings

- **Location:** `Eviewer.settings.json` next to `Eviewer.exe`.
- **Read-only folder:** a notice asks whether to save in `%AppData%\Eviewer` this time, or to save nothing for this session.
- **`--no-save`:** nothing is written; saved views can still be exported and imported manually.
- **Contents:** theme, time-zone mode, default period, limits, saved views (name plus query text).
- Settings are never written to the registry.

## 10. Error handling

| Situation | Behaviour |
|---|---|
| Standard user | Works; admin-only logs (e.g. Security) greyed out with "Run as administrator to include". |
| A log is missing, disabled or access is denied | Skipped and listed under "N logs skipped"; the scan continues. |
| Message can't be formatted | "Message not available on this PC" plus the Raw data tab. |
| Event or memory limit reached | Stop, keep partial results, ask to narrow the range or raise the limit. |
| Search cancelled | Partial results stay on screen. |
| Bad or invalid built-in rule | Caught by tests at build time; at run time the rule is skipped and logged in the About screen. |
| Settings folder read-only | Asks: save to the profile, or save nothing. |
| Unhandled exception | Error screen with copyable details; nothing written to disk. |

## 11. Testing

**Automated (GitHub Actions, Windows runner, every push):**

- **Unit tests (fake events):** query builder (EventQuery to XPath), query text read and write, text filter (match case, whole word, regex, accent option), grouping, time-zone conversion, limits, health-check logic with fake registry and system data.
- **Sample-log tests:** sample `.evtx` files under `tests/samples/`: unexpected restart (41, 6008, 1001), disk retries (153), failed logons and lockout (4625, 4740), app crash and hang (1000, 1002), service crash (7031, 7034), low memory (2004), DNS timeout (1014), Group Policy failure (1129), Windows Update failure (20), **a French Windows System log**, and events whose message can't be formatted. Each test checks the expected tiles, explanations, grouping and fallback.
- **Rule tests:** every rule file is checked against the schema, with required fields and no duplicate IDs.
- **Safety test:** scans the compiled code for forbidden write functions; the build fails if any are found.
- **Performance test:** a generated large `.evtx` must load and search within the §7 targets (with a tolerance for CI hardware).

**Manual before each release (`docs/release-checklist.md`):** Windows 11, Windows 10, Server 2022, Server 2016, Server 2012 R2; standard user and admin; run from USB and from a read-only share; watch Task Manager during a 24 h scan on a busy server.

## 12. Open items for the implementation plan

- Exact event lists for the eleven troubleshooters (to be reviewed by Dooshun).
- Exact list of forbidden write functions for the safety test.
- Sample `.evtx` files: export from lab machines, including one French Windows Server.
