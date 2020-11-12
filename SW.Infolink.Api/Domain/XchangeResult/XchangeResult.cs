using SW.PrimitiveTypes;
using System;

namespace SW.Infolink.Domain
{
    public class XchangeResult : BaseEntity<string>
    {
        private XchangeResult()
        {
        }

        public XchangeResult(string xchangeId, XchangeFile outputFile, XchangeFile responseFile = null, string responseXchangeId = null, string exception = null)
        {
            Id = xchangeId;
            Success = exception == null;
            Exception = exception;
            FinishedOn = DateTime.UtcNow;
            ResponseXchangeId = responseXchangeId;

            if (outputFile != null)
            {
                OutputName = outputFile.Filename;
                OutputSize = outputFile.Data.Length;
                OutputHash = outputFile.Hash;
                OutputBad = outputFile.BadData;
                OutputContentType = outputFile.ContentType;
            }

            if (responseFile != null)
            {
                ResponseName = responseFile.Filename;
                ResponseSize = responseFile.Data.Length;
                ResponseHash = responseFile.Hash;
                ResponseBad = responseFile.BadData;
                ResponseContentType = responseFile.ContentType;

            }

            Events.Add(new XchangeResultCreatedEvent
            {
                Id = Id
            });
        }

        public bool Success { get; private set; }
        public string Exception { get; private set; }
        public DateTime FinishedOn { get; private set; }
        public string ResponseXchangeId { get; private set; }

        public string OutputName { get; private set; }
        public int OutputSize { get; private set; }
        public string OutputHash { get; private set; }
        public bool OutputBad { get; private set; }
        public string OutputContentType { get; private set; }

        public string ResponseName { get; private set; }
        public int ResponseSize { get; private set; }
        public string ResponseHash { get; private set; }
        public bool ResponseBad { get; private set; }
        public string ResponseContentType { get; private set; }



    }
}
