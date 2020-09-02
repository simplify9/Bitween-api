using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Text;

namespace SW.Infolink.Model
{
    public class XchangeRequest
    {
        public int DocumentId { get; set; }
        public int SubscriberId { get; set; }
        public string Filename { get; set; }
        public string Data { get; set; }
        public string[] References { get; set; }
    }
}
