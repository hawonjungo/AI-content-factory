using System.Security.Cryptography;
using System.Text;
using AiContentFactory.Application.Publishing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Infrastructure.Security;

/// <summary>
/// AES-GCM encryption of OAuth tokens at rest, keyed by
/// <see cref="PublishingOptions.TokenEncryptionKey"/> (any string - hashed to a
/// 256-bit key). If no key is configured it falls back to storing plaintext
/// with a one-time warning, which is only acceptable for local development.
/// The ciphertext format is <c>v1:</c> + base64(nonce | tag | ciphertext).
/// </summary>
public class AesTokenProtector : ITokenProtector
{
    private const string EncPrefix = "v1:";
    private const string PlainPrefix = "plain:";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[]? _key;
    private readonly ILogger<AesTokenProtector> _logger;
    private bool _warned;

    public AesTokenProtector(IOptions<PublishingOptions> options, ILogger<AesTokenProtector> logger)
    {
        _logger = logger;
        var configured = options.Value.TokenEncryptionKey;
        _key = string.IsNullOrWhiteSpace(configured)
            ? null
            : SHA256.HashData(Encoding.UTF8.GetBytes(configured));
    }

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        if (_key is null)
        {
            WarnOnce();
            return PlainPrefix + plaintext;
        }

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        var blob = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, blob, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, blob, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, blob, NonceSize + TagSize, cipher.Length);

        return EncPrefix + Convert.ToBase64String(blob);
    }

    public string Unprotect(string ciphertext)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);

        if (ciphertext.StartsWith(PlainPrefix, StringComparison.Ordinal))
        {
            return ciphertext[PlainPrefix.Length..];
        }

        if (!ciphertext.StartsWith(EncPrefix, StringComparison.Ordinal))
        {
            // Legacy / unexpected - assume it was stored raw.
            return ciphertext;
        }

        if (_key is null)
        {
            throw new InvalidOperationException(
                "Stored token is encrypted but Publishing:TokenEncryptionKey is not configured.");
        }

        var blob = Convert.FromBase64String(ciphertext[EncPrefix.Length..]);
        var nonce = blob.AsSpan(0, NonceSize);
        var tag = blob.AsSpan(NonceSize, TagSize);
        var cipher = blob.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }

    private void WarnOnce()
    {
        if (_warned)
        {
            return;
        }

        _warned = true;
        _logger.LogWarning(
            "Publishing:TokenEncryptionKey is not set - OAuth tokens are being stored in PLAINTEXT. Set it in every non-local environment.");
    }
}
