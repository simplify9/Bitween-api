using System.Collections.Generic;
using SW.PrimitiveTypes;

namespace SW.Bitween.Model
{
    public class NotificationChannelCreate : IName
    {
        public string Name { get; set; } = null!;

        /// <summary>Any handler adapter, built in or custom.</summary>
        public string HandlerId { get; set; } = null!;

        /// <summary>Everything the adapter needs to deliver, such as recipients or a webhook url.</summary>
        public Dictionary<string, string> HandlerProperties { get; set; } = [];
    }

    public class NotificationChannelUpdate : NotificationChannelCreate { }

    public class NotificationChannelGet : NotificationChannelUpdate
    {
        public int Id { get; set; }
    }

    public class NotificationChannelRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public string HandlerId { get; set; } = null!;
    }
}
