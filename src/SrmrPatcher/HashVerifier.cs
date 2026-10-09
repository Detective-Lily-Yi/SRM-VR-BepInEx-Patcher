using System.Security.Cryptography;

namespace SrmrPatcher
{
    internal sealed class HashVerifier
    {
        public HashVerifier(bool continueOnMismatch = false)
        {
            ContinueOnMismatch = continueOnMismatch;
        }

        public bool ContinueOnMismatch { get; private set; }

        public string Verify(string path, IEnumerable<string> allowedHashes, string description)
        {
            string actualHash = Hashing.CalculateSha256(path);
            string[] expectedHashes = allowedHashes
                .Select(hash => hash.ToUpperInvariant())
                .ToArray();
            if (expectedHashes.Contains(actualHash, StringComparer.Ordinal))
            {
                return actualHash;
            }

            Console.Error.WriteLine(
                $"WARNING: {description} does not match a supported SHA-256 hash.");
            Console.Error.WriteLine($"  Actual:   {actualHash}");
            Console.Error.WriteLine($"  Expected: {string.Join(Environment.NewLine + "            ", expectedHashes)}");
            Console.Error.WriteLine(
                "The patch is build-specific. Continuing may fail or produce unusable files.");
            if (ContinueOnMismatch)
            {
                return actualHash;
            }

            Console.Error.Write("Continue despite this and later hash mismatches? [y/N]: ");
            string answer = Console.ReadLine() ?? string.Empty;
            if (answer is not ("y" or "Y" or "yes" or "YES" or "Yes"))
            {
                throw new InvalidOperationException(
                    "Installation cancelled because a file hash did not match");
            }

            ContinueOnMismatch = true;
            Console.Error.WriteLine(
                "WARNING: Continuing with unsupported hashes at the user's request.");
            return actualHash;
        }
    }

    internal static class Hashing
    {
        public static string CalculateSha256(string path)
        {
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }

        public static string CalculateSha256(ReadOnlySpan<byte> data) =>
            Convert.ToHexString(SHA256.HashData(data));
    }
}
