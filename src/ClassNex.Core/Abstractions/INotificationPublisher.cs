namespace ClassNex.Core.Abstractions;

/// <summary>Determines which waiting request the display handles first.</summary>
public enum NotificationPriority
{
    /// <summary>Background information that can wait behind classroom events.</summary>
    Low,
    /// <summary>Ordinary lesson and break events.</summary>
    Normal,
    /// <summary>Time-sensitive notices that take precedence over ordinary requests.</summary>
    Important
}

/// <summary>The detail page shown after a notification's initial mask.</summary>
/// <param name="Caption">Short heading shown beside the course details.</param>
/// <param name="Subject">Course name shown to the classroom.</param>
/// <param name="Teacher">Teacher label, or empty to omit it.</param>
/// <param name="Time">Formatted lesson time or range.</param>
/// <param name="Prefix">Text placed before the detail message.</param>
/// <param name="Message">Second-page text; may be empty for a single page.</param>
/// <param name="Vertical">Requests a vertical page turn between message and details.</param>
/// <param name="CountdownTo">Optional instant from which the presenter derives a live countdown.</param>
public sealed record NotificationOverlay(string Caption, string Subject, string Teacher, string Time,
    string Prefix = "", string Message = "", bool Vertical = false, DateTimeOffset? CountdownTo = null)
{
    /// <summary>When set, the heading is the class break's name and duration, rather than a plain caption.</summary>
    public (string Name, string Duration)? Break { get; init; }
}

/// <summary>A time-limited, identifiable presentation request. It contains no window or platform objects.</summary>
/// <param name="Id">Deduplication and cancellation key for this occurrence.</param>
/// <param name="Mask">Short text for the initial announcement.</param>
/// <param name="ExpiresAt">Absolute deadline beyond which the request is stale.</param>
/// <param name="MaskDuration">How long to show the mask before the details.</param>
/// <param name="Duration">Requested lifetime of the complete presentation.</param>
/// <param name="Priority">Relative order among waiting requests.</param>
/// <param name="Channel">Destination channel, normally classroom.</param>
/// <param name="Overlay">Optional detail page; null requests a mask alone.</param>
/// <param name="LeftGlyph">Leading glyph used by the presenter.</param>
/// <param name="RightGlyph">Trailing glyph used by the presenter.</param>
public sealed record NotificationRequest(string Id, string Mask, DateTimeOffset ExpiresAt,
    TimeSpan MaskDuration, TimeSpan Duration, NotificationPriority Priority = NotificationPriority.Normal,
    string Channel = "classroom", NotificationOverlay? Overlay = null, string LeftGlyph = "\uE0FF",
    string RightGlyph = "\uE54F");

/// <summary>Submits and withdraws notifications without acquiring the desktop surface.</summary>
public interface INotificationPublisher
{
    /// <summary>Returns whether the request was accepted; acceptance does not guarantee it will be displayed before expiry.</summary>
    bool Publish(NotificationRequest request);
    /// <summary>Withdraws an outstanding request by its occurrence ID.</summary>
    bool Cancel(string id);
}
