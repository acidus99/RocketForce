using System;
using System.Net;

namespace RocketForce
{
    public class Request
    {
        public DateTime Received { get; set; }
        public string RemoteIP { get; set; }
        public Uri Url { get; set; }
    }
}
