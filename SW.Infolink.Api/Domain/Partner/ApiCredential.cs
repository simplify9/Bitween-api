using System;

namespace SW.Infolink.Domain
{
    public class ApiCredential
    {
        private ApiCredential()
        {
        }

        public ApiCredential(string name, string key)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Key = key ?? throw new ArgumentNullException(nameof(key));
        }

        public string Name { get; private set; }
        public string Key { get; private set; }

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
