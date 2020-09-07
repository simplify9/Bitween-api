using SW.PrimitiveTypes;
using SW.Serverless.Sdk;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SW.Infolink.SampleMapper
{
    class Handler : IInfolinkHandler
    {
        public Handler()
        {
            Runner.Expect("User");
            Runner.Expect("Password");
        }

        public Task<XchangeFile> Handle(XchangeFile xchangeFile)
        {
            //Runner.st 
            return Task.FromResult(xchangeFile);
        }
    }
}
