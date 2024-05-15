using System;

namespace RocketForce;

public record Request
{
    public required DateTime Received { get; init; }
    public required string RemoteIP { get; init; }
    public required Uri Url { get; init; }
}