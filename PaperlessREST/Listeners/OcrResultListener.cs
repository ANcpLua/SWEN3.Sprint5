using PaperlessREST.BL;
using SWEN3.Sprint5.Consuming;
using SWEN3.Sprint5.Models;
using SWEN3.Sprint5.Sse;

namespace PaperlessREST.Listeners;

public class OcrResultListener : BackgroundService
{
    private readonly IRabbitMqConsumerFactory _consumerFactory;
    private readonly IDocumentService _documentService;
    private readonly ILogger<OcrResultListener> _logger;
    private readonly ISseStream<OcrEvent> _stream;

    public OcrResultListener(IRabbitMqConsumerFactory consumerFactory, IDocumentService documentService,
        ISseStream<OcrEvent> stream, ILogger<OcrResultListener> logger)
    {
        _consumerFactory = consumerFactory;
        _documentService = documentService;
        _stream = stream;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OCR Result Listener started");

        await using var consumer = await _consumerFactory.CreateConsumerAsync<OcrEvent>();

        await foreach (var result in consumer.ConsumeAsync(stoppingToken))
            await ProcessMessage(result, consumer, stoppingToken);

        _logger.LogInformation("OCR Result Listener stopped");
    }

    private async Task ProcessMessage(OcrEvent result, IRabbitMqConsumer<OcrEvent> consumer,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Received OCR result for job {JobId} with status {Status}", result.JobId,
                result.Status);

            var content = result.Status is "Completed" ? result.Text : null;
            var processed = await _documentService.ProcessOcrResultAsync(result.JobId, result.Status, content,
                result.ProcessedAt, cancellationToken);

            if (!processed)
            {
                await consumer.NackAsync(false);
                return;
            }

            _stream.Publish(result);

            await consumer.AckAsync();
            _logger.LogInformation("Successfully processed OCR result for job {JobId}", result.JobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing OCR result for job {JobId}", result.JobId);
            await consumer.NackAsync();
        }
    }
}