using ClassNex.Core.Abstractions;

namespace ClassNex.Services;

internal sealed class NotificationQueue
{
    private const int Capacity = 64;
    private readonly List<NotificationRequest> _pending = [];

    public bool Add(NotificationRequest request, DateTimeOffset now)
    {
        _pending.RemoveAll(item => item.ExpiresAt <= now);
        if (request.ExpiresAt <= now || request.Duration <= TimeSpan.Zero || request.MaskDuration < TimeSpan.Zero ||
            string.IsNullOrWhiteSpace(request.Id) || _pending.Exists(item => item.Id == request.Id))
            return false;

        var index = _pending.FindIndex(item => item.Priority < request.Priority);
        if (_pending.Count == Capacity)
        {
            if (index < 0) return false;
            _pending.RemoveAt(_pending.Count - 1);
        }

        _pending.Insert(index < 0 ? _pending.Count : index, request);
        return true;
    }

    public NotificationRequest? Take(DateTimeOffset now)
    {
        while (_pending.Count > 0)
        {
            var first = _pending[0];
            _pending.RemoveAt(0);
            if (first.ExpiresAt > now) return first;
        }
        return null;
    }

    public bool Remove(string id) => _pending.RemoveAll(item => item.Id == id) != 0;
    public void RemovePrefix(string prefix) => _pending.RemoveAll(item => item.Id.StartsWith(prefix, StringComparison.Ordinal));
    public void Clear() => _pending.Clear();
}
