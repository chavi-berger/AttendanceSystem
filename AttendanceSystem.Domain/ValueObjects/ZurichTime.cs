using System;

namespace AttendanceSystem.Domain.ValueObjects;

/// <summary>
/// Immutable value object wrapping a <see cref="DateTimeOffset"/> that originated from the
/// external Europe/Zurich time API. The official time NEVER comes from the server clock.
/// Equality is based on <see cref="Value"/> only — not on the source or receipt timestamp.
/// </summary>
public sealed class ZurichTime : IEquatable<ZurichTime>
{
    public DateTimeOffset Value { get; }
    public string Source { get; }          // API URL that provided this time
    public DateTimeOffset ReceivedAtUtc { get; } // when WE received it

    private ZurichTime(DateTimeOffset value, string source, DateTimeOffset receivedAt)
    {
        Value = value;
        Source = source;
        ReceivedAtUtc = receivedAt;
    }

    public static ZurichTime FromApiResponse(DateTimeOffset apiTime, string apiSource)
    {
        if (string.IsNullOrWhiteSpace(apiSource)) throw new ArgumentException("Source is required");
        return new ZurichTime(apiTime, apiSource, DateTimeOffset.UtcNow);
    }

    // Equality by Value only (not Source or ReceivedAt)
    public bool Equals(ZurichTime? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Value.Equals(other.Value);
    }

    public override bool Equals(object? obj) => Equals(obj as ZurichTime);

    public override int GetHashCode() => Value.GetHashCode();

    public static bool operator ==(ZurichTime? left, ZurichTime? right)
    {
        if (left is null) return right is null;
        return left.Equals(right);
    }

    public static bool operator !=(ZurichTime? left, ZurichTime? right) => !(left == right);

    public override string ToString() => $"{Value:O} (from {Source})";
}
