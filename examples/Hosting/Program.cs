using Charcoal.Components;
using Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// The generic host owns configuration, logging and DI; AddCharcoal registers
// the terminal app as a hosted service and injects the host's services into
// the component tree. q quits (the app then stops the host).
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton(new Clock(TimeProvider.System));
builder.Services.AddCharcoal<App>();
builder.Build().Run();
return 0;
