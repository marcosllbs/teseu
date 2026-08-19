using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Teseu.Api.Services.AI;
using Teseu.Api.Services.Jarvis;
using Xunit;

namespace Teseu.Api.Tests;

public class JarvisOrchestratorTests
{
    private readonly IJarvisAiProvider _mockProvider = Substitute.For<IJarvisAiProvider>();
    private readonly IOptions<JarvisOptions> _options = Options.Create(new JarvisOptions
    {
        Provider = "openai",
        MaxToolIterations = 5
    });

    private JarvisOrchestrator CreateOrchestrator(params IJarvisTool[] tools)
    {
        return new JarvisOrchestrator(
            _mockProvider,
            tools,
            _options,
            NullLogger<JarvisOrchestrator>.Instance);
    }

    [Fact]
    public async Task ChatAsync_SimpleAnswer_ReturnsWithoutToolCalls()
    {
        // Arrange
        _mockProvider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<IReadOnlyList<ToolDefinition>?>(), Arg.Any<CancellationToken>())
            .Returns(new AiCompletionResult
            {
                Message = new ChatMessage("assistant", "The CPU is at 25%."),
                Usage = new AiUsageMetrics { InputTokens = 100, OutputTokens = 20 },
                Model = "gpt-4o-mini"
            });

        var orchestrator = CreateOrchestrator();

        // Act
        var result = await orchestrator.ChatAsync("What's the CPU usage?", CancellationToken.None);

        // Assert
        Assert.Equal("The CPU is at 25%.", result.Message);
        Assert.Empty(result.ToolsUsed);
        Assert.Equal("gpt-4o-mini", result.Model);
        Assert.NotNull(result.RequestId);
        Assert.NotNull(result.Usage);
        Assert.Equal(100, result.Usage.InputTokens);
        Assert.Equal(20, result.Usage.OutputTokens);
    }

    [Fact]
    public async Task ChatAsync_WithToolCall_ExecutesToolAndReturnsAnswer()
    {
        // Arrange
        var tool = new FakeReadTool("get_cpu_status", new { usagePercent = 42.5 });

        // First call: model requests a tool
        _mockProvider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<IReadOnlyList<ToolDefinition>?>(), Arg.Any<CancellationToken>())
            .Returns(
                // First call returns tool call
                new AiCompletionResult
                {
                    Message = new ChatMessage("assistant", null, [
                        new ToolCallRequest("call_1", "get_cpu_status", JsonSerializer.SerializeToElement(new { }))
                    ]),
                    Usage = new AiUsageMetrics { InputTokens = 80, OutputTokens = 10 },
                    Model = "gpt-4o-mini"
                },
                // Second call returns final answer
                new AiCompletionResult
                {
                    Message = new ChatMessage("assistant", "CPU usage is currently 42.5%."),
                    Usage = new AiUsageMetrics { InputTokens = 150, OutputTokens = 15 },
                    Model = "gpt-4o-mini"
                });

        var orchestrator = CreateOrchestrator(tool);

        // Act
        var result = await orchestrator.ChatAsync("CPU usage?", CancellationToken.None);

        // Assert
        Assert.Equal("CPU usage is currently 42.5%.", result.Message);
        Assert.Single(result.ToolsUsed);
        Assert.Contains("get_cpu_status", result.ToolsUsed);
        Assert.Equal(230, result.Usage!.InputTokens);
        Assert.Equal(25, result.Usage!.OutputTokens);
    }

    [Fact]
    public async Task ChatAsync_UnknownTool_ReturnsErrorToolResult()
    {
        // Arrange: model asks for a tool that doesn't exist
        _mockProvider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<IReadOnlyList<ToolDefinition>?>(), Arg.Any<CancellationToken>())
            .Returns(
                new AiCompletionResult
                {
                    Message = new ChatMessage("assistant", null, [
                        new ToolCallRequest("call_1", "nonexistent_tool", JsonSerializer.SerializeToElement(new { }))
                    ]),
                    Usage = new AiUsageMetrics { InputTokens = 50, OutputTokens = 5 }
                },
                new AiCompletionResult
                {
                    Message = new ChatMessage("assistant", "I couldn't find that tool."),
                    Usage = new AiUsageMetrics { InputTokens = 80, OutputTokens = 10 }
                });

        var orchestrator = CreateOrchestrator();

        // Act
        var result = await orchestrator.ChatAsync("Do something", CancellationToken.None);

        // Assert: should still complete (error is passed back to model as context)
        Assert.Equal("I couldn't find that tool.", result.Message);
    }

    [Fact]
    public async Task ChatAsync_ControlToolBlocked_ReturnsPermissionError()
    {
        // Arrange
        var controlTool = new FakeControlTool("restart_container");

        _mockProvider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<IReadOnlyList<ToolDefinition>?>(), Arg.Any<CancellationToken>())
            .Returns(
                new AiCompletionResult
                {
                    Message = new ChatMessage("assistant", null, [
                        new ToolCallRequest("call_1", "restart_container", JsonSerializer.SerializeToElement(new { name = "grafana" }))
                    ]),
                    Usage = new AiUsageMetrics { InputTokens = 50, OutputTokens = 5 }
                },
                new AiCompletionResult
                {
                    Message = new ChatMessage("assistant", "I cannot restart containers — that requires Control permission."),
                    Usage = new AiUsageMetrics { InputTokens = 100, OutputTokens = 15 }
                });

        var orchestrator = CreateOrchestrator(controlTool);

        // Act
        var result = await orchestrator.ChatAsync("Restart Grafana", CancellationToken.None);

        // Assert
        Assert.Contains("restart_container", result.ToolsUsed);
        Assert.Contains("cannot", result.Message.ToLowerInvariant());
    }

    [Fact]
    public async Task ChatAsync_DangerousToolBlocked_ReturnsPermissionError()
    {
        // Arrange
        var dangerousTool = new FakeDangerousTool("reboot_server");

        _mockProvider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<IReadOnlyList<ToolDefinition>?>(), Arg.Any<CancellationToken>())
            .Returns(
                new AiCompletionResult
                {
                    Message = new ChatMessage("assistant", null, [
                        new ToolCallRequest("call_1", "reboot_server", JsonSerializer.SerializeToElement(new { }))
                    ]),
                    Usage = new AiUsageMetrics { InputTokens = 50, OutputTokens = 5 }
                },
                new AiCompletionResult
                {
                    Message = new ChatMessage("assistant", "I cannot reboot the server."),
                    Usage = new AiUsageMetrics { InputTokens = 100, OutputTokens = 10 }
                });

        var orchestrator = CreateOrchestrator(dangerousTool);

        // Act
        var result = await orchestrator.ChatAsync("Reboot server", CancellationToken.None);

        // Assert
        Assert.Contains("reboot_server", result.ToolsUsed);
    }

    [Fact]
    public async Task ChatAsync_MaxIterationsReached_ThrowsException()
    {
        // Arrange: model always requests tools, never gives a final answer
        var tool = new FakeReadTool("get_cpu_status", new { usagePercent = 50 });
        var options = Options.Create(new JarvisOptions { Provider = "openai", MaxToolIterations = 2 });

        _mockProvider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<IReadOnlyList<ToolDefinition>?>(), Arg.Any<CancellationToken>())
            .Returns(new AiCompletionResult
            {
                Message = new ChatMessage("assistant", null, [
                    new ToolCallRequest("call_1", "get_cpu_status", JsonSerializer.SerializeToElement(new { }))
                ]),
                Usage = new AiUsageMetrics { InputTokens = 50, OutputTokens = 5 }
            });

        var orchestrator = new JarvisOrchestrator(
            _mockProvider, [tool], options, NullLogger<JarvisOrchestrator>.Instance);

        // Act & Assert
        await Assert.ThrowsAsync<AiInvalidResponseException>(() =>
            orchestrator.ChatAsync("CPU?", CancellationToken.None));
    }

    [Fact]
    public async Task ChatAsync_EmptyResponse_ThrowsException()
    {
        // Arrange
        _mockProvider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<IReadOnlyList<ToolDefinition>?>(), Arg.Any<CancellationToken>())
            .Returns(new AiCompletionResult
            {
                Message = new ChatMessage("assistant", "   "),
                Usage = null
            });

        var orchestrator = CreateOrchestrator();

        // Act & Assert
        await Assert.ThrowsAsync<AiInvalidResponseException>(() =>
            orchestrator.ChatAsync("Hello", CancellationToken.None));
    }

    [Fact]
    public async Task ChatAsync_ToolThrowsException_ReturnsFailResult()
    {
        // Arrange
        var failingTool = new FakeFailingTool("get_cpu_status");

        _mockProvider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<IReadOnlyList<ToolDefinition>?>(), Arg.Any<CancellationToken>())
            .Returns(
                new AiCompletionResult
                {
                    Message = new ChatMessage("assistant", null, [
                        new ToolCallRequest("call_1", "get_cpu_status", JsonSerializer.SerializeToElement(new { }))
                    ]),
                    Usage = new AiUsageMetrics { InputTokens = 50, OutputTokens = 5 }
                },
                new AiCompletionResult
                {
                    Message = new ChatMessage("assistant", "CPU metrics are currently unavailable."),
                    Usage = new AiUsageMetrics { InputTokens = 100, OutputTokens = 10 }
                });

        var orchestrator = CreateOrchestrator(failingTool);

        // Act
        var result = await orchestrator.ChatAsync("CPU?", CancellationToken.None);

        // Assert
        Assert.Equal("CPU metrics are currently unavailable.", result.Message);
        Assert.Contains("get_cpu_status", result.ToolsUsed);
    }

    [Fact]
    public void GetToolPermissions_ReturnsCorrectLevels()
    {
        var tools = new IJarvisTool[]
        {
            new FakeReadTool("get_cpu_status", new { }),
            new FakeControlTool("restart_container"),
            new FakeDangerousTool("reboot_server")
        };

        var orchestrator = CreateOrchestrator(tools);
        var permissions = orchestrator.GetToolPermissions();

        Assert.Equal(PermissionLevel.Read, permissions["get_cpu_status"]);
        Assert.Equal(PermissionLevel.Control, permissions["restart_container"]);
        Assert.Equal(PermissionLevel.Dangerous, permissions["reboot_server"]);
    }
}

// --- Fake tools for testing ---

internal class FakeReadTool(string name, object data) : IJarvisTool
{
    public string Name => name;
    public string Description => $"Fake read tool: {name}";
    public PermissionLevel Permission => PermissionLevel.Read;
    public JsonElement ParameterSchema => JsonSerializer.SerializeToElement(new { type = "object", properties = new { } });
    public Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken ct) =>
        Task.FromResult(ToolResult.Ok(Name, data, 1));
}

internal class FakeControlTool(string name) : IJarvisTool
{
    public string Name => name;
    public string Description => $"Fake control tool: {name}";
    public PermissionLevel Permission => PermissionLevel.Control;
    public JsonElement ParameterSchema => JsonSerializer.SerializeToElement(new { type = "object", properties = new { } });
    public Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken ct) =>
        Task.FromResult(ToolResult.Ok(Name, new { restarted = true }, 100));
}

internal class FakeDangerousTool(string name) : IJarvisTool
{
    public string Name => name;
    public string Description => $"Fake dangerous tool: {name}";
    public PermissionLevel Permission => PermissionLevel.Dangerous;
    public JsonElement ParameterSchema => JsonSerializer.SerializeToElement(new { type = "object", properties = new { } });
    public Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken ct) =>
        Task.FromResult(ToolResult.Ok(Name, new { rebooted = true }, 5000));
}

internal class FakeFailingTool(string name) : IJarvisTool
{
    public string Name => name;
    public string Description => $"Fake failing tool: {name}";
    public PermissionLevel Permission => PermissionLevel.Read;
    public JsonElement ParameterSchema => JsonSerializer.SerializeToElement(new { type = "object", properties = new { } });
    public Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken ct) =>
        Task.FromResult(ToolResult.Fail(Name, "Prometheus is unavailable", 50));
}
