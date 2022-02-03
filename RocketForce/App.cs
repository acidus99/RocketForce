using System;
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

using Gemini.Net;
using Microsoft.Extensions.Logging;

namespace RocketForce
{
    using RequestCallback = System.Action<Request, Response, App>;

    public class App
    {
        //the request line is at most (1024 + 2) characters long. (max sized URL + CRLF)
        const int MaxRequestSize = 1024 + 2;

        private readonly TcpListener _listener;

        readonly List<Tuple<string, RequestCallback>> routeCallbacks;

        private readonly X509Certificate2 _serverCertificate;

        public ILogger<App> Logger { get; private set; }

        StaticFileModule FileModule;

        public App(IPAddress bindingAddress, int port, string directoryToServe, X509Certificate2 certificate, ILogger<App> logger)
        {

            _listener = new TcpListener(bindingAddress, port);
            routeCallbacks =  new List<Tuple<string, RequestCallback>>();

            _serverCertificate = certificate;
            Logger = logger;

            if (!String.IsNullOrEmpty(directoryToServe))
            {
                FileModule = new StaticFileModule(directoryToServe);
            }
        }

        public void OnRequest(string route, RequestCallback callback)
            => routeCallbacks.Add(new Tuple<string, RequestCallback>(route.ToLower(), callback));
       
        public void Run()
        {
            try
            {
                _listener.Start();
                Logger.LogInformation("Serving capsule on {0}", _listener.Server.LocalEndPoint.ToString());

                while (true)
                {
                    var client = _listener.AcceptTcpClient();
                    Task.Run(() => ProcessRequest(client));
                }
            }
            catch (SocketException e)
            {
                Logger.LogError("SocketException: {0}", e);
            }
            finally
            {
                _listener.Stop();
            }
        }

        private void ProcessRequest(TcpClient client)
        {
            SslStream sslStream = null;
            try
            {
                var remoteIP = getClientIP(client);
                sslStream = new SslStream(client.GetStream(), false);
                ProcessRequest(remoteIP, sslStream);
            }
            catch (AuthenticationException e)
            {
                Logger.LogError("AuthenticationException: {0}", e.Message);
                if (e.InnerException != null)
                {
                    Logger.LogError("Inner exception: {0}", e.InnerException.Message);
                }
                Logger.LogError("Authentication failed - closing the connection.");
            }
            catch (IOException e)
            {
                Logger.LogError("IOException: {0}", e.Message);
            }
            finally
            {
                sslStream.Close();
                client.Close();
            }
        }

        /// <summary>
        /// attempts to get the IP address of the remote client
        /// </summary>
        private string getClientIP(TcpClient client)
        {
            if (client.Client.RemoteEndPoint != null && (client.Client.RemoteEndPoint is IPEndPoint))
            {
                return (client.Client.RemoteEndPoint as IPEndPoint).Address.ToString();
            }
            return "unknown";
        }

        private void ProcessRequest(string remoteIP, SslStream sslStream)
        {
            sslStream.ReadTimeout = 5000;
            sslStream.AuthenticateAsServer(_serverCertificate, false, SslProtocols.Tls12 | SslProtocols.Tls13, false);

            // Read a message from the client.
            string rawRequest = ReadRequest(sslStream);

            var response = new Response(sslStream);

            if (rawRequest == null)
            {
                Logger.LogDebug("Could not read incoming URL");
                response.BadRequest("Missing URL");
                return;
            }

            Logger.LogDebug("Raw request: \"{0}\"", rawRequest);

            GeminiUrl url = null;
            try
            {
                url = new GeminiUrl(rawRequest);
            }
            catch (Exception)
            {
                Logger.LogDebug("Requested URL is invalid");
                response.BadRequest("Invalid URL");
                return;
            }

            var request = new Request
            {
                Url = url,
                RemoteIP = remoteIP
            };

            Logger.LogDebug("Request info:");
            Logger.LogDebug("\tRemote IP: \"{0}\"", request.RemoteIP);
            Logger.LogDebug("\tBaseURL: \"{0}\"", request.Url.NormalizedUrl);
            Logger.LogDebug("\tRoute: \"{0}\"", request.Route);

            //First look if this request matches a route...
            var callback = FindRoute(request.Route);
            if (callback != null)
            {
                callback(request, response, this);
                return;
            }

            //nope... look to see if we are handling file system requests
            if (FileModule != null)
            {
                FileModule.HandleRequest(request, response, Logger);
                return;
            }
            //nope, return a not found
            response.Missing("Could not find a file or route for this URL");
        }

        /// <summary>
        /// Finds the first callback that registered for a route
        /// We use "starts with" because we need to support routes that use parts of the path
        /// to pass variables/state (e.g. /search/{language}/{other-options}?search-term
        /// </summary>
        /// <param name="route"></param>
        private RequestCallback? FindRoute(string route)
            => routeCallbacks.Where(x => route.StartsWith(x.Item1))
                .Select(x => x.Item2).FirstOrDefault();

        /// <summary>
        /// Reads the request URL from the client.
        /// This looks complex, but allows for slow clients where the entire URL is not
        /// available in a single read from the buffer
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        private string ReadRequest(Stream stream)
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
                        throw new ApplicationException("Malformed Gemini request - missing LF after CR");
                    }
                    break;
                }
                //keep going if we haven't read too many
                readCount++;
                if (readCount > MaxRequestSize)
                {
                    throw new ApplicationException($"Invalid gemini request line. Did not find \\r\\n within {MaxRequestSize} bytes");
                }
                requestBuffer.Add(readBuffer[0]);
            }
            //spec requires request use UTF-8
            return Encoding.UTF8.GetString(requestBuffer.ToArray());
        }
    }
}
