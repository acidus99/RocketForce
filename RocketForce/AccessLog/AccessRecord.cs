using System;
namespace RocketForce.AccessLog
{
    public class AccessRecord
    {
        public string RawRequest { get; set; }
        public Request Request { get; set; }
        public Response Response { get; set; }
    }
}
