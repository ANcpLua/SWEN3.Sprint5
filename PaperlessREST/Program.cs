using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mapster;
using Microsoft.AspNetCore.Mvc;
using PaperlessREST;
using PaperlessREST.Extensions;
using PaperlessREST.Validation;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddDependencies();

var app = builder.Build();
TypeAdapterConfig.GlobalSettings.Scan(Assembly.GetExecutingAssembly());

await app.InitializeApplicationAsync();

app.ConfigureMiddleware(app.Environment);
app.MapEndpoints();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // Scalar UI (no Swagger)
    app.MapScalarApiReference("/docs", options =>
    {
        options.Title = "Paperless OCR API";
        options.Servers = [new ScalarServer("http://localhost:8080/")];
        options.WithTheme(ScalarTheme.Kepler);
    });
}

await app.RunAsync();

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(Document))]
[JsonSerializable(typeof(DocumentDto))]
[JsonSerializable(typeof(CreateDocumentResponse))]
[JsonSerializable(typeof(SearchQuery))]
[JsonSerializable(typeof(UploadDocumentRequest))]
[JsonSerializable(typeof(List<DocumentDto>))]
[JsonSerializable(typeof(List<object>))]
[JsonSerializable(typeof(HttpValidationProblemDetails))]
[JsonSerializable(typeof(ProblemDetails))]
public partial class AppJsonSerializerContext : JsonSerializerContext;