using System;
using System.Collections.Generic;
using System.Text;

namespace SW.Infolink.Model
{
    public class DocumentFilter
    {
        public DocumentFilter()
        {
            Properties = new Dictionary<string, PropertyFilter>();
            DocumentsWithNoPromotedProperties = new FilterResult();
        }

        public IDictionary<string, PropertyFilter> Properties  { get;  }

        public FilterResult DocumentsWithNoPromotedProperties { get; set; }

    }


    public class PropertyFilter
    {

        public PropertyFilter(string path)
        {
            Path = path;
            Ignored = new List<int>();
            SubscribersByValues = new Dictionary<string, ICollection<int>>();
        }

        public string Path { get; set; }
        public ICollection<int> Ignored { get; }
        public IDictionary<string, ICollection<int>> SubscribersByValues { get; }
    }




}
