using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Text;

namespace SW.Infolink
{
    public class PipelineFinishedMessage 
    {
        public int XchangeId { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }

        public string  ResponseFileName { get; set; }
    }
}
