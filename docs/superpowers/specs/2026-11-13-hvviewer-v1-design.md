# Hyper-V Viewer v1 — Design Spec

- **Date:** 2026-11-13
- **Status:** Draft for owner review
- **Repo:** `dooshun13/Hyper-V-Viewer`
- **Built from:** Eviewer (`dooshun13/eviewer`, commit `772c165`), copied and renamed to `HvViewer.*`. Eviewer's spec is in `docs/eviewer-base/superpowers/specs/` and still describes everything this spec does not change.

## 1. Purpose

Debugging a Hyper-V host means reading a dozen event logs (VMMS, Worker, Hypervisor, VmSwitch, StorageVSP, High-Availability, Integration, failover cluster, System) and matching events to the VM they are about. Hyper-V Viewer puts these in one read-only viewer: within about 30 seconds of opening it on a host, an admin can answer *which VMs are unhealthy, what went wrong recently, and where do I look?*

**Users:** system admins troubleshooting Windows Server Hyper-V hosts and clusters, including **production** hosts.

**Success criteria**

1. Copy the `HvViewer` folder to a host, run `HvViewer.exe`, and see the host's VMs and a useful 24-hour Hyper-V summary with no installation.
2. Open any VM's events from the VM list in one click.
3. Every important Hyper-V event comes with a plain-English explanation and a next step.
4. Running it has no noticeable impact on the host and changes nothing on it (§4).

## 2. Scope

### In scope (v1)

Everything in Eviewer v1 as amended (Home, Events search, event detail, Saved, Settings, the 11 general troubleshooters), plus:

- **Hyper-V log channels** (§5.1), shown first in the log picker under a "Hyper-V" folder.
- **9 Hyper-V troubleshooters** (§5.2), shown first on Home.
- **Virtual machines screen** (§5.3): read-only list of the host's VMs with state, health, uptime, vCPUs, memory, generation and configuration version.
- **Per-VM event filter** (§5.4): all Hyper-V events about one VM, matched by its VM ID.
- **Server guardrails** (§4), restored from the original Eviewer design.

### Out of scope

- Any action on a VM or the host: start, stop, save, checkpoint, migrate, connect to the console, change settings.
- Remote hosts and multi-host / cluster-wide views (v1 reads the local host only).
- VM CPU and memory **usage** graphs (needs performance counters over time; later).
- SCVMM, opening `.evtx` files, live tail, alerts, installer, translated UI.

## 3. Platform and packaging

- **Supported OS:** Windows Server 2016, 2019, 2022 and 2025 with the Hyper-V role and Desktop Experience; Windows 10 and 11 with Hyper-V enabled (for testing). Server Core and Hyper-V Server are not supported for the UI.
- On a machine without Hyper-V the app still works as a general event viewer; the VM screen says Hyper-V is not installed and Hyper-V tiles show "—".
- **Stack, packaging, build:** as Eviewer (.NET 10, WPF + WPF-UI, x64, portable folder, GitHub Actions on a Windows runner).

## 4. Guardrails (restored for production hosts)

Hyper-V Viewer runs on production hosts, so Eviewer's 2026-11-11 decision to drop the guardrails is reversed:

| Guardrail | Rule |
|---|---|
| Viewer only | Never clears, writes or reconfigures event logs; never writes the registry; never changes services, tasks, drivers or settings. |
| No Hyper-V changes | `root\virtualization\v2` is used only with WQL `SELECT` queries. No WMI method calls at all. Never `Win32_Product`. |
| Process priority | `BelowNormal` at start-up. |
| Parallel readers | At most **2** threads format messages in parallel. |
| Event limit | A dashboard scan or search stops after **500,000** events read and keeps what it has. |
| Memory limit | A scan or search stops when the process's managed memory passes **1 GB**. |
| Telling the user | When a limit stops a scan, the "logs skipped" line says which limit and asks to narrow the range. |
| Files | Only `%AppData%\HvViewer\HvViewer.settings.json` and exports the user saves. No temp files, logs or network access. |
| Safety test | A test reads the compiled assemblies' metadata and fails the build if any forbidden function, method or WMI method name is referenced (§8). |

Limits are constants in `ResourceLimits` for v1 (not user settings).

## 5. Hyper-V features

### 5.1 Log channels

`HyperVChannels` (Core) lists the channels the app knows, by internal name. The log picker shows every installed channel whose name starts with `Microsoft-Windows-Hyper-V-` in a top-level **Hyper-V** folder, plus `Microsoft-Windows-FailoverClustering/Operational` when present. Key channels:

`Microsoft-Windows-Hyper-V-VMMS-Admin`, `-VMMS-Operational`, `-VMMS-Networking`, `-VMMS-Storage`, `-Worker-Admin`, `-Worker-Operational`, `-Hypervisor-Admin`, `-Hypervisor-Operational`, `-Compute-Admin`, `-Compute-Operational`, `-VID-Admin`, `-VmSwitch-Operational`, `-StorageVSP-Admin`, `-Integration-Admin`, `-High-Availability-Admin`, `-Config-Admin`.

Windows has used both `…-Admin` and `…/Admin` spellings for some Hyper-V channels across versions. Rule matching treats `-` and `/` before the last segment as the same (`ChannelName.Same`), and the dashboard tries the other spelling when a channel is not found.

### 5.2 Hyper-V troubleshooters

Nine new rule files, same schema as Eviewer (`schema: 1`), shown before the general ones. Event IDs were checked against Microsoft documentation, Microsoft Q&A threads with event XML, and vendor references on 2026-11-13; the owner should review them on a real host (§10).

| Id | Title | Log | Provider | Events (severity) |
|---|---|---|---|---|
| `hyperv-vm-start` | VM start failures | Worker-Admin | Hyper-V-Worker | 3040, 3031, 3112, 3320, 12010 (error) |
| | | VMMS-Admin | Hyper-V-VMMS | 15130 (error), 14081 (error) |
| `hyperv-vm-crashes` | VM crashes & resets | Worker-Admin | Hyper-V-Worker | 18560, 18590 (critical) |
| `hyperv-live-migration` | Live migration | System | Hyper-V-High-Availability | 21502 (error) |
| | | High-Availability-Admin | Hyper-V-High-Availability | 21502, 21111 (error) |
| | | VMMS-Admin | Hyper-V-VMMS | 21026, 22040, 20408 (error) |
| `hyperv-checkpoints` | Checkpoints & merges | VMMS-Admin | Hyper-V-VMMS | 18012 (error), 18014, 19100 (warning) |
| | | Worker-Admin | Hyper-V-Worker | 3280 (error) |
| `hyperv-replica` | Hyper-V Replica | VMMS-Admin | Hyper-V-VMMS | 32022, 32552, 32068 (error), 32315 (warning) |
| `hyperv-networking` | Virtual switches | System | Hyper-V-VmSwitch | 22, 252 (warning) |
| `hyperv-storage` | VM storage | StorageVSP-Admin | Hyper-V-StorageVSP | 6, 8 (error) |
| | | VMMS-Admin | Hyper-V-VMMS | 12142, 14098 (error) |
| `hyperv-integration` | Integration services | Integration-Admin | Hyper-V-Integration | 4000 (error), 4010 (warning) |
| | | Integration-Admin | Hyper-V-Integration-KvpExchange | 4096 (info) |
| `hyperv-host` | Hypervisor & host | System | Hyper-V-Hypervisor | 41, 38, 48 (critical) |
| `failover-cluster` | Failover cluster | System | FailoverClustering | 1135, 1177 (critical), 1069, 1146, 1205 (error) |

(Log and provider names are shortened; the full names start with `Microsoft-Windows-`.)

Home shows at most **12** tiles (three rows): tiles that need attention first, then quiet tiles in rule order (Hyper-V first). A line under the tiles says how many quiet tiles are not shown.

### 5.3 Virtual machines screen

A new **VMs** item in the icon bar, between Home and Events.

- **Source:** `HyperVInventory` (System) with WQL `SELECT` queries only, in `root\virtualization\v2`:
  - `Msvm_ComputerSystem WHERE Caption = 'Virtual Machine'`: `Name` (VM ID), `ElementName`, `EnabledState`, `HealthState`, `OnTimeInMilliseconds`.
  - `Msvm_VirtualSystemSettingData WHERE VirtualSystemType = 'Microsoft:Hyper-V:System:Realized'`: `VirtualSystemIdentifier`, `VirtualSystemSubType` (generation), `Version`.
  - `Msvm_ProcessorSettingData` and `Msvm_MemorySettingData`: `InstanceID`, `VirtualQuantity` (vCPUs, startup MB), `DynamicMemoryEnabled`. Matched to the VM by the VM ID inside `InstanceID` (`Microsoft:<VM ID>\…`).
- **Columns:** name, state (Running, Off, Saved, Paused, Starting, …), health (OK / Degraded / Critical), uptime, vCPUs, memory (MB, "dynamic" when on), generation, configuration version, VM ID.
- **Actions:** *Show events* (opens Events with the per-VM filter, last 24 h), *Copy VM ID*, *Refresh*. Nothing else.
- **States:** Hyper-V not installed (namespace missing), access denied ("Run as administrator or join Hyper-V Administrators"), no VMs, WMI error (message, nothing else breaks).
- The state and health mappings are pure functions with unit tests; the WMI reader is a thin `IHyperVInventory` implementation tested with fakes.

### 5.4 Per-VM event filter

Hyper-V writes the VM ID (a GUID) into each VM-related event (`UserData/VmlEventLog/VmId` or event data). The filter is an ordinary `EventQuery`:

- **Text:** the VM ID, **target:** raw XML, match case off.
- **Logs:** every installed Hyper-V channel (§5.1) plus System; **range:** last 24 h by default.
- It goes through the existing search engine, so the query line, grouping, export and detail view all work unchanged. The ID is language-neutral, so this works on French Windows.

## 6. Architecture changes

- **Core:** `ResourceLimits`, `ResourceGuard` (event and memory checks, injectable clock-free memory reader), `ChannelName` (spelling-tolerant comparison), `HyperVChannels`. `SearchService` and `DashboardService` call the guard; `NativeEventSource` caps parallel formatting at 2.
- **System:** `HyperVInventory`, `VmInfo`, `VmStateText`, `IHyperVInventory`, `WindowsHyperVInventory` (WMI, read-only).
- **Rules:** nine new JSON files; built-in order puts Hyper-V first; `RuleSet` matching uses `ChannelName.Same`.
- **App:** `VmsViewModel` + `VmsView`, a VMs nav item, Home tile cap, `LogCatalog` Hyper-V folder, below-normal priority at start-up.

## 7. Performance targets

As Eviewer §7, plus: VM list for 100 VMs in under 2 s. The CPU and memory rows are again **enforced** limits (§4), not just goals.

## 8. Testing

- All Eviewer tests carried over, renamed.
- **Rule tests:** the §5.2 table is encoded in `BuiltInRulesTests`; every rule file loads with no errors.
- **Guard tests:** event limit and memory limit stop a scan and a search and report the reason.
- **ChannelName tests:** `X-Admin` equals `X/Admin`; unrelated names differ.
- **VM tests:** state and health mapping, uptime text, VM ID extraction from `InstanceID`, inventory merge, view-model states (not installed, denied, empty, list), *Show events* builds the right query.
- **Safety test:** reads every `HvViewer.*.dll` with `System.Reflection.Metadata` and fails if it finds references to forbidden APIs (`EvtClearLog`, `EvtSetChannelConfigProperty`, `EvtSaveChannelConfig`, `ClearLog`, `RegistryKey.SetValue/CreateSubKey/DeleteSubKey*/DeleteValue`, `ServiceController.Start/Stop/Pause/Continue`, `ManagementObject.InvokeMethod/Put/Delete`, `ManagementClass.CreateInstance`) or user strings naming forbidden WMI classes and methods (`Win32_Product`, `RequestStateChange`, `DefineSystem`, `DestroySystem`, `ModifySystemSettings`, `CreateSnapshot`, `MigrateVirtualSystemToHost`).

## 9. Non-English Windows

As Eviewer §8. The VM state, health and generation come from WMI as numbers and are mapped to English labels; VM names are shown as they are.

## 10. Open items for the owner

- Review the §5.2 event list on a real Server 2022/2025 host (export samples to `tests/samples/`).
- Confirm the channel spellings on your hosts (`Get-WinEvent -ListLog *Hyper-V*`), especially `Integration-Admin`.
- Are 500,000 events and 1 GB the right limits for your hosts?
- Should the VM list later show live CPU and memory use (performance counters)?
