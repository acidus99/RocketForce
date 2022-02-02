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

        public void Success(string mimeType = "text/gemini")
        {
            Write($"20 {mimeType}\r\n");
        }

        public void Input(string prompt)
        {
            Write($"10 {prompt}\r\n");
        }

        public void BadRequest(string msg)
        {
            Write($"59 {msg}\r\n");
        }

        public void Missing(string msg)
        {
            Write($"51 {msg}\r\n");
        }

        public void Redirect(string url)
        {
            Write($"30 {url}\r\n");
        }

        public void Write(string text)
            => Write(text, Encoding.UTF8);

        public void Write(string text, Encoding encoding)
        {
            fout.Write(encoding.GetBytes(text));
        }

    }
}
