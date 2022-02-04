using System;
using System.Net;

using Microsoft.Extensions.Logging;
using RocketForce;

namespace RocketForceExample
{
    class Server
    {
        static int Main(string[] args)
        {
            string capsulePath = "/var/gemini/capsule";

            using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
                builder.AddSimpleConsole(options =>
                {
                    options.SingleLine = true;
                    options.TimestampFormat = "hh:mm:ss ";
                })
                .SetMinimumLevel(LogLevel.Debug)
            );

            ILogger<App> logger = loggerFactory.CreateLogger<App>();

            App app = new App(
                "localhost",
                1965,
                $"{capsulePath}/public_root",
                CertificateUtils.LoadCertificate($"{capsulePath}/certs/localhost.crt", $"{capsulePath}/certs/localhost.key"),
                logger
            );

            app.OnRequest("/hello", (request, response, app) => {
                response.Success();
                response.Write($"# Hello there! The time is now {DateTime.Now}");
            });
            app.Run();

            return 0;
        }
    }
}