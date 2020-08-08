using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SW.Infolink.Sdk
{
    public interface IInfolinkClient
    {
        Task<TResponse> GetAsync<TResponse>(string url);
        Task<int> PostAsync(string url, object request);
        Task<TResponse> PostAsync<TResponse>(string url, object request);
        Task<int> DeleteAsync(string url);
    }
}
