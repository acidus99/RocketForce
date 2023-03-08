using System;
using System.Net;

using Gemini.Net;

namespace RocketForce
{
	public class GeminiRequest
	{
        public DateTime Received { get; set; }
        public string RemoteIP { get; set; }
        public GeminiUrl Url { get; set; }
        public string Route => WebUtility.UrlDecode(Url.Path).ToLower();
    }
}

