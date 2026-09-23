using Charcoal.Components;
using Charcoal.Terminal;
using Transcript;

// Transcript [--lines N]   N preloaded lines, 2000 by default
// Transcript --bench       10,000 lines and 300 streamed updates, headless, with frame timings
if (args.Contains("--bench")) return Bench.Run();

var lines = 2000;
var at = Array.IndexOf(args, "--lines");
if (at >= 0 && at + 1 < args.Length) lines = int.Parse(args[at + 1]);

var app = new TuiApp();
return app.Run<App>(new Dictionary<string, object?> { ["Preload"] = lines });
