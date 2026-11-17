using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Nook.Api.Storage;

namespace Nook.Api.Features.Publications;

/// <summary>
/// Issues and checks the tokens that stand in for the sign-in cookie in a publication's URLs. A token names its subject,
/// a user or <see cref="Anonymous"/> for a visitor with no session, and the UTC day it was issued, is signed for one item
/// with a secret kept in the data root, comes out the same for the same subject, item and day, and is accepted on its
/// day and the day after, so a book open across midnight keeps working. A token is URL-safe base64 with no padding, so
/// it fits in one path segment.
/// </summary>
/// <remarks>
/// The secret is a 32-byte random key in <see cref="NookStorage.KeysFolder"/>, made the first time a token is issued or
/// read. Data Protection is not used because its output is not the same twice, and a token has to be.
/// </remarks>
public sealed class PublicationTokens
{
    private const string SecretFileName = "publication-tokens.key";
    private const int SecretSize = 32;
    private const int DaySize = sizeof(int);
    private const int SignatureSize = HMACSHA256.HashSizeInBytes;
    private const int SecretReadAttempts = 50;
    private static readonly TimeSpan SecretReadPause = TimeSpan.FromMilliseconds(20);

    /// <summary>Kept apart from anything else the secret could ever sign.</summary>
    private static readonly byte[] Purpose = "nook publication token"u8.ToArray();

    /// <summary>The subject of a token issued to a visitor with no session. No user id, which is a GUID, equals it.</summary>
    public const string Anonymous = "anonymous";

    private readonly NookStorage storage;
    private readonly TimeProvider time;
    private readonly Lock secretLock = new();
    private byte[]? secret;

    public PublicationTokens(NookStorage storage, TimeProvider time)
    {
        this.storage = storage;
        this.time = time;
    }

    /// <summary>
    /// The token that lets <paramref name="subject"/>, a user id or <see cref="Anonymous"/>, read the item's
    /// publication, for today in UTC.
    /// </summary>
    public string Issue(Guid itemId, string subject)
    {
        ArgumentException.ThrowIfNullOrEmpty(subject);

        var day = Today();
        var user = Encoding.UTF8.GetBytes(subject);
        var token = new byte[DaySize + SignatureSize + user.Length];

        BinaryPrimitives.WriteInt32BigEndian(token, day);
        Sign(itemId, day, user, token.AsSpan(DaySize, SignatureSize));
        user.CopyTo(token, DaySize + SignatureSize);

        return Base64Url.EncodeToString(token);
    }

    /// <summary>
    /// Reads a token for the item. It is accepted when it was issued for this item, today or yesterday in UTC, and has
    /// not been changed; <paramref name="subject"/> is then the user id it was issued to, or <see cref="Anonymous"/>.
    /// </summary>
    public bool TryRead(string token, Guid itemId, out string subject)
    {
        subject = "";

        byte[] bytes;
        try
        {
            bytes = Base64Url.DecodeFromChars(token);
        }
        catch (FormatException)
        {
            return false;
        }

        // Base64 can spell the same bytes more than one way; only the spelling Issue gives is the token.
        if (bytes.Length <= DaySize + SignatureSize || Base64Url.EncodeToString(bytes) != token)
        {
            return false;
        }

        var day = BinaryPrimitives.ReadInt32BigEndian(bytes);
        var age = Today() - day;
        if (age is < 0 or > 1)
        {
            return false;
        }

        var user = bytes.AsSpan(DaySize + SignatureSize);
        Span<byte> expected = stackalloc byte[SignatureSize];
        Sign(itemId, day, user, expected);
        if (!CryptographicOperations.FixedTimeEquals(expected, bytes.AsSpan(DaySize, SignatureSize)))
        {
            return false;
        }

        subject = Encoding.UTF8.GetString(user);
        return true;
    }

    private int Today() => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime).DayNumber;

    private void Sign(Guid itemId, int day, ReadOnlySpan<byte> user, Span<byte> signature)
    {
        var message = new byte[Purpose.Length + 16 + DaySize + user.Length];
        var span = message.AsSpan();

        Purpose.CopyTo(span);
        itemId.TryWriteBytes(span.Slice(Purpose.Length, 16), bigEndian: true, out _);
        BinaryPrimitives.WriteInt32BigEndian(span.Slice(Purpose.Length + 16, DaySize), day);
        user.CopyTo(span[(Purpose.Length + 16 + DaySize)..]);

        HMACSHA256.HashData(Secret(), message, signature);
    }

    /// <summary>The secret, loaded once. A failed load is not kept, so the next use tries again.</summary>
    private byte[] Secret()
    {
        if (secret is { } loaded)
        {
            return loaded;
        }

        lock (secretLock)
        {
            return secret ??= LoadOrCreateSecret();
        }
    }

    /// <summary>
    /// Reads the secret, or makes it when there is none. The file is created only when it is not already there, so when
    /// two instances race, both end up with whichever got there first; the one that lost waits for the winner to finish
    /// writing it. A file that still does not hold a whole secret after that wait was left by a write that never
    /// finished, and is replaced.
    /// </summary>
    private byte[] LoadOrCreateSecret()
    {
        var path = Path.Combine(storage.KeysFolder, SecretFileName);
        Directory.CreateDirectory(storage.KeysFolder);

        if (TryCreateSecret(path) is { } created)
        {
            return created;
        }

        for (var attempt = 1; attempt <= SecretReadAttempts; attempt++)
        {
            if (TryReadSecret(path) is { } read)
            {
                return read;
            }

            // The file is still being written.
            Thread.Sleep(SecretReadPause);
        }

        File.Delete(path);
        return TryCreateSecret(path)
            ?? TryReadSecret(path)
            ?? throw new IOException($"The publication token secret '{path}' cannot be read.");
    }

    /// <summary>
    /// Makes the secret and writes it through to disk, or gives null when the file is already there. A write that
    /// fails leaves no file behind.
    /// </summary>
    private static byte[]? TryCreateSecret(string path)
    {
        FileStream file;
        try
        {
            file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }
        catch (IOException) when (File.Exists(path))
        {
            return null;
        }

        var value = RandomNumberGenerator.GetBytes(SecretSize);
        try
        {
            using (file)
            {
                file.Write(value);
                file.Flush(flushToDisk: true);
            }
        }
        catch
        {
            File.Delete(path);
            throw;
        }

        return value;
    }

    /// <summary>The secret in the file, or null when the file is missing, in use, or does not hold a whole secret.</summary>
    private static byte[]? TryReadSecret(string path)
    {
        try
        {
            var value = File.ReadAllBytes(path);
            return value.Length == SecretSize ? value : null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
