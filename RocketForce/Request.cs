using Gemini.Net;
using System.Net;

namespace RocketForce
{
    public class Request
    {
        public string RemoteIP { get; set; }
        public GeminiUrl Url { get; set; }
        public string Route => WebUtility.UrlDecode(Url.Path).ToLower();
    }
}
