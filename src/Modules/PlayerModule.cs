﻿using Discord;
using Discord.Interactions;
using Discord.Net;
using Discord.WebSocket;
using Lavalink4NET;
using Lavalink4NET.Artwork;
using Lavalink4NET.Player;
using Lavalink4NET.Rest;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

#pragma warning disable CS4014

namespace SawayaSharp.Modules;

[Group("player", "A list of commands for playing music")]
public class PlayerModule: InteractionModuleBase
{
    readonly SharedLocale _locale;
    readonly IAudioService _audio;
    readonly ArtworkService _artwork;
    
    public PlayerModule(SharedLocale locale, ILogger<PlayerModule> logger, IAudioService audio, ArtworkService artwork) {
        _locale = locale;
        _audio = audio;
        _artwork = artwork;
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

    static string BuildControlsDescription(QueuedLavalinkPlayer player) {
        const int width = 35;

        string title;
        string author;
        double percent;
        string timeText;

        if (player.CurrentTrack != null) {
            title = string.Join("\n", SplitToChunks(player.CurrentTrack.Title, width));
            author = string.Join("\n", SplitToChunks($"by {player.CurrentTrack.Author}", width));
            percent = player.Position.Position.TotalSeconds / player.CurrentTrack.Duration.TotalSeconds;
            timeText = $"{FormatTimeSpan(player.Position.Position)}/{FormatTimeSpan(player.CurrentTrack.Duration)}";
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
        
        var playerState = player.State switch
        {
            PlayerState.Playing => "▶",
            PlayerState.Paused => "❘❘",
            PlayerState.NotPlaying => "■",
            PlayerState.Destroyed => "❌", 
            PlayerState.NotConnected => "🔌",
            _ => throw new ArgumentOutOfRangeException()
        };
        
        var volumeText = $"🔊{player.Volume * 100:0}%";

        return $"{optinalTitle}{optionalAuthor}{seekBar}\n{playerState} {timeText} {volumeText}";
    }

    static (bool, Embed) BuildControlsEmbed(IAudioService audio, IGuild guild, IStringLocalizer locale) {
        var player = audio.GetPlayer<QueuedLavalinkPlayer>(guild.Id);
        if (player == null) {
            return (false, new EmbedBuilder
            {
                Description = locale["resp.player.controls.noplayer"]
            }.Build());
        }

        var embedBuilder = new EmbedBuilder
        {
            Description = $"```{BuildControlsDescription(player)}```",
        };

        if (player.CurrentTrack != null) {
            embedBuilder.AddField(locale["resp.player.play.link"],player.CurrentTrack.Uri!.ToString());
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

    static async Task UpdateControls(IAudioService audio, IStringLocalizer locale, BotData botData, IGuild guild, IUserMessage message) {
        Thread.CurrentThread.CurrentUICulture = botData.GetOrNewGuild(guild).GetLocale();
        try {
            await message.ModifyAsync(m =>
            {
                var (player, embed) = BuildControlsEmbed(audio, guild, locale);
                m.Embed = Optional.Create(embed);
            });
        }
        catch (HttpException) {
            ControlsMessages.Remove(guild, out _);
        }
    }

    public static async Task UpdateAllControls(IAudioService audio, IStringLocalizer locale, BotData botData) {
        foreach (var (guild, message) in ControlsMessages) {
            await UpdateControls(audio, locale, botData, guild, message);
        }
    }

    async Task RespondWithControls() {
        if (ControlsMessages.ContainsKey(Context.Guild)) {
            var message = ControlsMessages[Context.Guild];
            await message.DeleteAsync();
            ControlsMessages.Remove(Context.Guild, out _);
        }
        
        var (result, embed) = BuildControlsEmbed(_audio, Context.Guild, _locale);
        
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
        
        var player = _audio.GetPlayer<QueuedLavalinkPlayer>(Context.Guild.Id);
        if (player != null) {
            await player.SetVolumeAsync(volume / 100.0f);
            await RespondAsync(_locale["resp.player.volume.set", volume]);
        }
        else {
            await RespondAsync(_locale["resp.player.controls.noplayer"]);
        }
        
        await AutodeleteResponse();
    }

    [ComponentInteraction("player-play", true)]
    public async Task PlayPause() {
        var player = _audio.GetPlayer<QueuedLavalinkPlayer>(Context.Guild.Id);
        if (player != null) {
            if (player.State == PlayerState.Playing) {
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
        var player = _audio.GetPlayer<QueuedLavalinkPlayer>(Context.Guild.Id);
        if (player != null) {
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
        var player = _audio.GetPlayer<QueuedLavalinkPlayer>(Context.Guild.Id);
        if (player != null) {
            await player.StopAsync();
            await RespondAsync(_locale["resp.player.controls.stop"]);
        }
        else {
            await RespondAsync(_locale["resp.player.controls.noplayer"]);
        }
        
        await AutodeleteResponse();
    }

    const float VolumeIncrement = 0.05f;
    
    [ComponentInteraction("player-volup", true)]
    public async Task VolumeUp() {
        var player = _audio.GetPlayer<QueuedLavalinkPlayer>(Context.Guild.Id);
        if (player != null) {
            var newVolume = Math.Min(1.5f, player.Volume + VolumeIncrement);
            await player.SetVolumeAsync(newVolume);
            await RespondAsync(_locale["resp.player.controls.volume.increase", Math.Round(player.Volume * 100)]);
        }
        else {
            await RespondAsync(_locale["resp.player.controls.noplayer"]);
        }
        
        await AutodeleteResponse();
    }
    
    [ComponentInteraction("player-voldown", true)]
    public async Task VolumeDown() {
        var player = _audio.GetPlayer<QueuedLavalinkPlayer>(Context.Guild.Id);
        if (player != null) {
            var newVolume = Math.Max(0f, player.Volume - VolumeIncrement);
            await player.SetVolumeAsync(newVolume);
            await RespondAsync(_locale["resp.player.controls.volume.decrease", Math.Round(player.Volume * 100)]);
        }
        else {
            await RespondAsync(_locale["resp.player.controls.noplayer"]);
        }
        
        await AutodeleteResponse();
    }
    
    [ComponentInteraction("player-leave", true)]
    public async Task Leave() {
        var player = _audio.GetPlayer<QueuedLavalinkPlayer>(Context.Guild.Id);
        if (player != null) {
            await player.StopAsync(true);
            await RespondAsync(_locale["resp.player.controls.leave"]);
        }
        else {
            await RespondAsync(_locale["resp.player.controls.noplayer"]);
        }
        
        await AutodeleteResponse();
    }

    [SlashCommand("play", "Attempts to enqueue specified query")]
    [SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
    public async Task Search([Summary(description: "Song to look up")] string query) {
        var searchResult = query.Contains("https://") || query.Contains("http://") ?
            await _audio.GetTracksAsync(query) :
            await _audio.GetTracksAsync(query, SearchMode.YouTube);
        
        var resultCount = searchResult.Count();
        
        switch (resultCount) {
            case <= 0:
                await RespondAsync(embed: new EmbedBuilder
                {
                    Title = $"\"{query}\"",
                    Description = _locale["resp.player.play.noresults"],
                    Color = Color.Red
                }.Build());
                break;
            case 1:
                PlayLink(searchResult.First().Uri!.ToString());
                break;
            default:
            {
                var embed = new EmbedBuilder
                {
                    Title = $"\"{query}\"",
                    Color = Color.Purple
                };

                var buttons = new ComponentBuilder();

                var index = 1;
                foreach (var track in searchResult.Take(5)) {
                    
                    embed.AddField($"{index}. {track.Title}", track.Author);
                    buttons.WithButton(customId: $"player-playlink:{track.Uri}", label: index.ToString(), style: ButtonStyle.Secondary);

                    index++;
                }

                await RespondAsync(embed: embed.Build(), components: buttons.Build());
                break;
            }
        }
    }
    
    [ComponentInteraction("player-playlink:*", true)]
    // ReSharper disable once MemberCanBePrivate.Global
    public async Task PlayLink(string link) {
        var player = _audio.GetPlayer<QueuedLavalinkPlayer>(Context.Guild.Id);
        
        if (player == null) {
            var user = Context.User as SocketGuildUser;

            if (user!.VoiceChannel != null) {
                player = await _audio.JoinAsync<QueuedLavalinkPlayer>(Context.Guild.Id, user.VoiceChannel.Id, true);
                await player.SetVolumeAsync(0.5f);
            }
            else {
                await RespondAsync(_locale["resp.player.novoicechannel"]);
                await AutodeleteResponse();
                
                return;
            }
        }

        var track = await _audio.GetTrackAsync(link);

        if (track == null) {
            await RespondAsync(_locale["resp.player.play.invalidlink"]);
            await AutodeleteResponse();
            return;
        }
        
        if (player.Queue.Count < 20) {
            await player.PlayAsync(track, true);

            var embed = new EmbedBuilder
            {
                Color = Color.Purple,
                Title = _locale["resp.player.play.enqueued"]
            };

            embed.AddField(track.Title, track.Author);
            embed.AddField(_locale["resp.player.play.link"], track.Uri);

            var thumbnail = await _artwork.ResolveAsync(track);

            if (thumbnail != null) {
                embed.ImageUrl = thumbnail.ToString();
            }

            var buttons = new ComponentBuilder()
                .WithButton(customId: $"player-playlink:{track.Uri}", emote: new Emoji("🔁"), style: ButtonStyle.Secondary)
                .Build();

            await RespondAsync(embed: embed.Build(), components: buttons);
        }
        else {
            await RespondAsync(_locale["resp.player.play.toobigqueue"]);
            await AutodeleteResponse();
        }
    }

    [SlashCommand("queue", "Displays player queue")]
    public async Task ViewQueue() {
        var player = _audio.GetPlayer<QueuedLavalinkPlayer>(Context.Guild.Id);
        
        if (player != null) {
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