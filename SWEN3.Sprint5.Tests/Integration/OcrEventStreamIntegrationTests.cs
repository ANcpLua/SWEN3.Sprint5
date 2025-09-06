using SWEN3.Sprint5.Models;
using SWEN3.Sprint5.Sse;
using SWEN3.Sprint5.Tests.Helpers;

namespace SWEN3.Sprint5.Tests.Integration;

public class OcrEventStreamIntegrationTests
{
    [Theory]
    [InlineData("Completed", "ocr-completed")]
    [InlineData("Failed", "ocr-failed")]
    [InlineData("Processing", "ocr-failed")]
    public async Task MapOcrEventStream_ShouldEmitCorrectEventType(string status, string expectedEventType)
    {
        var sseStream = new SseStream<OcrEvent>();
        var ocrEvent = new OcrEvent(Guid.NewGuid(), status, status is "Completed" ? "Text" : null,
            DateTimeOffset.UtcNow);
        using var server = TestServerFactory.CreateSseTestServer(sseStream, endpoints => endpoints.MapOcrEventStream());
        using var client = server.CreateClient();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var responseTask = client.GetAsync("/api/v1/ocr-results", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        await Task.Delay(100, cts.Token);
        sseStream.Publish(ocrEvent);

        using var response = await responseTask;
        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var eventLine = await reader.ReadLineAsync(cts.Token);
        eventLine.Should().Be($"event: {expectedEventType}");
    }
}