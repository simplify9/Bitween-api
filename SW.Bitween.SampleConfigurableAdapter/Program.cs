using SW.Serverless.Sdk;
using System.Threading.Tasks;

namespace SW.Bitween.SampleConfigurableAdapter
{
    class Program
    {
        async static Task Main(string[] args) => await Runner.Run(new Handler());
    }
}
