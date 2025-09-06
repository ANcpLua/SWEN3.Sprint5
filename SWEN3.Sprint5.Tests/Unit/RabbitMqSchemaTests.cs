using SWEN3.Sprint5.Schema;

namespace SWEN3.Sprint5.Tests.Unit;

public class RabbitMqSchemaTests
{
    [Fact]
    public void RabbitMqSchema_ShouldDefineGenAIConstants()
    {
        RabbitMqSchema.GenAIEventQueue.Should().Be("GenAIEventQueue");
        RabbitMqSchema.GenAIEventRouting.Should().Be("genai.event");
    }

    [Fact]
    public void RabbitMqSchema_ShouldDefineAllRequiredQueues()
    {
        RabbitMqSchema.OcrCommandQueue.Should().NotBeNullOrWhiteSpace();
        RabbitMqSchema.OcrEventQueue.Should().NotBeNullOrWhiteSpace();
        RabbitMqSchema.GenAIEventQueue.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void RabbitMqSchema_ShouldDefineAllRoutingKeys()
    {
        RabbitMqSchema.OcrCommandRouting.Should().NotBeNullOrWhiteSpace();
        RabbitMqSchema.OcrEventRouting.Should().NotBeNullOrWhiteSpace();
        RabbitMqSchema.GenAIEventRouting.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void RabbitMqSchema_ShouldDefineExchange()
    {
        RabbitMqSchema.Exchange.Should().Be("paperless.exchange");
    }

    [Fact]
    public void RabbitMqSchema_QueueNamesFollowNamingConvention()
    {
        RabbitMqSchema.OcrCommandQueue.Should().EndWith("Queue");
        RabbitMqSchema.OcrEventQueue.Should().EndWith("Queue");
        RabbitMqSchema.GenAIEventQueue.Should().EndWith("Queue");
    }

    [Fact]
    public void RabbitMqSchema_RoutingKeysFollowNamingConvention()
    {
        RabbitMqSchema.OcrCommandRouting.Should().Contain(".");
        RabbitMqSchema.OcrEventRouting.Should().Contain(".");
        RabbitMqSchema.GenAIEventRouting.Should().Contain(".");
    }
}