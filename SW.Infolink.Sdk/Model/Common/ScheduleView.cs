using System;
using System.Collections.Generic;
using System.Text;

namespace SW.Infolink.Model
{
    public class ScheduleView
    {
        public Recurrence Recurrence { get; set; }
        public TimeSpan On { get; set; }
        public bool Backwards { get; set; }

    }
}
