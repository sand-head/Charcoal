# Charcoal

A component-style terminal UI library for .NET. Write `.razor` components
with HTML elements and CSS, and get a cell-diffed terminal frame out.
Blazor's own renderer reconciles the components; everything after the
render tree is this library.

```razor
@inject TuiApp App

<div class="app" tabindex="0" @onkeydown="OnKey">
    <div class="banner">Hello from Charcoal</div>
    <p class="muted">Press q to quit.</p>
</div>

@code {
    private void OnKey(KeyboardEventArgs e)
    {
        if (e.Key.Text != "q") return;
        App.Exit();
        e.Handled = true;
    }
}
```

```css
/* App.razor.css, scoped to App's elements as on the web */
.app { padding: 1 2; height: 100% }
.banner { width: fit-content; border: solid cyan; border-radius: 1; padding: 0 1; color: green; font-weight: bold }
.muted { opacity: 0.5 }
```

```csharp
return new TuiApp().Run<App>();
```

> **Status: early.** Linux works end to end; Windows is untested. The design
> is described in [docs/design.md](docs/design.md).

## What you get

- **HTML elements.** `div`, `p`, `span`, `strong`, `em`, `h1`–`h6`,
  `ul`/`li`, `pre`, `img`, `canvas`, `br`, `hr` and the rest, with a
  user-agent stylesheet that gives them their usual look: blocks stack,
  `strong` is bold, `em` italic, `mark` highlighted, `pre` keeps its
  whitespace, and paragraphs have a blank line around them. Bare text
  under a block needs no component. The library ships no components of its
  own: scrolling, editing and drawing all happen behind standard elements.
- **CSS, in cells.** `style="…"`, `class` and stylesheets go through one
  cascade: the user-agent sheet, then the app's sheets by specificity and
  order, then the inline style. Supported: `display: block | flex | grid |
  inline | none`; the flex and grid properties; sizes in cells, `%` or
  `fit-content`; `padding` and `margin` with the logical longhands and
  collapsing block margins; `gap`, `overflow`, `position: absolute`,
  `visibility`; `border` with `solid`, `double`, `dashed`, rounded corners
  from `border-radius` and heavy lines from `border-width: thick`; `color`,
  `background`, `font-weight`, `font-style`, `text-decoration`, `opacity`
  (dim), `filter: invert()` (inverse), `white-space`, `text-overflow`,
  `text-align`; custom properties with `var()`; `:focus`, `:focus-within`,
  `:root`, `:first-child`, `:last-child`, attribute selectors, and the
  descendant and child combinators. Colours are CSS names (the sixteen
  basic ones follow the terminal's palette), `#rrggbb` or `rgb()`.
  Properties a terminal cannot draw, such as `font-family`, are ignored, so
  a page's stylesheet still loads.
- **Media queries.** `@media` treats the terminal as the device: `width`
  and `height` in cells, with `min-`/`max-` and the range syntax such as
  `(60 <= width < 120)`; `orientation`; `aspect-ratio`;
  `prefers-color-scheme`, from the terminal's own background colour;
  `color`, from `COLORTERM`; `pointer`, from the mouse setting;
  `prefers-reduced-motion`, set by the app; `resolution` in `dppx`, once the
  terminal reports its cell size; and `not`, `and`, `or`, nesting and media
  types. A resize or a new colour scheme restyles what changed. `@supports`
  is answered from the property parser. An unknown feature or a pixel
  length makes a query false rather than failing the sheet.
- **Container queries.** `container-type: inline-size | size` and
  `container-name` make an element a query container. `@container [name]
  (max-width: 30) { … }` styles its descendants by its content box, with
  `inline-size` and `block-size` as well as `width` and `height`.
- **Component-scoped stylesheets.** A `Component.razor.css` beside a
  component is scoped to it exactly as Blazor does on the web, `::deep`
  included. The build embeds the bundle in the assembly and `TuiApp` loads
  it, and every referenced library's, before the first frame.
  `app.AddStylesheet(css)` adds a global sheet.
- **Pictures.** `<img src="logo.png" width="24" />` decodes PNG, JPEG, GIF,
  BMP, TGA and PSD without a native library. Terminals with the kitty
  graphics protocol (kitty, WezTerm, Ghostty, Konsole) show it at full
  resolution, and it still clips and scrolls like text. Sixel terminals
  (xterm, foot, mlterm, Contour) get it as Sixel, cropped to the visible
  cells. Elsewhere it is drawn as half blocks in truecolour. Given one side,
  the other follows the image's shape; given neither, it shrinks to fit.
  `alt` shows when the source does not decode.
- **Form controls.** `<input>` and `<textarea>` edit text with readline's
  keys, paste, click-to-place caret, and soft-wrapped lines in a textarea.
  `@bind` and `@bind:event="oninput"` work as on a page, as do `value`,
  `placeholder`, `type="password"`, `disabled`, `readonly`, `maxlength`,
  `size`, `rows`, `cols`, `wrap="off"` and `autofocus`. `@oninput` and
  `@onchange` carry the value, and `:disabled`, `:enabled` and
  `:placeholder-shown` style the controls. Your `@onkeydown` sees each key
  first, so Enter or Up can submit or recall history.
- **Blazor.** Parameters, `@key`, `@ref`, `EventCallback`, cascading values,
  `@inject`, `StateHasChanged` and `InvokeAsync` work as they do on the web.
- **Flexbox and grid on cells.** Direction, wrap, justify, align (items,
  self, content), grow/shrink/basis, percent and auto sizes, min/max with
  CSS's content-based automatic minimum, borders and absolute positioning.
  `display: grid` supports track templates such as `12 1fr auto 25%` and
  `repeat(3, 1fr)`, explicit placement and spans, auto-placement, gaps and
  per-cell alignment. Layout is cached per node, so a frame only
  re-measures what changed.
- **Scrolling.** `overflow: auto` makes any element a scroll container that
  scrolls with the wheel, and with ↑ ↓ PageUp PageDown Home End while
  focused. `ScrollTop`, `ScrollHeight` and `ClientHeight` are on the
  element, reachable through `@ref`, and `@onscroll` fires when it moves.
  Scroll anchoring (`overflow-anchor`) works as in CSS, so content growing
  above what you are reading does not push it down. To keep a log pinned to
  the bottom, use the same stylesheet as on the web:

  ```css
  .log > *       { overflow-anchor: none }
  .log > .bottom { overflow-anchor: auto }
  ```

  Unlike on the web, the sentinel needs no height, since the smallest height
  a terminal has is a whole row. Scrolling up moves the sentinel out of
  view, and the log stops following.
- **Text that measures right.** Grapheme clusters and wcwidth, so CJK and
  emoji take two cells and combining marks take none. `white-space` decides
  wrapping and collapsing, and `text-overflow` decides the cut.
- **Routing.** Blazor's own `@page`, `<Router>`, `<RouteView>`,
  `NavigationManager` and `NavigateTo`. Without an address bar,
  `TerminalNavigationManager` keeps the location in memory at `tui:///`.
  `<a href>` is underlined, tabbable without a `tabindex`, and followed by
  Enter or a click. Links outside the app, such as `https://` or `mailto:`,
  are only reported through `TuiApp.LinkFollowed`.
- **Events.** The DOM's names: `@onkeydown`, `@onclick`, `@onmousedown`,
  `@onmouseup`, `@onmousemove`, `@onwheel`, `@onscroll`, `@onfocus`,
  `@onblur`, `@onpaste`, `@oninput` and `@onchange`. Keys go to the focused
  element and bubble up. `tabindex="0"` joins the Tab order,
  `tabindex="-1"` is focusable by click only. Form controls place the
  terminal's cursor at their caret, and `caret="col,row"` does the same for
  any other focused element.
- **Mouse selection.** Dragging selects text, and releasing copies it to the
  clipboard with OSC 52, which also works over ssh. Ctrl+C during a drag
  copies too. `user-select: none` keeps an element out of the selection and
  `user-select: contain` confines a drag to an element's box.
  `app.CopyToClipboard(text)` copies anything else.
- **A frame is one write.** The screen is double-buffered and only changed
  spans are repainted, inside synchronized output. A streaming transcript of
  ten thousand lines paints in a few milliseconds.
- **Testable.** `HeadlessTerminal` captures every frame and injects input;
  the layout engine, the painter and the key parser have no I/O.

## Examples

Each one under `examples/` is a runnable project:

| Example | Shows |
|---|---|
| `Hello` | The smallest app: HTML tags, a scoped stylesheet, a picture. |
| `Layouts` | A gallery of the layout subset, page by page: flex, wrap, grid, automatic minimums, scrolling; one `.razor` and one `.razor.css` per page. |
| `Styled` | Cards styled by a global stylesheet: classes, ids, `:focus`, custom properties, inheritance, inline overrides. |
| `Counter` | State, `:focus` from a scoped sheet, a timer, key and mouse handlers. |
| `Picker` | A list with keyboard and mouse selection, rows that skip unchanged renders. |
| `Form` | `input` and `textarea` with `@bind`, a placeholder, a password, `autofocus`, Tab between fields, `:focus` and `:placeholder-shown`. |
| `Transcript` | A streaming, bottom-anchored transcript with a composer, entirely in components; `--bench` prints frame costs for 10,000 lines headless. |

```sh
dotnet run --project examples/Transcript -- --lines 5000
dotnet run --project examples/Transcript -- --bench
```

## Installing

Packages are published to GitHub Packages on each merge to `main` (pre-1.0):

```xml
<configuration>
  <packageSources>
    <add key="github" value="https://nuget.pkg.github.com/sand-head/index.json" />
  </packageSources>
</configuration>
```

```sh
dotnet add package Charcoal
```

An app project uses the Razor SDK, so its `.razor` files compile and its
`.razor.css` files are scoped and bundled:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Charcoal" Version="0.1.*" />
  </ItemGroup>
</Project>
```

with an `_Imports.razor` of `@using Charcoal.Components`, `@using Charcoal.Layout`,
`@using Charcoal.Rendering`, `@using Charcoal.Input`.

## How it is put together

```
.razor components → Blazor Renderer → host tree → cascade → block/flex/grid layout → Painter → Screen.Flush() → one write
terminal input thread → AnsiKeyParser → InputPump → focus + bubbling → @onkeydown …
```

`TuiApp.Run` owns one thread: it is the Blazor dispatcher, the input router
and the painter. Code on other threads reaches it with `InvokeAsync`, as on
the web. See [docs/design.md](docs/design.md) for details.

## Not yet supported

iTerm2 inline images; `!important`; pseudo-elements; `margin: auto`
centring; horizontal scrolling with the wheel and keys, though the node's
`ScrollLeft` works; `keyup`, which terminals do not report; OSC 8
hyperlinks for external links; scrolling to a `#fragment`, which still
matches a route; list markers; and clicks on inline elements inside text,
which go to the block that holds them. An inline element that a flex or
grid container gives its own box, such as a link in a nav bar, is
clickable.

## Building

```sh
dotnet build
dotnet test
```

.NET 10 SDK. No native dependencies.

## License

MIT.
