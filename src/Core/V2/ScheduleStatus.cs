namespace Core.V2;

[GenerateSerializer]
public sealed class ScheduleStatus
{
    [Id(0)]
    public bool IsRunning { get; set; }

    [Id(1)]
    public TimeSpan Interval { get; set; }

    [Id(2)]
    public int TickCount { get; set; }

    [Id(3)]
    public int? MaxTicks { get; set; }
}
