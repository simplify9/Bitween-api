using Microsoft.Extensions.Configuration;
using SW.HttpExtensions;
using System;
using System.Collections.Generic;
using System.Text;

namespace SW.Infolink.Sdk
{
    public class InfolinkClientOptions : ApiClientOptionsBase
    {
        public override string ConfigurationSection => "InfolinkClient";
    }
}
