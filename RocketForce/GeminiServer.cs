using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using Gemini.Net;

namespace RocketForce;

using RequestCallback = System.Action<GeminiRequest, Response, GeminiServer>;

using ExceptionCallback = System.Action<GeminiRequest, Response, GeminiServer, Exception>;

public class GeminiServer : AbstractGeminiApp
{
    private readonly List<Tuple<Route, RequestCallback>> routeCallbacks;

    private readonly List<Redirect> redirects;

    private StaticFileModule? fileModule;

    public ExceptionCallback ExceptionCallback { get; set; } = DefaultExceptionCallback;

    public GeminiServer(string hostname, int port, X509Certificate2 certificate, string? publicRootPath = null)
        : base(hostname, port, certificate)
    {
        routeCallbacks = new List<Tuple<Route, RequestCallback>>();
        redirects = new List<Redirect>();
        if (!String.IsNullOrEmpty(publicRootPath))
        {
            fileModule = new StaticFileModule(publicRootPath);
        }
    }

    public override void ProcessRequest(Request request, Response response)
    {
        GeminiRequest geminiRequest = CreateGeminiRequest(request);

        //First look if this request matches a route...
        var callback = FindRoute(geminiRequest.Route);
        if (callback != null)
        {
            try
            {
                callback(geminiRequest, response, this);
            }
            catch (Exception ex)
            {
                ExceptionCallback(geminiRequest, response, this, ex);
            }

            return;
        }

        var redirect = FindRedirect(geminiRequest.Url);
        if (redirect != null)
        {
            if (redirect.IsTemporary)
            {
                response.Redirect(redirect.TargetUrl);
            }
            else
            {
                response.RedirectPermanent(redirect.TargetUrl);
            }
            return;
        }

        if (fileModule != null)
        {
            //nope... look to see if we are handling file system requests
            fileModule.HandleRequest(request, response);
            return;
        }

        //nope, return a not found
        response.Missing("Could not find a file or route for this URL");
    }

    public static void DefaultExceptionCallback(GeminiRequest request, Response response, GeminiServer server, Exception ex)
    {
        //output to the console so the error still shows up in logs
        Console.WriteLine($"Unhandled Exception Callback: {ex.Message}");

        if (response.IsSending)
        {
            //response already in flight, so clear any existing line and add a new one
            response.WriteLine();
            response.WriteLine();
            response.WriteLine("# ERROR!");
            response.WriteLine("An unhandled error occurred while processing this URL.");
            response.WriteLine($"* URL: {request.Url}");
            response.WriteLine($"* Error: {ex.Message}");
            if (ex.StackTrace != null && server.IsLocalHost)
            {
                response.WriteLine("```");
                response.WriteLine(ex.StackTrace);
                response.WriteLine("```");
            }
        }
        else
        {
            response.BadRequest($"Error! Unhandled Exception: {ex.Message}");
        }
    }

    private GeminiRequest CreateGeminiRequest(Request request)
        => new GeminiRequest
        {
            Received = request.Received,
            RemoteIP = request.RemoteIP,
            Url = new GeminiUrl(request.Url)
        };

    /// <summary>
    /// Implement additional request validation. Specifically:
    /// - Must be a gemini URL scheme
    /// - Must be a request to the same hostname and port the server is listening on
    /// </summary>
    /// <param name="url"></param>
    /// <param name="response"></param>
    /// <returns></returns>
    protected override bool IsValidRequest(Uri url, Response response)
    {
        //must be a gemini scheme
        if (url.Scheme != "gemini")
        {
            //refuse to proxy to other protocols
            response.ProxyRefused("protocols");
            return false;
        }

        GeminiUrl geminiUrl;

        try
        {
            geminiUrl = new GeminiUrl(url);
        }
        catch (Exception)
        {
            response.BadRequest("Invalid URL");
            return false;
        }

        if (geminiUrl.Hostname != Hostname || geminiUrl.Port != Port)
        {
            response.ProxyRefused("hosts or ports");
            return false;
        }
        return true;
    }

    public void OnRequest(string route, RequestCallback callback)
       => routeCallbacks.Add(new Tuple<Route, RequestCallback>(new Route(route), callback));

    public void AddRedirect(Redirect redirect)
        => redirects.Add(redirect);

    /// <summary>
    /// Finds the first callback that registered for a route
    /// We use "starts with" because we need to support routes that use parts of the path
    /// to pass variables/state (e.g. /search/{language}/{other-options}?search-term
    /// </summary>
    /// <param name="route"></param>
    private RequestCallback? FindRoute(string route)
        => routeCallbacks.Where(x => x.Item1.IsMatch(route))
            .Select(x => x.Item2).FirstOrDefault();

    private Redirect? FindRedirect(GeminiUrl url)
        => redirects
            .Where(x => url.Path.StartsWith(x.UrlPrefix))
            .FirstOrDefault();
}