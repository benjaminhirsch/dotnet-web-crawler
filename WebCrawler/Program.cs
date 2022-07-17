using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Net;
using ConsoleTables;
using HtmlAgilityPack;

namespace WebCrawler;

internal static class Program
{
    private static readonly ConcurrentQueue<string?>? UrlsToParse = new();
    private static readonly ConcurrentDictionary<string, Url> ParsedUrls = new();
    private static string _domain = null!;
    private static readonly ConcurrentDictionary<string, string> FailedUrls = new();
    private static bool _doneEnqueueing;

    public static void Main(string?[] args)
    {
        if (args.Length <= 0)
        {
            Console.WriteLine("Missing URL to parse, unable to proceed");
            return;
        }

        // Normalize domain (e.g. remove trailing slash)
        var rawDomain = args[0] ?? throw new InvalidOperationException();
        _domain = rawDomain.EndsWith("/") ? rawDomain.Remove(rawDomain.Length - 1) : rawDomain;

        UrlsToParse?.Enqueue(_domain);
        var watch = Stopwatch.StartNew();


        const int taskCount = 10;
        var workers = new Task[taskCount];

        for (var i = 0; i < taskCount; ++i)
        {
            var workerId = i;
            var task = new Task(() => Worker(workerId));
            workers[i] = task;
            task.Start();
        }

        try
        {
            Task.WaitAll(workers);
        }
        catch (AggregateException ex)
        {
            Console.WriteLine(ex);
        }

        watch.Stop();
        var elapsedMs = watch.Elapsed;

        Console.WriteLine("\n\nParsed " + ParsedUrls.Count + " Urls total\n\n");

        var statusCodes = CalculateStatusCodes();
        var table = new ConsoleTable("Status Code", "Quantity");

        foreach (DictionaryEntry statusCode in statusCodes) table.AddRow(statusCode.Key, statusCode.Value);

        table.Write(Format.Alternative);

        if (ParsedUrls.Any(u => u.Value.statusCode == HttpStatusCode.NotFound))
        {
            Console.WriteLine("\n\nURLs not found (404):");
            Console.WriteLine("----------------------------------------");

            var notFoundTable = new ConsoleTable("URL", "Status Code");
            foreach (var url in ParsedUrls.Where(u => u.Value.statusCode == HttpStatusCode.NotFound))
                notFoundTable.AddRow(url.Key, url.Value.statusCode);
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

    private static void Worker(int workerId)
    {
        Console.WriteLine("Worker {0} is starting.", workerId);
        do
        {
            while (UrlsToParse != null && UrlsToParse.TryDequeue(out var url))
            {
                //Console.WriteLine("Worker {0} is processing item {1}", workerId, op);
                FetchUrl(url);
                Console.SetCursorPosition(0, Console.CursorTop);
                Console.Write(GetSpinner(UrlsToParse.Count % 4));
            }
        } while (!Volatile.Read(ref _doneEnqueueing) || !UrlsToParse!.IsEmpty);

        Console.WriteLine("Worker {0} is stopping.", workerId);
    }

    private static OrderedDictionary CalculateStatusCodes()
    {
        var statusCodes = new OrderedDictionary();

        foreach (var parsedUrl in ParsedUrls)
            if (!statusCodes.Contains(parsedUrl.Value.statusCode.ToString()))
            {
                statusCodes[parsedUrl.Value.statusCode.ToString()] = 1;
            }
            else
            {
                var currentCount = Convert.ToInt32(statusCodes[parsedUrl.Value.statusCode.ToString()]!.ToString());
                statusCodes[parsedUrl.Value.statusCode.ToString()] = currentCount + 1;
            }

        return statusCodes;
    }

    private static void FetchUrl(string? url)
    {
        if (url == null || !IsValid(url)) return;

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
            ParsedUrls.TryAdd(url, new Url(url, statusCode));


            if (hrefTags != null && hrefTags.Count > 0)
                foreach (var newUrl in hrefTags.Select(NormalizeUrl).Where(newUrl =>
                             IsValid(newUrl) &&
                             !ParsedUrls.Any(u => u.Key == newUrl) &&
                             UrlsToParse != null &&
                             !UrlsToParse.Contains(newUrl)))
                    UrlsToParse?.Enqueue(newUrl);

            if (UrlsToParse!.IsEmpty) Volatile.Write(ref _doneEnqueueing, true);
        }
        catch (UriFormatException e)
        {
            FailedUrls.TryAdd(url, e.Message);
        }
    }

    private static bool IsValid(string? url)
    {
        return url != null &&
               url.StartsWith(_domain) &&
               Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var uriResult)
               && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
    }

    private static string? NormalizeUrl(string url)
    {
        // Internal URL
        if (url.StartsWith(_domain)) return url;

        // External or already full qualified URL
        if (url.StartsWith("http://") || url.StartsWith("https://")) return url;

        if (url.StartsWith("/")) return _domain + url;

        if (url.Length > 1 && !url.StartsWith("/") && !url.Contains(":")) return _domain + "/" + url;

        return null;
    }

    private static string GetSpinner(int round)
    {
        return "Working: " + round switch
        {
            0 => "|\r",
            1 => "/\r",
            2 => "-\r",
            3 => "\\\r",
            _ => throw new ArgumentOutOfRangeException(nameof(round), round, null)
        };
    }
}