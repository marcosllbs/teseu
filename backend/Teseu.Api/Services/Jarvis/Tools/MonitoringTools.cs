using System.Diagnostics;
using System.Text.Json;
using Teseu.Api.Models;
using Teseu.Api.Services;

namespace Teseu.Api.Services.Jarvis.Tools;

/// <summary>Base class for parameterless read-only monitoring tools.</summary>
public abstract class ParameterlessReadTool(PrometheusService prometheus) : IJarvisTool
{
    protected PrometheusService Prometheus { get; } = prometheus;
    public abstract string Name { get; }
    public abstract string Description { get; }
    public PermissionLevel Permission => PermissionLevel.Read;
    public JsonElement ParameterSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new { },
        additionalProperties = false
    });

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var data = await ExecuteCoreAsync(cancellationToken);
            return ToolResult.Ok(Name, data, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            return ToolResult.Fail(Name, ex.Message, sw.ElapsedMilliseconds);
        }
    }

    protected abstract Task<object?> ExecuteCoreAsync(CancellationToken cancellationToken);
}

public sealed class GetServerStatusTool(PrometheusService prometheus) : ParameterlessReadTool(prometheus)
{
    public override string Name => "get_server_status";
    public override string Description => "Gets overall current server health data: system, uptime, CPU/load, memory, swap, root storage, network totals and temperature. Includes an overload assessment.";

    protected override async Task<object?> ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        var status = await Prometheus.GetServerStatusAsync(cancellationToken);
        var reasons = new List<string>();

        if (status.Cpu.UsagePercent >= 90)
            reasons.Add("CPU utilization is at least 90%.");
        if (status.Memory.UsagePercent >= 90)
            reasons.Add("Memory utilization is at least 90%.");
        if (status.Storage.UsagePercent >= 95)
            reasons.Add("Root storage utilization is at least 95%.");
        if (status.Cpu.Load1.HasValue && status.Cpu.LogicalCpus.HasValue && status.Cpu.Load1 > status.Cpu.LogicalCpus)
            reasons.Add("The 1-minute load average exceeds the logical CPU count.");

        var hasLoadEvidence = status.Cpu.UsagePercent.HasValue ||
            status.Memory.UsagePercent.HasValue ||
            status.Storage.UsagePercent.HasValue ||
            status.Cpu.Load1.HasValue;

        return new
        {
            status,
            assessment = new
            {
                isOverloaded = hasLoadEvidence ? reasons.Count > 0 : (bool?)null,
                reasons,
                criteria = "Overloaded when CPU >= 90%, memory >= 90%, root storage >= 95%, or 1-minute load average exceeds logical CPU count. Null means insufficient metrics."
            }
        };
    }
}

public sealed class GetCpuStatusTool(PrometheusService prometheus) : ParameterlessReadTool(prometheus)
{
    public override string Name => "get_cpu_status";
    public override string Description => "Gets current CPU utilization percentage and logical CPU count.";
    protected override async Task<object?> ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        var cpu = await Prometheus.GetCpuStatusAsync(cancellationToken);
        return new { cpu.UsagePercent, cpu.LogicalCpus };
    }
}

public sealed class GetMemoryStatusTool(PrometheusService prometheus) : ParameterlessReadTool(prometheus)
{
    public override string Name => "get_memory_status";
    public override string Description => "Gets current RAM totals, used, available and usage percentage.";
    protected override async Task<object?> ExecuteCoreAsync(CancellationToken cancellationToken) =>
        await Prometheus.GetMemoryStatusAsync(cancellationToken);
}

public sealed class GetStorageStatusTool(PrometheusService prometheus) : ParameterlessReadTool(prometheus)
{
    public override string Name => "get_storage_status";
    public override string Description => "Gets current total, used and available space for the root filesystem.";
    protected override async Task<object?> ExecuteCoreAsync(CancellationToken cancellationToken) =>
        await Prometheus.GetStorageStatusAsync(cancellationToken);
}

public sealed class GetNetworkStatusTool(PrometheusService prometheus) : ParameterlessReadTool(prometheus)
{
    public override string Name => "get_network_status";
    public override string Description => "Gets cumulative network bytes received and transmitted. These totals are not a current transfer rate.";
    protected override async Task<object?> ExecuteCoreAsync(CancellationToken cancellationToken) =>
        await Prometheus.GetNetworkStatusAsync(cancellationToken);
}

public sealed class GetTemperatureStatusTool(PrometheusService prometheus) : ParameterlessReadTool(prometheus)
{
    public override string Name => "get_temperature_status";
    public override string Description => "Gets the current maximum hardware temperature when exported by node-exporter.";
    protected override async Task<object?> ExecuteCoreAsync(CancellationToken cancellationToken) =>
        new { temperatureCelsius = await Prometheus.GetTemperatureStatusAsync(cancellationToken) };
}

public sealed class GetUptimeStatusTool(PrometheusService prometheus) : ParameterlessReadTool(prometheus)
{
    public override string Name => "get_uptime_status";
    public override string Description => "Gets server uptime with an exact seconds value and a preformatted duration. Use formattedDuration instead of calculating it.";

    protected override async Task<object?> ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        var seconds = await Prometheus.GetUptimeAsync(cancellationToken);
        return new
        {
            uptimeSeconds = seconds,
            formattedDuration = seconds.HasValue ? FormatDuration(TimeSpan.FromSeconds(seconds.Value)) : null
        };
    }

    private static string FormatDuration(TimeSpan duration)
    {
        var parts = new List<string>();
        if (duration.Days > 0) parts.Add($"{duration.Days} day(s)");
        if (duration.Hours > 0) parts.Add($"{duration.Hours} hour(s)");
        if (duration.Minutes > 0) parts.Add($"{duration.Minutes} minute(s)");
        if (parts.Count == 0) parts.Add($"{Math.Max(0, duration.Seconds)} second(s)");
        return string.Join(", ", parts);
    }
}

public sealed class GetContainersTool(PrometheusService prometheus) : ParameterlessReadTool(prometheus)
{
    public override string Name => "get_containers";
    public override string Description => "Gets containers visible in cAdvisor, ordered by memory use, with current CPU and memory metrics when available.";

    protected override async Task<object?> ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        var containers = await Prometheus.GetContainersAsync(cancellationToken);
        var formatted = containers.Select(FormatContainer).ToArray();
        return new { highestMemoryConsumer = formatted.FirstOrDefault(), containers = formatted };
    }

    private static object FormatContainer(ContainerStatusDto container) => new
    {
        container.Name,
        container.CpuUsagePercent,
        memoryUsage = FormatBytes(container.MemoryUsageBytes),
        memoryLimit = FormatBytes(container.MemoryLimitBytes),
        container.MemoryUsagePercent,
        container.TimestampUtc
    };

    internal static string? FormatBytes(double? bytes)
    {
        if (!bytes.HasValue) return null;
        var units = new[] { "B", "KiB", "MiB", "GiB", "TiB" };
        var value = bytes.Value;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
    }
}

public sealed class GetContainerStatusTool(PrometheusService prometheus) : IJarvisTool
{
    public string Name => "get_container_status";
    public string Description => "Gets current cAdvisor metrics for one named container or service. Returns null when it is not visible.";
    public PermissionLevel Permission => PermissionLevel.Read;
    public JsonElement ParameterSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new { name = new { type = "string", description = "Container or service name" } },
        required = new[] { "name" },
        additionalProperties = false
    });

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        if (!arguments.TryGetProperty("name", out var property) || string.IsNullOrWhiteSpace(property.GetString()))
            return ToolResult.Fail(Name, "A container name is required.", sw.ElapsedMilliseconds);

        var container = await prometheus.GetContainerStatusAsync(property.GetString()!, cancellationToken);
        if (container is null)
            return ToolResult.Ok(Name, new { found = false, message = "Container not visible in current metrics." }, sw.ElapsedMilliseconds);

        return ToolResult.Ok(Name, new
        {
            found = true,
            container.Name,
            container.CpuUsagePercent,
            memoryUsage = GetContainersTool.FormatBytes(container.MemoryUsageBytes),
            memoryLimit = GetContainersTool.FormatBytes(container.MemoryLimitBytes),
            container.MemoryUsagePercent,
            container.TimestampUtc
        }, sw.ElapsedMilliseconds);
    }
}
