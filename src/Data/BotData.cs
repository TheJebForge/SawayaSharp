using Discord;
using System.Collections.Concurrent;
using System.Text.Json;

namespace SawayaSharp.Data;

public class BotData
{
    public ConcurrentDictionary<ulong, GuildConfig> Guilds { get; set; } = new();

    public List<PlaylistInfo> Playlists { get; set; } = new();

    public GuildConfig GetOrNewGuild(IGuild guild) {
        return GetOrNewGuild(guild.Id);
    }
    
    public GuildConfig GetOrNewGuild(ulong id) {
        if (Guilds.ContainsKey(id)) return Guilds[id];
        
        Guilds[id] = new GuildConfig();
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