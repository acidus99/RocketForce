namespace RocketForce;

public class Route
{
    private bool _isExact { get; set; }

    private string _routePattern;

    public Route(string rule)
    {
        if (rule.EndsWith("$") && rule.Length > 1)
        {
            _routePattern = rule.Substring(0, rule.Length - 1);
            _isExact = true;

        }
        else
        {
            _routePattern = rule;
            _isExact = false;
        }
    }

    public bool IsMatch(string route)
        => _isExact ? route == _routePattern : route.StartsWith(_routePattern);
}