using Microsoft.Extensions.Configuration;

namespace SW.Infolink
{
    public class InfolinkSettings 
    {
        const string section = "Infolink";
        readonly IConfiguration configuration;
        public InfolinkSettings(IConfiguration configuration)
        {
            this.configuration = configuration.GetSection(section);
        }

        public string AdapterRootPath => configuration.GetValue(nameof(AdapterRootPath), "./adapters");
        public string AdminCredentials => configuration.GetValue(nameof(AdminCredentials), "admin:1234512345");

    }
}
