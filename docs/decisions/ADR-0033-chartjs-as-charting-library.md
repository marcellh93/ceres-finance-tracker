# ADR 0033: Chart.js as the Phase 2 Charting Library

## Status: Accepted

## Context

Phase 2 introduces a visual dashboard with seven chart types (line, bar, donut, horizontal
bar, and progress bars). A charting library must be chosen before dashboard components are
written. Requirements for Phase 2:

- Works with React (Phase 2 is a hybrid Razor + React setup per ADR-0014)
- Covers all seven Phase 2 chart types without a second library
- Lightweight — the Phase 2 app is still local/personal use, not a high-traffic product
- Well-maintained with active community support

Options considered:

- **Chart.js + react-chartjs-2** — canvas-based, lightest bundle of the major options, covers all Phase 2 chart types, `react-chartjs-2` is the official React wrapper, large community
- **Recharts** — SVG-based, React-native API (no wrapper needed), better for complex custom tooltips and drill-down interactions, slightly larger bundle
- **Nivo** — declarative and highly composable, but heavier and more opinionated; better fit for data-dense dashboards than personal finance
- **Highcharts** — commercial license required for non-personal use; ruled out before Phase 3

## Decision

**Chart.js** (via `react-chartjs-2`) is the Phase 2 charting library.

Rationale:
- Covers all seven Phase 2 chart types out of the box
- Smallest bundle of the shortlisted options — appropriate for a personal-use local app
- `react-chartjs-2` is the official wrapper with an active maintenance record
- If Phase 3 introduces interactions that Chart.js cannot handle cleanly (drill-down, complex custom tooltips, animated transitions), Recharts is the documented fallback — that evaluation happens at Phase 3 scope definition, not now

## Consequences

**Positive:**
- Single library covers all Phase 2 charts — no mixing of charting libraries in Phase 2
- Canvas-based rendering performs well for the data volumes expected in a personal finance app
- Large ecosystem of examples and Stack Overflow answers

**Negative:**
- Canvas-based rendering is less composable than SVG-based alternatives — custom overlays or annotations require more work
- If Recharts is introduced in Phase 3, two charting libraries will coexist until the older charts are migrated — a known future cost
- `react-chartjs-2` is a wrapper, not native React — API surface is slightly less idiomatic than Recharts
