# Charcoal

Razor components, rendered to the terminal.

Charcoal runs `.razor` components through Blazor's own renderer and paints the
result onto a grid of terminal cells. You write HTML elements and CSS. There
are no Charcoal components to learn.

> **Status: early.** Linux works end to end. The Windows console code is
> written but untested.

## Install

```sh
dotnet add package Charcoal
```

Packages are published to GitHub Packages on each merge to `main`, so add
the source first:

```xml
<configuration>
  <packageSources>
    <add key="github" value="https://nuget.pkg.github.com/sand-head/index.json" />
  </packageSources>
</configuration>
```

Your app needs the Razor SDK so `.razor` files compile and `.razor.css` files
are scoped:

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

Add an `_Imports.razor`:

```razor
@using Charcoal.Components
@using Charcoal.Layout
@using Charcoal.Rendering
@using Charcoal.Input
```

## Usage

`App.razor`:

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

`App.razor.css`, scoped to this component like any Blazor scoped stylesheet:

```css
.app { padding: 1 2; height: 100% }
.banner { width: fit-content; border: solid cyan; padding: 0 1; color: green }
.muted { opacity: 0.5 }
```

`Program.cs`:

```csharp
return new TuiApp().Run<App>();
```

## Elements

Write the HTML tags you already use: `div`, `p`, `span`, `strong`, `em`,
`h1`–`h6`, `ul`, `li`, `pre`, `a`, `img`, `canvas`, `input`, `textarea`, `br`,
`hr`, and the rest.

A user-agent stylesheet gives them their usual look. Blocks stack, `strong` is
bold, `em` is italic, `pre` keeps its whitespace, paragraphs get a blank line
above and below. Text can sit directly inside a block.

Two things differ from a browser, on purpose:

- `body` has no margin.
- Images, canvases and form controls are blocks, not inline boxes.

## CSS

Style with `style="…"`, `class` and stylesheets. One cascade: the user-agent
sheet, then your sheets by specificity and order, then inline styles.

| | |
|---|---|
| Layout | `display: block \| flex \| grid \| inline \| none`, the flex and grid properties, `position: absolute`, `overflow`, `visibility` |
| Sizing | `width`, `height`, `min-*`, `max-*` in cells, `%` or `fit-content` |
| Spacing | `padding`, `margin` with logical longhands, collapsing block margins, `gap` |
| Borders | `border: solid \| double \| dashed`, `border-radius` for rounded corners, `border-width: thick` for heavy lines, one side at a time |
| Text | `color`, `background`, `font-weight`, `font-style`, `text-decoration`, `text-align`, `white-space`, `text-overflow`, `opacity` (dim), `filter: invert()` |
| Selectors | type, `.class`, `#id`, `:focus`, `:focus-within`, `:root`, `:first-child`, `:last-child`, `:disabled`, `:enabled`, `:placeholder-shown`, `[attr]`, descendant and child combinators |
| Values | CSS colour names, `#rrggbb`, `rgb()`, custom properties and `var()` |

`text-overflow: ellipsis` cuts the end, `ellipsis clip` the start, and
`ellipsis ellipsis` the middle. Named colours follow the terminal palette
(`red` is the terminal's red); `gold` and the rest are exact.

Properties a terminal cannot draw, like `font-family` and `box-shadow`, are
parsed and ignored, so a stylesheet shared with a web page will load.

### Media queries

`@media` works against the terminal as a device:

| Feature | Value |
|---|---|
| `width`, `height` | cells, with `min-`/`max-` and range syntax like `(60 <= width < 120)` |
| `prefers-color-scheme` | the terminal's background colour, asked for with OSC 11 |
| `color` | bit depth, from `COLORTERM` |
| `pointer`, `hover` | whether the mouse is on |
| `resolution` | device pixels per cell, once the terminal reports its cell size |
| `orientation`, `aspect-ratio`, `grid`, `display-mode` | the usual values |

`not`, `and`, `or`, nesting and media types all work. A resize re-resolves
only the rules that changed. An unknown feature makes its query false instead
of making the sheet an error. `@supports` is answered at parse time.

### Container queries

`container-type: inline-size | size` and `container-name` make a query
container. `@container (max-width: 30) { … }` then styles its descendants
against its content box. Containers are checked after each layout.

### Scoped stylesheets

A `Component.razor.css` beside a component is scoped to that component, using
the same build step, the same `[b-…]` attribute and the same `::deep` as
Blazor on the web. `TuiApp` loads each assembly's bundle before the first
frame. For a global sheet, call `app.AddStylesheet(css)`.

## Events

`@onkeydown`, `@onclick`, `@onmousedown`, `@onmouseup`, `@onmousemove`,
`@onwheel`, `@onscroll`, `@onfocus`, `@onblur`, `@onpaste`, `@oninput`,
`@onchange`.

Keys go to the focused element and bubble up. `tabindex="0"` joins the Tab
order; `tabindex="-1"` can be focused by a click but not by Tab. A left click
focuses the nearest focusable element.

There is no `@onkeyup`: terminals report a key going down and nothing else.

A focused form control puts the terminal's cursor at its caret. On any other
element, `caret="col,row"` places it.

## Forms

`<input>` and `<textarea>` edit text on their own: a caret on grapheme
boundaries, readline keys (word jumps, Ctrl+A/E, Ctrl+U/K/W, Alt+D), paste,
click to place the caret, and soft-wrapped lines in a textarea. `text`,
`search`, `password`, `email`, `tel`, `url`, `date`, `time`,
`datetime-local`, `month` and `week` are text fields; temporal values use
normal ISO text because terminals have no calendar popup.

`type="number"` accepts decimal text and ArrowUp/ArrowDown steps by `step`,
within `min` and `max`. `type="range"` is a terminal slider: arrows step it,
Home and End choose its bounds, and a click positions its thumb. Checkboxes
and radios paint their normal glyphs, toggle with Space or a click, expose
`:checked` and `:indeterminate`, and support `@bind` to a `bool`. Radios with
the same `name` are one document-wide group; Charcoal has no form scoping.

`@bind` and `@bind:event="oninput"` work as on the web, as do `value`,
`checked`, `placeholder`, `disabled`, `readonly`, `min`, `max`, `step`,
`maxlength`, `size`, `rows`, `cols`, `wrap="off"` and `autofocus`. A
`<button>` and `input` types `button`, `submit` and `reset` are tabbable and
fire `@onclick` on Enter, Space or a click. `submit` and `reset` have no
special action because Charcoal does not yet
implement form submission or reset.

Your `@onkeydown` sees each key before the control does. Take Enter for submit
or Up for history and the control will not use it.

## Scrolling

`overflow: auto` makes any element a scroll container. Charcoal scrolls it:
the wheel over it, and ↑ ↓ PageUp PageDown Home End while it has focus.

`ScrollTop`, `ScrollHeight` and `ClientHeight` are on the element, reached
through `@ref`. `@onscroll` fires when it moves.

Scroll anchoring (`overflow-anchor`) works as the spec describes. One node
under the container is held still across layouts, so content growing above
what you are reading does not push it down.

To pin a log to the bottom, use the same technique as the web: rule out the
children and leave a sentinel at the end.

```css
.log > *       { overflow-anchor: none }
.log > .bottom { overflow-anchor: auto }
```

Scroll up and the sentinel leaves the view, so the log stops following.

The sentinel needs no height here. On the web it needs `height: 1px`, but the
smallest height a terminal has is a whole row. A stylesheet written for the
web still works; it just costs a row.

## Routing

Routing is Blazor's, unchanged: `@page`, `<Router>`, `<RouteView>`,
`NavigationManager` and `NavigateTo`.

A terminal has no address bar, so `TerminalNavigationManager` keeps the
location in memory under `tui:///`, with `Back()` for history.

```razor
@page "/inbox/{Id:int}"

<h1>message @Id</h1>
<p><a href="/inbox">back to the inbox</a></p>
```

`<a href>` is underlined, joins the Tab order without a `tabindex`, and is
followed by Enter or by a click.

An `href` with a scheme the app does not own, like `https://` or `mailto:`, is
left alone and reported on `TuiApp.LinkFollowed`, so you decide what to do
with it.

## Images

```razor
<img src="logo.png" width="24" alt="[logo]" />
```

PNG, JPEG, GIF, BMP, TGA and PSD decode with no native dependency. How an
image is drawn depends on the terminal:

| Terminal | How |
|---|---|
| kitty, WezTerm, Ghostty, Konsole | kitty graphics protocol, full resolution, clips and scrolls like text |
| xterm, foot, mlterm, Contour | Sixel, cropped to the visible cells and re-sent only where the frame changed |
| anywhere else | two pixel rows per cell in truecolour |

Give one side and the other follows the image's shape. Give neither and it
shrinks to fit. `alt` shows if the file will not decode.

## Selection and clipboard

Drag with the left button to select cells. Releasing copies them to the
clipboard with OSC 52, so it works over ssh: the clipboard belongs to the
terminal in front of the user. Ctrl+C during a drag copies too.

A drag only selects if no handler takes it first.

`user-select: none` keeps an element out of it. `user-select: contain` keeps a
drag inside one element. `app.CopyToClipboard(text)` copies anything else.

## Text

Text is measured in grapheme clusters using wcwidth, so CJK and emoji take two
cells and combining marks take none. `white-space` controls wrapping and
collapsing; `text-overflow` controls the cut.

## Performance

The screen is double-buffered. Each frame writes only the cells that changed,
with one pen change per run, wrapped in synchronized output. A streaming
transcript of ten thousand lines paints in milliseconds.

## Testing

`HeadlessTerminal` captures every frame and injects input. The layout engine,
the painter and the key parser are all pure, so they can be tested on their
own.

## Examples

Each directory under `examples/` is a runnable project.

| Example | Shows |
|---|---|
| `Hello` | The smallest app: HTML tags, a scoped stylesheet, a picture |
| `Layouts` | Flex, wrap, grid, automatic minimums and scrolling, one page each |
| `Styled` | Cards styled by a global sheet: classes, ids, `:focus`, custom properties |
| `Counter` | State, a timer, key and mouse handlers |
| `Picker` | A list with keyboard and mouse selection |
| `Form` | `input` and `textarea` with `@bind`, Tab between fields |
| `Routing` | Four pages, a nav bar and a route parameter |
| `Todo` | A task and reminder app: completion, due times, reminder lead, priority, notes and filters |
| `Transcript` | A streaming transcript with a composer; `--bench` prints frame costs |
| `Web` | All of the above in a browser, through `TuiApp.RunAsync` and the [slopterm](https://git.sand.town/sand_head/slopterm) emulator |

```sh
dotnet run --project examples/Transcript -- --lines 5000
dotnet run --project examples/Transcript -- --bench
```

`Web` is published to GitHub Pages on each merge to `main`. It is not in the
solution, because building it needs the `wasm-tools` workload:

```sh
dotnet workload install wasm-tools
dotnet run --project examples/Web
```

## How it works

```
.razor components → Blazor Renderer → host tree → cascade → layout → Painter → one write
terminal input → AnsiKeyParser → InputPump → focus and bubbling → @onkeydown …
```

`TuiApp.Run` owns one thread and is the Blazor dispatcher, the input router
and the painter. Reach it from another thread with `InvokeAsync`, as on the
web. Where the one thread must not block, as in a browser, `TuiApp.RunAsync`
runs the same loop and awaits input instead.

The design and the reasons behind it are in [docs/design.md](docs/design.md).

## Not supported yet

- Horizontal scrolling by wheel or key (`ScrollLeft` and `overflow-x` work)
- Clicking an inline element: hit-testing stops at the block, so a link in a
  flex nav bar is clickable but one inside a paragraph is Tab and Enter only
- List markers — `li` stacks but does not bullet
- File and image inputs, form submission and form reset
- Calendar, clock and colour-picker popovers; temporal values are editable ISO text
- `margin: auto` centring, `!important`, pseudo-elements
- OSC 8 terminal hyperlinks for external links
- `#fragment` matches a route but scrolls nothing
- iTerm2 inline images

## Building

```sh
dotnet build
dotnet test
```

.NET 10 SDK. No native dependencies.

## License

MIT.
