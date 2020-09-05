using SW.EfCoreExtensions;
using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Text;

namespace SW.Infolink.Domain
{
    class Partner : BaseEntity
    {

        private Partner()
        {
        }

        public Partner(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException($"'{nameof(name)}' cannot be null or whitespace", nameof(name));
            }

            Name = name;
            _Subscriptions = new HashSet<Subscription>();
            _ApiCredentials = new HashSet<ApiCredential>();
            
        }

        public string Name { get; set; }


        readonly HashSet<Subscription> _Subscriptions;
        public IReadOnlyCollection<Subscription> Subscriptions => _Subscriptions;

        readonly HashSet<ApiCredential> _ApiCredentials;

        public IReadOnlyCollection<ApiCredential> ApiCredentials => _ApiCredentials;
        public void SetApiCredentials(IEnumerable<ApiCredential> apiCredentials)
        {
            _ApiCredentials.Update(apiCredentials);
        }

    }
}
