using PaperlessREST.BL;
using RabbitMQ.Client.Exceptions;
using SWEN3.Sprint5.Consuming;
using SWEN3.Sprint5.Models;
using SWEN3.Sprint5.Sse;

namespace PaperlessREST.Listeners;

public class GenAIResultListener : BackgroundService
{
    private readonly IRabbitMqConsumerFactory _consumerFactory;
    private readonly IDocumentService _documentService;
    private readonly ILogger<GenAIResultListener> _logger;
    private readonly ISseStream<GenAIEvent> _sseStream;

    public GenAIResultListener(IRabbitMqConsumerFactory consumerFactory, IDocumentService documentService,
        ISseStream<GenAIEvent> sseStream, ILogger<GenAIResultListener> logger)
    {
        _consumerFactory = consumerFactory;
        _documentService = documentService;
        _sseStream = sseStream;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GenAI Result Listener started");

        try
        {
            await using var consumer = await _consumerFactory.CreateConsumerAsync<GenAIEvent>();

            await foreach (var genAiEvent in consumer.ConsumeAsync(stoppingToken))
            {
                if (stoppingToken.IsCancellationRequested)
                    break;

                await ProcessGenAIEventAsync(genAiEvent, consumer, stoppingToken);
            }
        }
        catch (OperationInterruptedException ex) when (ex.Message.Contains("no queue"))
        {
            _logger.LogWarning("GenAI Result Listener disabled - GenAIEvent queue not configured in RabbitMQ. " +
                               "Update SWEN3.Paperless.RabbitMq library to include GenAIEvent queue support.");

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected when shutting down
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in GenAI Result Listener");
            throw;
        }

        _logger.LogInformation("GenAI Result Listener stopped");
    }

    private async Task ProcessGenAIEventAsync(GenAIEvent genAiEvent, IRabbitMqConsumer<GenAIEvent> consumer,
        CancellationToken cancellationToken)
    {
        try
        {
            // Check if this is a success or failure event
            if (!string.IsNullOrWhiteSpace(genAiEvent.Summary))
            {
                _logger.LogInformation("Received GenAI summary for document {DocumentId}", genAiEvent.DocumentId);

                // Update document with summary
                var updated = await _documentService.UpdateDocumentSummaryAsync(genAiEvent.DocumentId,
                    genAiEvent.Summary, genAiEvent.GeneratedAt, cancellationToken);

                if (!updated)
                {
                    _logger.LogWarning("Failed to update document {DocumentId} with GenAI summary - document not found",
                        genAiEvent.DocumentId);
                    await consumer.AckAsync(); // Don't requeue if document doesn't exist
                    return;
                }

                _logger.LogInformation("Successfully updated document {DocumentId} with GenAI summary",
                    genAiEvent.DocumentId);

                // Publish to SSE stream for real-time updates
                _sseStream.Publish(genAiEvent);
            }
            else
            {
                // GenAI processing failed
                _logger.LogWarning("GenAI failed for document {DocumentId}: {Error}", genAiEvent.DocumentId,
                    genAiEvent.ErrorMessage ?? "Unknown error");

                // Still publish to SSE stream so UI knows about the failure
                _sseStream.Publish(genAiEvent);
            }

            await consumer.AckAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing GenAI result for document {DocumentId}", genAiEvent.DocumentId);
            await consumer.NackAsync();
        }
    }
}