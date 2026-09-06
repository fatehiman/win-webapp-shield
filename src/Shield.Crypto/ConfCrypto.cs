using System.Security.Cryptography;
using System.Text;

namespace Shield.Crypto;

/// <summary>
/// Password based file encryption used by the .conf files.
///
/// Format (all little endian, no text encoding):
///
///   offset  size  meaning
///   ------  ----  ----------------------------------------------------
///   0       8     magic  "WASENC1\0"
///   8       1     kdf id (1 = PBKDF2-HMAC-SHA256)
///   9       4     kdf iteration count (uint32)
///   13      16    random salt
///   29      12    random nonce (AES-GCM IV)
///   41      16    AES-GCM authentication tag
///   57      n     ciphertext
///
/// Cipher is AES-256-GCM. The key is derived from the password with
/// PBKDF2-HMAC-SHA256. GCM is an authenticated mode, so a wrong password or a
/// modified file is detected and reported instead of producing garbage.
/// </summary>
public static class ConfCrypto
{
    public static readonly byte[] Magic = "WASENC1\0"u8.ToArray();

    private const byte KdfPbkdf2Sha256 = 1;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    public const int DefaultIterations = 310_000;

    public static int HeaderSize => Magic.Length + 1 + 4 + SaltSize + NonceSize + TagSize;

    /// <summary>True when the buffer starts with the WASENC1 magic.</summary>
    public static bool IsEncrypted(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize) return false;
        for (int i = 0; i < Magic.Length; i++)
            if (data[i] != Magic[i]) return false;
        return true;
    }

    public static byte[] Encrypt(byte[] plaintext, string password, int iterations = DefaultIterations)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password must not be empty.", nameof(password));

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] key = DeriveKey(password, salt, iterations);

        byte[] cipher = new byte[plaintext.Length];
        byte[] tag = new byte[TagSize];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintext, cipher, tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        byte[] output = new byte[HeaderSize + cipher.Length];
        int p = 0;
        Magic.CopyTo(output, p); p += Magic.Length;
        output[p++] = KdfPbkdf2Sha256;
        BitConverter.GetBytes((uint)iterations).CopyTo(output, p); p += 4;
        salt.CopyTo(output, p); p += SaltSize;
        nonce.CopyTo(output, p); p += NonceSize;
        tag.CopyTo(output, p); p += TagSize;
        cipher.CopyTo(output, p);
        return output;
    }

    public static byte[] Decrypt(byte[] data, string password)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!IsEncrypted(data))
            throw new CryptoFormatException("File is not a WASENC1 encrypted file.");

        int p = Magic.Length;
        byte kdf = data[p++];
        if (kdf != KdfPbkdf2Sha256)
            throw new CryptoFormatException($"Unsupported key derivation id {kdf}.");

        int iterations = (int)BitConverter.ToUInt32(data, p); p += 4;
        if (iterations is < 1000 or > 20_000_000)
            throw new CryptoFormatException("Iteration count in header is out of range.");

        byte[] salt = data.AsSpan(p, SaltSize).ToArray(); p += SaltSize;
        byte[] nonce = data.AsSpan(p, NonceSize).ToArray(); p += NonceSize;
        byte[] tag = data.AsSpan(p, TagSize).ToArray(); p += TagSize;
        byte[] cipher = data.AsSpan(p).ToArray();

        byte[] key = DeriveKey(password, salt, iterations);
        byte[] plain = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, cipher, tag, plain);
        }
        catch (CryptographicException)
        {
            throw new WrongPasswordException(
                "Could not decrypt the file. The password is wrong, or the file is damaged.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
        return plain;
    }

    /// <summary>Encrypt a UTF-8 text and return the encrypted bytes.</summary>
    public static byte[] EncryptText(string text, string password)
        => Encrypt(new UTF8Encoding(false).GetBytes(text), password);

    /// <summary>Decrypt and return the content as UTF-8 text (BOM stripped).</summary>
    public static string DecryptText(byte[] data, string password)
    {
        byte[] plain = Decrypt(data, password);
        return StripBom(plain);
    }

    public static string StripBom(byte[] utf8)
    {
        if (utf8.Length >= 3 && utf8[0] == 0xEF && utf8[1] == 0xBB && utf8[2] == 0xBF)
            return Encoding.UTF8.GetString(utf8, 3, utf8.Length - 3);
        return Encoding.UTF8.GetString(utf8);
    }

    private static byte[] DeriveKey(string password, byte[] salt, int iterations)
        => Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, KeySize);
}

public class CryptoFormatException : Exception
{
    public CryptoFormatException(string message) : base(message) { }
}

public sealed class WrongPasswordException : CryptoFormatException
{
    public WrongPasswordException(string message) : base(message) { }
}
