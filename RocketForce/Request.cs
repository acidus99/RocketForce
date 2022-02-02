using Gemini.Net;

namespace RocketForce
{
    public class Request
    {
        public string RemoteIP { get; set; }
        public GeminiUrl Url { get; set; }
        public string Route => Url.Path.ToLower();
    }
}
