using SW.EfCoreExtensions;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Text;

namespace SW.Infolink.Domain
{
    class Partner : BaseEntity
    {
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
