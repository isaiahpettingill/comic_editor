using System.Security.Cryptography;
using Avalonia.Platform.Storage;

namespace ComicEditor.Editing;

// One chosen document handle; reopened from its bookmark/path after recovery.
public sealed class ProjectFile(IStorageFile file, string? diskHash)
{
    public IStorageFile File { get; } = file;
    public string? DiskHash { get; private set; } = diskHash;
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public async Task<byte[]> Read()
    {
        await using var stream = await File.OpenReadAsync();
        using var buffer = new MemoryStream(); await stream.CopyToAsync(buffer); return buffer.ToArray();
    }
    public async Task Write(byte[] bytes, bool checkExternalChanges)
    {
        if (checkExternalChanges && (DiskHash is null || Hash(await Read()) != DiskHash))
            throw new IOException("The file changed outside ComicEditor. Reopen it, or use Save as to keep your edits in another file.");
        var path = File.TryGetLocalPath();
        if (path is not null && !OperatingSystem.IsAndroid() && !OperatingSystem.IsBrowser())
        {
            if (System.IO.File.Exists(path)) path = new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path;
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await System.IO.File.WriteAllBytesAsync(temporary, bytes);
                System.IO.File.Move(temporary, path, overwrite: true);
            }
            finally { if (System.IO.File.Exists(temporary)) System.IO.File.Delete(temporary); }
        }
        else
        {
            await using var stream = await File.OpenWriteAsync();
            if (stream.CanSeek) stream.SetLength(0);
            await stream.WriteAsync(bytes); await stream.FlushAsync();
        }
        DiskHash = Hash(bytes);
    }
}
