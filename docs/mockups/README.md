# UI mockups

These are the approved visual direction for HvViewer v1. They come from the "HvViewer – UI mockups" design canvas.

| File | Screen |
|---|---|
| `home-dashboard.dc.html` | Home dashboard (last 24 h) |
| `events-search.dc.html` | Events search: text, from/to date and time, filters, log category dropdown (shown open), results, event detail panel |

## How to read them

- Each file is a design-canvas component: HTML markup with `{{holes}}` and `<sc-for>` loops, plus a `<script>` block whose `renderVals()` holds the **sample data**. They don't open as plain web pages. Read them as a layout and style reference.
- All hosts, counts and events are **sample data**. Don't hard-code them.
- Links to `Compare.dc.html` are leftovers: **Compare is out of scope** for v1.

## Visual tokens to reuse in WPF

| Token | Value |
|---|---|
| Window background | `#111214` |
| Icon bar background | `#16171A` |
| Card background | `#1A1B1F`, border `rgba(255,255,255,0.06)`, radius 20 |
| Inner tile | `#202226`, radius 14 |
| Text / secondary / muted | `#ECECEE` / `#C9CBD1` / `#A9ABB2` |
| Accent | `#5AB0FF` (selected background `#22303F` / `#24303D`) |
| Critical | `#FF5D6C` |
| Error | `#FF9446` |
| Warning | `#F2C94C` |
| Information | `#6CB6FF` |
| OK | `#4FD1A5` (badge background `#17271F`, text `#7EE2B0`) |
| Fonts | Segoe UI Variable / Inter for UI; Cascadia Mono / JetBrains Mono for IDs, times and queries |

Severity always comes with a label or shape as well as colour, never colour alone. The light theme is derived from the same roles.
