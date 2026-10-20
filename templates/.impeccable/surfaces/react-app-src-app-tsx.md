---
version: 1
slug: "react-app-src-app-tsx"
primary_target: "react-app/src/App.tsx"
related_targets: ["frontends/vue/src/App.vue","frontends/svelte/src/App.svelte","frontends/angular/src/app.ts","frontends/blazor/Pages/Home.razor"]
---

## Scope

The starter screen that `netwiv new` scaffolds, one design built in React, Vue, Svelte and Angular (TypeScript) and Blazor. Visitor mode: Operate.

## Audience and task

A .NET developer who just ran `netwiv dev`: confirm the frontend reaches C#, then find the files to edit. States: idle, calling, answered (message + count), failed (error text), and opened in a plain browser where C# is unreachable. `?smoke=1` auto-submits and reports through `smoke_done`.

## Direction contract

THESIS: The first screen is a working console, not a welcome page: one half performs the C# round trip, the other half is the map of what to edit. It refuses the centered hero card with a logo and a "Get started" button.

OWN-WORLD: NetWiv's Swiss technical program. Paper #f6f5fb and ink #17142a, one purple #512bd4 field, lavender #b9a8f5 for secondary marks, hairlines #d9d5ea, square corners everywhere, system sans for reading and the system monospace for paths and labels. The layered NetWiv mark is the only ornament. Dark appearance swaps to the site's soft dark set.

STORY: The developer sees the window is alive, types a name, presses Call greet, and watches the C# answer land with the call count kept in C# state. Then they read which file owns which part and leave to edit src/App and src-netwiv/Program.cs.

FIRST VIEWPORT: A thin top bar with the mark and app name at left and a connection label at right. Below, two equal columns split by a hairline. Left: a mono kicker naming the path (frontend to C#), a two-line headline, the name input underlined in ink with the Call greet button beside it, and the purple response field pinned to the column bottom showing the message large and the count in mono. Right: "Make it yours" over four ruled rows, path in mono and one plain sentence each. Signature move: the purple field names the C# method that answered and the count steps up on each call.

FORM: Split console, position 5 of 7 on the ranked list, seed:af3fdded694cd101

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance
