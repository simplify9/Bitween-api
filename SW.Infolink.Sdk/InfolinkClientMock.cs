using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using SW.PrimitiveTypes;
using SW.HttpExtensions;
using System.Security.Claims;

namespace SW.Infolink.Sdk
{
    public class InfolinkClientMock 
    {
        private readonly InfolinkClientOptions infolinkClientOptions;

        public InfolinkClientMock(InfolinkClientOptions infolinkClientOptions)
        {
            this.infolinkClientOptions = infolinkClientOptions;
        }

    }
}
