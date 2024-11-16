using System;
using System.IO;

namespace RocketForce.Logging;

public class W3CLogger
{
    private bool haveWrittenHeader;
    private readonly TextWriter logger;

    public W3CLogger()
        : this(Console.Out)
    {
    }

    public W3CLogger(TextWriter logger)
    {
        this.logger = logger;
        haveWrittenHeader = false;
    }

    public void LogAccess(AccessRecord record)
    {
        if (!haveWrittenHeader)
        {
            haveWrittenHeader = true;
            WriteHeader();
        }

        logger.WriteLine(
            $"{record.Date} {record.Time} {record.RemoteIP} {record.Url} {record.StatusCode} \"{record.Meta}\" {record.SentBytes} {record.TimeTaken}");
    }

    public void LogException(string remoteIp, string what, Exception? ex = null)
    {
        var date = AccessRecord.FormatDate(DateTime.UtcNow);
        var time = AccessRecord.FormatTime(DateTime.UtcNow);
        if (ex == null)
        {
            logger.WriteLine($"{date} {time} {remoteIp} ERROR: {what}");
        }
        else
        {
            logger.WriteLine($"{date} {time} {remoteIp} ERROR: {what} {ex.Message}");
            logger.WriteLine(ex.StackTrace);
        }
    }

    private void WriteHeader()
    {
        logger.WriteLine("#Version: 1.0");
        logger.WriteLine($"#Date: {DateTime.Now.ToUniversalTime().ToString("dd-MMM-yyyy HH:mm:ss")}");
        logger.WriteLine("#Fields: date time c-ip cs-uri sc-status x-meta sc-bytes sc-time-taken");
    }
}