namespace Teseu.Api.Models;

public sealed record ServerStatusDto
{
    public string? Hostname { get; init; }
    public string Status { get; init; } = "online";

    public SystemInfoDto System { get; init; } = new();
    public CpuStatusDto Cpu { get; init; } = new();
    public MemoryStatusDto Memory { get; init; } = new();
    public SwapStatusDto Swap { get; init; } = new();
    public StorageStatusDto Storage { get; init; } = new();
    public NetworkStatusDto Network { get; init; } = new();

    public double? TemperatureCelsius { get; init; }

    public DateTime TimestampUtc { get; init; }
}

public sealed record SystemInfoDto
{
    public string? OperatingSystem { get; init; }
    public string? Kernel { get; init; }
    public string? Architecture { get; init; }
    public double? UptimeSeconds { get; init; }
}

public sealed record CpuStatusDto
{
    public double? UsagePercent { get; init; }
    public double? Load1 { get; init; }
    public double? Load5 { get; init; }
    public double? Load15 { get; init; }
    public int? LogicalCpus { get; init; }
}

public sealed record MemoryStatusDto
{
    public double? TotalBytes { get; init; }
    public double? UsedBytes { get; init; }
    public double? AvailableBytes { get; init; }
    public double? UsagePercent { get; init; }
}

public sealed record SwapStatusDto
{
    public double? TotalBytes { get; init; }
    public double? UsedBytes { get; init; }
    public double? FreeBytes { get; init; }
    public double? UsagePercent { get; init; }
}

public sealed record StorageStatusDto
{
    public double? TotalBytes { get; init; }
    public double? UsedBytes { get; init; }
    public double? AvailableBytes { get; init; }
    public double? UsagePercent { get; init; }
}

public sealed record NetworkStatusDto
{
    public double? ReceivedBytes { get; init; }
    public double? TransmittedBytes { get; init; }
}

public sealed record ContainerStatusDto
{
    public required string Name { get; init; }
    public double? CpuUsagePercent { get; init; }
    public double? MemoryUsageBytes { get; init; }
    public double? MemoryLimitBytes { get; init; }
    public double? MemoryUsagePercent { get; init; }
    public DateTime TimestampUtc { get; init; }
}
