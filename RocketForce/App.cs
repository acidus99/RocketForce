using System;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Security.Authentication;
using System.IO;

using Gemini.Net;
using Microsoft.Extensions.Logging;

namespace RocketForce
{
    using RequestCallback = System.Action<Request, Response, ILogger<App>>;

    public class App
    {
        private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 1966);
        private readonly Dictionary<string, RequestCallback> _requestCallbacks = new Dictionary<string, RequestCallback>();
        private readonly byte[] _buffer = new byte[4096];
        private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();

        private readonly string _directoryToServe;
        private readonly X509Certificate2 _serverCertificate;
        private readonly ILogger<App> _logger;

        private RequestCallback _onBadRequestCallback;

        public App(string directoryToServe, X509Certificate2 certificate, ILogger<App> logger)
        {
            _directoryToServe = directoryToServe;
            _serverCertificate = certificate;
            _logger = logger;
        }

        public void OnRequest(string route, RequestCallback callback)
        {
            _requestCallbacks.Add(route, callback);
        }

        public void OnBadRequest(RequestCallback callback)
        {
            _onBadRequestCallback = callback;
        }

        public void Run()
        {
            try
            {
                _listener.Start();

                _logger.LogInformation("Serving capsule on {0}", _listener.Server.LocalEndPoint.ToString());

                while (true)
                {
                    ProcessRequest(_listener.AcceptTcpClient());
                }
            }
            catch (SocketException e)
            {
                _logger.LogError("SocketException: {0}", e);
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
                sslStream = new SslStream(client.GetStream(), false);
                ProcessRequest(sslStream);
            }
            catch (AuthenticationException e)
            {
                _logger.LogError("AuthenticationException: {0}", e.Message);
                if (e.InnerException != null)
                {
                    _logger.LogError("Inner exception: {0}", e.InnerException.Message);
                }
                _logger.LogError("Authentication failed - closing the connection.");
            }
            catch (IOException e)
            {
                _logger.LogError("IOException: {0}", e.Message);
            }
            finally
            {
                sslStream.Close();
                client.Close();
            }
        }

        private void ProcessRequest(SslStream sslStream)
        {
            sslStream.ReadTimeout = 5000;
            sslStream.AuthenticateAsServer(_serverCertificate, false, SslProtocols.Tls12 | SslProtocols.Tls13, false);

            // Read a message from the client.
            string rawRequest = ReadRequest(sslStream);

            var response = new Response(sslStream);

            if (rawRequest == null)
            {
                _logger.LogDebug("Could not read incoming URL");
                response.BadRequest("Missing URL");
                return;
            }

            _logger.LogDebug("Raw request: \"{0}\"", rawRequest);

            GeminiUrl url = null;
            try
            {
                url = new GeminiUrl(rawRequest);
            }
            catch (Exception)
            {
                _logger.LogDebug("Requested URL is invalid");
                response.BadRequest("Invalid URL");
                return;
            }

            var request = new Request
            {
                Url = url,
            };

            _logger.LogDebug("Request info:");
            _logger.LogDebug("\tRemote IP: \"{0}\"", request.RemoteIP);
            _logger.LogDebug("\tBaseURL: \"{0}\"", request.Url.NormalizedUrl);
            _logger.LogDebug("\tRoute: \"{0}\"", request.Route);

            
            RequestCallback callback;
            _requestCallbacks.TryGetValue(request.Route, out callback);
            if (callback != null)
            {
                callback(request, response, _logger);
            } 
            else if (_onBadRequestCallback != null)
            {
                _onBadRequestCallback(request, response, _logger);
            } 
            else
            {
                //nope, return a not found
                response.Missing("Could not find a file or route for this URL");
            }
        }

        private string ReadRequest(SslStream sslStream)
        {
            StringBuilder requestData = new StringBuilder();
            int bytes = sslStream.Read(_buffer, 0, _buffer.Length);
            char[] chars = new char[_decoder.GetCharCount(_buffer, 0, bytes)];
            _decoder.GetChars(_buffer, 0, bytes, chars, 0);
            string line = new string(chars);
            if (line.EndsWith("\r\n"))
            {
                return line.TrimEnd('\r', '\n');
            }
            return null;
        }
    }
}
