using SWEN3.Sprint5.Models;
using SWEN3.Sprint5.Sse;
using SWEN3.Sprint5.Tests.Helpers;

namespace SWEN3.Sprint5.Tests.Integration;

public class GenAIEventStreamIntegrationTests
{
    [Theory]
    [InlineData("Completed", "genai-completed")]
    [InlineData("Failed", "genai-failed")]
    public async Task MapGenAIEventStream_ShouldEmitCorrectEventType(string status, string expectedEventType)
    {
        var sseStream = new SseStream<GenAIEvent>();
        using var server = TestServerFactory.CreateSseTestServer(sseStream,
            endpoints => { endpoints.MapGenAIEventStream("/api/v1/events/genai"); });
        using var client = server.CreateClient();
        var genAiEvent = new GenAIEvent(Guid.NewGuid(),
            status == "Completed" ? "Test summary generated successfully" : string.Empty, DateTimeOffset.UtcNow,
            status == "Failed" ? "AI service temporarily unavailable" : null);
        var responseTask = client.GetAsync("/api/v1/events/genai", HttpCompletionOption.ResponseHeadersRead,
            TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        sseStream.Publish(genAiEvent);
        var response = await responseTask;
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.ToString().Should().Be("text/event-stream");
        await using var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        using var reader = new StreamReader(stream);
        var eventLine = await reader.ReadLineAsync(TestContext.Current.CancellationToken);
        eventLine.Should().Be($"event: {expectedEventType}");
    }

    [Fact]
    public async Task MapGenAIEventStream_WithSummary_ShouldStreamToClient()
    {
        var sseStream = new SseStream<GenAIEvent>();
        using var server = TestServerFactory.CreateSseTestServer(sseStream,
            endpoints => { endpoints.MapGenAIEventStream("/api/v1/events/genai"); });
        using var client = server.CreateClient();
        var genAiEvent = new GenAIEvent(Guid.NewGuid(),
            "This document contains important financial information including quarterly earnings, revenue projections, and market analysis for the upcoming fiscal year.",
            DateTimeOffset.UtcNow);
        var responseTask = client.GetAsync("/api/v1/events/genai", HttpCompletionOption.ResponseHeadersRead,
            TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        sseStream.Publish(genAiEvent);
        var response = await responseTask;
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        using var reader = new StreamReader(stream);
        var eventLine = await reader.ReadLineAsync(TestContext.Current.CancellationToken);
        var dataLine = await reader.ReadLineAsync(TestContext.Current.CancellationToken);
        eventLine.Should().Be("event: genai-completed");
        dataLine.Should().StartWith("data: ");
        dataLine.Should().Contain(genAiEvent.DocumentId.ToString());
        dataLine.Should().Contain("quarterly earnings");
    }

    [Fact]
    public async Task MapGenAIEventStream_WithError_ShouldStreamErrorEvent()
    {
        var sseStream = new SseStream<GenAIEvent>();
        using var server = TestServerFactory.CreateSseTestServer(sseStream,
            endpoints => { endpoints.MapGenAIEventStream("/api/v1/events/genai"); });
        using var client = server.CreateClient();
        var genAiEvent = new GenAIEvent(Guid.NewGuid(), string.Empty, DateTimeOffset.UtcNow,
            "GenAI service rate limit exceeded. Please try again later.");
        var responseTask = client.GetAsync("/api/v1/events/genai", HttpCompletionOption.ResponseHeadersRead,
            TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        sseStream.Publish(genAiEvent);
        var response = await responseTask;
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        using var reader = new StreamReader(stream);
        var eventLine = await reader.ReadLineAsync(TestContext.Current.CancellationToken);
        var dataLine = await reader.ReadLineAsync(TestContext.Current.CancellationToken);
        eventLine.Should().Be("event: genai-failed");
        dataLine.Should().StartWith("data: ");
        dataLine.Should().Contain(genAiEvent.DocumentId.ToString());
        dataLine.Should().Contain("rate limit exceeded");
    }

    [Fact]
    public async Task MapGenAIEventStream_MultipleEvents_ShouldStreamInOrder()
    {
        var sseStream = new SseStream<GenAIEvent>();
        using var server = TestServerFactory.CreateSseTestServer(sseStream,
            endpoints => { endpoints.MapGenAIEventStream("/api/v1/events/genai"); });
        using var client = server.CreateClient();
        var events = new[]
        {
            new GenAIEvent(Guid.NewGuid(), "First summary", DateTimeOffset.UtcNow),
            new GenAIEvent(Guid.NewGuid(), "Second summary", DateTimeOffset.UtcNow.AddSeconds(1)),
            new GenAIEvent(Guid.NewGuid(), string.Empty, DateTimeOffset.UtcNow.AddSeconds(2), "Error occurred")
        };
        var responseTask = client.GetAsync("/api/v1/events/genai", HttpCompletionOption.ResponseHeadersRead,
            TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        foreach (var evt in events) sseStream.Publish(evt);
        var response = await responseTask;
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        using var reader = new StreamReader(stream);
        var event1 = await reader.ReadLineAsync(TestContext.Current.CancellationToken);
        var data1 = await reader.ReadLineAsync(TestContext.Current.CancellationToken);
        await reader.ReadLineAsync(TestContext.Current.CancellationToken);
        var event2 = await reader.ReadLineAsync(TestContext.Current.CancellationToken);
        var data2 = await reader.ReadLineAsync(TestContext.Current.CancellationToken);
        await reader.ReadLineAsync(TestContext.Current.CancellationToken);
        var event3 = await reader.ReadLineAsync(TestContext.Current.CancellationToken);
        var data3 = await reader.ReadLineAsync(TestContext.Current.CancellationToken);
        event1.Should().Be("event: genai-completed");
        event2.Should().Be("event: genai-completed");
        event3.Should().Be("event: genai-failed");
        data1.Should().Contain("First summary");
        data2.Should().Contain("Second summary");
        data3.Should().Contain("Error occurred");
    }
}