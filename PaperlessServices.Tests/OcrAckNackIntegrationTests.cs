using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using SWEN3.Sprint5;
using SWEN3.Sprint5.Consuming;
using SWEN3.Sprint5.Models;
using SWEN3.Sprint5.Publishing;
using Testcontainers.RabbitMq;

namespace PaperlessServices.Tests;

[TestFixture]
[Category("Integration")]
public class OcrAckNackIntegrationTests
{
    [OneTimeSetUp]
    public async Task OneTimeSetup()
    {
        _rabbit = new RabbitMqBuilder().Build();
        await _rabbit.StartAsync(CancellationToken.None);
    }

    [OneTimeTearDown]
    public async Task OneTimeTeardown()
    {
        await _rabbit.DisposeAsync();
    }

    private RabbitMqContainer _rabbit = null!;

    private static async Task<IHost> BuildHostAsync(string uri, CancellationToken cancellationToken)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["RabbitMQ:Uri"] = uri;
        builder.Services.AddPaperlessRabbitMq(builder.Configuration);
        builder.Logging.ClearProviders().AddConsole();
        var host = builder.Build();
        await host.StartAsync(cancellationToken);
        return host;
    }

    private static async Task<bool> CheckForMessageAsync<T>(IRabbitMqConsumer<T> consumer,
        CancellationToken cancellationToken) where T : class
    {
        try
        {
            await foreach (var _ in consumer.ConsumeAsync(cancellationToken)) return true;
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    [Test]
    public async Task Ack_Removes_Message_From_Queue()
    {
        var uri = _rabbit.GetConnectionString();
        using var hostCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var host = await BuildHostAsync(uri, hostCts.Token);
        var publisher = host.Services.GetRequiredService<IRabbitMqPublisher>();
        var factory = host.Services.GetRequiredService<IRabbitMqConsumerFactory>();
        var cmd = new OcrCommand(Guid.NewGuid(), "ack.pdf", "/path/ack.pdf");
        await publisher.PublishOcrCommandAsync(cmd);
        await using var consumer = await factory.CreateConsumerAsync<OcrCommand>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await foreach (var received in consumer.ConsumeAsync(cts.Token))
        {
            Assert.That(received.JobId, Is.EqualTo(cmd.JobId));
            await consumer.AckAsync();
            break;
        }

        using var shortCts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var messageReceived = await CheckForMessageAsync(consumer, shortCts.Token);
        Assert.That(messageReceived, Is.False, "No redelivery expected after Ack");
    }

    [Test]
    public async Task Nack_Requeues_Message()
    {
        var uri = _rabbit.GetConnectionString();
        using var hostCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var host = await BuildHostAsync(uri, hostCts.Token);
        var publisher = host.Services.GetRequiredService<IRabbitMqPublisher>();
        var factory = host.Services.GetRequiredService<IRabbitMqConsumerFactory>();
        var cmd = new OcrCommand(Guid.NewGuid(), "nack.pdf", "/path/nack.pdf");
        await publisher.PublishOcrCommandAsync(cmd);
        OcrCommand? first = null;
        await using (var consumer = await factory.CreateConsumerAsync<OcrCommand>())
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await foreach (var received in consumer.ConsumeAsync(cts.Token))
            {
                first = received;
                await consumer.NackAsync();
                break;
            }
        }

        Assert.That(first, Is.Not.Null);
        using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var consumer2 = await factory.CreateConsumerAsync<OcrCommand>();
        OcrCommand? second = null;
        await foreach (var received in consumer2.ConsumeAsync(cts2.Token))
        {
            second = received;
            await consumer2.AckAsync();
            break;
        }

        Assert.That(second, Is.Not.Null);
        Assert.That(second!.JobId, Is.EqualTo(first!.JobId));
    }
}