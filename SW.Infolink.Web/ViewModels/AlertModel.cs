using System;
using System.Collections.Generic;
using System.Text;

namespace SW.Infolink.Web
{
    public class AlertModel
    {

        public AlertModel(string message, AlertType type)
        {
            Message = message;
            Type = type;
        }

        public string Message { get; set; }
        public AlertType Type { get; set; }

        public enum AlertType
        {
            Information,
            Warning,
            Error
        }
    }
}
