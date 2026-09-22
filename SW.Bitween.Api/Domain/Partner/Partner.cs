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
        /// What this partner's queued API calls are answered with when the work finished
        /// successfully but produced nothing to send back. <c>null</c> means the partner has no
        /// preference and whoever is answering falls back to its own default.
        /// </summary>
        public int? AcceptedResponseStatusCode { get; set; }

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

        /// <summary>
        /// Whether an empty result is answered 200 rather than 202. <paramref name="fallback"/>
        /// answers for a partner that has no preference of its own.
        /// </summary>
        public bool AnswersOkWhenEmpty(int? fallback) => (AcceptedResponseStatusCode ?? fallback) == 200;

        /// <summary>
        /// 200 and 202 are the only codes worth storing: the reply is built by asking whether the
        /// code is 200, so a third value would quietly behave as 202.
        /// </summary>
        public static bool IsValidAcceptedResponseStatusCode(int? code) => code is null or 200 or 202;
        

    }
}
