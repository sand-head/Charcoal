# sloptui

A component-style terminal UI library for .NET. Write `.razor` components,
lay them out with flexbox, and get a cell-diffed terminal frame out. Blazor's
own renderer reconciles the components; everything after the render tree is
this library.

```razor
@inject TuiApp App

<Box FlexDirection="FlexDirection.Column" Padding="new Edges(1, 2)" focusable="true" @onkeypress="OnKey">
    <Box Border="BorderStyle.Round" BorderColor="Color.Cyan" Padding="new Edges(0, 1)">
        <Text Bold="true" Color="Color.Green">Hello from sloptui</Text>
    </Box>
    <Text Dim="true">Press q to quit.</Text>
</Box>

@code {
    private void OnKey(KeyPressEventArgs e)
    {
        if (e.Key.Text != "q") return;
        App.Exit();
        e.Handled = true;
    }
}
```

```csharp
return new TuiApp().Run<App>();
```

> **Status: early.** Linux works end to end; Windows is untested. The design
> is described in [docs/design.md](docs/design.md).

## What you get

- **Razor components.** `Box`, `Text`, `Canvas`, `Spacer`, `Newline`, or the
  bare `<box>` and `<canvas>` elements with kebab-case attributes. Razor
  reserves `<text>` inside code blocks such as `@if` and `@foreach`, so use
  `<Run>`, the same component, there.
  Parameters, `@key`, `EventCallback`, cascading values, `@inject`,
  `StateHasChanged` and `InvokeAsync` work as they do on the web.
- **Flexbox and grid on cells.** Direction, wrap, justify, align (items,
  self, content), grow/shrink/basis, percent and auto sizes, min/max with
  CSS's content-based automatic minimum, padding, margin, gap, borders,
  absolute positioning and `display: none`. `display: grid` supports track
  templates such as `12 1fr auto 25%` and `repeat(3, 1fr)`, explicit
  placement and spans, auto-placement, gaps and per-cell alignment. Layout
  is cached per node, so a frame only re-measures what changed.
- **Scrolling.** `overflow: scroll` with `scroll-x` and `scroll-y`, and a
  `ScrollBox` component that handles the keyboard and the wheel, binds
  `ScrollTop`, and can stick to the bottom as content grows.
- **Stylesheets.** `app.AddStylesheet(css)` supports type, class, id,
  `:focus` and `:focus-within` selectors, descendant and child combinators,
  specificity and source order. Inline attributes win, and colour and text
  flags inherit into nested text.
- **Text that measures right.** Grapheme clusters and wcwidth, so CJK and
  emoji take two cells and combining marks take none. Wrap, truncate at the
  end, start or middle, or clip.
- **Events.** `@onkeypress`, `@onclick`, `@onmouse`, `@onfocus`, `@onblur`,
  `@onpaste`. Keys go to the focused element and bubble; `focusable="true"`
  joins the Tab order; a left click focuses. `cursor="col,row"` on an
  element puts the terminal's cursor there.
- **A frame is one write.** The screen is double-buffered and only changed
  spans are repainted, inside synchronized output. A streaming transcript of
  ten thousand lines paints in a few milliseconds.
- **Testable.** `HeadlessTerminal` captures every frame and injects input;
  the layout engine, the painter and the key parser have no I/O.

## Examples

Each one under `examples/` is a runnable project:

| Example | Shows |
|---|---|
| `Hello` | The smallest app. |
| `Layouts` | A gallery of the layout subset, page by page: flex, wrap, grid, automatic minimums, scrolling. |
| `Styled` | Cards styled by a stylesheet: classes, ids, `:focus`, inheritance, inline overrides. |
| `Counter` | State, focus, a timer, key and mouse handlers. |
| `Picker` | A list with keyboard and mouse selection, rows that skip unchanged renders. |
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
dotnet add package SlopTui
```

An app project uses the Razor SDK so its `.razor` files compile:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="SlopTui" Version="0.1.*" />
  </ItemGroup>
</Project>
```

with an `_Imports.razor` of `@using SlopTui.Components`, `@using SlopTui.Layout`,
`@using SlopTui.Rendering`, `@using SlopTui.Input`.

## How it is put together

```
.razor components → Blazor Renderer → host tree → FlexLayout → Painter → Screen.Flush() → one write
terminal input thread → AnsiKeyParser → InputPump → focus + bubbling → @onkeypress …
```

`TuiApp.Run` owns one thread: it is the Blazor dispatcher, the input router
and the painter. Code on other threads reaches it with `InvokeAsync`, as on
the web. See [docs/design.md](docs/design.md) for details.

## Building

```sh
dotnet build
dotnet test
```

.NET 10 SDK. No native dependencies.

## License

MIT.
