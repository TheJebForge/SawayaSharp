using System.Globalization;

namespace SawayaSharp.Data;

public class GuildConfig
{
    public string Locale { get; set; } = "en";

    public CultureInfo GetLocale() {
        return CultureInfo.GetCultureInfo(Locale);
    }

    public void SetLocale(CultureInfo locale) {
        Locale = locale.Name;
    }
}