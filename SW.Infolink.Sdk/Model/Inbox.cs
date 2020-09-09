using System;
using System.Collections.Generic;
using System.Text;

namespace SW.Infolink.Model
{
    public class InboxGet
    {
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public bool IncludeDelivered { get; set; }
    }

    public class InboxGetResponse
    {
        public int MyProperty { get; set; }

    }

    public class InboxReceive
    {


    }

    public class InboxReceiveResponse
    {
    }

}
