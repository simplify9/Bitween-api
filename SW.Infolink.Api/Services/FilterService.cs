
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using System;
using Microsoft.Extensions.Logging;
using SW.Infolink.Domain;
using Microsoft.EntityFrameworkCore;
using SW.EfCoreExtensions;
using SW.PrimitiveTypes;
using SW.Infolink.Model;

namespace SW.Infolink
{
    internal class FilterService
    {

        readonly IServiceScopeFactory ssf;
        readonly ILogger<FilterService> logger;
        readonly ReaderWriterLockSlim documentFilterLock = new ReaderWriterLockSlim();
        readonly IDictionary<int, DocumentFilter> documentFilterDictionary = new Dictionary<int, DocumentFilter>();


        DateTime documentFilterPreparedOn;

        public FilterService(IServiceScopeFactory ssf, ILogger<FilterService> logger)
        {
            this.ssf = ssf;
            this.logger = logger;
            documentFilterPreparedOn = DateTime.MinValue;
        }

        void Prepare()
        {
            using var scope = ssf.CreateScope();

            var repo = scope.ServiceProvider.GetRequiredService<InfolinkDbContext>();

            documentFilterDictionary.Clear();

            var docs = repo.List<Document>();

            foreach (var doc in docs)
            {

                var df = new DocumentFilter();
                documentFilterDictionary.Add(doc.Id, df);

                var subs = repo.List(new SubscribersByDocument(doc.Id));//.Select

                if (doc.PromotedProperties.Count == 0)
                {
                    df.SubscriptionsWithNoPropertyFilter.Hits = subs.Select(e => e.Id).ToHashSet();
                }
                else
                {
                    foreach (var iprop in doc.PromotedProperties)
                    {

                        var pf = new PropertyFilter(iprop.Value);
                        df.Properties.Add(iprop.Key, pf);

                        foreach (var sub in subs)
                        {
                            if (sub.DocumentFilter.TryGetValue(iprop.Key, out var val))
                            {
                                var valarr = val.Split(",".ToArray(), StringSplitOptions.RemoveEmptyEntries);
                                foreach (var valitem in valarr)
                                {
                                    var valclean = valitem.Replace("\"", "").Trim().ToLower();
                                    if (string.IsNullOrEmpty(valclean))
                                    {
                                        throw new InfolinkException();
                                    }
                                    else
                                    {
                                        if (!pf.SubscribersByValues.ContainsKey(valclean))
                                            pf.SubscribersByValues[valclean] = new List<int>();
                                        pf.SubscribersByValues[valclean].Add(sub.Id);
                                    }
                                }

                            }
                            else
                            {
                                pf.Ignored.Add(sub.Id);
                            }
                        }
                    }
                }
            }

        }

        public FilterResult Filter(int documentId, XchangeFile xchangeFile)
        {
            if (xchangeFile is null)
            {
                throw new InfolinkException("Invalid file.");
            }

            documentFilterLock.EnterWriteLock();
            try
            {
                if (DateTime.UtcNow.Subtract(documentFilterPreparedOn).TotalMinutes > 10)
                {
                    logger.LogDebug("Prepare()");
                    Prepare();
                    documentFilterPreparedOn = DateTime.UtcNow;
                }
            }
            finally
            {
                documentFilterLock.ExitWriteLock();
            }

            documentFilterLock.EnterReadLock();
            try
            {
                if (!documentFilterDictionary.TryGetValue(documentId, out var documentFilter))
                    throw new InfolinkException($"Document number {documentId} not found");

                if (documentFilter.Properties.Count == 0)
                {
                    return documentFilter.SubscriptionsWithNoPropertyFilter;
                }

                JToken doc = JObject.Parse(xchangeFile.Data);
                var filterResult = new FilterResult();

                foreach (var prop in documentFilter.Properties.Keys)
                {
                    var pf = documentFilter.Properties[prop];

                    HashSet<int> matchallprop = new HashSet<int>(pf.Ignored);

                    var node = doc.SelectToken(pf.Path);

                    if (node == null) throw new PromotedPropertyNotPresent(prop);


                    var val = node.Value<string>() == null ? string.Empty : node.Value<string>().ToLower().Trim();

                    filterResult.Properties.Add(prop, val);

                    if (pf.SubscribersByValues.TryGetValue(val, out var lstall))
                        matchallprop.UnionWith(lstall);

                    if (filterResult.Hits == null)
                        filterResult.Hits = matchallprop;
                    else
                        filterResult.Hits.IntersectWith(matchallprop);

                }

                return filterResult;
            }
            finally
            {
                documentFilterLock.ExitReadLock();
            }
        }
    }


}


