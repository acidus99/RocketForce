using Cuipod;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.Logging;
using System;
using System.Security.Cryptography.X509Certificates;

namespace CuipodExample
{
    class Server
    {
        static int Main(string[] args)
        {
            X509Certificate2 cert = CertificateUtils.LoadCertificate("certificate.crt", "privatekey.key");
            return AppMain("pages/", cert);
        }

        private static int AppMain(string directoryToServe, X509Certificate2 certificate)
        {
            using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
                builder
                .AddSimpleConsole(options =>
                {
                    options.SingleLine = true;
                    options.TimestampFormat = "hh:mm:ss ";
                })
                .SetMinimumLevel(LogLevel.Debug)
            );

            ILogger<App> logger = loggerFactory.CreateLogger<App>();

            App app = new App(
                directoryToServe,
                certificate,
                logger
            );

            // Serve files
            app.OnRequest("/", (request, response, logger) => {
                response.RenderFileContent("index.gmi");
            });

            // Input example
            app.OnRequest("/input", (request, response, logger) => {
                if (request.Parameters == null)
                {
                    response.SetInputHint("Please enter something: ");
                    response.Status = StatusCode.Input;
                }
                else
                {
                    // redirect to show/ route with input parameters
                    response.SetRedirectURL(request.BaseURL + "/show?" + request.Parameters);
                    response.Status = StatusCode.RedirectTemp;
                }
            });

            app.OnRequest("/show", (request, response, logger) => {
                if (request.Parameters == null)
                {
                    // redirect to input
                    response.SetRedirectURL(request.BaseURL + "/input");
                    response.Status = StatusCode.RedirectTemp;
                }
                else
                {
                    // show what has been entered
                    response.RenderPlainTextLine("# " + request.Parameters);
                }
            });

            // Or dynamically render content
            app.OnRequest("/dynamic/content", (request, response, logger) => {
                response.RenderPlainTextLine("# woah much dynamic content!");
            });

            // Optional but nice. In case it is specified and client will do a bad route 
            // request we will respond with Success status and render result from this lambda
            app.OnBadRequest((request, response, logger) => {
                response.RenderPlainTextLine("# Ohh No!!! Request is bad :(");
            });

            app.Run();

            return 0;
        }
    }
}
