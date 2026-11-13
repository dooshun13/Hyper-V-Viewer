# Hyper-V Viewer

A read-only event-log viewer for Windows Server **Hyper-V hosts**, built for debugging Hyper-V problems. It is built from [Eviewer](https://github.com/dooshun13/eviewer) and is safe to run on production hosts.

> **Status:** in development. The code is Eviewer renamed; the Hyper-V features below are planned in [`docs/superpowers/plans/2026-11-13-hvviewer-v1.md`](docs/superpowers/plans/2026-11-13-hvviewer-v1.md) and tracked as GitHub Issues (label `plan`).

Open it on a host and within seconds see which VMs are unhealthy, what went wrong in the last 24 hours, and where to look, with plain-English explanations and next steps.

## What it does (Phase 1, planned)

- **VM story:** pick a VM and see every start, shutdown, turn-off, reset, crash and move over the last 7 days, with Windows' own log messages in one list. Host-level events (host restart or power loss, cluster node loss) appear in each affected VM's story. A short hint is added **only when the cause is certain**.
- **Where it has been:** the VM's arrivals and departures as seen from this host.
- **Honest history:** says how far back each log really goes.
- **VMs screen:** state, health, uptime, vCPUs, memory, generation, version, and each VM's last stop or restart.
- **Host pulse:** hypervisor, VMMS, cluster node, host memory and VM storage at a glance.
- **Checkpoints:** old checkpoints and leftover backup (recovery) checkpoints.
- **Error-code decoder** and **Copy as PowerShell** for every search and story.
- **Hyper-V troubleshooters and logs:** VM start failures, crashes, live migration, checkpoints, Replica, virtual switches, storage, integration services, hypervisor, failover cluster.
- **Everything from Eviewer:** Home dashboard, Events search, event detail with explanations, saved views, CSV/HTML export, dark and light themes.

Phase 2 (later): SCVMM job history and VMM logs.

## Safe on production hosts

- **Viewer only:** it never clears or changes logs, the registry, services or settings, and never changes a VM. Hyper-V is read with WMI `SELECT` queries only; no WMI methods are called.
- **Light on the host:** below-normal priority, at most 2 parallel readers, and scans stop at 500,000 events or 1 GB of memory (partial results are kept).
- **Leaves almost no trace:** the only files it writes are its settings (`%AppData%\HvViewer`) and exports you save.
- A safety test fails the build if forbidden calls appear in the compiled code.

## Run it

Run `HvViewer.exe` from the published folder. It needs the .NET 10 Desktop Runtime (or use the self-contained release zip). Run it as administrator (or as a member of *Hyper-V Administrators* and *Event Log Readers*) to see the VM list and all logs.

## Build

Requires the .NET 10 SDK on Windows.

```bash
dotnet build HvViewer.sln -c Release
dotnet test  HvViewer.sln -c Release
dotnet publish src/HvViewer.App -c Release -r win-x64 --self-contained false -o out/HvViewer
```

## Docs

- Design: [`docs/superpowers/specs/2026-11-13-hvviewer-v1-design.md`](docs/superpowers/specs/2026-11-13-hvviewer-v1-design.md)
- Build plan: [`docs/superpowers/plans/2026-11-13-hvviewer-v1.md`](docs/superpowers/plans/2026-11-13-hvviewer-v1.md)
- Eviewer history: [`docs/eviewer-base/`](docs/eviewer-base/)
