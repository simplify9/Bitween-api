using System;

namespace SW.Infolink.Model
{
    public class NotificationsSearch
    {
        public int Id { get; set; }
        public string XchangeId { get; set; }
        public string NotifierName { get; set; }
        public bool Success { get; set; }
        public string Exception { get; set; }
        public DateTime FinishedOn { get; set; }
    }
}