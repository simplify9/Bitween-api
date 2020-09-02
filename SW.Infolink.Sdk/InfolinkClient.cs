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

        //public async Task<TResponse> GetAsync<TResponse>(string url)
        //{
        //    return await Builder.
        //        Jwt().
        //        Path(url).
        //        As<TResponse>().
        //        GetAsync();
        //}

        //public async Task<int> PostAsync(string url, object request)
        //{
        //    return await Builder.
        //        Jwt().
        //        Path(url).
        //        PostAsync(request, false);
        //}

        //public async Task<TResponse> PostAsync<TResponse>(string url, object request)
        //{

        //    var apiResult = await Builder.
        //        Jwt().
        //        Path(url).
        //        AsApiResult<TResponse>().
        //        PostAsync(request);

        //    if (apiResult.Success)
        //    {
        //        return apiResult.Response;
        //    }
        //    else if (apiResult.StatusCode == 400)
        //    {
        //        var messages = JsonConvert.DeserializeObject<IDictionary<string, IEnumerable<string>>>(apiResult.Body);
        //        var message = string.Empty;
        //        foreach (var kvp in messages)

        //            if (kvp.Key.StartsWith("Field."))
        //            {
        //                var fieldName = kvp.Key.Split(new char[] { '.' }, 2)[1];
        //                var removeIndex = fieldName.IndexOf("[");
        //                if (removeIndex > 0) fieldName = fieldName.Remove(removeIndex);
        //                message = string.Join(", ", kvp.Value);

        //            }
        //            else
        //            {
        //                message = string.Join(",", kvp.Value);
        //            }

        //        throw new Exception(message);
        //    }
        //    else
        //    {
        //        throw new Exception(apiResult.Body);
        //    }
        //}

        //public async Task<int> DeleteAsync(string url)
        //{
        //    return await Builder.
        //        Jwt().
        //        Path(url).
        //        DeleteAsync(false);
        //}

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
    }
}
