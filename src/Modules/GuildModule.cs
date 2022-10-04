using Discord.Interactions;
using Discord.WebSocket;
using System.Globalization;

namespace SawayaSharp.Modules;

[Group("guild", "A list of guild configuration commands")]
public class GuildModule: InteractionModuleBase
{
    SharedLocale _locale;
    BotData _botData;
    
    public GuildModule(BotData botData, SharedLocale locale) {
        _botData = botData;
        _locale = locale;
    }
    
    public enum Locales
    {
        English,
        Русский
    }

    [SlashCommand("locale", "Sets locale that the bot will be using for this guild")]
    public async Task SetLocale(Locales locale) {
        _botData.GetOrNewGuild(Context.Guild).Locale = locale switch
        {
            Locales.English => "en",
            Locales.Русский => "ru",
            _ => throw new ArgumentOutOfRangeException(nameof(locale), locale, null)
        };
        
        _botData.SaveData();
        Thread.CurrentThread.CurrentUICulture = _botData.GetOrNewGuild(Context.Guild).GetLocale();

        await RespondAsync(_locale["resp.guild.locale.set"]);
    }
}