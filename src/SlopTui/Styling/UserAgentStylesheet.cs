namespace SlopTui.Styling;

/// <summary>
/// The default look of the HTML elements, like a browser's own stylesheet,
/// with margins in cells.
/// </summary>
/// <remarks>
/// Unlike a browser's, <c>body</c> has no margin, and <c>img</c>, <c>canvas</c>
/// and the form controls are blocks that fit their content, since a picture
/// or a field cannot sit inside a line of text.
/// </remarks>
public static class UserAgentStylesheet
{
    public const string Css = """
        html, body, div, p, section, article, main, header, footer, nav, aside,
        ul, ol, li, pre, blockquote, h1, h2, h3, h4, h5, h6, hr, form, fieldset,
        figure, figcaption, address, dl, dt, dd, details, summary, menu, dialog,
        table, thead, tbody, tfoot, tr, td, th, canvas, img, input, textarea { display: block }

        input, textarea { width: fit-content }
        input[type="hidden"] { display: none }
        input:disabled, textarea:disabled { opacity: 0.5 }

        h1, h2, h3, h4, h5, h6, strong, b, th { font-weight: bold }
        em, i, cite, dfn, var, address { font-style: italic }
        u, ins { text-decoration: underline }
        s, del, strike { text-decoration: line-through }
        mark { background-color: yellow; color: black }

        pre { white-space: pre }
        textarea { white-space: pre-wrap }
        th, caption { text-align: center }

        p, h1, h2, h3, h4, h5, h6, blockquote, ul, ol, pre, figure, dl, fieldset, hr { margin-block: 1 }
        blockquote { margin-inline: 4 }
        ul, ol { padding-inline-start: 2 }
        dd { margin-inline-start: 4 }
        hr { border-top: solid }

        [hidden] { display: none }
        """;

    /// <summary>The parsed sheet, shared by every app.</summary>
    public static Stylesheet Sheet { get; } = Stylesheet.Parse(Css);
}
