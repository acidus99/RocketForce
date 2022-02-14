using System;
using System.IO;

namespace RocketForce.AccessLog
{
    public class AccessLogger :IDisposable
    {
        public string FilePath { get; private set; }
        private StreamWriter fout;

        public AccessLogger(string filePath)
        {
            FilePath = filePath;
            fout = new StreamWriter(FilePath,true, System.Text.Encoding.UTF8);
        }

        public void LogAccess(AccessRecord record, DateTime received)
        {
            var remoteIP = record.Request?.RemoteIP ?? "-";
            var url = record.Request?.Url.NormalizedUrl ?? "";
            var status = record.Response?.StatusCode ?? 0;
            var meta = record.Response?.Meta ?? "";
            var respLen = record.Response?.Length ?? 0;

            fout.WriteLine($"[{received}] {remoteIP} \"{url}\" {status} \"{meta}\" {respLen}"); ;
            //for now flush on every access for people tailing the log
            fout.Flush();
        }

        public void Dispose()
        {
            fout.Flush();
            fout.Close();
            fout.Dispose();
        }
    }
}
