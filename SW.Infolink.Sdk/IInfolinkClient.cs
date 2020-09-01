using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SW.Infolink.Sdk
{
    public interface IInfolinkClient : IBasicApiClient
    {
        Task<TResponse> GetAsync<TResponse>(string url);
        Task<int> PostAsync(string url, object request);
        Task<TResponse> PostAsync<TResponse>(string url, object request);
        Task<int> DeleteAsync(string url);
    }
}
