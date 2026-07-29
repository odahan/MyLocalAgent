using Microsoft.Extensions.Options;

namespace OD.MyLocalAgent.Tests.TestDoubles;

internal sealed class MutableOptionsMonitor<T> : IOptionsMonitor<T>
{
    private T _value;
    private event Action<T, string?>? Changed;

    public MutableOptionsMonitor(T value)
    {
        _value = value;
    }

    public T CurrentValue => _value;

    public T Get(string? name)
    {
        return _value;
    }

    public IDisposable? OnChange(Action<T, string?> listener)
    {
        Changed += listener;
        return new CallbackDisposable(() => Changed -= listener);
    }

    public void Set(T value)
    {
        _value = value;
        Changed?.Invoke(value, Options.DefaultName);
    }

    private sealed class CallbackDisposable : IDisposable
    {
        private readonly Action _dispose;

        public CallbackDisposable(Action dispose)
        {
            _dispose = dispose;
        }

        public void Dispose()
        {
            _dispose();
        }
    }
}
