﻿using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Security.Authentication;
using System.IO;
using System.Threading.Tasks;

using RocketForce.Logging;
using Microsoft.Extensions.Logging;

namespace RocketForce
{
    public abstract class AbstractGeminiApp
    {
        public abstract void ProcessRequest(Request request, Response response);

            //the request line is at most (1024 + 2) characters long. (max sized URL + CRLF)
        const int MaxRequestSize = 1024 + 2;

        /// <summary>
        /// Should we mask IPs of remote clients
        /// </summary>
        public bool IsMaskingRemoteIPs { get; set; } = true;

        private readonly X509Certificate2 serverCertificate;
        private readonly TcpListener listener;

        private W3CLogger accessLogger;
        protected string hostname;
        protected int port;

        public AbstractGeminiApp(string hostname, int port, X509Certificate2 certificate)
        {
            this.hostname = hostname;
            this.port = port;
            listener = TcpListener.Create(port);
            accessLogger = new W3CLogger();
            serverCertificate = certificate;
        }

        public void Run()
        {
            if (serverCertificate == null)
            {
                Console.WriteLine("Could not Load Server Key/Certificate. Exiting.");
                return;
            }

            try
            {
                listener.Start();
                while (true)
                {
                    var client = listener.AcceptTcpClient();
                    Task.Run(() => ProcessRequest(client));
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Exception in Run(): {e.Message}");
            }
            finally
            {
                listener.Stop();
            }
        }

        private void ProcessRequest(TcpClient client)
        {
            try
            {
                using (var sslStream = new SslStream(client.GetStream(), false))
                {
                    var received = DateTime.Now;
                    var remoteIP = getClientIP(client);
                    ProcessRequest(remoteIP, sslStream);
                }
            }
            catch (AuthenticationException)
            {
                //ignore any SSL issues
            }
            //Ensure that an exception processing a request doesn't take down the whole server
            catch (Exception e)
            {
                Console.WriteLine("Uncaught Exception in ProcessRequest! {0}", e.Message);
                Console.WriteLine(e.StackTrace);
            }
        }

        /// <summary>
        /// attempts to get the IP address of the remote client, or mask it
        /// </summary>
        private string getClientIP(TcpClient client)
        {
            if (!IsMaskingRemoteIPs && client.Client.RemoteEndPoint != null && (client.Client.RemoteEndPoint is IPEndPoint))
            {
                return (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "-";
            }
            return "-";
        }

        public void LogRequest(Request request, Response response)
        {
            var completed = DateTime.Now;

            var record = new AccessRecord
            {
                Date = AccessRecord.FormatDate(request.Received),
                Time = AccessRecord.FormatTime(request.Received),
                RemoteIP = request.RemoteIP,
                Url = request.Url.ToString(),
                StatusCode = response.StatusCode.ToString(),
                Meta = response.Meta,
                SentBytes = response.Length.ToString(),
                TimeTaken = AccessRecord.ComputeTimeTaken(request.Received, completed)
            };
            accessLogger.LogAccess(record);
        }

        public void LogInvalidRequest(DateTime received, string remoteIP, Response response)
        {
            var completed = DateTime.Now;

            var record = new AccessRecord
            {
                Date = AccessRecord.FormatDate(received),
                Time = AccessRecord.FormatTime(received),
                RemoteIP = remoteIP,
                StatusCode = response.StatusCode.ToString(),
                Meta = response.Meta,
                SentBytes = response.Length.ToString(),
                TimeTaken = AccessRecord.ComputeTimeTaken(received, completed)
            };
            accessLogger.LogAccess(record);
        }

        public void LogInvalidRequest(DateTime received, string remoteIP, string rawRequest, Response response)
        {
            var completed = DateTime.Now;

            var record = new AccessRecord
            {
                Date = AccessRecord.FormatDate(received),
                Time = AccessRecord.FormatTime(received),
                RemoteIP = remoteIP,
                Url = AccessRecord.Sanitize(rawRequest, false),
                StatusCode = response.StatusCode.ToString(),
                Meta = response.Meta,
                SentBytes = response.Length.ToString(),
                TimeTaken = AccessRecord.ComputeTimeTaken(received, completed)
            };
            accessLogger.LogAccess(record);
        }

        private void ProcessRequest(string remoteIP, SslStream sslStream)
        {
            sslStream.ReadTimeout = 5000;
            sslStream.AuthenticateAsServer(serverCertificate, false, SslProtocols.Tls12 | SslProtocols.Tls13, false);

            string rawRequest = null!;
            var response = new Response(sslStream);
            var received = DateTime.Now;
            try
            {
                // Read a message from the client.
                rawRequest = ReadRequestLine(sslStream);
            }
            catch (ApplicationException ex)
            {
                response.BadRequest(ex.Message);
                LogInvalidRequest(received, remoteIP, response);
                return;
            }

            Uri? url = ValidateRequest(rawRequest, response);
            if (url == null)
            {
                //we already populated the response object and reported the
                //appropriate status to the client, so we can exit
                LogInvalidRequest(received, remoteIP, rawRequest, response);
                return;
            }

            var request = new Request
            {
                Url = url,
                RemoteIP = remoteIP,
                Received = received
            };

            ProcessRequest(request, response);
            LogRequest(request, response);
        }

        /// <summary>
        /// Validates the raw gemini request. If not, writes the appropriate
        /// errors on the response object and returns null. if valid, returns
        /// GeminiUrl object
        /// </summary>
        private Uri? ValidateRequest(string rawRequest, Response response)
        {

            //The order of these checks, and the status codes they return, may seem odd
            //but are organized to pass the gemini-diagnostics check
            //https://github.com/michael-lazar/gemini-diagnostics
            if (rawRequest == null)
            {
                response.BadRequest("Missing URL");
                return null;
            }

            Uri requestUrl;
            try
            {
                requestUrl = new Uri(rawRequest);
            }
            catch (Exception)
            {
                response.BadRequest("Invalid URL");
                return null;
            }

            //Silly .NET URI will parse "/" as a "file" scheme with a "/" path! crazy
            //and say it is absolute. So explicitly look for :// to determine if absolute 
            if (!rawRequest.Contains("://"))
            {
                response.BadRequest("Relative URLs not allowed");
                return null;
            }

            //Do specific validation here
            if(!IsValidRequest(requestUrl, response))
            {
                //overriders of IsValidRequest will have already set the appropriate
                //response status code and message, so just return
                return null;
            }

            return requestUrl;
        }

        /// <summary>
        /// Allow for additional, validation depending on the app. Derived classes
        /// are responsible for set the appropriate status code and meta on the response
        /// object
        /// e.g: A Gemini Server will confirm that the scheme is "gemini" and matches the hostname, etc
        /// A proxy will accept other protocols
        /// </summary>
        /// <param name="url"></param>
        /// <param name="response"></param>
        /// <returns></returns>
        protected virtual bool IsValidRequest(Uri url, Response response)
        {
            return true;
        }

        /// <summary>
        /// Reads the request line URL from the client.
        /// This looks complex, but allows for slow clients where the entire URL is not
        /// available in a single read from the buffer
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        private string ReadRequestLine(Stream stream)
        {
            var requestBuffer = new List<byte>(MaxRequestSize);
            byte[] readBuffer = { 0 };

            int readCount = 0;
            while (stream.Read(readBuffer, 0, 1) == 1)
            {
                if (readBuffer[0] == (byte)'\r')
                {
                    //spec requires a \n next
                    stream.Read(readBuffer, 0, 1);
                    if (readBuffer[0] != (byte)'\n')
                    {
                        throw new ApplicationException("Invalid Request. Request line missing LF after CR");
                    }
                    break;
                }
                //keep going if we haven't read too many
                readCount++;
                if (readCount > MaxRequestSize)
                {
                    throw new ApplicationException($"Invalid Request. Did not find CRLF within {MaxRequestSize} bytes of request line");
                }
                requestBuffer.Add(readBuffer[0]);
            }
            //the URL itself should not be longer than the max size minus the trailing CRLF
            if (requestBuffer.Count > MaxRequestSize - 2)
            {
                throw new ApplicationException($"Invalid Request. URL exceeds {MaxRequestSize - 2}");
            }
            //spec requires request use UTF-8
            return Encoding.UTF8.GetString(requestBuffer.ToArray());
        }
    }
}
