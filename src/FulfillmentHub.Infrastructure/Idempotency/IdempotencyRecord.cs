namespace FulfillmentHub.Infrastructure.Idempotency;

public enum IdempotencyStatus
{
    InProgress = 0,
    Completed = 1,
}

/// <summary>
/// One idempotent request (ADR-010): who sent it (scope), the client's key, a hash of what was sent and, once
/// finished, the response to replay. Infrastructure record, not a domain entity.
/// </summary>
public sealed class IdempotencyRecord
{
    public const int KeyMaxLength = 64;
    public const int ScopeMaxLength = 64;
    public const int HashLength = 64;

    private IdempotencyRecord()
    {
    }

    public string Scope { get; private set; } = null!;

    public string Key { get; private set; } = null!;

    public string RequestHash { get; private set; } = null!;

    public IdempotencyStatus Status { get; private set; }

    public int? ResponseStatusCode { get; private set; }

    public string? ResponseBody { get; private set; }

    public string? ResponseContentType { get; private set; }

    public string? ResponseLocation { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public static IdempotencyRecord Start(string scope, string key, string requestHash, DateTimeOffset now, TimeSpan timeToLive) => new()
    {
        Scope = scope,
        Key = key,
        RequestHash = requestHash,
        Status = IdempotencyStatus.InProgress,
        CreatedAt = now,
        ExpiresAt = now.Add(timeToLive),
    };

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    /// <summary>Reuses an expired row for a new request (the old response is no longer replayable).</summary>
    public void Restart(string requestHash, DateTimeOffset now, TimeSpan timeToLive)
    {
        RequestHash = requestHash;
        Status = IdempotencyStatus.InProgress;
        ResponseStatusCode = null;
        ResponseBody = null;
        ResponseContentType = null;
        ResponseLocation = null;
        CreatedAt = now;
        ExpiresAt = now.Add(timeToLive);
    }

    public void Complete(int statusCode, string? body, string? contentType, string? location)
    {
        Status = IdempotencyStatus.Completed;
        ResponseStatusCode = statusCode;
        ResponseBody = body;
        ResponseContentType = contentType;
        ResponseLocation = location;
    }
}
