using System.Globalization;
using System.Text.Json;
using Teseu.Api.Models;

namespace Teseu.Api.Services;

public sealed class PrometheusService
{
    private readonly HttpClient _httpClient;

    public PrometheusService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ServerStatusDto> GetServerStatusAsync(CancellationToken cancellationToken = default)
    {
        var unameTask = QueryMetricAsync("node_uname_info", cancellationToken);

        var uptimeTask = GetUptimeAsync(cancellationToken);
        var cpuTask = GetCpuStatusAsync(cancellationToken);
        var memoryTask = GetMemoryStatusAsync(cancellationToken);
        var swapTask = GetSwapStatusAsync(cancellationToken);
        var storageTask = GetStorageStatusAsync(cancellationToken);
        var networkTask = GetNetworkStatusAsync(cancellationToken);
        var temperatureTask = GetTemperatureStatusAsync(cancellationToken);

        await Task.WhenAll(
            unameTask,
            uptimeTask,
            cpuTask,
            memoryTask,
            swapTask,
            storageTask,
            networkTask,
            temperatureTask
        );

        var uname = await unameTask;

        return new ServerStatusDto
        {
            Hostname = uname?.NodeName,

            System = new SystemInfoDto
            {
                OperatingSystem = uname?.SysName,
                Kernel = uname?.Release,
                Architecture = uname?.Machine,
                UptimeSeconds = await uptimeTask
            },
            Cpu = await cpuTask,
            Memory = await memoryTask,
            Swap = await swapTask,
            Storage = await storageTask,
            Network = await networkTask,
            TemperatureCelsius = await temperatureTask,

            TimestampUtc = DateTime.UtcNow
        };
    }

    public async Task<CpuStatusDto> GetCpuStatusAsync(CancellationToken cancellationToken = default)
    {
        var usageTask = QueryScalarAsync("100 - (avg by(instance) (rate(node_cpu_seconds_total{mode=\"idle\"}[5m])) * 100)", cancellationToken);
        var load1Task = QueryScalarAsync("node_load1", cancellationToken);
        var load5Task = QueryScalarAsync("node_load5", cancellationToken);
        var load15Task = QueryScalarAsync("node_load15", cancellationToken);
        var countTask = QueryScalarAsync("count(count by(cpu) (node_cpu_seconds_total))", cancellationToken);
        await Task.WhenAll(usageTask, load1Task, load5Task, load15Task, countTask);

        return new CpuStatusDto
        {
            UsagePercent = Round(await usageTask),
            Load1 = Round(await load1Task),
            Load5 = Round(await load5Task),
            Load15 = Round(await load15Task),
            LogicalCpus = (int?)await countTask
        };
    }

    public async Task<MemoryStatusDto> GetMemoryStatusAsync(CancellationToken cancellationToken = default)
    {
        var totalTask = QueryScalarAsync("node_memory_MemTotal_bytes", cancellationToken);
        var availableTask = QueryScalarAsync("node_memory_MemAvailable_bytes", cancellationToken);
        await Task.WhenAll(totalTask, availableTask);
        var total = await totalTask;
        var available = await availableTask;
        var used = Difference(total, available);

        return new MemoryStatusDto
        {
            TotalBytes = total,
            UsedBytes = used,
            AvailableBytes = available,
            UsagePercent = Percentage(used, total)
        };
    }

    public async Task<SwapStatusDto> GetSwapStatusAsync(CancellationToken cancellationToken = default)
    {
        var totalTask = QueryScalarAsync("node_memory_SwapTotal_bytes", cancellationToken);
        var freeTask = QueryScalarAsync("node_memory_SwapFree_bytes", cancellationToken);
        await Task.WhenAll(totalTask, freeTask);
        var total = await totalTask;
        var free = await freeTask;
        var used = Difference(total, free);

        return new SwapStatusDto
        {
            TotalBytes = total,
            UsedBytes = used,
            FreeBytes = free,
            UsagePercent = total == 0 ? 0 : Percentage(used, total)
        };
    }

    public async Task<StorageStatusDto> GetStorageStatusAsync(CancellationToken cancellationToken = default)
    {
        var totalTask = QueryScalarAsync("node_filesystem_size_bytes{mountpoint=\"/\",fstype!=\"rootfs\"}", cancellationToken);
        var availableTask = QueryScalarAsync("node_filesystem_avail_bytes{mountpoint=\"/\",fstype!=\"rootfs\"}", cancellationToken);
        await Task.WhenAll(totalTask, availableTask);
        var total = await totalTask;
        var available = await availableTask;
        var used = Difference(total, available);

        return new StorageStatusDto
        {
            TotalBytes = total,
            UsedBytes = used,
            AvailableBytes = available,
            UsagePercent = Percentage(used, total)
        };
    }

    public async Task<NetworkStatusDto> GetNetworkStatusAsync(CancellationToken cancellationToken = default)
    {
        var rxTask = QueryScalarAsync("sum(node_network_receive_bytes_total{device!=\"lo\"})", cancellationToken);
        var txTask = QueryScalarAsync("sum(node_network_transmit_bytes_total{device!=\"lo\"})", cancellationToken);
        await Task.WhenAll(rxTask, txTask);
        return new NetworkStatusDto { ReceivedBytes = await rxTask, TransmittedBytes = await txTask };
    }

    public async Task<double?> GetTemperatureStatusAsync(CancellationToken cancellationToken = default) =>
        Round(await QueryScalarAsync("max(node_hwmon_temp_celsius)", cancellationToken));

    public Task<double?> GetUptimeAsync(CancellationToken cancellationToken = default) =>
        QueryScalarAsync("time() - node_boot_time_seconds", cancellationToken);

    public async Task<IReadOnlyList<ContainerStatusDto>> GetContainersAsync(CancellationToken cancellationToken = default)
    {
        // A container can be scraped through more than one target. max avoids
        // multiplying its usage while still collapsing duplicate series by name.
        var memoryTask = QueryVectorAsync("max by(name) (container_memory_working_set_bytes{name!=\"\"})", cancellationToken);
        var limitTask = QueryVectorAsync("max by(name) (container_spec_memory_limit_bytes{name!=\"\"})", cancellationToken);
        var cpuTask = QueryVectorAsync("max by(name) (rate(container_cpu_usage_seconds_total{name!=\"\"}[5m])) * 100", cancellationToken);
        await Task.WhenAll(memoryTask, limitTask, cpuTask);

        var memory = (await memoryTask).ToDictionary(x => x.Name, x => x.Value);
        var limits = (await limitTask).ToDictionary(x => x.Name, x => x.Value);
        var cpu = (await cpuTask).ToDictionary(x => x.Name, x => x.Value);
        var names = memory.Keys.Concat(limits.Keys).Concat(cpu.Keys).Distinct(StringComparer.OrdinalIgnoreCase);

        return names.Select(name => new ContainerStatusDto
        {
            Name = name,
            CpuUsagePercent = cpu.TryGetValue(name, out var cpuValue) ? Round(cpuValue) : null,
            MemoryUsageBytes = memory.TryGetValue(name, out var memoryValue) ? memoryValue : null,
            MemoryLimitBytes = limits.TryGetValue(name, out var limitValue) && limitValue > 0 && limitValue < 9e18 ? limitValue : null,
            MemoryUsagePercent = memory.TryGetValue(name, out memoryValue) && limits.TryGetValue(name, out limitValue) && limitValue > 0 && limitValue < 9e18
                ? Percentage(memoryValue, limitValue)
                : null,
            TimestampUtc = DateTime.UtcNow
        }).OrderByDescending(x => x.MemoryUsageBytes).ToArray();
    }

    public async Task<ContainerStatusDto?> GetContainerStatusAsync(string name, CancellationToken cancellationToken = default) =>
        (await GetContainersAsync(cancellationToken)).FirstOrDefault(container =>
            container.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            container.Name.Contains(name, StringComparison.OrdinalIgnoreCase));

    private async Task<double?> QueryScalarAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"/api/v1/query?query={Uri.EscapeDataString(query)}",
                cancellationToken
            );

            response.EnsureSuccessStatusCode();

            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken)
            );

            var result = document.RootElement
                .GetProperty("data")
                .GetProperty("result");

            if (result.GetArrayLength() == 0)
                return null;

            var value = result[0]
                .GetProperty("value")[1]
                .GetString();

            if (double.TryParse(
                    value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var parsed))
            {
                return parsed;
            }

            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (HttpRequestException) { return null; }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (KeyNotFoundException) { return null; }
    }

    private async Task<UnameInfo?> QueryMetricAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"/api/v1/query?query={Uri.EscapeDataString(query)}",
                cancellationToken
            );

            response.EnsureSuccessStatusCode();

            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken)
            );

            var result = document.RootElement
                .GetProperty("data")
                .GetProperty("result");

            if (result.GetArrayLength() == 0)
                return null;

            var metric = result[0].GetProperty("metric");

            return new UnameInfo(
                GetProperty(metric, "nodename"),
                GetProperty(metric, "sysname"),
                GetProperty(metric, "release"),
                GetProperty(metric, "machine")
            );
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (HttpRequestException) { return null; }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (KeyNotFoundException) { return null; }
    }

    private async Task<IReadOnlyList<NamedValue>> QueryVectorAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"/api/v1/query?query={Uri.EscapeDataString(query)}", cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var result = document.RootElement.GetProperty("data").GetProperty("result");
            var values = new List<NamedValue>();
            foreach (var item in result.EnumerateArray())
            {
                var metric = item.GetProperty("metric");
                var name = GetProperty(metric, "name");
                var rawValue = item.GetProperty("value")[1].GetString();
                if (!string.IsNullOrWhiteSpace(name) && double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    values.Add(new NamedValue(name, value));
            }
            return values;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return []; }
        catch (HttpRequestException) { return []; }
        catch (JsonException) { return []; }
        catch (InvalidOperationException) { return []; }
        catch (KeyNotFoundException) { return []; }
    }

    private static string? GetProperty(
        JsonElement element,
        string property)
    {
        return element.TryGetProperty(property, out var value)
            ? value.GetString()
            : null;
    }

    private static double? Round(double? value)
    {
        return value.HasValue
            ? Math.Round(value.Value, 2)
            : null;
    }

    private static double? Difference(double? total, double? remainder) =>
        total.HasValue && remainder.HasValue ? total.Value - remainder.Value : null;

    private static double? Percentage(double? value, double? total) =>
        value.HasValue && total.HasValue && total.Value > 0 ? Round(value.Value / total.Value * 100) : null;

    private sealed record UnameInfo(
        string? NodeName,
        string? SysName,
        string? Release,
        string? Machine
    );

    private sealed record NamedValue(string Name, double Value);
}
