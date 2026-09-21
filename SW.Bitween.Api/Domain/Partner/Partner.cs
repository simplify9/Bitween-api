using SW.EfCoreExtensions;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Text;

namespace SW.Bitween.Domain
{
    public class Partner : BaseEntity
    {
        public const string TemplateVariableNamePrefix = "partner";
        public const int SystemId = 1;

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
        public Dictionary<string,string> AdapterProperties { get; set; }

        /// <summary>
        /// Names within <see cref="AdapterProperties"/> whose values are never returned by the API
        /// in clear. Opt-in and freely reversible: a partner property is ordinary text until
        /// someone marks it, which is why the list lives here rather than being inferred from the
        /// key's name — inferring would have hidden values that were readable yesterday.
        /// </summary>
        public List<string> SecretProperties { get; set; } = new();

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
