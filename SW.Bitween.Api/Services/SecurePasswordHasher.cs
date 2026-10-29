using System;
using System.Security.Cryptography;


namespace SW.Bitween
{
    public static class SecurePasswordHasher
    {
        /// <summary>
        /// Size of salt.
        /// </summary>
        private const int SaltSize = 16;

        /// <summary>
        /// What a stored hash looks like. V1 derived 20 bytes with PBKDF2-HMAC-SHA1 at 10,000
        /// iterations; V2 derives 32 with SHA256 at 210,000. New passwords are written as V2 and
        /// V1 is still verified, so accounts created before the change keep working.
        /// </summary>
        private const string V1Prefix = "$SWHASH$V1$";

        private const string V2Prefix = "$SWHASH$V2$";

        private const int V1HashSize = 20;

        private const int V2HashSize = 32;

        /// <summary>
        /// OWASP's floor for PBKDF2-HMAC-SHA256. The cost is the point: it is what makes an offline
        /// guess against a stolen table expensive.
        /// </summary>
        private const int DefaultIterations = 210_000;

        /// <summary>
        /// Creates a hash from a password.
        /// </summary>
        /// <param name="password">The password.</param>
        /// <param name="iterations">Number of iterations.</param>
        /// <returns>The hash.</returns>
        private static string Hash(string password, int iterations)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltSize);

            var hash = Rfc2898DeriveBytes.Pbkdf2(
                password, salt, iterations, HashAlgorithmName.SHA256, V2HashSize);

            // Salt first, then hash - Verify reads them back by the same offsets.
            var hashBytes = new byte[SaltSize + V2HashSize];
            salt.CopyTo(hashBytes, 0);
            hash.CopyTo(hashBytes, SaltSize);

            return $"{V2Prefix}{iterations}${Convert.ToBase64String(hashBytes)}";
        }

        /// <summary>
        /// Creates a hash from a password.
        /// </summary>
        /// <param name="password">The password.</param>
        /// <returns>The hash.</returns>
        public static string Hash(string password)
        {
            return Hash(password, DefaultIterations);
        }

        /// <summary>
        /// Whether a stored hash is weaker than what <see cref="Hash(string)"/> makes today, so it
        /// should be replaced once the password is known again.
        /// </summary>
        public static bool NeedsRehash(string hashString)
        {
            // V1 is PBKDF2-HMAC-SHA1 at 10,000 iterations; a V2 hash made with fewer iterations than today's
            // is as weak as its count. Either is replaced on the next sign-in that proves the password.
            if (hashString == null || hashString.StartsWith(V1Prefix, StringComparison.Ordinal)) return true;
            if (!hashString.StartsWith(V2Prefix, StringComparison.Ordinal)) return false;
            var parts = hashString[V2Prefix.Length..].Split('$');
            return parts.Length != 2 || !int.TryParse(parts[0], out var iterations) || iterations < DefaultIterations;
        }

        /// <summary>
        /// Checks if hash is supported.
        /// </summary>
        /// <param name="hashString">The hash.</param>
        /// <returns>Is supported?</returns>
        public static bool IsHashSupported(string hashString)
        {
            return hashString != null
                   && (hashString.StartsWith(V1Prefix, StringComparison.Ordinal)
                       || hashString.StartsWith(V2Prefix, StringComparison.Ordinal));
        }

        /// <summary>
        /// Verifies a password against a hash.
        /// </summary>
        /// <param name="password">The password.</param>
        /// <param name="hashedPassword">The hash.</param>
        /// <returns>Could be verified?</returns>
        public static bool Verify(string password, string hashedPassword)
        {
            // Check hash
            if (!IsHashSupported(hashedPassword))
            {
                throw new NotSupportedException("The hashtype is not supported");
            }

            var isV1 = hashedPassword.StartsWith(V1Prefix, StringComparison.Ordinal);
            var algorithm = isV1 ? HashAlgorithmName.SHA1 : HashAlgorithmName.SHA256;
            var hashSize = isV1 ? V1HashSize : V2HashSize;

            // Both prefixes are the same length, so one slice serves either version.
            var splittedHashString = hashedPassword[V2Prefix.Length..].Split('$');
            if (splittedHashString.Length != 2
                || !int.TryParse(splittedHashString[0], out var iterations)
                || iterations <= 0)
            {
                return false;
            }

            // A stored hash of the wrong length is malformed, not a wrong password. Returning
            // false rather than throwing keeps one corrupt row from 500ing the sign-in endpoint.
            var hashBytes = new byte[SaltSize + hashSize];
            if (!Convert.TryFromBase64String(splittedHashString[1], hashBytes, out var decoded)
                || decoded != hashBytes.Length)
            {
                return false;
            }

            var salt = hashBytes.AsSpan(0, SaltSize).ToArray();

            var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, algorithm, hashSize);

            // Constant time: comparing byte by byte and returning at the first difference leaks how
            // much of the hash was guessed correctly through how long the answer took.
            return CryptographicOperations.FixedTimeEquals(
                hash, hashBytes.AsSpan(SaltSize, hashSize));
        }
    }
}
