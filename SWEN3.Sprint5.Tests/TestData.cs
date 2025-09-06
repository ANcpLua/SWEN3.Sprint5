[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]

namespace SWEN3.Sprint5.Tests;

[ExcludeFromCodeCoverage]
internal static class Messages
{
    internal record SimpleMessage(int Id);

    internal class SseTestEvent
    {
        public int Id { get; init; }
        public string? Message { get; init; }
    }
}