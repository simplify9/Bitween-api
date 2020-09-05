using System;
using System.Collections.Generic;
using System.Text;

namespace SW.Infolink.Model
{
    public class ScheduleView
    {
        public Recurrence Recurrence { get; set; }
        public int Days { get; set; }
        public int Hours { get; set; }
        public int Minutes { get; set; }
        public bool Backwards { get; set; }

    }
}
