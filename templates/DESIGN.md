---
name: NetWiv starter
description: The screen `netwiv new` scaffolds, built five times from one generator; a split console on paper and ink with one purple answer field.
colors:
  paper: "#f6f5fb"
  ink: "#17142a"
  muted: "#5b5674"
  hair: "#d9d5ea"
  field: "#512bd4"
  field-muted: "#ddd5fc"
  field-hair: "rgb(255 255 255 / 0.3)"
  lavender: "#b9a8f5"
  white: "#ffffff"
  stop-muted: "#c9c4dc"
  paper-dark: "#17142a"
  ink-dark: "#dcd9ea"
  muted-dark: "#9a94b8"
  hair-dark: "#3a3456"
  rule-dark: "#7d77a0"
  field-dark: "#3b2e96"
  field-muted-dark: "#d4ccf8"
  field-hair-dark: "rgb(255 255 255 / 0.25)"
  stop-dark: "#2f2a4a"
  stop-ink-dark: "#ece9f8"
  stop-muted-dark: "#aaa4c6"
typography:
  headline:
    fontFamily: "system-ui, -apple-system, Segoe UI, Roboto, Noto Sans, Helvetica Neue, Arial, sans-serif"
    fontSize: "clamp(26px, 3.6vw, 34px)"
    fontWeight: 700
    lineHeight: 1.12
    letterSpacing: "-0.025em"
  answer:
    fontFamily: "system-ui, -apple-system, Segoe UI, Roboto, Noto Sans, Helvetica Neue, Arial, sans-serif"
    fontSize: "clamp(24px, 3.2vw, 30px)"
    fontWeight: 700
    lineHeight: 1.15
    letterSpacing: "-0.02em"
  title:
    fontFamily: "system-ui, -apple-system, Segoe UI, Roboto, Noto Sans, Helvetica Neue, Arial, sans-serif"
    fontSize: "15px"
    fontWeight: 700
    lineHeight: 1.5
  body:
    fontFamily: "system-ui, -apple-system, Segoe UI, Roboto, Noto Sans, Helvetica Neue, Arial, sans-serif"
    fontSize: "16px"
    fontWeight: 400
    lineHeight: 1.5
  input:
    fontFamily: "system-ui, -apple-system, Segoe UI, Roboto, Noto Sans, Helvetica Neue, Arial, sans-serif"
    fontSize: "17px"
    fontWeight: 400
    lineHeight: 1.5
  label:
    fontFamily: "system-ui, -apple-system, Segoe UI, Roboto, Noto Sans, Helvetica Neue, Arial, sans-serif"
    fontSize: "14px"
    fontWeight: 600
    lineHeight: 1.5
  small:
    fontFamily: "system-ui, -apple-system, Segoe UI, Roboto, Noto Sans, Helvetica Neue, Arial, sans-serif"
    fontSize: "14px"
    fontWeight: 400
    lineHeight: 1.5
  mono-path:
    fontFamily: "ui-monospace, SF Mono, SFMono-Regular, Cascadia Mono, Consolas, Liberation Mono, Menlo, monospace"
    fontSize: "14px"
    fontWeight: 600
    lineHeight: 1.4
  mono-route:
    fontFamily: "ui-monospace, SF Mono, SFMono-Regular, Cascadia Mono, Consolas, Liberation Mono, Menlo, monospace"
    fontSize: "13px"
    fontWeight: 400
    lineHeight: 1.4
    fontFeature: "tnum"
  mono-count:
    fontFamily: "ui-monospace, SF Mono, SFMono-Regular, Cascadia Mono, Consolas, Liberation Mono, Menlo, monospace"
    fontSize: "20px"
    fontWeight: 600
    lineHeight: 1
    fontFeature: "tnum"
  mono-error:
    fontFamily: "ui-monospace, SF Mono, SFMono-Regular, Cascadia Mono, Consolas, Liberation Mono, Menlo, monospace"
    fontSize: "12px"
    fontWeight: 400
    lineHeight: 1.5
rounded:
  none: "0px"
spacing:
  page-x: "clamp(20px, 4vw, 40px)"
  column-y: "clamp(28px, 6vh, 48px)"
  bar-y: "14px"
  stack: "28px"
  row: "14px"
  control-gap: "12px"
  brand-gap: "10px"
components:
  top-bar:
    backgroundColor: "{colors.paper}"
    textColor: "{colors.ink}"
    typography: "{typography.body}"
    padding: "14px clamp(20px, 4vw, 40px)"
  status-label:
    textColor: "{colors.muted}"
    typography: "{typography.mono-route}"
  button-primary:
    backgroundColor: "{colors.ink}"
    textColor: "{colors.white}"
    typography: "{typography.label}"
    rounded: "{rounded.none}"
    padding: "0 22px"
    height: "46px"
    width: "9.5em"
  button-primary-hover:
    backgroundColor: "{colors.field}"
    textColor: "{colors.white}"
  button-primary-disabled:
    backgroundColor: "{colors.muted}"
    textColor: "{colors.white}"
  button-primary-dark:
    backgroundColor: "{colors.ink-dark}"
    textColor: "{colors.paper-dark}"
    rounded: "{rounded.none}"
    padding: "0 22px"
    height: "46px"
  button-primary-dark-hover:
    backgroundColor: "{colors.lavender}"
    textColor: "{colors.paper-dark}"
  button-primary-dark-disabled:
    backgroundColor: "{colors.stop-dark}"
    textColor: "{colors.stop-muted}"
  input-underline:
    textColor: "{colors.ink}"
    typography: "{typography.input}"
    rounded: "{rounded.none}"
    padding: "10px 0"
  answer-field:
    backgroundColor: "{colors.field}"
    textColor: "{colors.white}"
    typography: "{typography.answer}"
    rounded: "{rounded.none}"
    padding: "20px 24px 18px"
  answer-field-failed:
    backgroundColor: "{colors.ink}"
    textColor: "{colors.white}"
  answer-field-dark:
    backgroundColor: "{colors.field-dark}"
    textColor: "{colors.white}"
  answer-field-failed-dark:
    backgroundColor: "{colors.stop-dark}"
    textColor: "{colors.stop-ink-dark}"
  file-row:
    textColor: "{colors.ink}"
    typography: "{typography.mono-path}"
    padding: "14px 0"
---

# Design System: NetWiv starter

## Overview

**Creative North Star: "The Working Console"**

The starter is a one-screen console, not a welcome page. A thin top bar carries the mark, the app name and a live status measurement; below it, two equal columns split by a hairline. The left column performs the C# round trip (headline, name field, Call greet, and a purple answer field pinned to the column bottom); the right column is the map of what to edit, four ruled rows of path and plain sentence. It adapts the NetWiv site's Swiss technical program (paper, ink, ink rules, one purple field, the layered mark as the only ornament) to a screen the developer will delete within minutes.

It is deliberately modest and self-contained: system faces only, no network assets, square corners, no shadows, one colour field, one authored motion. NetWiv identity is a 24px mark in the corner and nothing more. The screen exists in five frameworks (React, Vue, Svelte, Angular, Blazor) that must stay visually identical; they share one stylesheet, one markup structure and one copy deck, all generated from `scripts/generate-frontend-template-overlays.mjs`.

Rejected by the user: a high-contrast dark theme (the dark set is intentionally soft), and bundling the brand faces Roboto and JetBrains Mono.

**Key Characteristics:**
- Split console: round trip on the left, file map on the right, equal columns, hairline between.
- Paper ground, ink type, 2px ink rules under the top bar and above the file map.
- One purple answer field per screen; it stays the only colour field in dark mode.
- System sans for reading, system monospace only for paths, the call route, the count and the status measurement.
- Square corners everywhere except inside the layered mark; flat, no shadows.
- One authored motion: the answer "lands" on each new count.

## Colors

A violet-tinted paper-and-ink neutral world with one saturated purple field and one pale lavender, taken from the NetWiv brand palette. Every value is a custom property on `:root`; dark mode follows the system appearance (`prefers-color-scheme`) and swaps the whole set.

### Primary
- **Answer Purple** (field): the answer field's ground, the light-mode button hover, the input focus underline, the focus ring, the "answered" status square, text selection and the mark's front plate. Spent once per screen as a solid field.
- **Night Answer Purple** (field-dark): the answer field in dark mode, deliberately darker and quieter than the light field so it sits in the soft dark set while remaining the screen's only colour.

### Secondary
- **Back-Layer Lavender** (lavender): the mark's back plate and the "calling" status square in light mode; in dark mode it becomes the signal colour (answered square, focus ring, caret, button hover). Never a ground.

### Neutral
- **Program Paper** (paper): the page ground in light mode; also fills the gap plate that separates the mark's layers.
- **Program Ink** (ink): all type, the 2px rules, the primary button, and the failed answer block in light mode.
- **Margin Violet-Grey** (muted): status text, file-row sentences, the disabled button, and the idle and failed status squares.
- **Hairline** (hair): the column divider, file-row rules and the idle status square.
- **Field Lilac** (field-muted, field-muted-dark): secondary text inside the answer field (route, count label, idle message).
- **Field Hairline** (field-hair, field-hair-dark): the translucent white rule above the count.
- **Stop Grey** (stop-muted): secondary text in the failed block; in dark mode also the disabled button's text.
- **Night set** (paper-dark, ink-dark, muted-dark, hair-dark, rule-dark, stop-dark, stop-ink-dark, stop-muted-dark): ink becomes the ground, type becomes a soft lilac grey rather than white, rules soften to a mid violet-grey, and the failed block and disabled button become a raised dark violet.

### Named Rules
**The One Field Rule.** The answer field is the only solid colour on screen. In dark mode it stays the only colour field; the button becomes a light inverse block (ink-dark slab with paper-dark text) instead of a second dark purple.

**The Soft Dark Rule.** The dark set is soft by decision: body ink is a lilac grey, not white, and rules are mid-tone. Do not raise contrast toward a black-and-white dark theme; the user rejected it. White is reserved for type on the answer field.

**The Back Layer Rule.** Lavender is the layer behind: the mark's back plate, a status square, a hover or focus on ink. It never fills a surface.

## Typography

**Body Font:** the system sans (`system-ui`, -apple-system, Segoe UI, Roboto, Noto Sans, Helvetica Neue, Arial)
**Mono Font:** the system monospace (`ui-monospace`, SF Mono, Cascadia Mono, Consolas, Liberation Mono, Menlo)

**Character:** whatever the developer's OS draws, set tight and bold for the two large lines and plain for everything else. The site's Roboto and JetBrains Mono are deliberately not bundled: the starter must run offline from embedded assets, stay small, and read as the user's app.

### Hierarchy
- **Headline** (700, `clamp(26px, 3.6vw, 34px)`, 1.12, -0.025em, max 17ch, balanced): the single column headline; it switches to the offline sentence when the page is outside a NetWiv window.
- **Answer** (700, `clamp(24px, 3.2vw, 30px)`, 1.15, -0.02em): the C# message inside the field; 500 weight in field lilac while idle or waiting.
- **Title** (700, 15px): "Make it yours" above the file map.
- **Body** (400, 16px, 1.5): the root setting; the input runs at 17px.
- **Label** (600, 14px): the field label; the button uses 600 at the body size.
- **Small** (400, 14px): file-row sentences, the count label, failure detail.
- **Mono path** (600, 14px, 1.4): file paths in the map.
- **Mono route** (400, 13px, 1.4, tabular): the call route `commands.greet → Commands.Greet` and the top-bar status measurement.
- **Mono count** (600, 20px, 1, tabular): the call count.
- **Mono error** (400, 12px, 1.5): the raw error string under a failure.

### Named Rules
**The System Faces Rule.** System fonts only. No webfont files, no network fonts; the brand faces stay in the brand banners.

**The Mono Means Data Rule.** Monospace is reserved for paths, the call route, the count and the status measurement (plus the raw error string they produce). Headings, labels, sentences and the documentation site name are set in the sans.

## Layout

The page is a full-height grid: top bar, then a two-column split (`minmax(0, 1fr)` each) divided by a 1px hairline. Horizontal padding is `clamp(20px, 4vw, 40px)` throughout; columns pad `clamp(28px, 6vh, 48px)` vertically. The left column is a vertical stack with 28px gaps whose last item, the answer field, is pushed to the column bottom (`margin-top: auto`) so the answer always lands in the same place. The name input and button sit on one row with a 12px gap; the input takes the remaining width.

Everything is flush left. The window opens at 900 × 680 and may be resized smaller. At 720px and below the split becomes one column, the divider turns horizontal, and the answer field follows the form directly instead of pinning to the bottom.

**The Split Console Rule.** Two equal halves: the left performs, the right explains. Do not centre a card, and do not add a third region.

## Elevation & Depth

Flat. There are no elevation shadows, gradients or glass. Structure comes from rules: a 2px ink rule under the top bar and above the file map, 1px hairlines for the column divider and file rows, and a translucent white hairline inside the answer field. The only `box-shadow` in the build is a 1px solid line under the focused input that thickens its underline, a stroke rather than depth.

**The Rules Not Shadows Rule.** Separate regions with rules and the one colour field, never with elevation.

## Shapes

Every surface is square (`border-radius: 0`): the button, the input, the answer field, the status square. The single exception is the layered NetWiv mark, whose plates keep the brand icon's rounded corners; a paper-filled gap plate cuts the separation between its back and front layers. The mark's colours come from tokens (light: lavender back, purple front, white N; dark: purple back, white front, purple N). The status indicator is an 8px square, not a dot.

**The Square Except the Mark Rule.** Rounded corners belong only to the mark.

## Components

### Buttons
A plain solid slab, one per screen.
- **Shape:** square (0px), at least 46px tall and 9.5em wide, 22px side padding.
- **Primary:** ink slab with white 600 text in light mode; in dark mode a light inverse block (ink-dark with paper-dark text).
- **Hover / Focus:** background changes to answer purple (light) or lavender (dark) over 160ms `cubic-bezier(0.16, 1, 0.3, 1)`; focus is a 2px focus-colour outline offset 3px.
- **Disabled (calling):** muted slab, progress cursor, label "Calling…".

### Inputs / Fields
- **Style:** no box; transparent ground, 17px text, a 2px ink underline, square.
- **Focus:** the underline turns focus colour and thickens by 1px; the caret uses the focus colour; no outline box.

### Top Bar
- **Style:** 24px mark and the app name (600, truncates with an ellipsis) at left; the status label at right in muted mono 13px with tabular figures, led by an 8px square whose colour tracks the state (hair idle, lavender calling, signal answered, muted failed or offline). A 2px rule closes the bar.
- **States:** Ready, Not in a NetWiv window, Calling C#, Answered in N ms, No answer.

### File Map
- **Style:** "Make it yours" title over a 2px ink rule, then four rows separated by 1px hairlines, 14px vertical padding, 4px between the path and its sentence. Paths are mono 600; the final row names the documentation site in bold sans because it is a site, not a file.

### Answer Field (signature component)
The purple block where C# answers.
- **Content:** the call route in mono lilac at the top, the message large in white, then a translucent hairline over "Calls counted in C#" with the count in mono at the right ("—" before the first call).
- **States:** idle ("No call yet", 500 weight, lilac), waiting ("Waiting for C#…" until the first answer; later calls keep the previous answer visible), answered, failed (ink block in light, raised dark violet in dark; "C# did not answer", a hint naming what to check, the raw error in mono).
- **Motion:** each new count re-keys the message and count, which play "land": 360ms `cubic-bezier(0.16, 1, 0.3, 1)` from 0.35 opacity, 6px lower and a 2px blur. Disabled under `prefers-reduced-motion`, as is the button's colour transition.

## Do's and Don'ts

### Do:
- **Do** change the screen in `scripts/generate-frontend-template-overlays.mjs` and regenerate all five frameworks; never hand-edit the generated `App.tsx`, `App.vue`, `App.svelte`, `app.ts`, `Home.razor` or their stylesheets.
- **Do** define every colour as a custom property on `:root` with a matching value in the dark set.
- **Do** keep the answer field the only colour field in both appearances, with the button as an ink slab in light and a light inverse block in dark.
- **Do** set paths, the call route, the count and the status measurement in the system monospace, with tabular figures for numbers.
- **Do** keep the layered mark as the only ornament, coloured from the mark tokens.
- **Do** keep "land" the only authored motion and disable it under reduced motion.

### Don't:
- **Don't** load webfonts or any remote asset; the starter runs offline from embedded files.
- **Don't** raise the dark set to a high-contrast black-and-white theme.
- **Don't** add rounded corners to anything except the mark.
- **Don't** use shadows, gradients, blur or glass for depth.
- **Don't** add a second colour field, a lavender ground, or a purple tint wash.
- **Don't** add icons, illustrations or decorative glyphs beyond the mark.
- **Don't** replace the split console with a centred hero card and a "Get started" button.
