namespace Hosting;

/// <summary>Application service registered with the host and injected into the component.</summary>
public sealed class Clock(TimeProvider timeProvider)
{
    public DateTime Now => timeProvider.GetLocalNow().DateTime;
}
