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

        public XchangeFile File { get; set; }

        public string[] References { get; set; }

        public bool IgnoreSchedule { get; set; }
    }
}
