using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
//using System.ComponentModel.DataAnnotations;
using System.Text;

namespace SW.Infolink
{
    public class XchangeRequest
    {

        public int DocumentId { get; set; }

        public int SubscriberId { get; set; }

        public XchangeFile File { get; set; }

        //[Required]
        //[MaxLength(1024*1024*20)]
        //public string Data { get; set; }

        //[MaxLength(200)]
        //public string FileName { get; set; }

        //[MaxLength(10)]
        public string[] References { get; set; }

        public bool IgnoreSchedule { get; set; }
    }
}
