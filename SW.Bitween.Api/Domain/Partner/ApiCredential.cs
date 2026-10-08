using System;

namespace SW.Bitween.Domain
{
    public class ApiCredential
    {
        private ApiCredential()
        {
        }

        /// <param name="key">The key itself; only its hash is kept. See <see cref="PartnerKeyHash"/>.</param>
        public ApiCredential(string name, string key)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            if (key == null) throw new ArgumentNullException(nameof(key));
            Key = PartnerKeyHash.Stored(key);
            if (!PartnerKeyHash.IsHashed(key)) KeyPrefix = key.Length > 5 ? key[..5] : key;
        }

        public string Name { get; private set; }

        /// <summary>The key's hash — <c>sha256:...</c> — or, on a row not yet converted, the key.</summary>
        public string Key { get; private set; }

        /// <summary>The first characters of the key, so a screen can tell one key from another.</summary>
        public string KeyPrefix { get; private set; }

        public override bool Equals(object obj)
        {
            return obj is ApiCredential credential &&
                   Name == credential.Name;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Name);
        }
    }
}
