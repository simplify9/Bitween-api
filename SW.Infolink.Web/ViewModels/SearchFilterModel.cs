using Microsoft.AspNetCore.Mvc.Rendering;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SW.Infolink.Web
{
    public class SearchFilterModel
    {
        public List<SearchLine> Lines { get; set; } = new List<SearchLine>();
        public List<SelectListItem> Fields { get; set; } = new List<SelectListItem>();

        public string CreateNewName { get; set; }
        public class SearchLine
        {
            public SearchLine()
            {
            }

            //public SearchLine(string fieldName, int @operator, string value)
            //{
            //    FieldName = fieldName;
            //    Operator = @operator;
            //    Value = value;
            //}

            public string FieldName { get; set; }
            public int Operator { get; set; }
            public string Value { get; set; }
        }

        public  SearchyCondition ToSearchyCondition()
        {
            var filters = Lines
                .Where(sl => sl.FieldName != null && sl.Value != null)
                .Select(sl => new SearchyFilter(sl.FieldName, (SearchyRule)sl.Operator, sl.Value)).ToList();

            return new SearchyCondition(filters);
        }
    }
}
