using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using SWEN3.Sprint5.Consuming;
using SWEN3.Sprint5.Internal;
using SWEN3.Sprint5.Models;
using SWEN3.Sprint5.Publishing;
using SWEN3.Sprint5.Sse;

namespace SWEN3.Sprint5;

/// <summary>
///     <para>Use <see cref="PublishingExtensions.PublishOcrCommandAsync{T}" /> to publish OCR commands.</para>
///     <para>Use <see cref="PublishingExtensions.PublishOcrEventAsync{T}" /> to publish OCR events.</para>
///     <para>Use <see cref="GenAIPublishingExtensions.PublishGenAIEventAsync{T}" /> to publish GenAI events.</para>
///     <para>Use <see cref="IRabbitMqConsumerFactory.CreateConsumerAsync{T}" /> to create message consumers.</para>
///     <para>Use <see cref="PaperlessEndpointExtensions.MapOcrEventStream" /> to map SSE endpoints.</para>
/// </summary>
public static class RabbitMqExtensions
{
    private const string DefaultConfigSection = "RabbitMQ:Uri";

    /// <summary>
    ///     Adds RabbitMQ messaging services to the service collection.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configuration">The application configuration containing RabbitMQ settings.</param>
    /// <param name="includeOcrResultStream">Whether to include Server-Sent Events streaming for OCR results.</param>
    /// <param name="includeGenAiResultStream">Whether to include Server-Sent Events streaming for GenAI results.</param>
    /// <returns>The service collection for method chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when RabbitMQ URI is not configured.</exception>
    /// <example>
    ///     Basic usage (no SSE streaming):
    ///     <code>builder.Services.AddPaperlessRabbitMq(builder.Configuration);</code>
    ///     <para />
    ///     With SSE streaming enabled:
    ///     <code>services.AddPaperlessRabbitMq(config, includeOcrResultStream: true, includeGenAiResultStream: true);</code>
    /// </example>
    public static IServiceCollection AddPaperlessRabbitMq(this IServiceCollection services,
        IConfiguration configuration, bool includeOcrResultStream = false, bool includeGenAiResultStream = false)
    {
        var uri = configuration[DefaultConfigSection] ??
                  throw new InvalidOperationException($"Configuration value '{DefaultConfigSection}' is missing");

        var factory = new ConnectionFactory { Uri = new Uri(uri) };
        var connection = factory.CreateConnectionAsync().GetAwaiter().GetResult();

        services.AddSingleton(connection);
        services.AddSingleton<IRabbitMqPublisher, RabbitMqPublisher>();
        services.AddSingleton<IRabbitMqConsumerFactory, RabbitMqConsumerFactory>();
        services.AddHostedService<RabbitMqTopologySetup>();

        if (includeOcrResultStream) services.AddSseStream<OcrEvent>();
        if (includeGenAiResultStream) services.AddSseStream<GenAIEvent>();

        return services;
    }
}