using SW.Infolink.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SW.Infolink.Web.ViewModels
{
    public class SubmitXchange : XchangeRequest
    {
        public XchangeRequestOption Option { get; set; }
        //public string OptionName => 
    }

    public enum XchangeRequestOption
    {
        DocumentId,
        SubscriberId
    }

}
