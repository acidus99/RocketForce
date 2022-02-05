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

            //Define the app
            App app = new App(
                "localhost",
                1965,
                $"{capsulePath}/public_root",
                CertificateUtils.LoadCertificate($"{capsulePath}/certs/localhost.crt", $"{capsulePath}/certs/localhost.key"),
                "/var/gemini/capsule/logs/access.log"
            );

            //add some dynamic route handlers
            app.OnRequest("/hello", (request, response, app) => {
                response.Success();
                response.Write($"# Hello there! The time is now {DateTime.Now}");
            });

            //external loggers are supported but optional
            using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
                builder.AddSimpleConsole(options =>
                {
                    options.SingleLine = true;
                    options.TimestampFormat = "hh:mm:ss ";
                })
                .SetMinimumLevel(LogLevel.Warning)
            );
            app.Logger = loggerFactory.CreateLogger<App>();

            //start the app listening
            app.Run();

            return 0;
        }
    }
}