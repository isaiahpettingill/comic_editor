using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace ComicEditor.Updating;

public sealed record UpdateRelease(Version Version, string Runtime, string AssetName, Uri DownloadUrl, string Sha256, long Size);
public sealed record UpdateInstallation(Version Version, string Runtime);

public static class ReleaseClient
{
    public const string Repository = "isaiahpettingill/comic_editor";
    public const string ReleasesUrl = "https://github.com/" + Repository + "/releases/latest";
    private static readonly HttpClient Client = CreateClient();
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ComicEditor", "updates");
    public static Version AppVersion => typeof(ReleaseClient).Assembly.GetName().Version ?? new Version(0, 0, 0);
    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ComicEditor-Updater/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
    public static string? AssetName(string runtime) => runtime switch
    {
        "win-x64" => "ComicEditor-win-x64-setup.exe",
        "linux-x64" => "ComicEditor-linux-x64.tar.gz",
        "osx-x64" => "ComicEditor-osx-x64.tar.gz",
        "osx-arm64" => "ComicEditor-osx-arm64.tar.gz",
        _ => null
    };
    public static UpdateInstallation? ReadInstallation(string directory)
    {
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "update.json")));
            var root = json.RootElement;
            var runtime = root.GetProperty("runtime").GetString();
            if (runtime is null || AssetName(runtime) is null || !Version.TryParse(root.GetProperty("version").GetString(), out var version)) return null;
            return new(version, runtime);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException) { return null; }
    }
    public static UpdateRelease? Select(string json, UpdateInstallation installation)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v'), out var version) || Normalize(version) <= Normalize(installation.Version)) return null;
        var name = AssetName(installation.Runtime); if (name is null) return null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != name) continue;
            var digest = asset.TryGetProperty("digest", out var hash) ? hash.GetString() : null;
            var size = asset.GetProperty("size").GetInt64();
            if (digest is null || !digest.StartsWith("sha256:", StringComparison.Ordinal) || digest.Length != 71 || !digest[7..].All(Uri.IsHexDigit) || size is <= 0 or > 1_073_741_824)
                throw new InvalidDataException("The release does not include a valid download checksum and size. Try again after publication finishes.");
            var expected = $"https://github.com/{Repository}/releases/download/{tag}/{name}";
            if (asset.GetProperty("browser_download_url").GetString() != expected) throw new InvalidDataException("The release download is outside the expected repository.");
            var hasSignature = root.GetProperty("assets").EnumerateArray().Any(candidate =>
                candidate.GetProperty("name").GetString() == name + ".sig" &&
                candidate.GetProperty("size").GetInt64() == ReleaseSignature.Size &&
                candidate.GetProperty("browser_download_url").GetString() == expected + ".sig");
            if (!hasSignature) throw new InvalidDataException("The release signature is missing. Try again after publication finishes.");
            return new(version, installation.Runtime, name, new Uri(expected), digest[7..], size);
        }
        throw new InvalidDataException("The new release does not yet have a package for this platform. Try again shortly.");
    }
    public static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
    public static async Task<UpdateRelease?> Check(UpdateInstallation installation, CancellationToken cancellation = default, HttpClient? client = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var json = await (client ?? Client).GetStringAsync($"https://api.github.com/repos/{Repository}/releases/latest", timeout.Token);
        return Select(json, installation);
    }
    public static Task<string> Download(UpdateRelease release, string directory, IProgress<double>? progress = null, CancellationToken cancellation = default, HttpClient? client = null)
        => DownloadSigned(release, directory, ReleaseSignature.PublicKey, progress, cancellation, client);

    internal static async Task<string> DownloadSigned(UpdateRelease release, string directory, string publicKey, IProgress<double>? progress = null, CancellationToken cancellation = default, HttpClient? client = null)
    {
        if (release.AssetName != AssetName(release.Runtime)) throw new InvalidDataException("Unexpected update asset name.");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, release.AssetName); var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromMinutes(15));
        try
        {
            byte[] digest;
            using var response = await (client ?? Client).GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync(timeout.Token))
            await using (var output = File.Create(temporary))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920]; long total = 0;
                while (true)
                {
                    var count = await source.ReadAsync(buffer, timeout.Token); if (count == 0) break;
                    total += count;
                    if (total > release.Size) throw new InvalidDataException("The download exceeds its published size.");
                    hash.AppendData(buffer, 0, count); await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
                    progress?.Report((double)total / release.Size);
                }
                digest = hash.GetHashAndReset();
                if (total != release.Size || !Convert.ToHexString(digest).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Update verification failed. The installed application has not changed.");
            }
            using var signed = await (client ?? Client).GetAsync(new Uri(release.DownloadUrl.AbsoluteUri + ".sig"), HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            signed.EnsureSuccessStatusCode();
            await using var signatureStream = await signed.Content.ReadAsStreamAsync(timeout.Token);
            var signature = new byte[ReleaseSignature.Size + 1];
            var length = await signatureStream.ReadAtLeastAsync(signature, signature.Length, throwOnEndOfStream: false, timeout.Token);
            if (length != ReleaseSignature.Size) throw new InvalidDataException("The update signature has an invalid length.");
            ReleaseSignature.Verify(digest, signature[..length], publicKey);
            File.Move(temporary, destination, overwrite: true); return destination;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
