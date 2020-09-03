using SW.Infolink;
using System;
using System.Collections.Generic;
using System.Text;

namespace SW.Infolink.Model
{
    public class SubscriptionRow : SubscriptionConfig
    {
        public int Id { get; set; }
        //public string KeySetName { get; set; }
        public string DocumentName { get; set; }
        //public string HandlerName { get; set; }
        //public string MapperName { get; set; }
    }
}
