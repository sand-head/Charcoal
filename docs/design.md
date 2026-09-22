# sloptui design

sloptui renders Blazor components to a terminal. Components are `.razor`
files reconciled by Blazor's own `Renderer`; the library styles them with
CSS, lays them out on a grid of terminal cells, paints them into a cell
buffer, and writes the difference from the previous frame to the terminal.

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
| `SlopTui.Styling` | `Stylesheet`, `Selector`, `StyleResolver`, `StyleContext`, `UserAgentStylesheet`: the cascade | `SlopTui.Layout`, `SlopTui.Components` (the host tree it matches) |
| `SlopTui.Components` | `TerminalRenderer`, `TerminalDispatcher`, host tree, `MarkupParser`, `TuiApp`, `ScrollBox`, `Canvas`, focus, event args, `EventHandlers` | everything above |

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

- `HostNode`: the base, with `Parent` and logical `Children`.
- `HostElement : HostNode`: an element named as written (`div`, `span`,
  `img` …) with its attributes and resolved style. Its `Node` is the
  `LayoutNode` the engine sees, whose children are the element's logical
  descendants with containers flattened out and inline content grouped
  into anonymous text leaves.
- `HostTextNode : HostNode`: a text frame, collected into a text leaf by
  the block it stands in.
- `HostContainer : HostNode`: a component or region, transparent to layout.

Edits are applied as `BrowserRenderer.ts` applies them: `PrependFrame`,
`RemoveFrame`, `SetAttribute`, `RemoveAttribute`, `UpdateText`, `StepIn`,
`StepOut`, `PermutationListEntry/End`; sibling indices count *logical*
children.

## Elements

The element names are HTML's. Any name renders, and what it is comes from
the cascade, as on a page. The user-agent stylesheet (`UserAgentStylesheet`)
makes `div`, `p`, `section`, `h1`–`h6`, `ul`, `li`, `pre`, `blockquote`,
`hr`, `img`, `canvas` and the other block elements `display: block`; makes
headings, `strong`, `b` and `th` bold and `em`, `i`, `cite` and `var`
italic; underlines `u`, strikes through `s` and `del`, and shows `mark` black
on yellow; keeps whitespace in `pre`; gives paragraphs, headings, lists and
quotes a `margin-block` of one row; indents `blockquote`, `dd` and lists;
draws a top border on `hr`; and hides `[hidden]`. Every other element is
`display: inline`, the CSS initial value, so an unknown tag is a styled run
of the text around it. Unlike a browser's sheet, `body` has no margin and
`img` and `canvas` are blocks. A stylesheet can change any of it.

What an element does with its children follows its `display`:

- **Block** (and inline, when the engine is handed an inline element):
  children stack top to bottom, each as wide as the content box unless it
  has a `width` (`fit-content` hugs the content). Vertical margins between
  neighbours collapse to the larger one, and a first or last child's margin
  collapses through a container that has no border or padding on that side.
- **Flex** and **grid**: the algorithms below, with the children as items.
- **Inline**: not a box. Inline elements and the text around them, directly
  under a block, are grouped into an anonymous text leaf
  (`AnonymousTextNode`), as CSS wraps a block's inline content in anonymous
  boxes. The leaf wraps as one text in the block's inherited text
  properties, with each run in its own element's look, and stays current
  when its text changes or an ancestor restyles. A `br` is a forced line
  break. Whitespace follows the block's `white-space`: under `normal` runs
  of spaces, tabs and newlines, including the indentation between elements
  written on separate lines, collapse to one space and disappear at line
  ends, so an input that must keep typed spaces needs `white-space: pre`.
  Whitespace alone between two blocks makes no leaf.
- **None**: out of layout and paint.

Razor compiles static HTML into a single *markup* frame. The browser
renderer hands such a frame to the DOM parser; `MarkupParser` does the same
here, reading elements with quoted, unquoted or bare attributes, void and
self-closing tags, comments, entities, and text as written. Its nodes go in
a container and are restyled when the container is inserted, so they
resolve against their real ancestors.

Attributes are HTML's: `class`, `id`, `style`, `tabindex`, `src`, `alt`,
`hidden`, `width` and `height` on `img` (applied where the cascade sets no
size), and the event attributes. `@ref` works: the renderer records each
captured reference, and `TerminalRenderer.Element(ref)` returns the
`HostElement`. Components use it to reach an element's state, such as a
scroll position (`node.ScrollTop`) or a canvas painter (`element.Painter`),
where a page would use JavaScript interop. The one attribute HTML does not
have is `caret="col,row"`, which places the terminal's cursor in the
focused element's content box.

**`img`** is a leaf showing a decoded picture (`ImageLayoutNode`). `src` is
a file path or a `data:` URI; `ImageDecoder` uses StbImageSharp, and a
failure shows the `alt` text instead. Sizing assumes an 8×16-pixel cell
until the terminal reports its own: without a size the image is
`ceil(px/8)` × `ceil(px/16)` cells, shrunk to the available width with its
shape kept; one given side sets the other from the aspect ratio, and two
given sides stretch it. `ImagePainter` paints two pixel rows per cell with
the upper-half block, in truecolour, and leaves a mostly transparent
sample's cell alone. The available height is ignored, because it differs
between the unbounded hypothetical measure and the final one.

**Full-resolution pictures.** Before its first frame the app sends the kitty
graphics query (a one-pixel image with `a=q`), XTWINOPS 16 for the cell size
in pixels, and DA1. The parser turns the answers into `ReplyEvent`s, and
`TuiApp` records them in `Graphics`, the per-app object every element
receives: `Kitty` when the query said OK, `CellPixels` from the size report,
and `Detected` once DA1 answers, since a terminal without the protocol only
answers DA1. Pictures painted before the answers are repainted.

With `Kitty`, pictures use the protocol's Unicode placeholder mode.
`Graphics.Place` transmits each (image, columns, rows) once, as compressed
RGBA in quiet mode with a virtual placement of that many cells, and the
cells become `U+10EEEE` with row and column diacritics and the image id as
their foreground colour. The terminal draws the picture over exactly those
cells, so clipping, scrolling and overlap need nothing beyond the cell diff.
A transmission goes out in the same write as the frame that first uses it,
and every image is deleted on exit. Ids carry a per-process high byte so two
apps in one terminal do not replace each other's pictures.

**Sixel.** A terminal that lists attribute 4 in its DA1 reply and lacks the
kitty protocol gets Sixel pictures. `Sixel.Encode` scales the image to the
placement's pixels, quantises it to a 252-colour palette, and leaves
transparent pixels undrawn. The terminal paints a Sixel picture at the
cursor and forgets it, so pictures are tracked as a placement list rather
than as cells. The painter blanks the cells a picture covers and records the
placement with its visible part. After the diff, `Graphics.SixelOutput`
sends every placement that is new, has moved, or stands on a repainted row,
cropped to its visible cells because the terminal cannot clip it. Mode 8452
keeps a picture on the last row from scrolling the screen, and mode 1070
gives each picture its own colour registers. Encodings are cached per
placement. iTerm2 inline images are not supported.

**`canvas`** is a leaf painted by a delegate on the element (`Painter`). The
`Canvas` component captures its element with `@ref` and sets the delegate
after each render, so a component can draw a whole region, such as a chart,
without a node per cell. It is sized and placed by style like any element.

## Styles

Every property is a CSS property under its CSS name, measured in cells where
a page uses pixels: `padding: 1 2` is one row and two columns. `ch`, `em`,
`rem` and `lh` are accepted and also mean cells; `px` is refused.
`StyleParser` turns declarations into `Style`, the CSS subset the engine and
the painter read:

- Layout: `display` (`block`, `inline`, `flex`, `grid`, `none`; the
  `inline-*` forms map to their block-level versions), `position`,
  `top`/`right`/`bottom`/`left`, `width`/`height`/`min-*`/`max-*` (cells,
  `N%`, `auto`, `fit-content`), `padding` and `margin` with the physical and
  logical longhands, `gap`/`row-gap`/`column-gap`, `overflow` (`visible`,
  `hidden`, `scroll`/`auto`; the `-x` and `-y` forms set the same value),
  `visibility`, the flex properties with the `flex` and `flex-flow`
  shorthands, and the grid template and placement properties.
- Box: `background`/`background-color`; the `border` and `border-{side}`
  shorthands (a width, a style and a colour in any order);
  `border-style` and `border-{side}-style` (`none`, `solid`, `double`,
  `dashed`, `dotted`); `border-width` (`thin` and `medium` draw a single
  line, `thick` a heavy one); `border-color` (the text colour when unset);
  and `border-radius`, where any radius rounds a solid border's corners.
- Text, inherited: `color`, `font-weight` (`bold` and 600 or more are bold,
  `lighter` and below 400 dim), `font-style`, `text-decoration`
  (`underline`, `line-through`, `none`), `opacity` (below 1 is dim),
  `filter` (`invert()` of half or more swaps the colours), `white-space`
  and `text-align`. `text-overflow` is not inherited: `ellipsis` cuts the
  end, `ellipsis clip` the start and `ellipsis ellipsis` the middle.
- Custom properties (`--name`) and `var(--name, fallback)`. An undefined
  variable without a fallback drops the declaration, as in CSS.
- Properties a terminal cannot draw, such as `font-family`, `line-height`,
  `box-shadow`, `transition` and `z-index`, are accepted and ignored. Any
  other unknown name is an error inline and a warning in a sheet. A bad
  value for a known property is always an error.

Colours (`Color.Parse`): the sixteen palette names (`red`, `bright-blue`,
`gray`) follow the terminal's theme; the other CSS names (`gold`,
`rebeccapurple` …) are exact sRGB; `#rgb`, `#rrggbb`, `rgb(r, g, b)`,
`ansi(n)`; and `currentcolor`, `transparent` and `default` mean the
terminal's own colour.

The text properties inherit into every element, as in CSS, so a box carries
the look its text will have and an anonymous leaf needs only its block's
style. Flags that an element turns off stay off, so `font-weight: normal`
under a bold parent is normal (`Style.TextStyleReset`). Backgrounds,
borders, sizes and layout properties do not inherit.

## Stylesheets

Styles come from `style="…"` on an element, from global sheets
(`TuiApp.AddStylesheet(css)`), and from component-scoped sheets. The cascade
(`StyleResolver`) is CSS's without `!important`: the user-agent sheet, then
the app's sheets ordered by specificity (ids; then classes, attributes and
pseudo-classes; then types), sheet order and rule order, with the last
winning; then the inline style. A selector list counts the specificity of
the selector that matched.

`Stylesheet.Parse` reads selector lists; compounds of a type (`div`, `*`),
`.class`, `#id`, `[name]`, `[name=value]`, `:focus`, `:focus-within`,
`:root`, `:first-child` and `:last-child`; the descendant and child
combinators, which see through components; comments; declarations; and
`@media` and `@supports` blocks. Other at-rules such as `@import` and
`@layer` are skipped with a warning in `Warnings`, as are unknown
properties. A bad value throws a `FormatException` naming the line,
selector and property.

**Media queries.** Rules inside an `@media` block carry its
`MediaQueryList`, combined with `and` when blocks nest, and the cascade
skips rules whose list does not match the `StyleContext`'s
`MediaEnvironment`. Each list remembers its answer for the last
environment. The environment describes the terminal as a device: its width
and height in cells, its colour scheme, its bits per colour component,
whether the mouse is on, and a reduced-motion preference. `TuiApp` sets the
size at start and on every resize, the colour bits from `COLORTERM` and
`TERM` on a real console, and the colour scheme from the relative luminance
of the terminal's background, asked for with OSC 11 alongside the graphics
queries. A change to the environment re-resolves every element, but only
when a sheet uses `@media`. The grammar is Media Queries Level 4 (types,
`not`/`only`/`and`/`or`, ranges) with three-valued logic: an unknown
feature, or a length in pixels, is unknown, and a query that ends unknown
does not match, so a page's `768px` breakpoints load but never fire.
`@supports` is answered at parse time from the property parser, so
`(display: grid)` holds and `(gap: 1px)` does not, and from
`Selector.Parse` for `selector(…)`. A block that does not hold is dropped
with a warning.

**Container queries.** `container-type`, with `container-name` and the
`container` shorthand, makes an element a query container with size
containment on the contained axis: its size there does not depend on its
content, so the queries cannot change the size they read. An `@container`
block attaches a `ContainerQuery` to its rules, evaluated against the nearest
ancestor container with a matching name. The container's
`ContainerEnvironment` is the app's media environment with the content box
as width and height. Before the first layout a container has no box and its
rules do not apply. After each layout the app restyles the descendants of
every container whose box changed and lays out again, at most three times.
Nested `@container` blocks are rejected; inside `@media` the conditions
combine.

**Scoped stylesheets** are Blazor's own. For a `Component.razor.css` beside a
component, the Razor SDK stamps a `b-…` attribute on that component's
elements, rewrites the sheet's selectors to match it (`::deep` included),
and bundles the project's sheets into
`obj/…/projectbundle/<Project>.bundle.scp.css`. A web page links the bundle;
here `build/SlopTui.targets` embeds it in the assembly, and `TuiApp.Run`
loads the bundle of every non-framework assembly the app uses, dependencies
first, with `AddScopedStylesheets`. To the host tree the scope is an
ordinary attribute, matched by an ordinary attribute selector.

Styles are re-resolved as narrowly as possible. An attribute change
re-resolves the element and its descendants, whose selectors may test it;
an inherited property change reaches the descendants; siblings are never
touched. A focus change re-resolves the old and new focus paths, and their
subtrees only when a sheet has a focus pseudo-class left of a combinator.
Adding or removing a sheet re-resolves everything. The renderer resolves
each new element once, when it is inserted.

## Layout

Layout is a measure pass and an arrange pass, rather than Yoga's single
pass, because a terminal works in integers and the property set is small:

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
- `Layout(root, viewport)` is `Measure` and then `Arrange` from
  `(0,0,viewport)`.
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
- **Block layout** (`display: block`): children stack, stretched to the
  content width unless sized, with vertical margins collapsed between
  neighbours and through a container's unpadded edges.
  `MarginTopThrough` and `MarginBottomThrough` carry a collapsed margin up
  to the parent; nothing collapses out of a flex item, a grid item or the
  root. A block's own content width is its widest child's, so a block that
  is a flex item takes its max-content size. A percentage height resolves
  only against a definite container height.
- **Scrolling.** A box with `overflow: scroll` or `hidden` shifts its
  children by the node's `ScrollTop` and `ScrollLeft`, which are set through
  `@ref` as in the DOM, and records its `ContentSize`. The engine does not
  clamp the offsets, and changing one re-arranges without re-measuring. The
  `ScrollBox` component clamps them and handles the arrow and page keys,
  the wheel, `ScrollTop` and `StickToBottom`.
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

`Painter.Paint(root, buffer)` walks the arranged tree depth-first. For each
box it fills the background if one is set, draws the border in its
`border-color` or the text colour, and paints the children, clipped to the
padding box unless `overflow` is visible; `visibility: hidden` skips a
subtree. A text leaf paints its wrapped lines (`TextLayout`) aligned by
`text-align`, each run in its element's look, and a cluster without a
background keeps the fill beneath it. A canvas calls its delegate with a
buffer clipped to its rect.

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
sets `Handled` stops it. Focus lives in `FocusManager`: an element with a
`tabindex` can take focus, and one with a non-negative `tabindex` is in the
Tab cycle, in tree order. `Tab` and `Shift+Tab` move focus unless a handler
took the key. Mouse events hit-test the arranged tree and dispatch `@onclick`
(and `@onmouse` for everything else) from the deepest box outward; a left
click focuses the nearest focusable element. `@onfocus` / `@onblur` fire on
change.

Event names and argument types are declared in `EventHandlers` with
`[EventHandler]`, as `Microsoft.AspNetCore.Components.Web` declares the
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

- Public API is `PascalCase`. Elements, attributes and properties use the
  HTML and CSS names; terminal-only features (dim, inverse, the caret) use
  the closest standard construct.
- Public types carry a short summary. Comments explain why, not what.
- Tests are xunit, one file per type under test, named for the behaviour
  (`A_flush_with_nothing_changed_writes_nothing`).
- No `.Result`, no `.Wait()`, no `Thread.Sleep` outside `ConsoleTerminal`.
- `TreatWarningsAsErrors` is on. BL0006 is the one suppressed warning.
