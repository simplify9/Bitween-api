using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SW.Infolink.Web.ViewModels
{
    public class SubmitXchange 
    {
        public XchangeRequestOption Option { get; set; }
        public int DocumentId { get; set; }
        public int SubscriberId { get; set; }
        public XchangeFile File { get; set; }
        public string[] References { get; set; }
        public string Data { get; set; }
    }

    public enum XchangeRequestOption
    {
        DocumentId,
        SubscriberId
    }

}
