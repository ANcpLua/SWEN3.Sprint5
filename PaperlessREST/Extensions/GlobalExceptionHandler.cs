using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace PaperlessREST.Extensions;

public sealed class TimeOffsetOptions
{
    public TimeSpan Offset { get; init; } = TimeSpan.Zero;
}

internal readonly record struct ExceptionInfo(int StatusCode, LogLevel Level, string ErrorCode)
{
    public static ExceptionInfo Classify(Exception ex)
    {
        return ex switch
        {
            ArgumentNullException => new ExceptionInfo(400, LogLevel.Warning, "E_ARG_NULL"),
            ArgumentException or InvalidOperationException or JsonException => new ExceptionInfo(400, LogLevel.Warning, "E_INVALID_ARG"),
            UnauthorizedAccessException => new ExceptionInfo(403, LogLevel.Warning, "E_UNAUTHORIZED"),
            KeyNotFoundException or FileNotFoundException => new ExceptionInfo(404, LogLevel.Information, "E_NOT_FOUND"),
            OperationCanceledException => new ExceptionInfo(499, LogLevel.Information, "E_CANCELLED"),
            NotImplementedException => new ExceptionInfo(501, LogLevel.Error, "E_NOT_IMPLEMENTED"),
            TimeoutException => new ExceptionInfo(504, LogLevel.Error, "E_TIMEOUT"),
            _ => new ExceptionInfo(500, LogLevel.Error, "E_INTERNAL")
        };
    }
}

public sealed class ExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<ExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception,
        CancellationToken cancellationToken)
    {
        var info = ExceptionInfo.Classify(exception);

        if (info.StatusCode is not 499 || !cancellationToken.IsCancellationRequested)
            logger.Log(info.Level, exception, "{Method} {Path} → {StatusCode} [{ErrorCode}]", context.Request.Method,
                context.Request.Path, info.StatusCode, info.ErrorCode);

        context.Response.StatusCode = info.StatusCode;

        return (info.StatusCode is 499 && cancellationToken.IsCancellationRequested) ||
               await problemDetails.TryWriteAsync(new ProblemDetailsContext
               {
                   HttpContext = context,
                   Exception = exception,
                   ProblemDetails =
                   {
                       Status = info.StatusCode,
                       Type = $"urn:app:error:{info.ErrorCode}",
                       Extensions = { ["code"] = info.ErrorCode }
                   }
               });
    }
}

public sealed class ProblemDetailsCustomization(
    IHostEnvironment env,
    IOptionsMonitor<JsonOptions> json,
    IOptions<TimeOffsetOptions> offset,
    TimeProvider time) : IConfigureOptions<ProblemDetailsOptions>
{
    public void Configure(ProblemDetailsOptions options)
    {
        options.CustomizeProblemDetails =
            ctx => Enrich(ctx, env.IsDevelopment(), json.CurrentValue, offset.Value, time);
    }

    private static void Enrich(ProblemDetailsContext ctx, bool isDev, JsonOptions jsonOpts, TimeOffsetOptions timeOpts,
        TimeProvider time)
    {
        var (pd, http, ex) = (ctx.ProblemDetails, ctx.HttpContext, ctx.Exception);

        pd.Instance ??= $"{http.Request.Method} {http.Request.Path}";

        var now = time.GetUtcNow();
        var activity = Activity.Current ?? http.Features.Get<IHttpActivityFeature>()?.Activity;
        var endpoint = http.GetEndpoint();

        pd.Extensions["timestamp"] = now.ToString("o");
        pd.Extensions["requestId"] = http.TraceIdentifier;

        if (timeOpts.Offset != TimeSpan.Zero)
            pd.Extensions["timestampLocal"] = now.ToOffset(timeOpts.Offset).ToString("o");

        if (activity?.TraceId.ToString() is { } traceId)
            pd.Extensions["traceId"] = traceId;

        if (endpoint?.DisplayName is { } name)
            pd.Extensions["endpoint"] = name;

        if (endpoint is RouteEndpoint route)
            pd.Extensions["route"] = route.RoutePattern.RawText;

        if (pd is HttpValidationProblemDetails validation)
            TransformValidation(validation, jsonOpts.SerializerOptions.PropertyNamingPolicy);

        if (ex is not null)
            pd.Detail = isDev ? pd.Detail ?? ex.Message :
                pd.Status >= 500 ? "An error occurred while processing your request." : ex.Message;

        if (isDev && ex is not null)
            pd.Extensions["exception"] = new
            {
                type = ex.GetType().Name,
                message = ex.Message,
                stackTrace = ex.StackTrace?.Split('\n',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                inner = ex.InnerException?.Message
            };
    }

    private static void TransformValidation(HttpValidationProblemDetails validation, JsonNamingPolicy? policy)
    {
        var count = validation.Errors.Values.Sum(x => x.Length);
        validation.Detail = $"Validation failed with {count} error(s).";

        if (policy is null) return;

        var entries = validation.Errors.ToArray();
        validation.Errors.Clear();
        foreach (var (key, value) in entries)
            validation.Errors[policy.ConvertName(key)] = value;
    }
}