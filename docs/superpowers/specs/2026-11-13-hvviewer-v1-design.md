# Hyper-V Viewer v1 — Design (Phase 1: Hyper-V host)

- **Date:** 2026-11-13 (replaces the first draft of the same date)
- **Status:** Approved (owner delegated decisions, 2026-11-13)
- **Repo:** `dooshun13/Hyper-V-Viewer`. Code base: Eviewer copied and renamed to `HvViewer.*` (already on `main`).
- **Phase 2 (later plan):** SCVMM. Not part of this document except where noted.

## 1. Intent

Admins and engineers troubleshoot Hyper-V every day. The question they ask most is:

> **Why did this VM restart or power off, and where has it been over the last 7 days?**

Today that means opening a dozen event logs on the host and lining events up by hand. Hyper-V Viewer does the lining up: for any VM it shows, in time order, every start, stop, reset, crash and move, using **Windows' own log messages**, and adds a short hint **only when the cause is certain**.

It runs **locally on the Hyper-V host** and is strictly read-only, so it is safe on production hosts.

## 2. Goals, non-goals, anti-goals

**Goals**
1. Pick a VM, see its last 7 days of lifecycle events (start, shut down, turn off, save, reset, crash, migrate, cluster move) with the original messages, in one list.
2. See host-level events that affect every VM on the host (host reboot or power loss, Hyper-V management service stop, cluster node loss) in each VM's list.
3. See where the VM has been (this host's view of arrivals and departures).
4. Know how far back the logs actually go.
5. Keep every Eviewer feature (Home, Events search, detail, Saved, Settings) and add Hyper-V logs and troubleshooters.

**Non-goals (Phase 1)**
- SCVMM (Phase 2: VMM job history with read-only `Get-SCJob` / `Find-SCJob`, plus the VMM event logs).
- Running inside VMs (guest mode). Host only.
- Remote hosts or multi-host views.
- Any action on a VM or the host.

**Anti-goals (a failure even if it works)**
- The app **states a cause it is not sure of**. A wrong hint is worse than none.
- The app changes anything on the host, or slows a busy host down.
- The app silently shows less history than asked for (for example because a log is small) without saying so.

## 3. Constraints

- **Local only**: reads this host's event logs and this host's WMI.
- **Guardrails** (from Eviewer's original design, restored): viewer only; Hyper-V WMI with `SELECT` queries only, no WMI method calls; below-normal priority; at most 2 parallel readers; scans stop at 500,000 events or 1 GB managed memory and keep partial results; only files written are `%AppData%\HvViewer\HvViewer.settings.json` and exports the user saves; a safety test scans the compiled code for forbidden calls.
- **Language**: English UI; must work on French Windows. Match events by provider + event ID + log internal name, never by message text.
- **Log retention**: Hyper-V logs are often small and may not reach back 7 days. The app cannot change log sizes (viewer only).
- **Platform**: Windows Server 2016–2025 with Hyper-V (Desktop Experience); Windows 10/11 with Hyper-V for testing. .NET 10, WPF + WPF-UI, as Eviewer.

## 4. Approaches considered

| Approach | Trade-off | Decision |
|---|---|---|
| **Smart engine**: a VM story engine reads known lifecycle events into one list per VM, shows the original messages, adds a hint only when a rule is certain | Biggest build; the hint rules need care | **Chosen** by the owner |
| **Smart search**: preset searches per VM showing raw events | Cheap, but the admin still works out the cause; no placement view | Not chosen; kept as the "Show all events for this VM" link |

Owner decisions: local only; host only (no guest mode); smart engine; **show the log messages, add a hint only when sure**; SCVMM later.

## 5. What the admin sees

### 5.1 VMs screen (the main screen)

```
 VMs on HOST01                                   history back to: 2026-11-08 (Worker-Admin)
 ┌───────────────┬─────────┬─────────┬─────────────────────────────────────────┐
 │ Name          │ State   │ Uptime  │ Last stop or restart                    │
 ├───────────────┼─────────┼─────────┼─────────────────────────────────────────┤
 │ SQL01         │ Running │ 2 h     │ 11-13 02:14  Host restarted             │
 │ WEB02         │ Running │ 6 d     │ 11-07 21:00  'WEB02' was shut down …    │
 │ DC03          │ Off     │ —       │ 11-13 09:41  'DC03' was turned off …    │
 └───────────────┴─────────┴─────────┴─────────────────────────────────────────┘
   [Story]  [All events]  [Copy VM ID]                                  [Refresh]
```

- Read-only list from WMI: name, state, health, uptime, vCPUs, memory, generation, configuration version, VM ID.
- **Last stop or restart**: the newest lifecycle event for the VM, as its original message (short form), with time.
- **Story** opens 5.2. **All events** opens Events filtered on the VM ID (all Hyper-V logs + System, last 7 days).

### 5.2 VM story (per VM, default last 7 days)

```
 SQL01 — story, last 7 days                         [1 d] [7 d] [Custom]
 2026-11-13 02:16  Worker-Admin 18500   'SQL01' started successfully. (Virtual machine ID …)
 2026-11-13 02:14  System 6005          The Event log service was started.
 2026-11-13 02:13  System 41            The system has rebooted without cleanly shutting down first…
                   Hint: the host restarted without a clean shutdown; every VM on it stopped.
 2026-11-10 18:02  System 1641          Cluster role 'SQL01' is moving from NODE2 to HOST01.
 ⚠ Worker-Admin only goes back to 2026-11-08 09:12.                        [Raw events]
```

- Each line: time, log (short name), event ID, **Windows' message as formatted** (may be French), and the row opens the normal event detail.
- **Hint** lines appear only under rows where a hint rule is certain (§6.3). No hint otherwise.
- If a VM stopped or restarted and **no host-side cause** exists, no hint is invented. The row simply has no hint. (A fixed note at the top of the screen explains that a restart started inside the guest may leave no event on the host.)
- **History coverage**: a warning line for any log whose oldest event is newer than the start of the range.
- Export the story as HTML or CSV (Save dialog), and "Copy for ticket" (plain text).

### 5.3 Where the VM has been (inside the story)

A small panel above the list: current host (this host), then each move this host recorded, newest first: "moved here from NODE2 at 11-10 18:02", "moved to NODE3 at …", using the move events in §6.1 and their own messages. Panel title says "as seen from this host". Full cross-host history comes with SCVMM in Phase 2.

### 5.4 Everything else

- Home, Events, Saved, Settings from Eviewer, with: Hyper-V logs in a "Hyper-V" log-picker folder; nine Hyper-V troubleshooters plus Failover cluster on Home (tiles capped at 12, three rows).
- A **VMs** item in the icon bar between Home and Events.

## 6. The VM story engine

### 6.1 Lifecycle event catalog

A JSON file (`src/HvViewer.Rules/Lifecycle/lifecycle.json`, embedded, schema-checked like the troubleshooters) lists the events that belong in a story. Each entry: provider, event ID, log, **kind** (`start`, `shutdown`, `turnoff`, `save`, `reset`, `crash`, `migrate`, `clustermove`, `hostdown`, `hostup`, `servicedown`), **scope** (`vm` = carries a VM ID; `host` = affects all VMs on the host), and **verified** (`true` when checked on a real host).

Starting list (owner or builder confirms the ones marked *verify* on a real Server 2022/2025 host before shipping; unverified entries stay in the file with `verified: false` and are still shown, but never produce a hint):

| Kind | Scope | Log | Provider | ID | Status |
|---|---|---|---|---|---|
| start | vm | Hyper-V-Worker-Admin | Hyper-V-Worker | 18500 | verified (Microsoft docs) |
| turnoff | vm | Hyper-V-Worker-Admin | Hyper-V-Worker | 18502 | verified (Microsoft docs) |
| shutdown | vm | Hyper-V-Worker-Admin | Hyper-V-Worker | 18504 | verified ID; timing ambiguous |
| save | vm | Hyper-V-Worker-Admin | Hyper-V-Worker | 18508 | *verify* |
| reset | vm | Hyper-V-Worker-Admin | Hyper-V-Worker | 18560 | verified |
| crash | vm | Hyper-V-Worker-Admin | Hyper-V-Worker | 18590 | verified |
| migrate | vm | Hyper-V-VMMS-Admin | Hyper-V-VMMS | migration started / completed IDs | *verify* (find on a host) |
| migrate (failed) | vm | Hyper-V-High-Availability-Admin, System | Hyper-V-High-Availability | 21502 | verified |
| migrate (failed) | vm | Hyper-V-VMMS-Admin | Hyper-V-VMMS | 21024, 21026 | verified |
| clustermove | vm | System (and FailoverClustering/Operational) | FailoverClustering | 1641 | verified ID; *verify* log |
| hostdown | host | System | Kernel-Power | 41 | verified |
| hostdown | host | System | EventLog | 6008 | verified |
| hostdown | host | System | User32 | 1074 | verified (planned restart, has user and reason) |
| hostdown | host | System | EventLog | 6006 | verified (clean stop of event log service) |
| hostup | host | System | EventLog | 6005 | verified |
| servicedown | host | Hyper-V-VMMS-Admin | Hyper-V-VMMS | VMMS shutting down with VMs running | *verify* ID |
| clustermove | host | System | FailoverClustering | 1135 | verified (node removed) |

### 6.2 Building a story

1. Range: default last 7 days (Every query has a time range).
2. Read only the catalog's IDs from each catalog log (XPath ID filter) within the range, under the resource guard.
3. **VM events**: keep events whose event XML contains the VM's ID (GUID, case-insensitive). The ID is language-neutral.
4. **Host events**: add every `host` scope event in the range to every VM that was running at that time on this host. "Was running" is inferred from the VM's own start/stop events; if unknown, the host event is still shown (it may have affected the VM), but with no hint.
5. Sort newest first; group identical consecutive events (×N) as Eviewer does.
6. Messages come from `MessageCache` / `FormatMessage`, exactly as Windows formats them. If a message cannot be formatted, show the event data fields (Eviewer's fallback).
7. **Coverage**: for each catalog log, read its oldest event inside the range (one query, oldest first, one result); if it is much newer than the range start and the log is near its maximum size, add the warning line. (Still a time-ranged query.)

### 6.3 Hints: only when sure

A hint is a fixed English sentence attached to one row. Hint rules live in the same JSON. A rule produces a hint **only if all of these hold**:

- every event it relies on has `verified: true`;
- the match is by provider + ID + log (never message text), plus the VM ID where the rule is about one VM;
- the events fall inside the rule's time window (for example, the VM's stop is within 5 minutes before a host `hostdown` event);
- there is no competing explanation in the same window (for example, both a `crash` and a `hostdown` → no hint).

Starting hint rules (all others are left out of v1):

| Rule | Shown under | Hint text (draft) |
|---|---|---|
| Host unclean restart | Kernel-Power 41 | "The host restarted without a clean shutdown. Every VM running on it stopped." |
| Host planned restart | User32 1074 | "The host was restarted or shut down by the user or process named in this message." |
| Guest crash | Worker 18590 | "The guest operating system crashed. The error codes are the guest's stop code." |
| Triple-fault reset | Worker 18560 | "Hyper-V reset this VM after a fatal processor error inside the guest." |
| Turned off | Worker 18502 | "The VM was turned off (like pulling the power), not shut down from inside." |
| Cluster move after node loss | FailoverClustering 1641 within 10 min after 1135 | "This move followed a cluster node being removed from the cluster." |

Hint texts are written by us, short and plain, never copied from Microsoft.

### 6.4 Placement (where it has been)

From the story's `migrate` and `clustermove` events for the VM: direction (here / away) and the other host's name come from the **event data fields** (not the message text). If the other host's name is not in the event data, the panel shows the event's message and no extracted name.

## 7. Other features (decided 2026-11-13; the owner delegated the choice)

In Phase 1: 1 host pulse, 2 error-code decoder, 3 checkpoints, 6 copy as PowerShell. Deferred to a later plan: 4 replica health, 5 since your last visit.

1. **Host pulse** on Home: hypervisor running, VMMS running, cluster node state, host free memory, free space on VM storage paths and CSVs, virtual switch NIC links, pending reboot. Each green / amber / red with one line of why. All read-only queries.
2. **Error-code decoder**: HRESULTs found in messages (e.g. `0x80070005`, `0x8007274C`) get their standard name and meaning from a built-in table, shown in the detail view only.
3. **Checkpoints**: VMs with checkpoints, their age and type, and recovery checkpoints left by backup software. WMI `SELECT` only.
4. **Replica health**: table of replica state, health and last replication time. WMI `SELECT` only.
5. **Since your last visit**: on Home, what is new since the app was last opened (time stored in the settings file).
6. **Copy as PowerShell**: each story and search offers the equivalent `Get-WinEvent -FilterXml` command.

## 8. Testing

- All Eviewer tests carried over (one known failure after the rename is Task 1 of the plan).
- Story engine: pure tests with fake events: VM filtering by ID, host events attached to running VMs, sorting and grouping, coverage warning, and **every hint rule both firing and not firing** (missing event, outside window, competing explanation, unverified event).
- Catalog file: schema test like the troubleshooter rules.
- Placement: extraction from event data; message-only fallback.
- VMs screen, story screen: view-model tests with fake inventory and fake events.
- Safety test, guard tests, French Windows sample log (when available).

## 9. Left to the builder

- Layout details of the VMs and story screens, following the Eviewer theme.
- Finding and confirming the IDs marked *verify* in §6.1 on a real host, and the 1641 log.
- Event data field names for migration and cluster move events (read from real event XML).
- Exact HRESULT table content for §7.2.

## 10. Decisions taken for the owner (2026-11-13) and open questions

- Limits kept at 500,000 events and 1 GB (revisit after the release-checklist test on a busy host).
- §7 scope as above.
- Open: confirm the *verify* IDs in §6.1 and the 1641 event-data field names on a real host.
