using System;
using System.IO;
using System.Net.Security;
using System.Text;
namespace RocketForce
{
    public class Response
    {
        readonly SslStream fout;

        public Response(SslStream respStream)
        {
            fout = respStream;
        }

        public void Input(string prompt)
            => WriteStatusLine(10, prompt);

        public void Success(string mimeType = "text/gemini")
            => WriteStatusLine(20, mimeType);

        public void Redirect(string url)
            => WriteStatusLine(30, url);

        public void Missing(string msg)
            => WriteStatusLine(51, msg);

        public void ProxyRefused(string msg)
            => WriteStatusLine(53, $"Will not proxy requests for other {msg}");

        public void BadRequest(string msg)
            => WriteStatusLine(59, msg);

        private void WriteStatusLine(int statusCode, string msg)
            => Write($"{statusCode} {msg}\r\n");

        public void Write(byte[] data)
            => fout.Write(data);

        public void Write(string text)
            => Write(text, Encoding.UTF8);

        public void Write(string text, Encoding encoding)
            => fout.Write(encoding.GetBytes(text));

        public void WriteLine(string text = "")
            => Write(text + "\n", Encoding.UTF8);
    }
}
