using System.ComponentModel.DataAnnotations;
using CreatePdf.NET;
using Elastic.Clients.Elasticsearch;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using PaperlessServices;
using SWEN3.Sprint5;
using SWEN3.Sprint5.Consuming;
using SWEN3.Sprint5.GenAI;
using SWEN3.Sprint5.Models;
using SWEN3.Sprint5.Publishing;
using SWEN3.Sprint5.Schema;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddOcrServices(builder.Configuration);
builder.Services.AddGenAIServices(builder.Configuration);

var host = builder.Build();

await host.RunAsync();

namespace PaperlessServices
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddOcrServices(this IServiceCollection services,
            ConfigurationManager configuration)
        {
            services.AddPaperlessRabbitMq(configuration).AddHostedService<OcrWorker>()
                .AddOptionsWithValidateOnStart<MinioOptions>().BindConfiguration("Storage:Minio")
                .ValidateDataAnnotations().Services.AddSingleton<IMinioClient>(sp =>
                {
                    var options = sp.GetRequiredService<IOptions<MinioOptions>>().Value;
                    // Remove http:// or https:// from endpoint if present
                    var endpoint = options.Endpoint;
                    if (endpoint.StartsWith("http://"))
                        endpoint = endpoint[7..];
                    else if (endpoint.StartsWith("https://"))
                        endpoint = endpoint[8..];

                    return new MinioClient().WithEndpoint(endpoint)
                        .WithCredentials(options.AccessKey, options.SecretKey).WithSSL(options.UseSsl).Build();
                }).AddSingleton(new ElasticsearchClient(
                    new ElasticsearchClientSettings(new Uri(configuration["Elasticsearch:Uri"]!)).DefaultIndex(
                        configuration["Elasticsearch:IndexName"]!))).AddSingleton<IStorageService, StorageService>()
                .AddSingleton<ISearchIndexService, SearchIndexService>().AddSingleton<IOcrService, OcrService>()
                .AddSingleton<IOcrProcessor, OcrProcessor>();

            return services;
        }

        public static IServiceCollection AddGenAIServices(this IServiceCollection services,
            ConfigurationManager configuration)
        {
            services.AddPaperlessRabbitMq(configuration);
            services.AddOptionsWithValidateOnStart<GeminiOptions>().BindConfiguration("GenAI:Gemini")
                .ValidateDataAnnotations();

            services.AddHttpClient<ITextSummarizer, GeminiService>();
            services.AddHostedService<GenAIWorker>();

            return services;
        }
    }

    public interface IStorageService
    {
        Task<Stream> DownloadAsync(string filePath, CancellationToken cancellationToken = default);
    }

    public class StorageService : IStorageService
    {
        private readonly ILogger<StorageService> _logger;
        private readonly IMinioClient _minio;
        private readonly IOptions<MinioOptions> _options;

        public StorageService(IMinioClient minio, IOptions<MinioOptions> options, ILogger<StorageService> logger)
        {
            _minio = minio;
            _options = options;
            _logger = logger;
        }

        public async Task<Stream> DownloadAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var stream = new MemoryStream();
            await _minio.GetObjectAsync(
                new GetObjectArgs().WithBucket(_options.Value.BucketName).WithObject(filePath)
                    .WithCallbackStream(async (s, ct) => await s.CopyToAsync(stream, ct)), cancellationToken);

            stream.Position = 0;
            _logger.LogInformation("Downloaded file from storage: {FilePath}", filePath);
            return stream;
        }
    }

    public interface ISearchIndexService
    {
        Task IndexDocumentAsync(Guid id, string fileName, string content, string storagePath,
            CancellationToken cancellationToken = default);
    }

    public class SearchIndexService : ISearchIndexService
    {
        private readonly ElasticsearchClient _elastic;
        private readonly ILogger<SearchIndexService> _logger;

        public SearchIndexService(ElasticsearchClient elastic, ILogger<SearchIndexService> logger)
        {
            _elastic = elastic;
            _logger = logger;
        }

        public async Task IndexDocumentAsync(Guid id, string fileName, string content, string storagePath,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await InitializeAsync(cancellationToken);

                var response = await _elastic.IndexAsync(new
                {
                    id, fileName, content, status = "Completed", processedAt = DateTimeOffset.UtcNow, storagePath,
                    createdAt = DateTimeOffset.UtcNow
                }, i => i.Id(id.ToString()).Refresh(Refresh.True), cancellationToken);

                if (!response.IsValidResponse)
                    _logger.LogWarning("Elasticsearch indexing reported invalid response for {DocumentId}: {Reason}",
                        id, response.ElasticsearchServerError?.Error.Reason);
                else
                    _logger.LogInformation("Indexed document {DocumentId} in search index", id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to index document {DocumentId} in Elasticsearch. Proceeding without search indexing.", id);
            }
        }

        private async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            var indexName = _elastic.ElasticsearchClientSettings.DefaultIndex;
            if (string.IsNullOrWhiteSpace(indexName))
            {
                _logger.LogWarning("Elasticsearch DefaultIndex is not configured; skipping index initialization.");
                return;
            }

            var exists = false;
            try
            {
                var existsResponse = await _elastic.Indices.ExistsAsync(indexName, cancellationToken);
                exists = existsResponse.Exists;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to check if Elasticsearch index '{IndexName}' exists. Will attempt to create it.",
                    indexName);
            }

            if (exists)
                return;

            try
            {
                var createResponse = await _elastic.Indices.CreateAsync(indexName,
                    c => c.Mappings(m => m.Properties<object>(p =>
                        p.Keyword("id").Text("fileName").Text("content").Keyword("status").Date("processedAt")
                            .Keyword("storagePath"))), cancellationToken);

                if (!createResponse.IsValidResponse)
                    _logger.LogWarning("Failed to create Elasticsearch index '{IndexName}': {Reason}", indexName,
                        createResponse.ElasticsearchServerError?.Error.Reason);
                else
                    _logger.LogInformation("Created Elasticsearch index: {IndexName}", indexName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Exception while creating Elasticsearch index '{IndexName}'.", indexName);
            }
        }
    }

    public interface IOcrService
    {
        Task<string> ExtractTextAsync(Stream pdfStream);
    }

    public class OcrService : IOcrService
    {
        private readonly ILogger<OcrService> _logger;

        public OcrService(ILogger<OcrService> logger)
        {
            _logger = logger;
        }

        public async Task<string> ExtractTextAsync(Stream pdfStream)
        {
            var text = await Pdf.Load(pdfStream).OcrAsync();
            _logger.LogInformation("Extracted {CharCount} characters from PDF", text.Length);
            return text;
        }
    }

    public interface IOcrProcessor
    {
        Task<OcrEvent> ProcessDocumentAsync(OcrCommand command, CancellationToken cancellationToken = default);
    }

    public class OcrProcessor : IOcrProcessor
    {
        private readonly ILogger<OcrProcessor> _logger;
        private readonly IOcrService _ocrService;
        private readonly ISearchIndexService _searchService;
        private readonly IStorageService _storageService;

        public OcrProcessor(IStorageService storageService, IOcrService ocrService, ISearchIndexService searchService,
            ILogger<OcrProcessor> logger)
        {
            _storageService = storageService;
            _ocrService = ocrService;
            _searchService = searchService;
            _logger = logger;
        }

        public async Task<OcrEvent> ProcessDocumentAsync(OcrCommand command,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Download PDF
                await using var stream = await _storageService.DownloadAsync(command.FilePath, cancellationToken);

                // Extract text
                var text = await _ocrService.ExtractTextAsync(stream);

                // Index document - errors are logged inside the service
                await _searchService.IndexDocumentAsync(command.JobId, command.FileName, text, command.FilePath,
                    cancellationToken);

                _logger.LogInformation("Successfully processed OCR job {JobId}", command.JobId);
                return new OcrEvent(command.JobId, "Completed", text, DateTimeOffset.UtcNow);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process OCR job {JobId}", command.JobId);
                return new OcrEvent(command.JobId, "Failed", null, DateTimeOffset.UtcNow);
            }
        }
    }

    public class OcrWorker : BackgroundService
    {
        private readonly IRabbitMqConsumerFactory _consumerFactory;
        private readonly ILogger<OcrWorker> _logger;
        private readonly IOcrProcessor _processor;
        private readonly IRabbitMqPublisher _publisher;

        public OcrWorker(IRabbitMqConsumerFactory consumerFactory, IOcrProcessor processorFactory,
            IRabbitMqPublisher publisher, ILogger<OcrWorker> logger)
        {
            _consumerFactory = consumerFactory;
            _processor = processorFactory;
            _publisher = publisher;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await using var consumer = await _consumerFactory.CreateConsumerAsync<OcrCommand>();

            await foreach (var request in consumer.ConsumeAsync(stoppingToken))
                await ProcessMessage(request, consumer, stoppingToken);
        }

        private async Task ProcessMessage(OcrCommand request, IRabbitMqConsumer<OcrCommand> consumer,
            CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Processing OCR job {JobId} for file {FileName}", request.JobId,
                    request.FileName);

                var result = await _processor.ProcessDocumentAsync(request, cancellationToken);
                await _publisher.PublishOcrEventAsync(result);

                // If OCR was successful and we have text, publish GenAI command
                if (result.Status == "Completed" && !string.IsNullOrWhiteSpace(result.Text))
                {
                    var genAiCommand = new GenAICommand(request.JobId, result.Text!, request.FileName);
                    await _publisher.PublishAsync(RabbitMqSchema.GenAICommandRouting, genAiCommand);
                    _logger.LogInformation("Published GenAI command for document {DocumentId}", request.JobId);
                }

                await consumer.AckAsync();

                _logger.LogInformation("Published OCR result for job {JobId} with status {Status}", request.JobId,
                    result.Status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish OCR result for job {JobId}", request.JobId);
                await consumer.NackAsync();
            }
        }
    }

    public class MinioOptions
    {
        [Required(ErrorMessage = "MinIO endpoint is required")]
        public string Endpoint { get; set; } = null!;

        [Required(ErrorMessage = "MinIO access key is required")]
        public string AccessKey { get; set; } = null!;

        [Required(ErrorMessage = "MinIO secret key is required")]
        public string SecretKey { get; set; } = null!;

        [Required(ErrorMessage = "MinIO bucket name is required")]
        public string BucketName { get; set; } = null!;

        public bool UseSsl { get; set; } = false;
    }
}