using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace SlopTui.Routing;

/// <summary>
/// A <see cref="NavigationManager"/> that keeps the location in memory, so
/// <c>@page</c>, <c>&lt;Router&gt;</c> and <c>NavigateTo</c> work without an
/// address bar.
/// </summary>
/// <remarks>
/// The base URI only exists because navigation deals in absolute URIs.
/// <see cref="NavigationOptions.ForceLoad"/> is ignored since there is no
/// document to reload. The history is a stack the app can walk with
/// <see cref="Back"/>, which no key is bound to by default.
/// </remarks>
public sealed class TerminalNavigationManager : NavigationManager
{
    public const string DefaultBaseUri = "tui:///";

    private readonly List<string> _history = [];

    public TerminalNavigationManager(string baseUri = DefaultBaseUri, string? startAt = null)
    {
        if (!baseUri.EndsWith('/'))
        {
            baseUri += '/';
        }
        var start = startAt is null ? baseUri : new Uri(new Uri(baseUri), startAt).ToString();
        Initialize(baseUri, start);
        _history.Add(start);
    }

    /// <summary>The locations visited, oldest first, ending with the current one.</summary>
    public IReadOnlyList<string> History => _history;

    public bool CanGoBack => _history.Count > 1;

    /// <summary>Returns to the previous location, if there is one.</summary>
    public bool Back()
    {
        if (!CanGoBack) return false;
        _history.RemoveAt(_history.Count - 1);
        Uri = _history[^1];
        NotifyLocationChanged(isInterceptedLink: false);
        return true;
    }

    /// <remarks>Navigating to the current location still notifies, as on the web.</remarks>
    protected override void NavigateToCore(string uri, NavigationOptions options)
    {
        var absolute = ToAbsoluteUri(uri).ToString();
        if (options.ReplaceHistoryEntry && _history.Count > 0)
        {
            _history[^1] = absolute;
        }
        else
        {
            _history.Add(absolute);
        }

        Uri = absolute;
        NotifyLocationChanged(isInterceptedLink: false);
    }

    /// <summary>The app handles every click already, so there is nothing to enable.</summary>
    internal sealed class Interception : INavigationInterception
    {
        public Task EnableNavigationInterceptionAsync() => Task.CompletedTask;
    }

    /// <summary>
    /// Does not scroll to a <c>#hash</c> target, since it is unclear which of
    /// the app's scroll containers should move. The route still matches.
    /// </summary>
    internal sealed class NoHashScrolling : IScrollToLocationHash
    {
        public Task RefreshScrollPositionForHash(string locationAbsolute) => Task.CompletedTask;
    }
}
