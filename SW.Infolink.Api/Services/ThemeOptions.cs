namespace SW.Infolink.Services
{
    public class ThemeOptions
    {
        public const string ConfigurationSection = "Theme";

        public string LoginLogo { get; set; } = "/Graphics/s9.png";
        public string BitweenLogo { get; set; } = "/Graphics/BitweenFull.svg";

        public string BitweenText { get; set; } =
            "is all-in-one solution to solving integration with third parties, automating workflows\n                                with exchanges coming from all forms of requests, ranging from internal messages to\n                                files dumped on a server.";

        public string LinkedinLink { get; set; } = "https://www.linkedin.com/company/simplify9";
        public string GithubLink { get; set; } = "https://github.com/simplify9";
        public string BitweenIcon { get; set; } = "/Graphics/BitweenIcon.png";
        public string BitweenHeaderIcon { get; set; } = "/Graphics/BitweenIcon.svg";
        public string WebsiteLink { get; set; } = "https://www.simplify9.com/";
        public string CompanyName { get; set; } = "Simplify9";
        public string AllRightsReserved { get; set; } = "All Rights Reserved.";
        public string CopyRightsIcon { get; set; } = "©";
        public string TabTitle { get; set; } = "Bitween";
        public string TabIcon { get; set; } = "/favicon.ico";
    }
}