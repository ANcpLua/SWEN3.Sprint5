using System.Text.Json;
using RabbitMQ.Client;
using SWEN3.Sprint5.Schema;

namespace SWEN3.Sprint5.Publishing;

public class RabbitMqPublisher : IRabbitMqPublisher
{
    private readonly IConnection _connection;

    public RabbitMqPublisher(IConnection connection)
    {
        _connection = connection;
    }

    public async Task PublishAsync<T>(string routingKey, T message) where T : class
    {
        await using var channel = await _connection.CreateChannelAsync();
        await channel.BasicPublishAsync(RabbitMqSchema.Exchange, routingKey,
            JsonSerializer.SerializeToUtf8Bytes(message));
    }
}