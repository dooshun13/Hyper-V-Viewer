# Hyper-V Viewer

A read-only event-log viewer for Windows Server **Hyper-V hosts**, built for debugging Hyper-V problems. It is built from [Eviewer](https://github.com/dooshun13/eviewer) and is safe to run on production hosts.

Open it on a host and within seconds see which VMs are unhealthy, what went wrong in the last 24 hours, and where to look, with plain-English explanations and next steps.

## What it does

- **Virtual machines:** a read-only list of the host's VMs (state, health, uptime, vCPUs, memory, generation, configuration version). *Show events* opens every Hyper-V event about that VM.
- **Hyper-V troubleshooters (9):** VM start failures, VM crashes & resets, Live migration, Checkpoints & merges, Hyper-V Replica, Virtual switches, VM storage, Integration services, Hypervisor & host, plus Failover cluster.
- **Hyper-V logs:** VMMS, Worker, Hypervisor, Compute, VID, VmSwitch, StorageVSP, Integration, High-Availability and more, grouped in a "Hyper-V" folder in the log picker.
- **Everything from Eviewer:** Home dashboard, Events search (exact text, date and time to the second, IDs, sources, levels, one-line query), event detail with explanations, 11 general Windows troubleshooters, saved views, CSV/HTML export, dark and light themes.

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
- Status: [`docs/status.md`](docs/status.md)
- Eviewer history: [`docs/eviewer-base/`](docs/eviewer-base/)
