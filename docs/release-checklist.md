# Release checklist

HvViewer is a personal tool that runs on the owner's own PC (spec section 4), so this list is short.

## Before building

- [ ] `dotnet build HvViewer.sln -c Release` has no warnings (warnings are errors).
- [ ] `dotnet test HvViewer.sln -c Release` passes. Tests that need `tests/samples/*.evtx` do nothing when the file is absent; export the samples (see `tests/samples/README.md`) before a release so they really run.
- [ ] Performance tests pass against `tests/samples/perf.evtx` (a copy of this PC's System log): scan 60k events under 3 s, text search over about 50k events under 5 s, filtering loaded data under 200 ms, memory under 300 MB.

## Try it on this PC

- [ ] Start `HvViewer.exe`. The window is usable within about 2 seconds and Home fills in within about 10 seconds.
- [ ] Run once as a standard user and once as administrator. As a standard user the Security tile says "Run as administrator to include" (not 0) and the Security log is greyed out in the log picker.
- [ ] Home: change 1 h / 24 h / 7 d / Custom; open a tile; the Events screen shows that troubleshooter's events.
- [ ] Events: search for text, set an exact From/To with seconds, copy the query line, type a query line and press Enter, export CSV and HTML and open both.
- [ ] Event detail: Explained, Message, XML and Raw data tabs; "Around this event"; Copy for ticket; the docs link opens in the browser.
- [ ] Settings: Dark, Light and Follow Windows; Local and UTC (times on Home and Events change).
- [ ] Saved views: save, open, rename, delete.
- [ ] Task Manager during a 24 h scan: CPU and memory stay reasonable and the window stays responsive.
- [ ] A French Windows PC, if one is available: the same events match, the explanations stay English, accent-insensitive search works.

## Publishing

- [ ] `dotnet publish src/HvViewer.App -c Release -r win-x64 --self-contained false -o out/HvViewer`
- [ ] Run `out/HvViewer/HvViewer.exe` once from the published folder.
- [ ] Tag the commit (`git tag v1.0.0-beta.1`) and push it only when you mean to release. CI then builds the zip and attaches it to a GitHub release.
