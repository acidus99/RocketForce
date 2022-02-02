using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace RocketForce
{
    public class StaticFileModule
    {
        public string PublicRoot { get; set; } = "";


        public StaticFileModule(string path)
        {
            PublicRoot = path;
        }

        public void HandleRequest(Request request, Response response, ILogger<App> logger)
        {
            string attemptedPath = Path.GetFullPath("." + request.Url.Path, PublicRoot);
            attemptedPath = HandleDefaultFile(attemptedPath);
            if(!attemptedPath.StartsWith(PublicRoot))
            {
                logger.LogCritical("Security issue! Attempt to escape public root!");
                response.BadRequest("invalid request");
                return;
            }

            if(File.Exists(attemptedPath))
            {
                SendFile(attemptedPath, response);
                return;
            }

            response.Missing("Cannot locate file for URL");
        }

        /// <summary>
        /// Determines if the requested path is a directory, and rewrites path to use default file
        /// </summary>
        private string HandleDefaultFile(string attemptedPath)
        {
            if(Directory.Exists(attemptedPath))
            {
                return attemptedPath + "index.gmi";
            }
            return attemptedPath;
        }

        /// <summary>
        /// Give a file, attempt to find a mimetype based on its file extension.
        /// TODO: Replace this with a better/extensible MIME Type config system
        /// </summary>
        private string MimeForFile(string filePath)
        {
            switch(ExtensionForFile(filePath))
            {
                case "gmi":
                    return "text/gemini";
                case "txt":
                    return "text/plain";

                case "png":
                    return "image/png";

                case "jpg":
                case "jpeg":
                    return "image/jpeg";

                case "gif":
                    return "image/gif";

                default:
                    return "application/octet-stream";
            }
        }

        private string ExtensionForFile(string filePath)
        {
            var ext = System.IO.Path.GetExtension(filePath);
            return (ext.Length > 1) ? ext.Substring(1) : ext;
        }

        private void SendFile(string filePath, Response response)
        {
            response.Success(MimeForFile(filePath));
            response.Write(File.ReadAllBytes(filePath));
        }
    }
}
