namespace SW.Bitween.Services
{
    /// <summary>
    /// Branding. Every value here is an editable setting, so these initializers are the product
    /// defaults — what "reset to default" returns a setting to. A deployment can still seed
    /// different values through configuration, but only once: the first boot imports them into the
    /// Settings table, and from then on the table is the only source (see <c>SettingsService</c>).
    /// </summary>
    public class ThemeOptions
    {
        public const string ConfigurationSection = "Theme";

        public string LoginLogo { get; set; } = "/brand/BitweenFull.svg";
        public string BitweenLogo { get; set; } = "/brand/BitweenFull.svg";

        public string BitweenText { get; set; } =
            "is all-in-one solution to solving integration with third parties, automating workflows " +
            "with exchanges coming from all forms of requests, ranging from internal messages to " +
            "files dumped on a server.";

        public string LinkedinLink { get; set; } = "https://www.linkedin.com/company/simplify9";
        public string GithubLink { get; set; } = "https://github.com/simplify9";
        public string BitweenIcon { get; set; } = "/brand/BitweenIcon.png";
        public string BitweenHeaderIcon { get; set; } = "/brand/BitweenIcon.svg";
        public string WebsiteLink { get; set; } = "https://www.simplify9.com/";
        public string CompanyName { get; set; } = "Simplify9";
        public string AllRightsReserved { get; set; } = "All Rights Reserved.";
        public string CopyRightsIcon { get; set; } = "©";
        public string TabTitle { get; set; } = "Bitween";
        public string TabIcon { get; set; } = "/favicon.svg";

        /// <summary>Accent color the UI derives its whole brand ramp from. Hex, e.g. <c>#e3311d</c>.</summary>
        public string PrimaryColor { get; set; } = "#e3311d";

        public bool ShowFooter { get; set; } = true;
    }
}
