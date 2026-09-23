using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Charcoal.Components;
using Charcoal.Layout;
using Charcoal.Rendering;
using Charcoal.Terminal;

namespace Charcoal.Tests.Components;

public class MouseSelectionTests
{
    private static CellBuffer Frame(params string[] rows)
    {
        var buffer = new CellBuffer(rows.Max(r => r.Length) + 2, rows.Length);
        for (var y = 0; y < rows.Length; y++) buffer.PutText(0, y, rows[y], Color.Default, Color.Default, TextStyle.None);
        return buffer;
    }

    [Fact]
    public void A_range_is_half_open_and_normalised_whichever_way_it_was_dragged()
    {
        var selection = new MouseSelection();
        selection.Press(new CellPosition(1, 5), new Rect(0, 0, 10, 3));
        selection.Drag(new CellPosition(0, 2));
        selection.Release(new CellPosition(0, 2));
        var (start, end) = selection.Normalized()!.Value;
        Assert.Equal((new CellPosition(0, 2), new CellPosition(1, 5)), (start, end));
        Assert.True(selection.Contains(0, 2));
        Assert.True(selection.Contains(0, 9));
        Assert.True(selection.Contains(1, 4));
        Assert.False(selection.Contains(1, 5));
        Assert.False(selection.Contains(0, 1));

        var click = new MouseSelection();
        click.Press(new CellPosition(0, 0), new Rect(0, 0, 10, 3));
        click.Release(new CellPosition(0, 0));
        Assert.False(click.Active);
    }

    [Fact]
    public void A_drag_is_clamped_to_its_region()
    {
        var selection = new MouseSelection();
        selection.Press(new CellPosition(2, 3), new Rect(2, 1, 5, 3));   // columns 2–6, rows 1–3
        selection.Drag(new CellPosition(9, 40));
        Assert.Equal(new CellPosition(3, 7), selection.Focus);
        selection.Drag(new CellPosition(-1, -1));
        Assert.Equal(new CellPosition(1, 2), selection.Focus);
    }

    [Fact]
    public void The_text_is_the_frames_rows_with_wide_glyphs_whole_and_trailing_blanks_dropped()
    {
        var frame = Frame("ab字cd  ", "second line", "third");
        var selection = new MouseSelection();
        selection.Press(new CellPosition(0, 3), new Rect(0, 0, frame.Width, 3));   // the right half of 字
        selection.Release(new CellPosition(2, 3));
        Assert.Equal("字cd\nsecond line\nthi", selection.Text(frame));

        frame.PushClip(new Rect(0, 0, frame.Width, 3));
        selection.Highlight(frame);
        Assert.Equal(TextStyle.Inverse, frame[3, 0].Style);
        Assert.Equal(TextStyle.None, frame[1, 0].Style);
        Assert.Equal(TextStyle.Inverse, frame[0, 1].Style);
        Assert.Equal(TextStyle.None, frame[3, 2].Style);

        var blank = new MouseSelection();
        blank.Press(new CellPosition(0, 6), new Rect(0, 0, frame.Width, 3));
        blank.Release(new CellPosition(0, 8));
        Assert.Null(blank.Text(frame));
    }

    private sealed class Host : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div"); b.AddAttribute(1, "style", "user-select: none"); b.AddContent(2, "chrome line"); b.CloseElement();
            b.OpenElement(3, "div"); b.AddAttribute(4, "id", "body"); b.AddAttribute(5, "style", "user-select: contain; padding: 0 1");
            b.OpenElement(6, "div"); b.AddContent(7, "hello world"); b.CloseElement();
            b.OpenElement(8, "div"); b.AddContent(9, "second line"); b.CloseElement();
            b.CloseElement();
            b.OpenElement(10, "div"); b.AddContent(11, "footer"); b.CloseElement();
        }
    }

    private static void WaitUntil(Func<bool> condition, string what, Task run)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (run.IsFaulted) throw new InvalidOperationException($"The app ended while waiting for {what}.", run.Exception!.GetBaseException());
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Timed out waiting for {what}.");
            Thread.Sleep(5);
        }
    }

    [Fact]
    public async Task A_drag_highlights_a_release_copies_and_user_select_decides_where()
    {
        var terminal = new HeadlessTerminal(20, 5);
        var app = new TuiApp(terminal, new TuiAppOptions { FrameInterval = TimeSpan.Zero });
        string? copied = null;
        app.Selection.Copied += text => copied = text;
        var run = Task.Run(() => app.Run<Host>());
        WaitUntil(() => terminal.Writes.Count > 0, "the first frame", run);

        // Press on "hello" (row 2, column 2 → SGR 1-based 2;2), drag to after "world" (column 13).
        terminal.Inject("\e[<0;2;2M");
        terminal.Inject("\e[<32;13;2M");
        WaitUntil(() => app.Selection.Active && app.Selection.Dragging, "the drag", run);
        Thread.Sleep(50);
        Assert.Contains(";7mhello wor", terminal.Writes[^1]);   // the highlight reached the terminal: inverse video over the cells

        // Drag past the body into the footer: confined to the body's box, so the footer row is never selected.
        terminal.Inject("\e[<32;5;4M");
        Thread.Sleep(50);
        Assert.Equal(2, app.Selection.Focus!.Value.Row);   // row index 2 is the body's last row

        var before = terminal.Writes.Count;
        terminal.Inject("\e[<0;13;2m");   // release back on the first row
        WaitUntil(() => copied is not null && terminal.Writes.Count > before, "the copy", run);
        Assert.Equal("hello world", copied);
        Assert.Contains(Ansi.CopyToClipboard("hello world"), terminal.Output);
        Assert.False(app.Selection.Active);

        // A press on the chrome selects nothing, and dragging from there selects nothing either.
        copied = null;
        terminal.Inject("\e[<0;1;1M\e[<32;8;1M\e[<0;8;1m");
        Thread.Sleep(80);
        Assert.Null(copied);
        Assert.False(app.Selection.Active);

        // Ctrl+C during a drag copies instead of exiting.
        terminal.Inject("\e[<0;2;3M\e[<32;8;3M");
        WaitUntil(() => app.Selection.Active, "the second drag", run);
        terminal.Inject("\x03");
        WaitUntil(() => copied is not null, "the copy from Ctrl+C", run);
        Assert.Equal("second", copied);
        Assert.False(run.IsCompleted);

        app.Exit();
        await run;
    }

    [Fact]
    public void Osc_52_carries_the_clipboard_selection_and_utf8_base64()
    {
        var escape = Ansi.CopyToClipboard("héllo→");
        Assert.StartsWith("\e]52;c;", escape);
        Assert.EndsWith("\e\\", escape);
        var payload = escape["\e]52;c;".Length..^2];
        Assert.Equal("héllo→", System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
        Assert.Equal("", Ansi.CopyToClipboard(""));
        Assert.Equal("", Ansi.CopyToClipboard(new string('x', Ansi.ClipboardMaxChars + 1)));
        Assert.Equal(UserSelect.Contain, StyleParser.Apply(Style.Default, "user-select", "contain").UserSelect);
        Assert.Equal(UserSelect.None, StyleParser.Apply(Style.Default, "user-select", "none").UserSelect);
    }
}
