using Microsoft.Extensions.Configuration;
using SW.HttpExtensions;
using System;
using System.Collections.Generic;
using System.Text;

namespace SW.Bitween.Sdk
{
    public class BitweenClientOptions : ApiClientOptionsBase
    {
        public override string ConfigurationSection => "BitweenClient";
    }
}
