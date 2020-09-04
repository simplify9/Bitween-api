using SW.Serverless.Sdk;
using System;
using System.Threading.Tasks;

namespace SW.Infolink.SampleMapper
{
    class Program
    {
        async static Task Main(string[] args) => await Runner.Run(new Handler());
    }
}
