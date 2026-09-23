using System.Globalization;
using System.Text;
using Wcwidth;

namespace Charcoal.Rendering;

/// <summary>
/// How many terminal columns text occupies, measured per grapheme cluster so
/// emoji sequences, flags and combining marks match what the terminal draws.
/// </summary>
public static class TextWidth
{
    /// <summary>
    /// The columns one grapheme cluster occupies. Emoji sequences are two wide;
    /// anything else is as wide as its first codepoint.
    /// </summary>
    public static int Cluster(string cluster)
    {
        if (cluster.Length == 0) return 0;
        if (cluster.Length == 1 && cluster[0] is >= ' ' and <= '~') return 1;

        var first = default(Rune);
        var count = 0;
        var allRegionalIndicators = true;
        var emojiPresentation = false;
        var textPresentation = false;

        foreach (var rune in cluster.EnumerateRunes())
        {
            if (count++ == 0) first = rune;
            var value = rune.Value;
            if (value is < 0x1F1E6 or > 0x1F1FF) allRegionalIndicators = false;
            if (value is 0x200D or 0xFE0F or (>= 0x1F3FB and <= 0x1F3FF)) emojiPresentation = true;
            if (value == 0xFE0E) textPresentation = true;
        }

        if (count == 0) return 0;
        if (count >= 2 && allRegionalIndicators) return 2;
        if (emojiPresentation) return 2;
        if (textPresentation) return 1;

        // wcwidth answers -1 for control characters; count them as one column
        // so they cannot throw off the column accounting.
        var width = UnicodeCalculator.GetWidth(first);
        return width < 0 ? 1 : width;
    }

    /// <summary>The columns a whole string occupies, cluster by cluster.</summary>
    public static int Measure(string text)
    {
        var total = 0;
        foreach (var (_, width) in Clusters(text)) total += width;
        return total;
    }

    /// <summary>The text's grapheme clusters paired with their widths.</summary>
    public static ClusterEnumerator Clusters(string text) => new(text);

    /// <summary>The length in chars of the longest prefix of whole clusters that fits in <paramref name="columns"/>.</summary>
    public static int TakePrefix(string text, int columns, out int used)
    {
        used = 0;
        var taken = 0;
        foreach (var (cluster, width) in Clusters(text))
        {
            if (used + width > columns) break;
            taken += cluster.Length;
            used += width;
        }
        return taken;
    }

    /// <summary>An allocation-free walk over grapheme clusters for ASCII text.</summary>
    public struct ClusterEnumerator(string text)
    {
        private int _index;

        public (string Cluster, int Width) Current { get; private set; }

        public readonly ClusterEnumerator GetEnumerator() => this;

        public bool MoveNext()
        {
            if (_index >= text.Length) return false;

            var length = StringInfo.GetNextTextElementLength(text.AsSpan(_index));
            var first = text[_index];
            var cluster = length == 1 && first < AsciiClusters.Length
                ? AsciiClusters[first]
                : text.Substring(_index, length);
            _index += length;
            Current = (cluster, Cluster(cluster));
            return true;
        }
    }

    private static readonly string[] AsciiClusters =
        [.. Enumerable.Range(0, 128).Select(value => ((char)value).ToString())];
}
