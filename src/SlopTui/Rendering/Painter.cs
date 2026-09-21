using SlopTui.Layout;

namespace SlopTui.Rendering;

/// <summary>
/// Paints an arranged layout tree depth-first, so later siblings paint over
/// earlier ones. A box without a background lets its parent's show through.
/// </summary>
public static class Painter
{
    public static void Paint(LayoutNode root, CellBuffer buffer) => PaintNode(root, buffer);

    private static void PaintNode(LayoutNode node, CellBuffer buffer)
    {
        var style = node.Style;
        if (style.Display == Display.None) return;
        var rect = node.Layout;
        if (rect.IsEmpty) return;

        if (style.Background != Color.Default) buffer.Fill(rect, Cell.Space(style.Color, style.Background));
        if (style.Border != BorderStyle.None) Borders.Draw(buffer, rect, style);

        var content = rect.Deflate(style.Inset);

        if (node is ITextContent text)
        {
            buffer.PushClip(content);
            PaintText(text, style, content, buffer);
            buffer.PopClip();
            return;
        }

        if (node is ICustomPaint custom)
        {
            buffer.PushClip(content);
            custom.Paint(buffer, content);
            buffer.PopClip();
            return;
        }

        var clip = style.Overflow != Overflow.Visible;
        if (clip) buffer.PushClip(rect.Deflate(style.BorderEdges));
        foreach (var child in node.Children) PaintNode(child, buffer);
        if (clip) buffer.PopClip();
    }

    private static void PaintText(ITextContent text, Style style, Rect content, CellBuffer buffer)
    {
        if (content.IsEmpty) return;
        var lines = TextLayout.Wrap(text.Runs, content.Width, style.Wrap);
        for (var i = 0; i < lines.Count; i++)
        {
            var y = content.Y + i;
            if (y >= content.Bottom) break;
            var x = content.X;
            foreach (var run in lines[i])
            {
                var fg = run.Foreground == Color.Default ? style.Color : run.Foreground;
                var bg = run.Background == Color.Default ? style.Background : run.Background;
                var flags = run.Style | style.TextStyle;
                x += buffer.PutText(x, y, run.Text, fg, bg, flags);
                if (x >= content.Right) break;
            }
        }
    }
}
