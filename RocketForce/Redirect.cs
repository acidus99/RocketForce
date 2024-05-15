namespace RocketForce;

public record Redirect
{
    public bool IsTemporary { get; set; } = true;

    public required string UrlPrefix { get; set; }

    public required string TargetUrl { get; set; }
}