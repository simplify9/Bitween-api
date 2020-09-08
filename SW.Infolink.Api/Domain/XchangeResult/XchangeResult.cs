using SW.PrimitiveTypes;
using System;

namespace SW.Infolink.Domain
{
    class XchangeResult : BaseEntity<string>
    {
        private XchangeResult()
        {
        }

        public XchangeResult(string xchangeId, XchangeFile outputFile, XchangeFile responseFile = null, string responseXchangeId = null, string exception = null)
        {
            Id = xchangeId;//Guid.NewGuid().ToString("N");
            Success = exception == null;
            Exception = exception;
            //XchangeId = xchangeId;
            FinishedOn = DateTime.UtcNow;
            ResponseXchangeId = responseXchangeId;

            if (outputFile != null)
            {
                OutputName = outputFile.Filename;
                OutputSize = outputFile.Data.Length;
                OutputHash = outputFile.Hash;
            }

            if (responseFile != null)
            {
                ResponseName = responseFile.Filename;
                ResponseSize = responseFile.Data.Length;
                ResponseHash = responseFile.Hash;
            }

            Events.Add(new XchangeResultCreatedEvent
            {
                Id = Id
            });
        }

        //public string XchangeId { get; private set; }
        public bool Success { get; private set; }
        public string Exception { get; private set; }
        public DateTime FinishedOn { get; private set; }
        public string ResponseXchangeId { get; private set; }
        public string OutputName { get; private set; }
        public int OutputSize { get; private set; }
        public string OutputHash { get; private set; }
        public string ResponseName { get; private set; }
        public int ResponseSize { get; private set; }
        public string ResponseHash { get; private set; }

    }
}
