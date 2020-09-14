using SW.PrimitiveTypes;
using SW.Serverless.Sdk;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SW.Infolink.SampleHandler
{
    class Handler : IInfolinkHandler
    {
        public Handler()
        {
            Runner.Expect("ContentType", "text/plain");
        }

        public Task<XchangeFile> Handle(XchangeFile xchangeFile)
        {
            //Runner.CorrelationId
            //Runner.st 
            return Task.FromResult(xchangeFile);
        }
    }
}
