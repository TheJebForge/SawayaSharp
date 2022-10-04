using Microsoft.Extensions.Localization;
using System.Reflection;

namespace SawayaSharp;

public class SharedLocale: IStringLocalizer
{
    IStringLocalizer _locale;

    public SharedLocale(IStringLocalizerFactory factory) {
        _locale = factory.Create("locale", Assembly.GetExecutingAssembly().FullName!);
    }


    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => _locale.GetAllStrings(includeParentCultures);

    public LocalizedString this[string name] => _locale[name];
    public LocalizedString this[string name, params object[] arguments] => _locale[name, arguments];
}