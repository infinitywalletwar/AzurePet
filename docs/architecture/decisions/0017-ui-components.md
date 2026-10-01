# ADR-0017: UI components: own accessible component set on semantic HTML, no third-party component library in MVP

- **Status:** Accepted (2026-10-01)
- **Date:** 2026-10-01
- **Deciders:** the user (approver), solution-architect
- **Requirements:** NFR-016, NFR-026, NFR-080, NFR-081, FR-010–FR-012, FR-014, C-01, C-08

## Context

The UI uses Blazor with Static SSR for the client portal and identity
pages and Interactive Server for staff areas (ADR-0008). Constraints any
component approach must meet:

1. **Portal components must work without interactivity** (Static SSR,
   enhanced forms). Many Blazor component libraries assume an
   interactive render mode for basic controls (dropdowns, dialogs, date
   pickers).
2. **Strict CSP:** `script-src 'self'; style-src 'self'`, no inline
   scripts or styles (NFR-026, ADR-0011). Libraries that emit `style="…"`
   attributes, inject `<style>` elements or need `eval` would force
   `'unsafe-inline'`.
3. **WCAG 2.1 AA** (NFR-080) and responsive from 360 px (NFR-081).
4. **Per-tenant theming through CSS custom properties only** (FR-010,
   FR-011, ADR-0011).
5. **Small portal payload** for LCP ≤ 2.5 s on 4G (NFR-016).
6. **One part-time builder** (C-08): building components costs time.

The v1 architecture deferred the choice to the walking-skeleton lab; the
review asked for an explicit decision or constraint.

## Decision

1. **No third-party component library in MVP.** Build a small, owned
   component set in a shared Razor class library: layout shell, buttons,
   form field wrapper (label, hint, error with `aria-describedby`),
   alert/notice, status badge, pager, tabs, dialog (native `<dialog>`
   with a small static JS module from `'self'` for staff areas), and
   empty-state and error pages.
2. **Built-in Blazor components first:** `EditForm` and `Input*`
   components with `DataAnnotations` validation (work in Static SSR and
   interactive modes), `NavLink`, `InputFile`, and **QuickGrid**
   (`Microsoft.AspNetCore.Components.QuickGrid`, first-party) for staff
   grids. Native HTML controls where they are accessible enough (for
   example `<input type="date">` for date of loss, `<select>`).
3. **Styling:** one plain CSS file of base styles plus Blazor CSS
   isolation (bundled to a `'self'` stylesheet). All colours and brand
   values come from the CSS custom properties rendered by `/theme.css`
   (ADR-0011). No CSS framework requiring a build step at MVP; a small
   utility layer of our own is allowed.
4. **Rules every component must pass** (they are also the gate for any
   future library):
   - renders and is usable in Static SSR (portal components);
   - no inline `<script>`, no `style` attributes, no injected `<style>`
     elements; works with the CSP above (CSP violations fail UI tests);
   - keyboard operable with visible focus, correct roles and labels;
     automated **axe** checks pass in UI tests (NFR-080);
   - themeable only through the brand custom properties;
   - MIT-compatible licence, no runtime cost.
5. **Walking-skeleton lab check:** run QuickGrid and the .NET 10
   `ReconnectModal` under the CSP in report-only mode first, then
   enforce. If either needs inline styles, wrap or replace it.

**Trigger to adopt a library:** the staff areas need complex widgets
(accessible combobox with search, virtualised data grid with column
resizing, rich date-range picker) whose own implementation is estimated
above about 3 lab-days. First candidate: **Microsoft Fluent UI Blazor**
(`Microsoft.FluentUI.AspNetCore.Components`), staff areas only, after a
CSP and axe spike; the result is recorded in a superseding ADR.

## Alternatives considered

| Option | Static SSR portal | Strict CSP | Accessibility | Theming via CSS variables | Effort | Verdict |
|---|---|---|---|---|---|---|
| **Own component set + built-in components + QuickGrid (chosen)** | Yes, by design | Yes, by design | Our responsibility, verified by axe | Native | Medium (small set) | **Chosen** |
| Microsoft Fluent UI Blazor | Partly; many components are web components that need JS; SSR support varies per component | Needs a spike: web-component styling and some `Style` parameters may conflict with `style-src 'self'` | Strong focus on accessibility | Design tokens; mapping to tenant colours needed | Low for staff UIs | **First candidate at the trigger**, staff areas only |
| MudBlazor | Most interactive components need an interactive render mode | Uses inline styles widely; would need `'unsafe-inline'` | Mixed | Own theme provider | Low | Rejected (CSP, SSR) |
| Radzen Blazor | Similar: interactive-first | Inline styles | Mixed | Own themes | Low | Rejected (CSP, SSR) |
| Bootstrap (CSS + its JS) | CSS works in SSR | CSS fine; JS from `'self'` fine | Good base, still needs care | Sass variables at build; CSS variables partly | Low–medium; extra weight in portal | Not chosen: more CSS than needed; may be revisited for speed if building the set takes too long |
| Tailwind CSS | Yes | Yes (built CSS) | Neutral | Via config/CSS variables | Needs a Node build step in the .NET toolchain | Rejected for MVP (toolchain) |

## Consequences

**Trade-offs**

- (+) Every component meets the CSP, SSR and theming constraints by
  construction; the portal stays light (NFR-016).
- (+) No third-party UI dependency to patch or to lose.
- (−) We build and maintain about a dozen components and own their
  accessibility; axe checks catch much but not all (manual keyboard and
  screen-reader checks in the hardening lab).
- (−) Staff UIs look plainer than with a full library until the trigger.

**Scalability**

- None at runtime. Smaller portal payload helps NFR-016 at design load.

**Operations**

- Component rules are part of the dotnet-code-reviewer checklist; CSP is
  enforced in UI tests (CSP violation reports fail the test).

**Cost**

- 0 USD at run load and design load (no licences).
