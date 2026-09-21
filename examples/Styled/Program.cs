using SlopTui.Components;
using Styled;

// The components carry class names; the stylesheet decides how they look.
var app = new TuiApp();
app.AddStylesheet("""
    /* the page */
    box.page { padding: 1 2; gap: 1; flex-direction: column; }
    .title { color: bright-green; bold: true; }
    .muted { color: bright-black; }

    /* cards: a row of them, each a bordered column */
    .cards { gap: 2; }
    .card { flex-direction: column; border: round; border-color: bright-black; padding: 0 1; width: 26; color: white; }
    .card.warn { border-color: yellow; }
    .card .heading { bold: true; }
    .card text { wrap: wrap; }
    /* later in the sheet and equally specific, so focus beats .warn */
    .card:focus { border-color: bright-green; }
    .card:focus > .heading { color: bright-green; }
    #footer { color: bright-black; italic: true; }
    """);
return app.Run<App>();
