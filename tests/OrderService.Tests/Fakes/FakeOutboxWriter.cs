using OrderService.Application.Ports;

namespace OrderService.Tests.Fakes;

public sealed class FakeOutboxWriter : IOutboxWriter
{
    private readonly List<(string Topic, string AggregateKey, object Message)> _calls = new();

    public IReadOnlyList<(string Topic, string AggregateKey, object Message)> Calls => _calls;

    public Task AddAsync<T>(string topic, string aggregateKey, T message, CancellationToken cancellationToken)
    {
        _calls.Add((topic, aggregateKey, message!));
        return Task.CompletedTask;
    }

    public void Reset() => _calls.Clear();

    public IEnumerable<(string Topic, string AggregateKey, object Message)> CallsFor(string topic) =>
        _calls.Where(x => x.Topic == topic);

    public T? SingleMessage<T>(string topic) where T : class =>
        _calls.Where(x => x.Topic == topic).Select(x => x.Message as T).SingleOrDefault();
}
