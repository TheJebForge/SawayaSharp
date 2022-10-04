using Discord;
using System.Globalization;
using System.Text.Json;

namespace SawayaSharp;

public class BotData
{
    public Dictionary<ulong, GuildConfig> Guilds { get; set; } = new();
    
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

    public GuildConfig GetOrNewGuild(IGuild guild) {
        return GetOrNewGuild(guild.Id);
    }
    
    public GuildConfig GetOrNewGuild(ulong id) {
        if (Guilds.ContainsKey(id)) return Guilds[id];
        
        Guilds.Add(id, new GuildConfig());
        SaveData();
        return Guilds[id];
    }
    
    
    
    
    
    
    
    public void SaveData() {
        var content = JsonSerializer.Serialize(this);
        File.WriteAllText("botdata.json", content);
    }

    public static BotData LoadData() {
        try {
            var content = File.ReadAllText("botdata.json");
            var obj = JsonSerializer.Deserialize<BotData>(content);
            return obj ?? new BotData();
        }
        catch (FileNotFoundException) {
            return new BotData();
        }
    }
}