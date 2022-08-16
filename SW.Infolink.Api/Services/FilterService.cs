using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using System.Xml.XPath;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using SW.Infolink.Domain;
using SW.EfCoreExtensions;
using SW.PrimitiveTypes;
using SW.Infolink.Model;

namespace SW.Infolink
{
    public class FilterService
    {
        readonly IServiceScopeFactory ssf;
        readonly ILogger<FilterService> logger;
        readonly ReaderWriterLockSlim documentFilterLock = new ReaderWriterLockSlim();
        readonly IDictionary<int, DocumentFilter> documentFilterDictionary = new Dictionary<int, DocumentFilter>();

        DateTime? documentFilterPreparedOn;

        public FilterService(IServiceScopeFactory ssf, ILogger<FilterService> logger)
        {
            this.ssf = ssf;
            this.logger = logger;
        }

        void Prepare()
        {
            using var scope = ssf.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<InfolinkDbContext>();
            var docs = repo.List<Document>();

            documentFilterDictionary.Clear();

            foreach (var doc in docs)
            {
                var df = new DocumentFilter();
                documentFilterDictionary.Add(doc.Id, df);

                var subs = repo.List(new SubscribersByDocument(doc.Id));//.Select

                if (doc.PromotedProperties.Count == 0)
                {
                    df.DocumentsWithNoPromotedProperties.Hits = subs.Select(e => e.Id).ToHashSet();
                }
                else
                {
                    foreach (var iprop in doc.PromotedProperties)
                    {
                        var pf = new PropertyFilter(iprop.Value);
                        df.Properties.Add(iprop.Key, pf);

                        foreach (var sub in subs)
                            if (sub.DocumentFilter.TryGetValue(iprop.Key, out var val))
                            {
                                var valarr = val.Split(',', StringSplitOptions.RemoveEmptyEntries);
                                foreach (var valitem in valarr)
                                {
                                    var valclean = valitem.Replace("\"", "").Trim().ToLower();
                                    if (string.IsNullOrEmpty(valclean))
                                        throw new InfolinkException($"Empty filter value for subscriber {sub.Id}.");

                                    else
                                    {
                                        if (!pf.SubscribersByValues.ContainsKey(valclean))
                                            pf.SubscribersByValues[valclean] = new List<int>();
                                        pf.SubscribersByValues[valclean].Add(sub.Id);
                                    }
                                }
                            }
                            else
                                pf.Ignored.Add(sub.Id);

                    }
                }
            }

        }

        public FilterResult Filter(int documentId, XchangeFile xchangeFile, DocumentFormat format)
        {
            if (xchangeFile is null)
            {
                throw new InfolinkException("Invalid file.");
            }

            documentFilterLock.EnterWriteLock();
            try
            {
                if (documentFilterPreparedOn == null || DateTime.UtcNow.Subtract(documentFilterPreparedOn.Value).TotalMinutes > 10)
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
                    throw new InfolinkException($"Document {documentId} not found.");

                if (documentFilter.Properties.Count == 0)
                    return documentFilter.DocumentsWithNoPromotedProperties;
                var filterResult = new FilterResult();
                var firstHit = true;
                
                if (format == DocumentFormat.Json)
                {
                    JToken doc = JObject.Parse(xchangeFile.Data);
                    foreach (var prop in documentFilter.Properties.Keys)
                    {
                        var pf = documentFilter.Properties[prop];
                        var matchallprop = new HashSet<int>(pf.Ignored);
                        var node = doc.SelectToken(pf.Path);
                        if (node == null) throw new PromotedPropertyNotPresent(prop);

                        var val = node.Value<string>() == null ? string.Empty : node.Value<string>().ToLower().Trim();

                        filterResult.Properties.Add(prop, val);

                        if (pf.SubscribersByValues.TryGetValue(val, out var lstall))
                            matchallprop.UnionWith(lstall);

                        if (firstHit)
                        {
                            filterResult.Hits = matchallprop;
                            firstHit = false;
                        }
                        else
                            filterResult.Hits.IntersectWith(matchallprop);
                    }

                   
                }
                else
                {

                    var doc = XDocument.Parse(xchangeFile.Data);
                    
                    foreach (var prop in documentFilter.Properties.Keys)
                    {
                        var pf = documentFilter.Properties[prop];
                        var matchallprop = new HashSet<int>(pf.Ignored);
                        var node = doc.XPathSelectElement(pf.Path);
                        //if (node == null) throw new PromotedPropertyNotPresent(prop);

                        var val = node?.Value == null ? string.Empty : node.Value.ToLower().Trim();

                        filterResult.Properties.Add(prop, val);

                        if (pf.SubscribersByValues.TryGetValue(val, out var lstall))
                            matchallprop.UnionWith(lstall);

                        if (firstHit)
                        {
                            filterResult.Hits = matchallprop;
                            firstHit = false;
                        }
                        else
                            filterResult.Hits.IntersectWith(matchallprop);
                    }
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


