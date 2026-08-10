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

    public async Task<ServerStatusDto> GetServerStatusAsync()
    {
        var unameTask = QueryMetricAsync("node_uname_info");

        var uptimeTask = QueryScalarAsync(
            "time() - node_boot_time_seconds"
        );

        var cpuUsageTask = QueryScalarAsync(
            "100 - (avg by(instance) (rate(node_cpu_seconds_total{mode=\"idle\"}[5m])) * 100)"
        );

        var load1Task = QueryScalarAsync("node_load1");
        var load5Task = QueryScalarAsync("node_load5");
        var load15Task = QueryScalarAsync("node_load15");

        var cpuCountTask = QueryScalarAsync(
            "count(count by(cpu) (node_cpu_seconds_total))"
        );

        var memoryTotalTask = QueryScalarAsync(
            "node_memory_MemTotal_bytes"
        );

        var memoryAvailableTask = QueryScalarAsync(
            "node_memory_MemAvailable_bytes"
        );

        var swapTotalTask = QueryScalarAsync(
            "node_memory_SwapTotal_bytes"
        );

        var swapFreeTask = QueryScalarAsync(
            "node_memory_SwapFree_bytes"
        );

        var diskTotalTask = QueryScalarAsync(
            "node_filesystem_size_bytes{mountpoint=\"/\",fstype!=\"rootfs\"}"
        );

        var diskAvailableTask = QueryScalarAsync(
            "node_filesystem_avail_bytes{mountpoint=\"/\",fstype!=\"rootfs\"}"
        );

        var networkRxTask = QueryScalarAsync(
            "sum(node_network_receive_bytes_total{device!=\"lo\"})"
        );

        var networkTxTask = QueryScalarAsync(
            "sum(node_network_transmit_bytes_total{device!=\"lo\"})"
        );

        var temperatureTask = QueryScalarAsync(
            "max(node_hwmon_temp_celsius)"
        );

        await Task.WhenAll(
            unameTask,
            uptimeTask,
            cpuUsageTask,
            load1Task,
            load5Task,
            load15Task,
            cpuCountTask,
            memoryTotalTask,
            memoryAvailableTask,
            swapTotalTask,
            swapFreeTask,
            diskTotalTask,
            diskAvailableTask,
            networkRxTask,
            networkTxTask,
            temperatureTask
        );

        var uname = await unameTask;
        var memoryTotal = await memoryTotalTask;
        var memoryAvailable = await memoryAvailableTask;

        double? memoryUsed =
            memoryTotal.HasValue && memoryAvailable.HasValue
                ? memoryTotal.Value - memoryAvailable.Value
                : null;

        double? memoryUsage =
            memoryTotal.HasValue &&
            memoryTotal.Value > 0 &&
            memoryUsed.HasValue
                ? memoryUsed.Value / memoryTotal.Value * 100
                : null;

        var swapTotal = await swapTotalTask;
        var swapFree = await swapFreeTask;

        double? swapUsed =
            swapTotal.HasValue && swapFree.HasValue
                ? swapTotal.Value - swapFree.Value
                : null;

        double? swapUsage =
            swapTotal.HasValue &&
            swapTotal.Value > 0 &&
            swapUsed.HasValue
                ? swapUsed.Value / swapTotal.Value * 100
                : 0;

        var diskTotal = await diskTotalTask;
        var diskAvailable = await diskAvailableTask;

        double? diskUsed =
            diskTotal.HasValue && diskAvailable.HasValue
                ? diskTotal.Value - diskAvailable.Value
                : null;

        double? diskUsage =
            diskTotal.HasValue &&
            diskTotal.Value > 0 &&
            diskUsed.HasValue
                ? diskUsed.Value / diskTotal.Value * 100
                : null;

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

            Cpu = new CpuStatusDto
            {
                UsagePercent = Round(await cpuUsageTask),
                Load1 = Round(await load1Task),
                Load5 = Round(await load5Task),
                Load15 = Round(await load15Task),

                LogicalCpus = (int?)await cpuCountTask
            },

            Memory = new MemoryStatusDto
            {
                TotalBytes = memoryTotal,
                UsedBytes = memoryUsed,
                AvailableBytes = memoryAvailable,
                UsagePercent = Round(memoryUsage)
            },

            Swap = new SwapStatusDto
            {
                TotalBytes = swapTotal,
                UsedBytes = swapUsed,
                FreeBytes = swapFree,
                UsagePercent = Round(swapUsage)
            },

            Storage = new StorageStatusDto
            {
                TotalBytes = diskTotal,
                UsedBytes = diskUsed,
                AvailableBytes = diskAvailable,
                UsagePercent = Round(diskUsage)
            },

            Network = new NetworkStatusDto
            {
                ReceivedBytes = await networkRxTask,
                TransmittedBytes = await networkTxTask
            },

            TemperatureCelsius = Round(await temperatureTask),

            TimestampUtc = DateTime.UtcNow
        };
    }

    private async Task<double?> QueryScalarAsync(string query)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"/api/v1/query?query={Uri.EscapeDataString(query)}"
            );

            response.EnsureSuccessStatusCode();

            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync()
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
        catch
        {
            return null;
        }
    }

    private async Task<UnameInfo?> QueryMetricAsync(string query)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"/api/v1/query?query={Uri.EscapeDataString(query)}"
            );

            response.EnsureSuccessStatusCode();

            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync()
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
        catch
        {
            return null;
        }
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

    private sealed record UnameInfo(
        string? NodeName,
        string? SysName,
        string? Release,
        string? Machine
    );
}
