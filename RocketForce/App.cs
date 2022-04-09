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
using RocketForce.AccessLog;
using Microsoft.Extensions.Logging;

namespace RocketForce
{
    using RequestCallback = System.Action<Request, Response, App>;

    public class App
    {
        //the request line is at most (1024 + 2) characters long. (max sized URL + CRLF)
        const int MaxRequestSize = 1024 + 2;

        /// <summary>
        /// Optional external logger
        /// </summary>
        public ILogger<App> Logger { get; set; }

        /// <summary>
        /// Should we mask IPs of remote clients
        /// </summary>
        public bool IsMaskingRemoteIPs { get; set; } = true;

        private readonly X509Certificate2 serverCertificate;
        private readonly List<Tuple<string, RequestCallback>> routeCallbacks;
        private readonly TcpListener listener;

        private AccessLogger accessLogger;
        private StaticFileModule fileModule;
        private string hostname;
        private int port;

        public App(string hostname, int port, X509Certificate2 certificate, string publicRootPath = null, string accessLogPath = null)
        {
            this.hostname = hostname;
            this.port = port;
            listener = TcpListener.Create(port);

            routeCallbacks =  new List<Tuple<string, RequestCallback>>();

            serverCertificate = certificate;

            if (!String.IsNullOrEmpty(publicRootPath))
            {
                fileModule = new StaticFileModule(publicRootPath);
            }
            if (!String.IsNullOrEmpty(accessLogPath))
            {
                accessLogger = new AccessLogger(accessLogPath);
            }
        }

        public void OnRequest(string route, RequestCallback callback)
            => routeCallbacks.Add(new Tuple<string, RequestCallback>(route.ToLower(), callback));
       
        public void Run()
        {
            if(serverCertificate == null)
            {
                Console.WriteLine("Could not Load Server Key/Certificate. Exiting.");
                return;
            }

            try
            {
                DisplayLaunchBanner();
                listener.Start();
                Logger?.LogInformation("Serving capsule on {0}", listener.Server.LocalEndPoint.ToString());

                while (true)
                {
                    var client = listener.AcceptTcpClient();
                    Task.Run(() => ProcessRequest(client));
                }
            }
            catch (Exception e)
            {
                Logger?.LogError(e, $"Uncaught Exception: {e.Message}");
            }
            finally
            {
                listener.Stop();
            }
        }

        private void DisplayLaunchBanner()
        {
            Console.WriteLine("3... 2... 1...");
            Console.WriteLine(
@"    ____             __        __  ______                     ______
   / __ \____  _____/ /_____  / /_/ ____/___  _____________  / / / /
  / /_/ / __ \/ ___/ //_/ _ \/ __/ /_  / __ \/ ___/ ___/ _ \/ / / / 
 / _, _/ /_/ / /__/ ,< /  __/ /_/ __/ / /_/ / /  / /__/  __/_/_/_/  
/_/ |_|\____/\___/_/|_|\___/\__/_/    \____/_/   \___/\___(_|_|_)   
                                                                    ");

            Console.WriteLine("Gemini Server");
            Console.WriteLine("https://github.com/acidus99/RocketForce");
            Console.WriteLine();
            Console.WriteLine($"Hostname:\t{hostname}");
            Console.WriteLine($"Port:\t\t{port}");
            Console.WriteLine($"Access Log:\t{fileModule?.PublicRoot ?? "Not logging"}");
            Console.WriteLine($"Public Root:\t{fileModule?.PublicRoot ?? "Not serving static files"}");
            Console.WriteLine($"Route Handlers ({routeCallbacks.Count}):");
            foreach(var route in routeCallbacks.Select(x=>x.Item1))
            {
                Console.WriteLine($"    Route: {route}");
            }
        }

        private void ProcessRequest(TcpClient client)
        {
            SslStream sslStream = null;
            try
            {
                sslStream = new SslStream(client.GetStream(), false);
                var received = DateTime.Now;
                var remoteIP = getClientIP(client);
                AccessRecord record = ProcessRequest(remoteIP, sslStream);
                accessLogger?.LogAccess(record, received);
            }
            catch (AuthenticationException e)
            {
                Logger?.LogError("AuthenticationException: {0}", e.Message);
                if (e.InnerException != null)
                {
                    Logger?.LogError("Inner exception: {0}", e.InnerException.Message);
                }
                Logger?.LogError("Authentication failed - closing the connection.");
            }
            //Ensure that an exception processing a request doesn't take down the whole server
            catch (Exception e)
            {

                Logger?.LogError("Uncaught Exception in ProcessRequest! {0}", e.Message);
            }
            finally
            {
                sslStream.Close();
                client.Close();
            }
        }

        /// <summary>
        /// attempts to get the IP address of the remote client, or mask it
        /// </summary>
        private string getClientIP(TcpClient client)
        {
            if (!IsMaskingRemoteIPs && client.Client.RemoteEndPoint != null && (client.Client.RemoteEndPoint is IPEndPoint))
            {
                return (client.Client.RemoteEndPoint as IPEndPoint).Address.ToString();
            }
            return "-";
        }

        private AccessRecord ProcessRequest(string remoteIP, SslStream sslStream)
        {
            sslStream.ReadTimeout = 5000;
            sslStream.AuthenticateAsServer(serverCertificate, false, SslProtocols.Tls12 | SslProtocols.Tls13, false);

            string rawRequest = null;
            var response = new Response(sslStream);
            var accessRecord = new AccessRecord
            {
                Response = response
            };

            try
            {
                // Read a message from the client.
                rawRequest = ReadRequest(sslStream);
                accessRecord.RawRequest = rawRequest;
            } catch(ApplicationException ex)
            {
                response.BadRequest(ex.Message);
                return accessRecord;
            }

            Logger?.LogDebug($"Raw incoming request: \"{rawRequest}\"");

            GeminiUrl url = ValidateRequest(rawRequest, response);
            if(url == null)
            {
                //we already reported the appropriate status to the client, exit
                return accessRecord; 
            }

            var request = new Request
            {
                Url = url,
                RemoteIP = remoteIP
            };
            accessRecord.Request = request;

            Logger?.LogDebug("\tParsed URL: \"{0}\"", request.Url.NormalizedUrl);
            Logger?.LogDebug("\tRoute: \"{0}\"", request.Route);

            //First look if this request matches a route...
            var callback = FindRoute(request.Route);
            if (callback != null)
            {
                callback(request, response, this);
                return accessRecord;
            }

            //nope... look to see if we are handling file system requests
            if (fileModule != null)
            {
                fileModule.HandleRequest(request, response, Logger);
                return accessRecord;
            }
            //nope, return a not found
            response.Missing("Could not find a file or route for this URL");
            return accessRecord;
        }

        /// <summary>
        /// Validates the raw gemini request. If not, writes the appropriate errors on the response object and returns null
        /// if valid, returns GeminiUrl object
        /// </summary>
        private GeminiUrl ValidateRequest(string rawRequest, Response response)
        {
            GeminiUrl ret = null;

            //The order of these checks, and the status codes they return, may seem odd
            //and are organized to pass the gemini-diagnostics check
            //https://github.com/michael-lazar/gemini-diagnostics

            if (rawRequest == null)
            {
                Logger?.LogDebug("Could not read incoming URL");
                response.BadRequest("Missing URL");
                return null;
            }

            Logger?.LogDebug("Raw request: \"{0}\"", rawRequest);

            Uri plainUrl = null;
            try {
                plainUrl = new Uri(rawRequest);
            } catch(Exception)
            {
                response.BadRequest("Invalid URL");
                return null;
            }

            //Silly .NET URI will parse "/" as a "file" scheme with a "/" path! crazy
            //and say it is absolute. So explicitly look for :// to determine if absolute 
            if(!rawRequest.Contains("://"))
            {
                response.BadRequest("Relative URLs not allowed");
                return null;
            }

            if(plainUrl.Scheme != "gemini")
            {
                //refuse to proxy to other protocols
                response.ProxyRefused("protocols");
                return null;
            }
            
            try
            {
                ret = new GeminiUrl(rawRequest);
            }
            catch (Exception)
            {
                response.BadRequest("Invalid URL");
                return null;
            }

            if(ret.Hostname != hostname || ret.Port != port)
            {
                response.ProxyRefused("hosts or ports");
                return null;
            }

            return ret;
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
            if(requestBuffer.Count > MaxRequestSize - 2)
            {
                throw new ApplicationException($"Invalid Request. URL exceeds {MaxRequestSize - 2}");
            }
            //spec requires request use UTF-8
            return Encoding.UTF8.GetString(requestBuffer.ToArray());
        }
    }
}
