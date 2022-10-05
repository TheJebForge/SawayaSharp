using Discord;
using Discord.Interactions;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Victoria;
using Victoria.Enums;
using Victoria.Responses.Search;

#pragma warning disable CS4014

namespace SawayaSharp.Modules;

[Group("player", "A list of commands for playing music")]
public class PlayerModule: InteractionModuleBase
{
    readonly SharedLocale _locale;
    readonly LavaNode _lavaNode;
    readonly ILogger<PlayerModule> _logger;


    public PlayerModule(SharedLocale locale, LavaNode lavaNode, ILogger<PlayerModule> logger) {
        _locale = locale;
        _lavaNode = lavaNode;
        _logger = logger;
    }

    readonly static ConcurrentDictionary<IGuild, IUserMessage> ControlsMessages = new();

    static string FormatTimeSpan(TimeSpan timeSpan) {
        var dayFormat = timeSpan.TotalDays >= 1 ? "d\\." : "";
        var hourFormat = timeSpan.TotalHours >= 1 ? "hh\\:" : "";

        return timeSpan.ToString($"{dayFormat}{hourFormat}mm\\:ss");
    }

    static IEnumerable<string> SplitToChunks(string str, int chunkSize) {
        for (var index = 0; index < str.Length; index += chunkSize)
        {
            yield return str.Substring(index, Math.Min(chunkSize, str.Length - index));
        }
    } 

    static string BuildControlsDescription(LavaPlayer player) {
        const int width = 35;

        string title;
        string author;
        double percent;
        string timeText;

        if (player.Track != null) {
            title = string.Join("\n", SplitToChunks(player.Track.Title, width));
            author = string.Join("\n", SplitToChunks($"by {player.Track.Author}", width));
            percent = player.Track.Position.TotalSeconds / player.Track.Duration.TotalSeconds;
            timeText = $"{FormatTimeSpan(player.Track.Position)}/{FormatTimeSpan(player.Track.Duration)}";
        }
        else {
            title = "";
            author = "";
            percent = 0.0;
            timeText = "00:00/00:00";
        }

        var optinalTitle = title.Length > 0 ? $"{title}\n" : "";
        var optionalAuthor = author.Length > 0 ? $"{author}\n" : "";
        
        var seekBar = string.Join("",
            Enumerable.Range(1, width)
                .Select(i => 1.0 / width * i < percent ? "█" : "▁")
            );
        
        var playerState = player.PlayerState switch
        {
            PlayerState.Playing => "▶",
            PlayerState.Paused => "❘❘",
            PlayerState.Stopped => "■",
            PlayerState.None => "❌",
            _ => throw new ArgumentOutOfRangeException()
        };
        
        var volumeText = $"🔊{player.Volume}%";

        return $"{optinalTitle}{optionalAuthor}{seekBar}\n{playerState} {timeText} {volumeText}";
    }

    static (bool, Embed) BuildControlsEmbed(LavaNode lavaNode, IGuild guild, IStringLocalizer locale) {
        if (!lavaNode.TryGetPlayer(guild, out var player))
            return (false, new EmbedBuilder
            {
                Description = locale["resp.player.controls.noplayer"]
            }.Build());
        
        var embedBuilder = new EmbedBuilder
        {
            Description = $"```{BuildControlsDescription(player)}```",
        };

        if (player.Track != null) {
            embedBuilder.Url = player.Track.Url;
        }
            
        return (true, embedBuilder.Build());
    }

    static MessageComponent BuildControlsButtons() {
        return new ComponentBuilder()
            .WithButton(customId: "player-play", emote: new Emoji("⏯"), style: ButtonStyle.Secondary)
            .WithButton(customId: "player-skip", emote: new Emoji("⏭"), style: ButtonStyle.Secondary)
            .WithButton(customId: "player-stop", emote: new Emoji("⏹"), style: ButtonStyle.Secondary)
            .WithButton(customId: "player-voldown", emote: new Emoji("🔉"), style: ButtonStyle.Secondary, row: 1)
            .WithButton(customId: "player-volup", emote: new Emoji("🔊"), style: ButtonStyle.Secondary, row: 1)
            .WithButton(customId: "player-leave", emote: new Emoji("⏏"), style: ButtonStyle.Secondary, row: 1)
            .Build();
    }

    static async Task UpdateControls(LavaNode lavaNode, IStringLocalizer locale, BotData botData, IGuild guild, IUserMessage message) {
        Thread.CurrentThread.CurrentUICulture = botData.GetOrNewGuild(guild).GetLocale();
        try {
            await message.ModifyAsync(m =>
            {
                var (player, embed) = BuildControlsEmbed(lavaNode, guild, locale);
                m.Embed = Optional.Create(embed);
            });
        }
        catch (HttpException) {
            ControlsMessages.Remove(guild, out _);
        }
    }

    public static async Task UpdateAllControls(LavaNode lavaNode, IStringLocalizer locale, BotData botData) {
        foreach (var (guild, message) in ControlsMessages) {
            await UpdateControls(lavaNode, locale, botData, guild, message);
        }
    }

    async Task RespondWithControls() {
        if (ControlsMessages.ContainsKey(Context.Guild)) {
            var message = ControlsMessages[Context.Guild];
            await message.DeleteAsync();
            ControlsMessages.Remove(Context.Guild, out _);
        }
        
        var (result, embed) = BuildControlsEmbed(_lavaNode, Context.Guild, _locale);
        
        await RespondAsync(
            embed: embed,
            components: BuildControlsButtons());

        ControlsMessages.TryAdd(Context.Guild, await GetOriginalResponseAsync());
    }

    async Task AutodeleteResponse() {
        var message = await GetOriginalResponseAsync();
        Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2.0));
            try {
                await message.DeleteAsync();
            }
            catch (HttpException) { }
        });
    }
    
    [SlashCommand("controls", "Displays controls for the player")]
    public async Task Controls() {
        await RespondWithControls();
    }

    [SlashCommand("volume", "Sets playback volume")]
    public async Task SetVolume([Summary(description: "Volume to set 0-150")] int volume) {
        volume = Math.Max(0, Math.Min(150, volume));
        
        if (_lavaNode.TryGetPlayer(Context.Guild, out var player)) {
            await player.UpdateVolumeAsync((ushort)volume);
            await RespondAsync(_locale["resp.player.volume.set", volume]);
        }
        else {
            await RespondAsync(_locale["resp.player.controls.noplayer"]);
        }
        
        await AutodeleteResponse();
    }

    [ComponentInteraction("player-play", true)]
    public async Task PlayPause() {
        if (_lavaNode.TryGetPlayer(Context.Guild, out var player)) {
            if (player.PlayerState == PlayerState.Playing) {
                await player.PauseAsync();
                await RespondAsync(_locale["resp.player.controls.pause"]);
            }
            else {
                await player.ResumeAsync();
                await RespondAsync(_locale["resp.player.controls.play"]);
            }
        }
        else {
            await RespondAsync(_locale["resp.player.controls.noplayer"]);
        }

        await AutodeleteResponse();
    }
    
    [ComponentInteraction("player-skip", true)]
    public async Task Skip() {
        if (_lavaNode.TryGetPlayer(Context.Guild, out var player)) {
            if (player.Queue.Count > 0) {
                await player.SkipAsync();
                await RespondAsync(_locale["resp.player.controls.skipped"]);
            }
            else {
                await RespondAsync(_locale["resp.player.controls.emptyqueue"]);
            }
        }
        else {
            await RespondAsync(_locale["resp.player.controls.noplayer"]);
        }
        
        await AutodeleteResponse();
    }
    
    [ComponentInteraction("player-stop", true)]
    public async Task Stop() {
        if (_lavaNode.TryGetPlayer(Context.Guild, out var player)) {
            await player.StopAsync();
            await RespondAsync(_locale["resp.player.controls.stop"]);
        }
        else {
            await RespondAsync(_locale["resp.player.controls.noplayer"]);
        }
        
        await AutodeleteResponse();
    }

    const int VolumeIncrement = 5;
    
    [ComponentInteraction("player-volup", true)]
    public async Task VolumeUp() {
        if (_lavaNode.TryGetPlayer(Context.Guild, out var player)) {
            var newVolume = Math.Min(150, player.Volume + VolumeIncrement);
            await player.UpdateVolumeAsync((ushort)newVolume);
            await RespondAsync(_locale["resp.player.controls.volume.increase", player.Volume]);
        }
        else {
            await RespondAsync(_locale["resp.player.controls.noplayer"]);
        }
        
        await AutodeleteResponse();
    }
    
    [ComponentInteraction("player-voldown", true)]
    public async Task VolumeDown() {
        if (_lavaNode.TryGetPlayer(Context.Guild, out var player)) {
            var newVolume = Math.Max(0, player.Volume - VolumeIncrement);
            await player.UpdateVolumeAsync((ushort)newVolume);
            await RespondAsync(_locale["resp.player.controls.volume.decrease", player.Volume]);
        }
        else {
            await RespondAsync(_locale["resp.player.controls.noplayer"]);
        }
        
        await AutodeleteResponse();
    }
    
    [ComponentInteraction("player-leave", true)]
    public async Task Leave() {
        if (_lavaNode.TryGetPlayer(Context.Guild, out var player)) {
            await _lavaNode.LeaveAsync(player.VoiceChannel);
            await RespondAsync(_locale["resp.player.controls.leave"]);
        }
        else {
            await RespondAsync(_locale["resp.player.controls.noplayer"]);
        }
        
        await AutodeleteResponse();
    }

    [SlashCommand("play", "Attempts to enqueue specified query")]
    public async Task Search([Summary(description: "Song to look up")] string query) {
        var searchResult = query.Contains("https://") || query.Contains("http://") ?
            await _lavaNode.SearchAsync(SearchType.Direct, query) :
            await _lavaNode.SearchAsync(SearchType.YouTube, query);

        if (searchResult.Tracks.Count <= 0) {
            await RespondAsync(embed: new EmbedBuilder
            {
                Title = $"\"{query}\"",
                Description = _locale["resp.player.play.noresults"],
                Color = Color.Red
            }.Build());
        }
        else {
            if (searchResult.Tracks.Count == 1) {
                PlayLink(query);
            }
            else {
                var embed = new EmbedBuilder
                {
                    Title = $"\"{query}\"",
                    Color = Color.Purple
                };

                var buttons = new ComponentBuilder();

                var index = 1;
                foreach (var track in searchResult.Tracks.Take(5)) {
                    if (track == null) continue;
                    
                    embed.AddField($"{index}. {track.Title}", track.Author);
                    buttons.WithButton(customId: $"player-playlink:{track.Url}", label: index.ToString(), style: ButtonStyle.Secondary);

                    index++;
                }

                await RespondAsync(embed: embed.Build(), components: buttons.Build());
            }
        }
    }
    
    [ComponentInteraction("player-playlink:*", true)]
    // ReSharper disable once MemberCanBePrivate.Global
    public async Task PlayLink(string link) {
        if (!_lavaNode.TryGetPlayer(Context.Guild, out var player)) {
            var user = Context.User as SocketGuildUser;

            if (user!.VoiceChannel != null) {
                player = await _lavaNode.JoinAsync(user.VoiceChannel);
            }
            else {
                await RespondAsync(_locale["resp.player.novoicechannel"]);                
            }
        }

        var searchResult = await _lavaNode.SearchAsync(SearchType.Direct, link);
        var track = searchResult.Tracks.FirstOrDefault();

        var queued = false;
        
        switch (player.Queue.Count) {
            case <= 0 when player.Track == null:
                await player.PlayAsync(track);
                await player.UpdateVolumeAsync(50);
                queued = true;
                break;
            case < 20:
                player.Queue.Enqueue(track);
                queued = true;
                break;
        }
        
        await RespondAsync(_locale[queued ? "resp.player.play.enqueued" : "resp.player.play.toobigqueue"]);
        
        await AutodeleteResponse();
    }

    [SlashCommand("queue", "Displays player queue")]
    public async Task ViewQueue() {
        if (_lavaNode.TryGetPlayer(Context.Guild, out var player)) {
            var embed = new EmbedBuilder
            {
                Title = _locale["resp.player.queue.title"],
                Color = Color.Purple
            };

            if (player.Queue.Count <= 0) {
                embed.Description = _locale["resp.player.queue.empty"];
            }
            
            foreach (var track in player.Queue) {
                embed.AddField(track.Title, track.Author);
            }

            await RespondAsync(embed: embed.Build());
        }
        else {
            await RespondAsync(_locale["resp.player.controls.noplayer"]);
        }
    }
}