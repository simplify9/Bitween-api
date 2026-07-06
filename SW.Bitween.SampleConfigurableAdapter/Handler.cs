using SW.PrimitiveTypes;
using SW.Serverless.Sdk;
using System;
using System.Threading.Tasks;

namespace SW.Bitween.SampleConfigurableAdapter
{
    class Handler : IInfolinkHandler
    {
        public Handler()
        {
            Runner.Expect("DelayMs", "0");
            Runner.Expect("SimulateError", "false");
            Runner.Expect("ErrorMessage", "Simulated error");
            Runner.Expect("OutputData", "");
        }

        public async Task<XchangeFile> Handle(XchangeFile xchangeFile)
        {
            var delayMs = Runner.StartupValueOf<int>("DelayMs");
            if (delayMs > 0)
                await Task.Delay(delayMs);

            if (Runner.StartupValueOf<bool>("SimulateError"))
                throw new InvalidOperationException(Runner.StartupValueOf("ErrorMessage"));

            var outputData = Runner.StartupValueOf("OutputData");
            if (!string.IsNullOrEmpty(outputData))
                return new XchangeFile(outputData, xchangeFile.Filename);

            return xchangeFile;
        }
    }
}
