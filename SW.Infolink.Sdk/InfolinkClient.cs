using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using SW.HttpExtensions;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading.Tasks;

namespace SW.Infolink.Sdk
{
    public class InfolinkClient : ApiClientBase<InfolinkClientOptions>, IBasicApiClient
    {
        public InfolinkClient(HttpClient httpClient, RequestContext requestContext, InfolinkClientOptions infolinkClientOptions) : base(httpClient, requestContext, infolinkClientOptions)
        {
        }

        public Task<ApiResult<int?>> Create<TRequest>(string url, TRequest payload)
        {
            return Builder.Jwt().Path(url).AsApiResult<int?>().PostAsync(payload);
        }

        public Task<ApiResult<TResponse>> GetById<TResponse>(string url, int id)
        {
            return Builder.Jwt().Path($"{url}/{id}").AsApiResult<TResponse>().GetAsync();
        }

        public Task<ApiResult> Update<TRequest>(string url, TRequest payload)
        {
            return Builder.Jwt().Path(url).AsApiResult().PostAsync(payload);
        }
        public Task<ApiResult> Delete(string url)
        {
            return Builder.Jwt().Path(url).AsApiResult().DeleteAsync();
        }


        async public Task<ApiResult<string>> LookupValue(string searchUrl)
        {
            return await Builder.Path(searchUrl).AsApiResult<string>().GetAsync();
        }

        public Task<ApiResult<SearchyResponse<TModel>>> Search<TModel>(string searchUrl)
        {
            return Builder.Path(searchUrl).AsApiResult<SearchyResponse<TModel>>().GetAsync();
        }

        public Task<ApiResult<IDictionary<string, string>>> Search(string searchUrl)
        {
            return Builder.Path(searchUrl).AsApiResult<IDictionary<string, string>>().GetAsync();
        }

        public Task<ApiResult<string>> GeneratePartnerApiKey()
        {
            return Builder.Path("partners/generatekey").AsApiResult<string>().GetAsync();
        }
    }
}
