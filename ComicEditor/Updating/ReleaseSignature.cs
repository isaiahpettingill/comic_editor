using System.Security.Cryptography;

namespace ComicEditor.Updating;

internal static class ReleaseSignature
{
    public const int Size = 384;
    public static string PublicKey { get; } = LoadPublicKey();

    private static string LoadPublicKey()
    {
        using var stream = typeof(ReleaseSignature).Assembly.GetManifestResourceStream("ComicEditor.DesktopSigningKey")
            ?? throw new InvalidOperationException("The release verification key is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static void Verify(byte[] hash, byte[] signature, string publicKey)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKey);
        if (rsa.KeySize != 3072 || signature.Length != Size || !rsa.VerifyHash(hash, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
            throw new InvalidDataException("The update's digital signature is invalid. The installed application has not changed.");
    }
}
