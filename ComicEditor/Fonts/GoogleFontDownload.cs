using System.Net;
using System.Text.RegularExpressions;

namespace ComicEditor.Fonts;

public sealed record DownloadedFont(string Family, string FileName, byte[] Data, string License, string SourceUrl);

public static partial class GoogleFontDownload
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(60) };
    private const string Repository = "https://raw.githubusercontent.com/google/fonts/main/";
    private const int MaximumFontBytes = 32 * 1024 * 1024;

    public static async Task<bool> IsReachableAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, Repository + "ofl/notosans/METADATA.pb");
            using var response = await Client.SendAsync(request, timeout.Token);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
    }

    public static string FamilyFromLink(string link)
    {
        if (!Uri.TryCreate(link.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.Host is not ("fonts.google.com" or "fonts.googleapis.com"))
            throw new ArgumentException("Paste a Google Fonts specimen or CSS link.");
        string? family = null;
        if (uri.AbsolutePath.StartsWith("/specimen/", StringComparison.Ordinal))
            family = Uri.UnescapeDataString(uri.AbsolutePath[10..].Split('/')[0].Replace('+', ' '));
        else
            foreach (var query in uri.Query.TrimStart('?').Split('&'))
                if (query.StartsWith("family=", StringComparison.Ordinal))
                { family = Uri.UnescapeDataString(query[7..].Replace('+', ' ')).Split(':', '|')[0]; break; }
        if (string.IsNullOrWhiteSpace(family) || family.Length > 100 || !family.All(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '-'))
            throw new ArgumentException("The link must identify one font family, for example fonts.google.com/specimen/Anton.");
        return family.Trim();
    }

    public static async Task<DownloadedFont> FetchAsync(string link, CancellationToken cancellationToken = default)
    {
        var family = FamilyFromLink(link);
        var slug = new string(family.Where(char.IsAsciiLetterOrDigit).ToArray()).ToLowerInvariant();
        foreach (var licenseDirectory in new[] { "ofl", "apache", "ufl" })
        {
            var directory = Repository + licenseDirectory + "/" + slug + "/";
            using var metadataResponse = await Client.GetAsync(directory + "METADATA.pb", cancellationToken);
            if (metadataResponse.StatusCode == HttpStatusCode.NotFound) continue;
            metadataResponse.EnsureSuccessStatusCode();
            var metadata = await metadataResponse.Content.ReadAsStringAsync(cancellationToken);
            var faces = FontBlocks().Matches(metadata).Select(m => m.Value).ToArray();
            var normal = faces.FirstOrDefault(s => s.Contains("style: \"normal\"") && s.Contains("weight: 400")) ?? faces.FirstOrDefault();
            var filename = normal is null ? "" : FileName().Match(normal).Groups[1].Value;
            if (filename.Length == 0 || filename.IndexOfAny(['/', '\\']) >= 0)
                throw new InvalidDataException("The Google Fonts listing has no downloadable font face.");
            var source = directory + Uri.EscapeDataString(filename);
            using var response = await Client.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaximumFontBytes) throw new InvalidDataException("Font exceeds 32 MiB.");
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var output = new MemoryStream();
            var buffer = new byte[65536];
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (output.Length + read > MaximumFontBytes) throw new InvalidDataException("Font exceeds 32 MiB.");
                output.Write(buffer, 0, read);
            }
            var licenseName = licenseDirectory == "ofl" ? "OFL.txt" : licenseDirectory == "ufl" ? "UFL.txt" : "LICENSE.txt";
            var license = await Client.GetStringAsync(directory + licenseName, cancellationToken);
            return new DownloadedFont(family, filename, output.ToArray(), license, source);
        }
        throw new InvalidDataException("That family was not found in the Google Fonts catalog.");
    }

    [GeneratedRegex(@"fonts\s*\{[^}]*\}")]
    private static partial Regex FontBlocks();
    [GeneratedRegex("filename:\\s*\"([^\"]+)\"")]
    private static partial Regex FileName();
}
