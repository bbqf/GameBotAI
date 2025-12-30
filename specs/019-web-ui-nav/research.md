# Research: Web UI Navigation Restructure

## Decisions

### Navigation pattern
- **Decision**: Top horizontal tabs for Authoring, Execution, Configuration; collapse to a simple menu on narrow screens while preserving one-click switching and active-state styling.
- **Rationale**: Matches requirement for fast context switching; keeps labels visible on desktop; mobile-friendly via collapse.
- **Alternatives considered**: Left vertical rail (adds cursor travel, poorer mobile fit); route-only switching without persistent nav (hurts discoverability).

### Responsive breakpoint and behavior
- **Decision**: Collapse tabs into a simple menu at ~768px viewport width; keep keyboard focus order and aria-current on active item.
- **Rationale**: Aligns with common tablet/mobile breakpoints; ensures accessibility in reduced space.
- **Alternatives considered**: Always-on tabs with truncation (risks unreadable labels); multiple breakpoints with icon-only mode (higher complexity, less clarity).

### Execution placeholder treatment
- **Decision**: Provide a dedicated empty-state view under Execution explaining future functionality and offering links back to Authoring/Configuration.
- **Rationale**: Sets user expectation, prevents dead ends, supports navigation consistency.
- **Alternatives considered**: Hide Execution until ready (conflicts with requirement); duplicate authoring links (confusing scope).

### Data/API footprint
- **Decision**: No new backend endpoints; reuse existing Actions/Sequences/Commands data fetches; only navigation shell changes.
- **Rationale**: Navigation restructure does not alter domain data; minimizes risk and scope.
- **Alternatives considered**: Introduce new navigation config endpoint (unnecessary for current scope).

### Performance and accessibility budgets
- **Decision**: p95 tab switch renders visible content in <150ms after assets loaded; layout shift <0.1 CLS equivalent; navigation fully keyboard operable with visible focus; labels readable in collapsed menu.
- **Rationale**: Keeps navigation feel instantaneous and accessible; aligns with constitution performance/UX principles.
- **Alternatives considered**: No explicit budget (violates constitution); stricter 50ms target (unlikely to be meaningful given React render and test overhead).
