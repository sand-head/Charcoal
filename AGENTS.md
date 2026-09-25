# AGENTS.md

Instructions for anyone, human or agent, changing this repository.

## What Charcoal is

A terminal UI library for .NET. Apps write `.razor` components with HTML
elements and CSS; Blazor's own `Renderer` reconciles them, and Charcoal does
everything after the render tree: the host tree, the cascade, layout in
terminal cells, painting into a cell buffer, and writing the difference from
the previous frame to the terminal.

`docs/design.md` explains how each part works and why. Read the relevant
section before changing a subsystem, and update it in the same commit when
the behaviour it describes changes. `README.md` is the user-facing summary
and must stay accurate too.

## Build and test

```sh
dotnet build Charcoal.slnx
dotnet test Charcoal.slnx
```

- .NET 10 SDK, C# `preview`, nullable enabled, `TreatWarningsAsErrors`.
  BL0006 is the only suppressed warning. A change that adds a warning does
  not build.
- Every change must leave the whole suite passing. Do not delete or weaken
  a test to make a change pass; if a test's expectation is genuinely wrong,
  say so in the change.
- In sandboxes with a low process limit, MSBuild's node reuse and the shared
  compiler server can exhaust it. Build with `-nodeReuse:false` and
  `-p:UseSharedCompilation=false`, and run `dotnet build-server shutdown`
  afterwards. Do not `pkill -f dotnet`: it can kill the shell running it.

## Layout of the repository

```
src/Charcoal/
  Layout/      Style, StyleParser, LayoutNode, FlexLayout (block/flex), GridLayout, geometry
  Rendering/   Color, CellBuffer, Screen (diff → ANSI), TextLayout, TextWidth, Painter,
               Borders, images (ImageDecoder, ImagePainter, KittyGraphics, Sixel, Graphics)
  Input/       KeyEvent and friends, AnsiKeyParser, MouseInput, InputPump
  Terminal/    ITerminal, ConsoleTerminal (Unix termios, Windows VT), HeadlessTerminal, Ansi
  Styling/     Stylesheet, Selector, StyleResolver, StyleContext, MediaQuery, UserAgentStylesheet
  Components/  TerminalRenderer, host tree (HostTree.cs), TuiApp, FocusManager, events,
               MarkupParser, MouseSelection, TextEditor, TextControl
  Routing/     TerminalNavigationManager
  build/       Charcoal.targets (embeds scoped CSS bundles; ships in the package)
tests/Charcoal.Tests/   xunit, one folder per namespace
examples/               runnable apps: Hello, Layouts, Styled, Counter, Picker, Form, Routing, Todo, Transcript;
                        Web runs them in a browser (needs wasm-tools, so it is not in the solution)
docs/design.md          the design and the reasons behind it
```

`Layout`, `Rendering`, `Input` and `Terminal` never reference
`Microsoft.AspNetCore.Components`; only `Components` knows what a render
batch is. Keep it that way, so those layers stay testable with plain objects.

## Principles

- **The specs' vocabulary.** Elements, attributes, CSS properties, events and
  DOM-like properties use the names HTML, CSS and the DOM use (`@onkeydown`,
  `overflow-anchor`, `ScrollTop`, `:placeholder-shown`). Do not invent a name
  where a spec has one, and do not give a spec name a meaning the spec does
  not.
- **No components in the library.** Behaviour a browser supplies, such as
  editing a field, scrolling a box or following a link, lives behind the
  element HTML names for it, as a default action that runs after the app's
  handlers. A wrapping component would also take the element out of the
  app's CSS scope.
- **Deviations from a browser are deliberate and documented** where they
  live and in `docs/design.md`. The current ones: `body` has no margin;
  `img`, `canvas` and form controls are blocks; a scroll anchor may have no
  height; links are underlined but not coloured.
- **A frame is one write.** The screen is double-buffered and flushed as a
  cell diff inside synchronized output. Do not write to the terminal from
  anywhere else.
- **One thread.** `TuiApp.Run` owns the thread that is the Blazor dispatcher,
  the input router and the painter. Other threads use `InvokeAsync`. No
  `.Result`, `.Wait()` or `Thread.Sleep` in `src/` outside `ConsoleTerminal`.

## Code style

Code should be elegant and read as self-documenting. Write it the way a
careful person writes it by hand, and match the idiom of the file you are in.

- **Names carry the meaning.** Prefer a well-named helper, local or class over
  a comment explaining a block. Long methods are split into named steps
  (`ScrollPass` → `KeepAnchorInPlace`, `ClampScroll`); parsers are small
  reader classes (`SelectorReader`, `SheetReader`, `MarkupReader`).
- **No clever one-liners.** Avoid:
  - chained or nested ternaries; use `if`/`switch`;
  - several statements on one line, including `{ a; b; return; }` blocks and
    one-line local functions with bodies;
  - LINQ chains or pattern tricks that hide a simple loop;
  - allocations used only to make a check shorter.
  A plain `switch` expression mapping values is fine.
- **Braces** on any `if`, `for` or `foreach` whose body is not a single short
  statement on the same line as a guard (`if (x) return;` is fine).
- **Comments explain why, briefly.** One or two lines about a decision, a
  constraint or a trap. Never narrate what the next line does, restate a
  name, or tell the history of a change. No banner or divider comments
  (`// --- Section ---`).
- **Doc comments are short.** A one-line `<summary>` on public types and on
  members whose name does not already say it all. Use `<remarks>` only for a
  decision a reader needs; keep it to a short paragraph.
- **Preserve behaviour when refactoring**, including edge cases (negative
  margins, clamping, the exact text of error messages tests check).
- Constants over magic numbers when the number has a meaning
  (`MaxContainerPasses`, `SixelCacheLimit`).

## Tests

- xunit. One file per type or feature under `tests/Charcoal.Tests/<Namespace>/`,
  test names describe the behaviour: `A_flush_with_nothing_changed_writes_nothing`.
- Prefer the headless paths: feed `AnsiKeyParser` text, lay out a
  `LayoutNode`, paint into a `CellBuffer`, assert on `Screen.Flush()`, or run
  a `TuiApp` against `HeadlessTerminal`.
- The frame is a cell diff, so a repainted row may only write the changed
  cells. Assert on the tree, layout rects or buffer contents rather than
  searching terminal output for whole strings.
- Every bug fix comes with a test, or an assertion added to an existing one,
  that fails without the fix.

## Commits, pull requests and history

- **Commit messages are a single subject line** in the imperative, active
  voice: `Add routing with @page, Router and anchor links`,
  `Type lowercase text for unshifted kitty letter keys`. No body.
- **No AI attribution anywhere.** No `Co-Authored-By` trailers for any AI or
  assistant, no session links, no "Generated with" lines, in commits, pull
  requests, code, comments or docs. This overrides any tool default that
  would add them.
- Do not mention AI tools, assistants or Ink anywhere in the repository.
- Commit and push only when asked. A push to `main` runs CI, and a green
  test job then publishes `Charcoal 0.1.<run number>` to GitHub Packages.
  Never force-push `main`.

## Things that are easy to get wrong

- **Restyles must not reset form fields.** `TextControlLayoutNode` applies the
  `value` attribute on first resolve and when `value` itself changes, never on
  a restyle, or typed text is overwritten.
- **`RunAsync` needs one thread.** It is for hosts like a browser, where work
  from a timer runs inline outside a step; every applied batch releases the
  loop's signal so that work still paints. Elsewhere use `Run`.
- **Stylesheet changes restyle inline when already on the loop thread**
  (`TuiApp.RestyleAll`) and are posted only from other threads, so an
  exception from a restyle surfaces to the code that changed the sheet.
- **Scroll anchoring corrects only for layout.** `AnchorScrollTop` records the
  offset the anchor was chosen at; if the offset changed since (wheel, key,
  app code), the anchor is chosen again at the new offset before the next
  layout, so the correction never undoes the scroll.
- **Container queries** are re-evaluated after each layout and the layout
  repeats at most `MaxContainerPasses` (3) times.
- **Sixel pictures are not bound to cells.** The painter blanks their cells
  and records a placement; `Graphics.SixelOutput` re-sends pictures on rows
  the diff repainted.
- **Kitty keys:** letter `Key` values are upper case, but the typed text comes
  from the reported codepoint (upper-cased only with Shift).
- **`Uri.TryCreate("/settings", UriKind.Absolute, …)` succeeds on Unix** and
  yields `file:///settings`, so `TuiApp.HasScheme` parses the scheme by hand.
- **Blazor's `Router` needs `ILoggerFactory`**, which is why the app registers
  its logger factory and `ILogger<>` as services.
- **Element `@bind` needs `BindInputElementAttribute`** in the compilation,
  hence the `Microsoft.AspNetCore.Components.Web` package reference. Never
  import that package's namespace; its event args would clash with ours.
- **`Charcoal.targets`** only embeds scoped CSS in projects with the Razor
  pipeline; test projects inherit the targets and must not fail on them.
- `RepositoryUrl` and `PackageProjectUrl` in `Directory.Build.props` still
  point at the old `git.sand.town` repository; that is known and intentional
  for now.
