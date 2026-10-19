using System.Buffers.Binary;
using System.Security.Cryptography;

namespace InfoID.Infrastructure.Backup;

/// <summary>
/// Optional password protection for backup files (they contain cardholder data).
///
/// File layout: "IIDBKENC" (8) | version (1) | PBKDF2 iterations (4, LE) | salt (16) |
/// IV (16) | AES-256-CBC ciphertext of the backup ZIP | HMAC-SHA256 (32).
/// Keys: PBKDF2-SHA256(password, salt) -> 64 bytes = 32 encryption + 32 MAC.
/// Encrypt-then-MAC: the HMAC covers everything before it and is checked before a single
/// byte is decrypted, so a wrong password or a modified file is detected reliably and
/// never produces garbage. Streams throughout -- backups can be large.
/// </summary>
public static class BackupCrypto
{
    private static readonly byte[] Magic = "IIDBKENC"u8.ToArray();
    private const byte FormatVersion = 1;
    private const int DefaultIterations = 210_000;
    private const int MaxIterations = 5_000_000;
    private const int HeaderLength = 8 + 1 + 4 + 16 + 16;
    private const int MacLength = 32;

    /// <summary>True when the file starts with the encrypted-backup signature.</summary>
    public static bool IsEncrypted(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> head = stackalloc byte[8];
            return stream.ReadAtLeast(head, 8, throwOnEndOfStream: false) == 8 && head.SequenceEqual(Magic);
        }
        catch (IOException)
        {
            return false;
        }
    }

    public static void Encrypt(string plainPath, string encryptedPath, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var iv = RandomNumberGenerator.GetBytes(16);
        var (encKey, macKey) = DeriveKeys(password, salt, DefaultIterations);
        try
        {
            using var output = new FileStream(encryptedPath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var mac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, macKey);

            var header = new byte[HeaderLength];
            Magic.CopyTo(header, 0);
            header[8] = FormatVersion;
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(9, 4), DefaultIterations);
            salt.CopyTo(header, 13);
            iv.CopyTo(header, 29);
            output.Write(header);
            mac.AppendData(header);

            using (var aes = Aes.Create())
            {
                aes.Key = encKey;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using var macWriter = new MacWriteStream(output, mac);
                using var crypto = new CryptoStream(macWriter, aes.CreateEncryptor(), CryptoStreamMode.Write, leaveOpen: true);
                using var input = File.OpenRead(plainPath);
                input.CopyTo(crypto, 1 << 16);
                crypto.FlushFinalBlock();
            }

            output.Write(mac.GetHashAndReset());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encKey);
            CryptographicOperations.ZeroMemory(macKey);
        }
    }

    /// <summary>Decrypts to plainPath. Returns false for a wrong password or a damaged /
    /// modified file (the two can't be told apart, by design).</summary>
    public static bool TryDecrypt(string encryptedPath, string plainPath, string password)
    {
        using var input = File.OpenRead(encryptedPath);
        if (input.Length < HeaderLength + 16 + MacLength) return false;

        var header = new byte[HeaderLength];
        input.ReadExactly(header);
        if (!header.AsSpan(0, 8).SequenceEqual(Magic) || header[8] != FormatVersion) return false;
        var iterations = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(9, 4));
        if (iterations < 1 || iterations > MaxIterations) return false;
        var salt = header.AsSpan(13, 16).ToArray();
        var iv = header.AsSpan(29, 16).ToArray();

        var (encKey, macKey) = DeriveKeys(password, salt, iterations);
        try
        {
            var cipherLength = input.Length - HeaderLength - MacLength;

            // 1) Verify the HMAC over header + ciphertext before decrypting anything.
            using (var mac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, macKey))
            {
                mac.AppendData(header);
                var buffer = new byte[1 << 16];
                var remaining = cipherLength;
                while (remaining > 0)
                {
                    var read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                    if (read <= 0) return false;
                    mac.AppendData(buffer, 0, read);
                    remaining -= read;
                }

                var expected = new byte[MacLength];
                input.ReadExactly(expected);
                if (!CryptographicOperations.FixedTimeEquals(expected, mac.GetHashAndReset())) return false;
            }

            // 2) Decrypt.
            input.Position = HeaderLength;
            using var aes = Aes.Create();
            aes.Key = encKey;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            using var limited = new LimitedReadStream(input, cipherLength);
            using var crypto = new CryptoStream(limited, aes.CreateDecryptor(), CryptoStreamMode.Read);
            using var output = new FileStream(plainPath, FileMode.Create, FileAccess.Write, FileShare.None);
            crypto.CopyTo(output, 1 << 16);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encKey);
            CryptographicOperations.ZeroMemory(macKey);
        }
    }

    private static (byte[] EncKey, byte[] MacKey) DeriveKeys(string password, byte[] salt, int iterations)
    {
        var keys = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 64);
        var enc = keys.AsSpan(0, 32).ToArray();
        var mac = keys.AsSpan(32, 32).ToArray();
        CryptographicOperations.ZeroMemory(keys);
        return (enc, mac);
    }

    /// <summary>Writes through to the file and feeds every byte to the HMAC.</summary>
    private sealed class MacWriteStream : Stream
    {
        private readonly Stream _inner;
        private readonly IncrementalHash _mac;
        public MacWriteStream(Stream inner, IncrementalHash mac) { _inner = inner; _mac = mac; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count)
        {
            _mac.AppendData(buffer, offset, count);
            _inner.Write(buffer, offset, count);
        }
    }

    /// <summary>Reads at most <c>length</c> bytes (the ciphertext, not the trailing HMAC).</summary>
    private sealed class LimitedReadStream : Stream
    {
        private readonly Stream _inner;
        private long _remaining;
        public LimitedReadStream(Stream inner, long length) { _inner = inner; _remaining = length; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_remaining <= 0) return 0;
            var read = _inner.Read(buffer, offset, (int)Math.Min(count, _remaining));
            _remaining -= read;
            return read;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
