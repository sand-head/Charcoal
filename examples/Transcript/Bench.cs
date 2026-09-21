using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SlopTui.Components;
using SlopTui.Layout;
using SlopTui.Terminal;

namespace Transcript;

/// <summary>Streams 300 updates into a 10,000-line transcript on a headless terminal and prints frame timings.</summary>
public static class Bench
{
    public static int Run()
    {
        var terminal = new HeadlessTerminal(160, 45);
        var app = new TuiApp(terminal, new TuiAppOptions { FrameInterval = TimeSpan.Zero });
        var frames = new List<FrameStats>();
        var bytes = 0L;
        app.FramePainted += stats =>
        {
            frames.Add(stats);
            bytes += stats.Bytes;
        };

        App? instance = null;
        app.Services.AddSingleton(new AppHandle(a => instance = a));

        var driver = new Thread(() =>
        {
            while (frames.Count == 0) Thread.Sleep(5);
            for (var i = 0; i < 300; i++)
            {
                app.InvokeAsync(() => instance!.StreamOnce()).Wait();
                while (app.FrameCount < frames.Count + 1 && app.Renderer.Dirty) Thread.Sleep(1);
            }
            app.InvokeAsync(() => app.Exit()).Wait();
        });
        driver.Start();

        var sw = Stopwatch.StartNew();
        app.Run<App>(new Dictionary<string, object?> { ["Preload"] = 10_000, ["Streaming"] = false });
        sw.Stop();
        driver.Join();

        var first = frames[0];
        Console.WriteLine($"frames {frames.Count} in {sw.Elapsed.TotalMilliseconds:F0} ms; first frame {first.Total.TotalMilliseconds:F1} ms ({first.Bytes} bytes)");

        var streamed = frames.Skip(1).ToList();
        if (streamed.Count > 0) PrintStreamingStats(streamed);

        Console.WriteLine($"bytes written total {bytes}");
        return 0;
    }

    private static void PrintStreamingStats(List<FrameStats> frames)
    {
        var totals = frames.Select(f => f.Total.TotalMilliseconds).Order().ToArray();
        var median = totals[totals.Length / 2];
        var p90 = totals[(int)(totals.Length * 0.9)];
        Console.WriteLine($"streaming frame: median {median:F2} ms · p90 {p90:F2} ms · max {totals[^1]:F2} ms");

        var layout = frames.Average(f => f.Layout.TotalMilliseconds);
        var paint = frames.Average(f => f.Paint.TotalMilliseconds);
        var flush = frames.Average(f => f.Flush.TotalMilliseconds);
        var bytes = frames.Average(f => f.Bytes);
        Console.WriteLine($"  layout {layout:F2} ms · paint {paint:F2} ms · flush {flush:F2} ms · {bytes:F0} bytes");
    }
}

/// <summary>Lets the bench reach the App instance the renderer created.</summary>
public sealed class AppHandle(Action<App> register)
{
    public void Register(App app) => register(app);
}
