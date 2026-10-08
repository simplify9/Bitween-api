using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Mime;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SW.Bitween.Resources.Xchanges
{
    [Unprotect]
    public class Update(RequestContext requestContext, XchangeService xchangeService, BitweenDbContext dbContext,
        BitweenOptions BitweenSettings, IInfolinkCache cache,
        Microsoft.AspNetCore.Http.IHttpContextAccessor httpContextAccessor = null) : ICommandHandler<string, object,object>
    {
        public async Task<object> Handle(string documentIdOrName, dynamic request)
        {
            Document document;

            // Inject external request context values into the object — minus the caller's
            // credentials. This lands in the stored input file, where anyone who can view
            // exchanges reads it, and is handed to mappers and handlers, none of which need the
            // partner's key to do their job.
            request._ExternalRequestContext = JsonConvert.SerializeObject(
                requestContext.Values.Where(v => !IsCredential(v.Name, BitweenSettings.PartnerKeyHeader)).ToList());

            if (int.TryParse(documentIdOrName, out var documentId))
                document = await cache.DocumentByIdAsync(documentId);
            else
                document = await cache.DocumentByNameAsync(documentIdOrName);

            if (document is null)
                throw new SWNotFoundException("Document");

            var par = await dbContext.AuthorizePartner(requestContext, BitweenSettings.PartnerKeyHeader);

            var subs = (await cache.ListSubscriptionsByDocumentAsync(document.Id))
                .Where(i => i.PartnerId == par.Partner.Id)
                .ToList();

            if (subs?.Count > 1)
                throw new SWValidationException("Subscriptions",
                    "You can only have one subscription of for each document");

            var sub = subs.SingleOrDefault();

            string body = request.ToString();
            var schemaErrors = await DocumentSchema.Check(document.DocumentFormat, document.ValidationSchema, body);
            if (schemaErrors.Count > 0)
                throw new SWValidationException("SCHEMA_MISMATCH", string.Join("\n", schemaErrors));

            if (par.Partner.Id == Partner.SystemId && sub is null)
            {
                await xchangeService.SubmitFilterXchange(document.Id,new XchangeFile(request.ToString()));
                return null;
            }

            if (sub is null)
                throw new SWNotFoundException("No subscription of type ApiCall was found for this document");

            var xchangeReferences = new List<string> { $"partnerkey: {par.KeyName}" };

            var waitResponseHeader = requestContext.Values
                .Where(item => item.Name.ToLower() == "waitresponse")
                .Select(item => item.Value).FirstOrDefault();

            // No header, no wait — as before. With one, the wait is capped; see ResultWait.
            var waitResponse = 0;
            if (int.TryParse(waitResponseHeader, out var waitResponseValue))
            {
                waitResponse = ResultWait.Clamp(waitResponseValue, BitweenSettings.MaxResponseWaitSeconds);
                xchangeReferences.Add($"waitresponse: {waitResponse}");
            }

            var xchangeFile = new XchangeFile(request.ToString());

            var globalAdapterValuesSets = await cache.ListGlobalAdapterValuesSetsAsync();
            var validatorProperties = sub.ValidatorProperties.ToDictionary().Fill(par.Partner, globalAdapterValuesSets);
            await xchangeService.RunValidator(sub.ValidatorId, validatorProperties, xchangeFile);

            // Same Idempotency-Key from the same partner for the same type: the first call's exchange.
            var idempotencyKey = requestContext.Values
                .Where(v => v.Type == RequestValueType.HttpHeader &&
                            v.Name.Equals(IdempotencyGuard.Header, StringComparison.OrdinalIgnoreCase))
                .Select(v => v.Value?.Trim()).FirstOrDefault();
            if (idempotencyKey?.Length > IdempotencyGuard.MaxKeyLength)
                throw new SWValidationException("IDEMPOTENCY_KEY",
                    $"{IdempotencyGuard.Header} is longer than {IdempotencyGuard.MaxKeyLength} characters.");
            var idempotencyScope = $"legacy:partner:{par.Partner.Id}:document:{document.Id}";

            var xchangeId = string.IsNullOrEmpty(idempotencyKey)
                ? null
                : await IdempotencyGuard.FindAsync(dbContext, idempotencyScope, idempotencyKey);
            if (xchangeId is null)
            {
                var subscription = await cache.SubscriptionByIdAsync(sub.Id);
                var xchange = await xchangeService.CreateXchange(subscription, xchangeFile,
                    xchangeReferences.ToArray(), Guid.NewGuid().ToString("N"));
                if (!string.IsNullOrEmpty(idempotencyKey))
                    await IdempotencyGuard.StageAsync(dbContext, idempotencyScope, idempotencyKey, xchange.Id);
                try
                {
                    await dbContext.SaveChangesAsync();
                    xchangeId = xchange.Id;
                }
                catch (DbUpdateException ex) when (IdempotencyGuard.IsDuplicateKey(ex))
                {
                    dbContext.ChangeTracker.Clear();
                    xchangeId = await IdempotencyGuard.FindAsync(dbContext, idempotencyScope, idempotencyKey);
                }
            }

            if (waitResponse <= 0)
                return new CqApiResult<string>(xchangeId)
                {
                    Status = CqApiResultStatus.Ok
                };

            var available = await ResultWait.UntilAsync(() => IsResultAvailable(xchangeId), waitResponse,
                httpContextAccessor?.HttpContext?.RequestAborted ?? System.Threading.CancellationToken.None);

            if (available)
            {

                var xchangeResult = await dbContext.FindAsync<XchangeResult>(xchangeId);

                switch (xchangeResult!.Success)
                {
                    case true when xchangeResult.ResponseSize == 0:
                    {
                        return new CqApiResult<string>(xchangeId)
                        {
                            Status = BitweenSettings.ApiCallSubscriptionResponseAcceptedStatusCode == 200
                                ? CqApiResultStatus.Ok
                                : CqApiResultStatus.UnderProcessing
                        };
                    }
                    case true when xchangeResult.ResponseSize != 0:
                    {
                        var response = await xchangeService.GetFile(xchangeId, XchangeFileType.Response);
                        var result = new CqApiResult<string>(response);
                        result.AddHeader("location", xchangeId);
                        result.Status = xchangeResult.ResponseBad ? CqApiResultStatus.Error : CqApiResultStatus.Ok;
                        result.ContentType = xchangeResult.ResponseContentType ?? MediaTypeNames.Application.Json;
                        return result;
                    }
                    case false:
                        throw new SWValidationException("failure", "Internal processing error.");
                }
            }

            return new CqApiResult<string>(xchangeId)
            {
                Status = CqApiResultStatus.UnderProcessing
            };
        }

        private async Task<bool> IsResultAvailable(string xchangeId)
        {
            return await dbContext.Set<XchangeResult>()
                .AsNoTracking()
                .AnyAsync(i => i.Id == xchangeId);
        }

        private static readonly HashSet<string> CredentialNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "authorization", "proxy-authorization", "cookie", PartnerKeyHeaders.Default,
        };

        private static bool IsCredential(string name, string partnerKeyHeader) =>
            name is not null &&
            (CredentialNames.Contains(name) ||
             (!string.IsNullOrWhiteSpace(partnerKeyHeader) &&
              name.Equals(partnerKeyHeader.Trim(), StringComparison.OrdinalIgnoreCase)));
    }
}