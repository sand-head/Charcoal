# sloptui design

sloptui renders Blazor components to a terminal. Components are `.razor`
files reconciled by Blazor's own `Renderer`; the library lays them out with
flexbox on a grid of terminal cells, paints them into a cell buffer, and
writes the difference from the previous frame to the terminal.

The component model is Blazor's. Everything after the render tree belongs to
this library.

## Layers

```
 .razor components ──► Blazor Renderer ──► host tree (HostElement/HostText)
                                               │  LayoutNode: Style + Children
                                               ▼
                                          FlexLayout (measure/arrange, cached)
                                               │  Rect per node, absolute cells
                                               ▼
                                          Painter ──► CellBuffer (Screen.Back)
                                               │
                                               ▼
                                          Screen.Flush() ──► one write ──► ITerminal
 ITerminal input thread ──► AnsiKeyParser ──► InputEvent ──► focus/bubbling ──► @onkeypress …
```

| Namespace | Owns | Depends on |
|---|---|---|
| `SlopTui.Layout` | `Style` (the CSS subset), `Length`, `Edges`, geometry, `LayoutNode`, `FlexLayout`, `StyleParser` | `SlopTui.Rendering` for `Color` only |
| `SlopTui.Rendering` | `Color`, `TextStyle`, `TextRun`, `Cell`, `CellBuffer`, `Screen` (diff → ANSI), `TextWidth`, `TextLayout` (wrapping), `Painter`, `ITextContent`, `ICustomPaint` | `SlopTui.Layout` for `Rect`/`Style` |
| `SlopTui.Input` | `Key`, `KeyModifiers`, `KeyEvent`, `MouseEvent`, `PasteEvent`, `FocusEvent`, `AnsiKeyParser`, `InputPump` | nothing |
| `SlopTui.Terminal` | `ITerminal`, `ConsoleTerminal` (Unix termios + Windows VT), `HeadlessTerminal`, `TerminalOptions` | `SlopTui.Layout` for `Size` |
| `SlopTui.Styling` | `Stylesheet`, `Selector`, `StyleResolver`, `StyleContext` — a CSS subset over the same properties | `SlopTui.Layout`, `SlopTui.Components` (the host tree it matches) |
| `SlopTui.Components` | `TerminalRenderer`, `TerminalDispatcher`, host tree, `TuiApp`, `Box`, `Text`/`Run`, `Canvas`, `ScrollBox`, `Spacer`, `Newline`, focus, event args, `EventHandlers` | everything above |

Rules between the layers:

- `Layout`, `Rendering`, `Input`, `Terminal` never reference
  `Microsoft.AspNetCore.Components`. They are testable with plain objects.
- Only `Components` knows what a render batch is.
- Every layer has a headless test path: the parser is pure (text and a clock
  stamp in, events out), the layout engine works on any `LayoutNode`, the
  painter writes into a `CellBuffer`, `Screen.Flush()` returns a string, and
  `HeadlessTerminal` captures writes and injects input.

## The host tree

Blazor's diff produces edits against a tree of *frames*: elements, text,
attributes, components, regions. The browser renderer applies those edits to
the DOM through a "logical element" layer where components and regions are
containers that do not exist in the DOM. The host tree does the same:

- `HostNode` — base: `Parent`, logical `Children`.
- `HostElement : HostNode, LayoutNode` — a named element (`box`, `text`,
  `canvas`) with attributes. Its *layout* children are its logical descendants
  with containers flattened out.
- `HostTextNode : HostNode` — a text frame. It is not a layout node; the
  nearest `text` element ancestor collects its runs.
- `HostContainer : HostNode` — a component or region. Transparent to layout.

Edits are applied exactly as `BrowserRenderer.ts` does: `PrependFrame`,
`RemoveFrame`, `SetAttribute`, `RemoveAttribute`, `UpdateText`, `StepIn`,
`StepOut`, `PermutationListEntry/End`; sibling indices count *logical*
children.

## Elements and attributes

Three element names. Attribute names are kebab-case CSS names. Blazor passes
element attribute values as strings, so every typed value (`Edges`, `Length`,
`Color`, the enums) has a `ToString` that `StyleParser` reads back. A `bool`
arrives as itself, and `false` omits the attribute.

**`box`** — a flex or grid container. Layout attributes: `display`
(flex|grid|none), `flex-direction` (row|column|row-reverse|column-reverse),
`flex-wrap` (nowrap|wrap|wrap-reverse), `justify-content`
(flex-start|center|flex-end|space-between|space-around|space-evenly),
`align-items` / `align-self` (stretch|flex-start|center|flex-end),
`align-content` (stretch|flex-start|center|flex-end|space-between|space-around),
`justify-items` (grid), `flex-grow`, `flex-shrink`, `flex-basis`, the `flex`
shorthand, `width`, `height`, `min-width`, `min-height`, `max-width`,
`max-height` (cells, `N%`, or `auto`), `padding`,
`padding-{top,right,bottom,left}`, `padding-x`, `padding-y`, `margin*`
likewise, `gap`, `row-gap`, `column-gap`, `overflow` (visible|hidden|scroll),
`scroll-x`, `scroll-y`, `position` (relative|absolute), `top`, `right`,
`bottom`, `left`, `grid-template-columns`, `grid-template-rows`,
`grid-column`, `grid-row`, `grid-{column,row}-{start,end,span}`, plus
`class` and `id` for stylesheets. Visual attributes: `background` (a colour), `border`
(none|single|double|round|bold|classic), `border-color`,
`border-{top,right,bottom,left}` (booleans, default all on when a border is
set). `style="…"` takes the same properties as inline CSS text.

**`text`** — a leaf for layout, wrapped to its width. Visual attributes:
`color`, `background`, `bold`, `dim`, `italic`, `underline`, `inverse`,
`strikethrough`, `wrap` (wrap|truncate|truncate-start|truncate-middle|clip).
It also takes the box layout attributes that make sense for a flex item
(`flex-*`, `width`, `height`, `min-*`, `max-*`, `margin*`, `padding*`,
`align-self`). A `text` nested in a `text` is a styled run inheriting the
outer style; it is not a layout node. `"\n"` inside text is a line break.
The HTML inline tags are text elements with a preset: `strong`/`b` bold,
`em`/`i` italic, `u` underline, `s`/`del`/`strike` strikethrough, `mark`
inverse, `span` nothing, and `br` a line break. Sheets and attributes apply
on top of the preset, and type selectors match the tag as written, so
`strong { color: red }` matches `<strong>` and no other bold run.

Bare text under a box is laid out too. Each run of text nodes and inline
tags directly under a box becomes an anonymous text leaf
(`AnonymousTextNode`), as CSS wraps inline content in an anonymous box. It
wraps as one text, takes the colour and flags a `text` child would inherit
and the box's `wrap` mode, and stays current when its text changes or an
ancestor restyles. Markup formatting makes no leaf: in a markup frame,
whitespace with a line break collapses to one space inside the text and to
nothing at its ends, and the leaf trims spaces at its own edges. A line
break is `<br>`, `<Newline />` or a `"\n"` in a value. The HTML block tags
(`div`, `p`, `section`, `article`, `main`, `header`, `footer`, `nav`,
`aside`, `ul`, `ol`, `li`, `pre`, `blockquote`, `h1`–`h6`) are boxes whose
children stack, and headings are bold.

**`img`** — a leaf showing a decoded picture (`ImageLayoutNode`). `src` is
a file path or a `data:` URI; `ImageDecoder` uses StbImageSharp, and a
failure shows the `alt` text instead. Sizing assumes an 8×16-pixel cell:
without a size the image is `ceil(px/8)` × `ceil(px/16)` cells, shrunk to
the available width with its shape kept; one given side sets the other from
the aspect ratio, and two given sides stretch it. `ImagePainter` paints two
pixel rows per cell with the upper-half block, in truecolour, and leaves a
mostly transparent sample's cell alone. The available height is ignored,
because it differs between the unbounded hypothetical measure and the final one.

**`canvas`** — a leaf painted by a delegate. The `Canvas` component
registers its `Paint` delegate in the `CanvasRegistry` service and puts the
registry key in the element's `paint` attribute, since an element attribute
cannot hold a delegate. It takes the flex-item layout attributes.

Colours: `default`, the sixteen ANSI names (`black … white`,
`bright-black … bright-white`), `#rrggbb`, `rgb(r,g,b)`, `ansi(n)` for the
256-colour index. See `Color.Parse`.

Lengths: an integer is cells; `50%` is a percentage of the parent's content
box on that axis; `auto` means "from content".

## Stylesheets

Styles can be set inline, as attributes or `style="…"`, or for the whole
app with `TuiApp.AddStylesheet(css)` and the `Stylesheets` list.
`SlopTui.Styling.Stylesheet.Parse` reads a CSS subset:

- Selectors: a type (`box`, `text`, `canvas`, `*`), `.class`, `#id`,
  `:focus` (the focused element), `:focus-within` (an ancestor of it), in
  compounds; the descendant (space) and child (`>`) combinators; lists with
  commas. Component boundaries are transparent to combinators.
- Declarations: the same property names the attributes take, kebab-case.
  An unknown property is kept and reported in `Warnings`; a bad value is a
  `FormatException` at parse time naming the line, selector and property.
- The cascade is CSS's: specificity (ids, then classes and pseudo-classes,
  then types), then sheet order, then rule order, last wins; a selector list
  contributes the specificity of the selector that matched. The element's
  own attributes beat every rule, and `style="…"` beats the attributes
  beside it.
- Inheritance runs with or without a sheet: a `text` whose colour resolved
  to `default` takes the nearest ancestor element's non-default colour, and
  the text-style flags of every ancestor OR in. `wrap` does not inherit;
  boxes inherit nothing.
- Elements carry `class` (whitespace-separated) and `id`. A change to
  either, or to an element's resolved colour or flags, re-resolves its
  descendants; siblings are left alone. Adding or removing a sheet
  re-resolves everything. A focus change re-resolves the old and new
  focus paths, and a focused element's subtree when a sheet has a focus
  pseudo-class left of a combinator.

## Layout

Measure/arrange, not a single Yoga pass, because a terminal is integers and
the subset is small:

- `Measure(node, availableWidth?, availableHeight?) → Size` computes the
  node's intrinsic size under constraints. A leaf answers from
  `MeasureContent`; a box lays its children out under the constraints and
  reports the extent. Results are cached on the node, keyed on the
  constraints, until `InvalidateLayout()` is called on the node or a
  descendant (it bubbles to the root).
- `Arrange(node, rect)` assigns final rects to the subtree. Main-axis sizes
  come from the flex algorithm: hypothetical sizes from basis, explicit size
  or content; free space distributed by grow, or by shrink weighted by basis;
  min/max clamping with the freeze loop; then justify-content and gaps.
  Cross-axis sizes come from explicit size, `stretch`, or content; align
  offsets after.
- `Layout(root, viewport)` = `Measure` then `Arrange` from `(0,0,viewport)`.
- `display: none` removes a node from flow and paint. `position: absolute`
  removes it from flow and places it by its offsets inside the parent's
  padding box.
- **Automatic minimum size.** As in CSS, a flex item with `min-*: auto`
  cannot shrink below its content, and an item with `overflow: hidden` or
  `scroll` can shrink to zero. Text's minimum is its longest word across and
  its height at its width down. A box's minimum is computed through the box:
  its children's minimums side by side on its main axis, plus gaps and
  inset, or the largest of them across. So a column holding a clipped
  transcript has the minimum of its other rows, not of the transcript, and
  needs no `min-height: 0`. Minimums that do not fit together all hold, and
  the row overflows.
- **`flex-wrap`.** `wrap` and `wrap-reverse` form lines from hypothetical
  sizes, margins and gaps. The flex algorithm runs per line, a line is as
  tall as its tallest item, `align-content` shares leftover cross space
  between lines, and `row-gap` separates them.
- **Grid.** Columns come from `grid-template-columns` (one auto column by
  default) and rows from `grid-template-rows` plus implicit auto rows.
  Tracks are cells, `N%`, `Nfr` or `auto`; spanning items only grow the
  auto and `fr` tracks they cover. Auto-placement is CSS's sparse row-major
  cursor. `grid-column` and `grid-row` accept `2`, `2 / 4`, `span 2` and
  `2 / span 2`. `justify-items`, `align-items` and `align-self` place an
  item in its area, and `align-content` spends leftover height.
- **Scrolling.** A box with `overflow: scroll` or `hidden` shifts its
  children by `scroll-x` and `scroll-y` and records its `ContentSize`. The
  engine does not clamp the offsets; the `ScrollBox` component does, and
  handles the arrow and page keys, the wheel, `ScrollTop` and
  `StickToBottom`.
- A container's cross size is measured with its items at their final main
  sizes, so a row is as tall as its rewrapped text.
- When content overflows, `justify-content: flex-end` keeps the end in
  view, `center` overflows both ways, and the `space-*` values fall back to
  flex-start.
- A clean subtree on an unchanged rect is not re-arranged, and a child
  entirely outside the visible region is placed but not arranged until it
  comes into view.
- Rects are absolute terminal cells, so the painter needs no coordinate walk.

After one text changes, a frame re-measures that text node and re-arranges
its ancestors; siblings answer from their cache.

## Painting and flushing

`Painter.Paint(root, buffer)` walks the arranged tree depth-first: fill the
box background if set, draw the border, then paint children clipped to the
box's padding box unless `overflow: visible`. Text paints its wrapped lines
(`TextLayout`), a canvas calls its delegate with a buffer clipped to its rect.

`Screen` holds two `CellBuffer`s, shown and back. `Flush()` compares rows by
hash, repaints only the span between the first and last differing cells of a
changed row, emits one pen change per run of equal attributes, clears an
emptied tail with `ESC[K`, wraps the frame in DEC 2026 when the terminal
supports it, and returns the whole frame as one string. The app writes it
with one call, because many small writes make a terminal lag.

## Input

The input thread reads chunks and stamps each with the clock. `AnsiKeyParser`
is pure: chunks and stamps in, `InputEvent`s out; it resolves the lone-ESC
ambiguity with an 8 ms gap measured on those stamps, holds split sequences
and paste bodies, decodes SGR mouse, CSI/SS3 keys, kitty keyboard sequences,
and swallows terminal replies. `InputPump` feeds it from the thread's queue
on the app loop and ticks it so a pending ESC expires.

Routing on the app loop: a key goes to the focused element's `@onkeypress`,
then bubbles to each ancestor's, then to the root's; the first handler that
sets `Handled` stops it. Focus lives in `FocusManager`: elements with
`focusable="true"` are registered in tree order; `Tab` and `Shift+Tab` move
it unless a handler took the key. Mouse events hit-test the arranged tree and
dispatch `@onclick` (and `@onmouse` for everything else) from the deepest
element outward. `@onfocus` / `@onblur` fire on change.

Event names and argument types are declared in `EventHandlers` with
`[EventHandler]`, exactly as `Microsoft.AspNetCore.Components.Web` declares the
DOM's, so Razor type-checks handlers.

## The app loop

One thread, the one that called `TuiApp.Run`. It is the Blazor dispatcher:
`TerminalDispatcher.InvokeAsync` queues work for it and completes when it
ran; from the loop thread it runs inline. Per iteration: drain the dispatcher
queue, pump input, run due timers, and if a render batch or a resize landed
since the last frame and at least 16 ms passed, lay out and paint and flush.
Idle, it waits on the queue with a timeout only as short as the next thing it
is waiting for (a pending ESC, a timer, resize polling on Windows).

`TuiApp.Exit()` ends the loop; `Run` restores the terminal on every path
including an unhandled exception, which is rethrown after the restore so the
message lands on a readable screen.

## Conventions

- Public API is `PascalCase`; element attribute names are kebab-case.
- Public types carry a short summary. Comments explain why, not what.
- Tests are xunit, one file per type under test, named for the behaviour
  (`A_flush_with_nothing_changed_writes_nothing`).
- No `.Result`, no `.Wait()`, no `Thread.Sleep` outside `ConsoleTerminal`.
- `TreatWarningsAsErrors` is on. BL0006 is the one suppressed warning.
