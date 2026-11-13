# Sample logs

Real `.evtx` files used by the sample-log and performance tests. They hold event data from real machines, so they are **never committed** (`*.evtx` is git-ignored). A test whose file is missing simply does nothing, so the suite stays green without them.

Export a log with `wevtutil` (it only reads):

```
wevtutil epl <LogName> <file.evtx> "/q:<XPath>"
```

| File | What it should contain | Example export |
|---|---|---|
| `perf.evtx` | A big log for the performance tests (tens of thousands of events) | `wevtutil epl System perf.evtx` |
| `restart.evtx` | An unexpected restart: Kernel-Power 41, EventLog 6008 and 6005, 1001 | `wevtutil epl System restart.evtx "/q:*[System[(EventID=41 or EventID=6005 or EventID=6008 or EventID=1001)]]"` |
| `disk-retries.evtx` | Disk retries, event 153 (several close together) | `wevtutil epl System disk-retries.evtx "/q:*[System[Provider[@Name='disk'] and (EventID=153 or EventID=7 or EventID=11)]]"` |
| `logons.evtx` | Failed logons and a lockout, 4625 and 4740 (needs an administrator) | `wevtutil epl Security logons.evtx "/q:*[System[(EventID=4625 or EventID=4740)]]"` |
| `crashes.evtx` | App crash and hang, 1000 and 1002 | `wevtutil epl Application crashes.evtx "/q:*[System[(EventID=1000 or EventID=1002)]]"` |
| `services.evtx` | Service crashes, 7031 and 7034 | `wevtutil epl System services.evtx "/q:*[System[Provider[@Name='Service Control Manager'] and (EventID=7031 or EventID=7034)]]"` |
| `low-memory.evtx` | Low virtual memory, 2004 | `wevtutil epl System low-memory.evtx "/q:*[System[EventID=2004]]"` |
| `dns.evtx` | DNS client timeouts, 1014 | `wevtutil epl System dns.evtx "/q:*[System[EventID=1014]]"` |
| `group-policy.evtx` | Group Policy failures, 1129 | `wevtutil epl System group-policy.evtx "/q:*[System[Provider[@Name='Microsoft-Windows-GroupPolicy'] and EventID=1129]]"` |
| `windows-update.evtx` | A failed update, WindowsUpdateClient 20 | `wevtutil epl System windows-update.evtx "/q:*[System[Provider[@Name='Microsoft-Windows-WindowsUpdateClient'] and EventID=20]]"` |
| `french-system.evtx` | The System log of a French Windows, with at least one event the rules know | `wevtutil epl System french-system.evtx` (on the French PC) |
| `no-publisher.evtx` | Events whose publisher is not installed on the PC running the tests, so the message cannot be built | export from a PC with software that is not installed here |

`perf.evtx` on your own PC is just a copy of your own System log (the command in the table). The CI script `tools/ci/make-perf-log.ps1` builds one on a throw-away GitHub runner by writing test events; do not run it on a PC you care about.
