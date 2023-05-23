using System.IO;
using System.Net.Security;
using System.Text;
namespace RocketForce
{
    public class Response
    {
        public int StatusCode { get; private set; }
        public string Meta { get; private set; }
        /// <summary>
        /// number of bytes sent to the client
        /// </summary>
        public int Length { get; private set; }

        readonly SslStream fout;

        public Response(SslStream respStream)
        {
            fout = respStream;
            StatusCode = 0;
            Meta = "";
        }

        public void Input(string prompt)
            => WriteStatusLine(10, prompt);

        public void Success(string mimeType = "text/gemini")
            => WriteStatusLine(20, mimeType);

        public void Redirect(string url)
            => WriteStatusLine(30, url);

        public void RedirectPermanent(string url)
            => WriteStatusLine(31, url);

        public void Missing(string msg)
            => WriteStatusLine(51, msg);

        public void ProxyRefused(string msg)
            => WriteStatusLine(53, $"Will not proxy requests for other {msg}");

        public void BadRequest(string msg)
            => WriteStatusLine(59, msg);

        public void Error(string msg)
         => WriteStatusLine(40, msg);

        public void WriteStatusLine(int statusCode, string msg = "")
        {
            StatusCode = statusCode;
            Meta = msg;
            //don't use writeline since I need a full \r\n
            Write(Encoding.UTF8.GetBytes($"{statusCode} {msg}\r\n"));
        }

        public void Write(byte[] data)
        {
            if (data != null)
            {
                Length += data.Length;
                try
                {
                    fout.Write(data);
                } catch(IOException)
                {
                    // If the user navigated away from this while a page was still loading,
                    // we can get a broken pipe exception here. Ignore it
                }
            }
        }

        public void Write(string text)
            => Write(Encoding.UTF8.GetBytes(text));

        public void WriteLine(string text = "")
            => Write(text + "\n");

        public void CopyFrom(Stream stream)
        {
            byte[] buffer = new byte[32 * 1024];

            int read = 0;

            do
            {
                read = stream.Read(buffer);
                fout.Write(buffer, 0, read);
                Length += read;
            } while (read > 0);
        }
    }
}
