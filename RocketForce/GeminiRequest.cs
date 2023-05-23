using System;
using System.Net;

using Gemini.Net;

namespace RocketForce
{
	public record GeminiRequest
	{
        public required DateTime Received { get; init; }
        public required string RemoteIP { get; init; }
        public required GeminiUrl Url { get; init; }
        public string Route => WebUtility.UrlDecode(Url.Path).ToLower();
    }
}

