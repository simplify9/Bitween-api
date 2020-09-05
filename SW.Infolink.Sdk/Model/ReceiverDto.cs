//using System;
//using System.Collections.Generic;
//using System.Text;

//namespace SW.Infolink.Model
//{
//    public class ReceiverDto
//    {
//        //public int Id { get; set; }
//        public string Name { get; set; }
//        public int ReceiverId { get; set; }
//        public IDictionary<string, string> Properties { get;  set; }

//        public string GetProperty(string name)
//        {
//            return Properties[name];
//        }

//        public string GetProperty(string name, string defaultValue)
//        {
//            Properties.TryGetValue(name, out string val);
//            if (val == null) val = defaultValue;
//            return val;
//        }

//    }
//}
