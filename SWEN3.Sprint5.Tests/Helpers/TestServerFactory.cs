using SWEN3.Sprint5.Sse;

namespace SWEN3.Sprint5.Tests.Helpers;

internal static class TestServerFactory
{
    public static TestServer CreateSseTestServer<T>(ISseStream<T> sseStream,
        Action<IEndpointRouteBuilder> configureEndpoints) where T : class
    {
        var hostBuilder = Host.CreateDefaultBuilder().ConfigureWebHostDefaults(webBuilder =>
        {
            webBuilder.UseTestServer().ConfigureServices(services =>
            {
                services.AddSingleton<ISseStream<T>>(sseStream);
                services.AddRouting();
            }).Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(configureEndpoints);
            });
        });
        var host = hostBuilder.Build();
        host.StartAsync().GetAwaiter().GetResult();
        return host.GetTestServer();
    }

    public static TestServer CreateSseTestServerWithTestEndpoint(ISseStream<Messages.SseTestEvent> sseStream)
    {
        return CreateSseTestServer(sseStream,
            endpoints =>
            {
                endpoints.MapSse<Messages.SseTestEvent>("/sse-test", evt => new { evt.Id, evt.Message },
                    _ => "test-event");
            });
    }
}