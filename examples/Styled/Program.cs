using Charcoal.Components;
using Styled;

// The components carry class names; this global stylesheet decides how they
// look. The other examples use component-scoped .razor.css files instead.
var app = new TuiApp();
app.AddStylesheet("""
    :root { --accent: bright-green; --muted: bright-black }

    /* the page */
    .page { display: flex; flex-direction: column; gap: 1; padding: 1 2; height: 100% }
    .title { color: var(--accent); font-weight: bold }
    .muted { color: var(--muted) }

    /* cards: a row of them, each a bordered column */
    .cards { display: flex; gap: 2 }
    .card { border: solid var(--muted); border-radius: 1; padding: 0 1; width: 26; color: white }
    .card p { margin: 0 }
    .card.warn { border-color: yellow }
    .card .heading { font-weight: bold }
    /* later in the sheet and equally specific, so focus beats .warn */
    .card:focus { border-color: var(--accent) }
    .card:focus > .heading { color: var(--accent) }
    #footer { color: var(--muted); font-style: italic }
    """);
return app.Run<App>();
