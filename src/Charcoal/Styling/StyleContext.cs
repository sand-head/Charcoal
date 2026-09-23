using System.Collections.ObjectModel;
using Charcoal.Components;

namespace Charcoal.Styling;

/// <summary>An app's stylesheets in cascade order.</summary>
public sealed class StylesheetCollection : Collection<Stylesheet>
{
    /// <summary>Raised after any change to the list.</summary>
    public event Action? Changed;

    /// <summary>Parses CSS and appends it.</summary>
    public Stylesheet Add(string css)
    {
        var sheet = Stylesheet.Parse(css);
        Add(sheet);
        return sheet;
    }

    protected override void InsertItem(int index, Stylesheet item)
    {
        base.InsertItem(index, item);
        Changed?.Invoke();
    }

    protected override void RemoveItem(int index)
    {
        base.RemoveItem(index);
        Changed?.Invoke();
    }

    protected override void SetItem(int index, Stylesheet item)
    {
        base.SetItem(index, item);
        Changed?.Invoke();
    }

    protected override void ClearItems()
    {
        base.ClearItems();
        Changed?.Invoke();
    }
}

/// <summary>The stylesheets and focused element that styles are resolved against.</summary>
public sealed class StyleContext
{
    public StyleContext()
    {
        Sheets.Changed += () => _focusFlags = null;
    }

    private (bool Depends, bool Descendants)? _focusFlags;

    /// <summary>Later sheets win at equal specificity.</summary>
    public StylesheetCollection Sheets { get; } = new();

    public HostElement? Focused { get; internal set; }

    /// <summary>What <c>@media</c> is evaluated against, kept current by the app.</summary>
    public MediaEnvironment Media { get; set; } = MediaEnvironment.Default;

    /// <summary>Whether any sheet uses <c>@media</c>, so a change to <see cref="Media"/> restyles.</summary>
    public bool UsesMedia => Sheets.Any(s => s.UsesMedia);

    /// <summary>Whether any sheet uses <c>@container</c>, so containers are measured after each layout.</summary>
    public bool UsesContainer => Sheets.Any(s => s.UsesContainer);

    public bool DependsOnFocus => Flags().Depends;

    /// <summary>Whether focus can restyle elements below the focused one and its ancestors.</summary>
    public bool FocusAffectsDescendants => Flags().Descendants;

    private (bool Depends, bool Descendants) Flags()
    {
        _focusFlags ??= (Sheets.Any(s => s.DependsOnFocus), Sheets.Any(s => s.FocusAffectsDescendants));
        return _focusFlags.Value;
    }
}
