# Product

<!-- impeccable:product-schema 1 -->

## Platform

web (rendered inside a NetWiv desktop window: WebView2 on Windows,
WebKitGTK 4.1 on Linux, WKWebView on macOS)

## Stack

The starter frontend that `netwiv new` scaffolds. One design, implemented
five times: React + TypeScript, Vue + TypeScript, Svelte + TypeScript and
Angular (each built with Vite or the Angular CLI and the `@netwiv/api`
package vendored into the project), and Blazor WebAssembly. Each copy must
stay small enough that a developer can read it in one sitting and delete it.
The C# backend in `src-netwiv/Program.cs` is shared by all five.

## Users

.NET developers who have just run `netwiv new` and opened the window for the
first time with `netwiv dev`. They want to know that the frontend reaches
their C# code, and where to start editing. Most will replace this screen with
their own app within minutes.

## Product Purpose

Prove the round trip in one screen: the user types a name, the frontend calls
the C# `greet` command through generated bindings, and the C# side answers
with a message and a call count kept in managed state. Then point at the
files to edit and the documentation to read next.

## Operating Context

- Shown in a desktop window that opens at 900 × 680 and can be resized
  smaller. It may also be opened in a plain browser through the dev server,
  where C# calls cannot run.
- Appears in development, often next to an editor and a terminal.
- Light and dark system appearance both occur.

## Capabilities and Constraints

- The backend command returns `{ message, count }`; the call can fail and the
  error must be shown.
- Automated GUI smoke tests open the window with `?smoke=1`; the starter must
  submit its form automatically in that mode and report the result through
  the `smoke_done` command.
- No network fonts or remote assets: the app must work offline from embedded
  assets.
- Templates ship to every user, so copy is English.

## Brand Commitments

- Name spelled `NetWiv`; lowercase `netwiv` for the CLI, config files and npm.
- Brand assets live in `assets/brand/`: the mark (stacked layers with a woven
  N), palette `#512BD4`, `#B9A8F5`, `#17142A`, `#F6F5FB`, `#FFFFFF`, and
  JetBrains Mono (OFL-1.1) and Roboto (Apache-2.0) in the banners.
- Voice: plain and factual. No overstated capability claims.
- The starter is the user's app, not a NetWiv advertisement: NetWiv identity
  stays modest and easy to remove.
