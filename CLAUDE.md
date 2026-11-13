# CLAUDE.md — Hyper-V Viewer

Hyper-V Viewer (`HvViewer`) is a **read-only event-log viewer for Windows Server Hyper-V hosts**, built for debugging Hyper-V problems: VMs that will not start or crash, failed live migrations, checkpoints, replication, virtual switches, storage, integration services and failover clusters. It also shows a read-only list of the host's VMs and their status.

It is built from **Eviewer** (`dooshun13/eviewer`, copied and renamed on 2026-11-13). Unlike Eviewer, it is meant to run on **production Hyper-V hosts**, so the production-safety guardrails are back.

**Source of truth:** `docs/superpowers/specs/2026-11-13-hvviewer-v1-design.md`. Read it before starting any work. If a request conflicts with the spec, stop and ask.
**Build plan:** `docs/superpowers/plans/2026-11-13-hvviewer-v1.md`.
**Eviewer history:** `docs/eviewer-base/` (the original spec, plan and status; reference only).

## Rules (non-negotiable)

1. **Viewer only.** Never clear, write to or reconfigure event logs (`EvtClearLog`, `EvtSetChannelConfigProperty`, `EvtSaveChannelConfig`, `EventLogSession.ClearLog`, etc.). Never write to the registry, create or change services, scheduled tasks or drivers, or change system settings.
2. **Never change a VM or the host's Hyper-V configuration.** WMI in `root\virtualization\v2` is used for **queries only** (`SELECT`). Never call WMI methods (`InvokeMethod`, `RequestStateChange`, `DefineSystem`, `ModifySystemSettings`, `DestroySystem`, checkpoint, migration or replication methods). Never use `Win32_Product`.
3. **Safe on a busy host.** The process runs at below-normal priority, uses at most 2 parallel readers, and every scan and search stops at the event limit and the memory limit (`ResourceGuard`), keeping partial results and saying so.
4. **Every query has a time range.**
5. **Files written:** the settings file in `%AppData%\HvViewer` and export files the user saves through a Save dialog. Nothing else: no temp files, no logs of its own, no network access.
6. A **safety test** scans the compiled assemblies for forbidden calls (rules 1 and 2). It must stay green; never weaken it to make a change pass.

## Language and localisation

- The UI is **English only**.
- It must work correctly on **non-English Windows (French in particular)**. Match events by **provider name + event ID + the log's internal name**, never by message text or translated display names. Read the level as a number (1–5).
- Show Windows' own messages as formatted (they may be in French). Explanations are in English.

## Tech stack

- .NET 10 (LTS), C#, WPF, **WPF-UI** (Fluent / Windows 11 style), MVVM (CommunityToolkit.Mvvm).
- Event reading: `System.Diagnostics.Eventing.Reader` (`ManagedEventSource`) **and** direct P/Invoke to `wevtapi.dll` (`NativeEventSource`).
- VM list: read-only WMI queries (`System.Management`) against `root\virtualization\v2`.
- Tests: xUnit.
- Target: x64, `net10.0-windows`. Runs on Windows Server 2016, 2019, 2022, 2025 with the Hyper-V role (Desktop Experience), and Windows 10/11 with Hyper-V.

## Project layout

```
HvViewer.sln
src/HvViewer.Core      engine: IEventSource, EventQuery, QueryText, MessageCache, Grouping, ResourceGuard (no UI)
src/HvViewer.System    SystemInfo, RoleDetection, HealthChecks, Hyper-V VM inventory (read-only)
src/HvViewer.Rules     troubleshooter rule files (JSON, embedded) + loader + matcher
src/HvViewer.App       WPF screens and view-models only, no business logic
tests/HvViewer.Tests   unit, sample-log, rule and performance tests
tests/HvViewer.App.Tests  view-model and safety tests
docs/                  spec, plan, mockups, release checklist
```

Dependencies go one way: **App → Rules, System → Core.** Core, System and Rules must never reference WPF. (`HvViewer.System` uses the namespace `HvViewer.Machine`.)

## Commands

```bash
dotnet build HvViewer.sln -c Release
dotnet test  HvViewer.sln -c Release
dotnet publish src/HvViewer.App -c Release -r win-x64 --self-contained false -o out/HvViewer
```

- WPF, `wevtapi` and WMI need **Windows**. Tests that read real event logs or WMI only run on Windows; mark them so they skip cleanly elsewhere.

## How to work

- Test first (TDD): write the failing test, make it pass, then refactor.
- Keep files small and focused; one class per file.
- Commit after each completed plan task, with a clear message.
- Rule files: provider + ID matching only. Explanations are written by us (short, plain English) with an official docs link. Never copy Microsoft's text.
- If something in the plan turns out wrong or impossible, stop and say so. Don't quietly change the design.
