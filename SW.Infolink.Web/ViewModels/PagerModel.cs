using System;
using System.Collections.Generic;
using System.Text;

namespace SW.Infolink.Web
{
    public class PagerModel
    {
        public int PageSize { get; set; } = 20;
        public int CurrentPage { get; set; } = 1;
        public int TotalCount { get; set; }
        public int TotalPages => (int)Math.Ceiling(decimal.Divide(TotalCount, PageSize));
    }
}
