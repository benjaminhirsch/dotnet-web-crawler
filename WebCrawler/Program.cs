using System.Collections;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Net;
using ConsoleTables;
using HtmlAgilityPack;

namespace WebCrawler;

internal static class Program
{
    private static readonly Queue<string?>? UrlsToParse = new();
    private static readonly List<Url> ParsedUrls = new();
    private static string _domain = null!;
    private static readonly List<KeyValuePair<string, string>> FailedUrls = new();

    public static void Main(string?[] args)
    {
        if (args.Length <= 0)
        {
            Console.WriteLine("Missing URL to parse, unable to proceed");
            return;
        }

        UrlsToParse?.Enqueue(args[0]);
        _domain = args[0] ?? throw new InvalidOperationException();

        var watch = Stopwatch.StartNew();
        while (UrlsToParse!.Count > 0)
        {
            var url = UrlsToParse.Dequeue();

            if (url == null) throw new Exception("Unable to dequeue url");

            FetchUrl(url);
            Console.SetCursorPosition(0, Console.CursorTop);
            Console.Write("\r{0}%", url);
        }

        watch.Stop();
        var elapsedMs = watch.Elapsed;

        Console.WriteLine("\n\nParsed " + ParsedUrls.Count + " Urls total\n\n");

        var statusCodes = new OrderedDictionary();

        foreach (var parsedUrl in ParsedUrls)
            if (!statusCodes.Contains(parsedUrl.statusCode.ToString()))
            {
                statusCodes[parsedUrl.statusCode.ToString()] = 1;
            }
            else
            {
                var currentCount = Convert.ToInt32(statusCodes[parsedUrl.statusCode.ToString()]!.ToString());
                statusCodes[parsedUrl.statusCode.ToString()] = currentCount + 1;
            }


        var table = new ConsoleTable("Status Code", "Quantity");
        foreach (DictionaryEntry statusCode in statusCodes) table.AddRow(statusCode.Key, statusCode.Value);

        table.Write(Format.Alternative);

        if (ParsedUrls.Exists(u => u.statusCode == HttpStatusCode.NotFound))
        {
            Console.WriteLine("\n\nURLs not found (404):");
            Console.WriteLine("----------------------------------------");

            var notFoundTable = new ConsoleTable("URL", "Status Code");
            foreach (var url in ParsedUrls.Where(u => u.statusCode == HttpStatusCode.NotFound))
                notFoundTable.AddRow(url.url, url.statusCode);
            notFoundTable.Write(Format.Alternative);
        }

        if (FailedUrls.Count > 0)
        {
            Console.WriteLine("\n\nFailed URLs:");
            Console.WriteLine("----------------------------------------");

            var errorTable = new ConsoleTable("URL", "Error");
            foreach (var urlAndError in FailedUrls) errorTable.AddRow(urlAndError.Key, urlAndError.Value);
            errorTable.Write(Format.Alternative);
        }

        var elapsedTime =
            $"{elapsedMs.Hours:00}:{elapsedMs.Minutes:00}:{elapsedMs.Seconds:00}.{elapsedMs.Milliseconds / 10:00}";
        Console.WriteLine("\n\nTotal execution time: {0}", elapsedTime);
    }

    private static void FetchUrl(string url)
    {
        url = NormalizeUrl(url);

        var web = new HtmlWeb();
        var statusCode = HttpStatusCode.OK;
        web.PostResponse += (_, response) =>
        {
            if (response != null) statusCode = response.StatusCode;
        };

        try
        {
            var htmlDoc = web.Load(url);
            var hrefTags = htmlDoc.DocumentNode.SelectNodes("//a[@href]")?
                .Select(link => link.Attributes["href"])
                .Select(att => att.Value).ToList();

            // Add parsed Url
            ParsedUrls.Add(new Url(url, statusCode));

            if (hrefTags != null)
                foreach (var normalizedUrl in hrefTags.Select(NormalizeUrl).Where(normalizedUrl =>
                             IsValid(normalizedUrl) &&
                             !ParsedUrls.Exists(u => u.url == normalizedUrl) &&
                             UrlsToParse != null &&
                             !UrlsToParse.Contains(normalizedUrl)))
                    UrlsToParse?.Enqueue(normalizedUrl);
        }
        catch (UriFormatException e)
        {
            FailedUrls.Add(new KeyValuePair<string, string>(url, e.Message));
        }
    }

    private static bool IsValid(string? url)
    {
        return url != null &&
               !url.Contains('#') &&
               !url.Contains("tel:") &&
               !url.Contains("mailto:") &&
               !url.Contains("javascript:") &&
               url.StartsWith(_domain) &&
               url.Length > 0;
    }

    private static string NormalizeUrl(string url)
    {
        if (!url.StartsWith(_domain))
        {
            // External or already full qualified URL
            if (url.StartsWith("http")) return url;
            return _domain + url;
        }

        return url;
    }
}