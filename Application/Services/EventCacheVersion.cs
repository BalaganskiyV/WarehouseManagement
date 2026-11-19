namespace Application.Services;
public sealed class EventCacheVersion
{
    private long _value;
    public long Value => Interlocked.Read(ref _value);
    public void Changed() => Interlocked.Increment(ref _value);
}
