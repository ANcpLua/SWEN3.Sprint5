using SWEN3.Sprint5.Models;
using SWEN3.Sprint5.Publishing;
using SWEN3.Sprint5.Schema;

namespace SWEN3.Sprint5.Tests.Integration;

public class GenAIIntegrationTests : IAsyncLifetime
{
    private readonly RabbitMqContainer _container = new RabbitMqBuilder().Build();
    private IHost _host = null!;
    private IServiceProvider _serviceProvider = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        var hostBuilder = Host.CreateDefaultBuilder().ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMQ:Uri"] = _container.GetConnectionString()
            });
        }).ConfigureServices((context, services) =>
        {
            services.AddPaperlessRabbitMq(context.Configuration);
            services.AddLogging();
        });
        _host = hostBuilder.Build();
        _serviceProvider = _host.Services;
        await _host.StartAsync();
        await Task.Delay(TimeSpan.FromSeconds(2));
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task GenAIEvent_ShouldPublishAndConsume_Successfully()
    {
        var publisher = _serviceProvider.GetRequiredService<IRabbitMqPublisher>();
        var consumerFactory = _serviceProvider.GetRequiredService<IRabbitMqConsumerFactory>();
        var testEvent = new GenAIEvent(Guid.NewGuid(), "This is a test summary for integration testing",
            DateTimeOffset.UtcNow);
        await publisher.PublishGenAIEventAsync(testEvent);
        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
        GenAIEvent? receivedEvent = null;
        await using var consumer = await consumerFactory.CreateConsumerAsync<GenAIEvent>();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var message in consumer.ConsumeAsync(cts.Token))
        {
            receivedEvent = message;
            await consumer.AckAsync();
            break;
        }

        receivedEvent.Should().NotBeNull();
        receivedEvent.DocumentId.Should().Be(testEvent.DocumentId);
        receivedEvent.Summary.Should().Be(testEvent.Summary);
        receivedEvent.GeneratedAt.Should().BeCloseTo(testEvent.GeneratedAt, TimeSpan.FromSeconds(1));
        receivedEvent.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task GenAIEvent_WithError_ShouldPublishAndConsume_Successfully()
    {
        var publisher = _serviceProvider.GetRequiredService<IRabbitMqPublisher>();
        var consumerFactory = _serviceProvider.GetRequiredService<IRabbitMqConsumerFactory>();
        var testEvent = new GenAIEvent(Guid.NewGuid(), string.Empty, DateTimeOffset.UtcNow,
            "Failed to generate summary: API rate limit exceeded");
        await publisher.PublishGenAIEventAsync(testEvent);
        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
        GenAIEvent? receivedEvent = null;
        await using var consumer = await consumerFactory.CreateConsumerAsync<GenAIEvent>();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var message in consumer.ConsumeAsync(cts.Token))
        {
            receivedEvent = message;
            await consumer.AckAsync();
            break;
        }

        receivedEvent.Should().NotBeNull();
        receivedEvent.DocumentId.Should().Be(testEvent.DocumentId);
        receivedEvent.Summary.Should().BeEmpty();
        receivedEvent.ErrorMessage.Should().Be(testEvent.ErrorMessage);
    }

    [Fact]
    public async Task GenAIEvent_MultipleEvents_ShouldProcessInOrder()
    {
        var publisher = _serviceProvider.GetRequiredService<IRabbitMqPublisher>();
        var consumerFactory = _serviceProvider.GetRequiredService<IRabbitMqConsumerFactory>();
        var events = Enumerable.Range(1, 3).Select(i => new GenAIEvent(Guid.NewGuid(),
            $"Summary {i}", DateTimeOffset.UtcNow.AddMinutes(i))).ToList();
        await using var consumer = await consumerFactory.CreateConsumerAsync<GenAIEvent>();
        foreach (var evt in events) await publisher.PublishGenAIEventAsync(evt);
        var receivedEvents = new List<GenAIEvent>();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        await foreach (var message in consumer.ConsumeAsync(cts.Token))
        {
            receivedEvents.Add(message);
            await consumer.AckAsync();
            if (receivedEvents.Count >= 3)
                break;
        }

        receivedEvents.Should().HaveCount(3);
        receivedEvents.Should()
            .BeEquivalentTo(events, options => options.WithStrictOrdering().ComparingByMembers<GenAIEvent>());
    }

    [Fact]
    public async Task GenAIQueue_ShouldExistInTopology()
    {
        var connection = _serviceProvider.GetRequiredService<IConnection>();
        await using var channel =
            await connection.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
        var result =
            await channel.QueueDeclarePassiveAsync(RabbitMqSchema.GenAIEventQueue,
                TestContext.Current.CancellationToken);
        result.Should().NotBeNull();
        result.QueueName.Should().Be(RabbitMqSchema.GenAIEventQueue);
    }

    [Fact]
    public async Task GenAIEvent_Nack_ShouldRequeue()
    {
        var publisher = _serviceProvider.GetRequiredService<IRabbitMqPublisher>();
        var consumerFactory = _serviceProvider.GetRequiredService<IRabbitMqConsumerFactory>();
        var testEvent = new GenAIEvent(Guid.NewGuid(), "Nack test", DateTimeOffset.UtcNow);
        await publisher.PublishGenAIEventAsync(testEvent);
        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
        await using (var consumer1 = await consumerFactory.CreateConsumerAsync<GenAIEvent>())
        {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await foreach (var _ in consumer1.ConsumeAsync(cts.Token))
            {
                await consumer1.NackAsync(true);
                break;
            }
        }

        GenAIEvent? redeliveredEvent = null;
        await using (var consumer2 = await consumerFactory.CreateConsumerAsync<GenAIEvent>())
        {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await foreach (var message in consumer2.ConsumeAsync(cts.Token))
            {
                redeliveredEvent = message;
                await consumer2.AckAsync();
                break;
            }
        }

        redeliveredEvent.Should().NotBeNull();
        redeliveredEvent.DocumentId.Should().Be(testEvent.DocumentId);
        redeliveredEvent.Summary.Should().Be(testEvent.Summary);
    }
}