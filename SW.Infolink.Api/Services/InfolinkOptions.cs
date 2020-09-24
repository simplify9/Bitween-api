
namespace SW.Infolink
{
    public class InfolinkOptions 
    {
        public const string ConfigurationSection = "Infolink";

        public InfolinkOptions()
        {
            AdapterPath = "./adapters";
            AdminCredentials = "admin:1234512345";
            DocumentPrefix = "temp30/infolinkdocs";
            ClientIpHeaderName = "X-Real-IP";
        }

        public string AdapterPath { get; set; }
        public string AdminCredentials { get; set; }
        public string DocumentPrefix { get; set; }
        public string ClientIpHeaderName { get; set; }

    }
}
