using System.Text.Json.Serialization;
using Asp.Versioning;
using Elastic.Clients.Elasticsearch;
using FluentValidation;
using Mapster;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Minio;
using PaperlessREST.BL;
using PaperlessREST.DAL;
using PaperlessREST.Listeners;
using SWEN3.Sprint5;

namespace PaperlessREST.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPaperlessServices(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContextFactory<DocumentPersistence>((_, opts) =>
        {
            opts.UseNpgsql(config.GetConnectionString("PaperlessDb"), o => o.MapEnum<DocumentStatus>());
        });

        services.AddSingleton<IDocumentRepository, DocumentRepository>()
            .AddSingleton<IDocumentService, DocumentService>();
        services.AddOptionsWithValidateOnStart<MinioOptions>().BindConfiguration("Storage:Minio")
            .ValidateDataAnnotations();

        services.AddSingleton<IMinioClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<MinioOptions>>().Value;
            // Remove http:// or https:// from endpoint if present
            var endpoint = opts.Endpoint;
            if (endpoint.StartsWith("http://"))
                endpoint = endpoint[7..];
            else if (endpoint.StartsWith("https://"))
                endpoint = endpoint[8..];

            return new MinioClient().WithEndpoint(endpoint).WithCredentials(opts.AccessKey, opts.SecretKey)
                .WithSSL(opts.UseSsl).Build();
        });

        services.AddSingleton(new ElasticsearchClient(
            new ElasticsearchClientSettings(new Uri(config["Elasticsearch:Uri"]!))
                .DefaultIndex(config["Elasticsearch:IndexName"]!).ThrowExceptions()));

        var rabbitEnabledSetting = config["RabbitMQ:Enabled"];
        var rabbitEnabled = !string.Equals(rabbitEnabledSetting, "false", StringComparison.OrdinalIgnoreCase);

        services.AddPaperlessRabbitMq(config, includeOcrResultStream: rabbitEnabled, includeGenAiResultStream: rabbitEnabled);

        services.AddSingleton<IDocumentStorageService, DocumentStorageService>()
            .AddSingleton<IDocumentSearchService, DocumentSearchService>();

        if (rabbitEnabled)
        {
            services.AddHostedService<GenAIResultListener>();
            services.AddHostedService<OcrResultListener>();
        }

        services.AddValidatorsFromAssemblyContaining<Program>(ServiceLifetime.Singleton);

        services.AddOpenApi(o =>
        {
            o.CreateSchemaReferenceId = t => t.Type.IsEnum ? null : OpenApiOptions.CreateDefaultSchemaReferenceId(t);

            o.AddDocumentTransformer((doc, _, _) =>
            {
                doc.Info = new OpenApiInfo
                {
                    Title = "Paperless OCR API",
                    Version = "v1",
                    Description = "API for uploading and processing PDF documents with OCR"
                };
                return Task.CompletedTask;
            });
        });

        services.AddApiVersioning(v =>
        {
            v.DefaultApiVersion = new ApiVersion(1, 0);
            v.AssumeDefaultVersionWhenUnspecified = true;
            v.ReportApiVersions = true;
        }).AddApiExplorer(opts =>
        {
            opts.GroupNameFormat = "'v'VVV";
            opts.SubstituteApiVersionInUrl = true;
        });

        return services;
    }
}

public static class ValidationHandlerExtensions
{
    public static IServiceCollection AddOptimizedErrorHandling(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddExceptionHandler<ExceptionHandler>();
        services.AddProblemDetails();
        services.ConfigureOptions<ProblemDetailsCustomization>();
        services.Configure<TimeOffsetOptions>(_ => { });
        return services;
    }
}

public static class ApplicationBuilderExtensions
{
    public static IApplicationBuilder ConfigureMiddleware(this IApplicationBuilder app, IWebHostEnvironment env)
    {
        app.UseStaticFiles();
        app.UseExceptionHandler();
        
        if (env.IsDevelopment())
        {
            app.UseStatusCodePages();
        }
        
        app.UseHttpLogging();
        return app;
    }
}

public static class DependenciesConfig
{
    public static void AddDependencies(this WebApplicationBuilder builder)
    {
        builder.Services.AddHttpLogging(o =>
        {
            o.LoggingFields = HttpLoggingFields.RequestProperties | HttpLoggingFields.RequestHeaders;
        });
        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            o.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
            o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
        });
        builder.Services.Configure<TimeOffsetOptions>(builder.Configuration.GetSection("TimeOffset"));
        builder.Services.AddMapster();
        builder.Services.AddPaperlessServices(builder.Configuration);
        builder.Services.AddOptimizedErrorHandling();
    }
}