using Discord;
using Discord.Interactions;
using Discord.Net;
using Discord.WebSocket;
using Lavalink4NET;
using Lavalink4NET.Artwork;
using Lavalink4NET.Player;
using Lavalink4NET.Rest;
using Microsoft.Extensions.Localization;
using SawayaSharp.Data;
using SawayaSharp.Utils;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
// ReSharper disable UnusedTupleComponentInReturnValue

namespace SawayaSharp.Modules;

[Group("player", "A list of commands for playing music")]
public class PlayerModule: InteractionModuleBase
{
    readonly SharedLocale _locale;
    readonly IAudioService _audio;
    readonly ArtworkService _artwork;
    
    public PlayerModule(SharedLocale locale, IAudioService audio, ArtworkService artwork) {
        _locale = locale;
        _audio = audio;
        _artwork = artwork;
    }

    readonly static ConcurrentDictionary<ulong, IUserMessage> ControlsMessages = new();

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
            timeText = $"{Util.FormatTimeSpan(player.Position.Position)}/{Util.FormatTimeSpan(player.CurrentTrack.Duration)}";
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

        var loopText = player.IsLooping ? "⭯" : "➡";

        return $"{optinalTitle}{optionalAuthor}{seekBar}\n{playerState} {timeText} {volumeText} {loopText}";
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

    static MessageComponent BuildControlsButtons(IStringLocalizer locale) {
        return new ComponentBuilder()
            .WithButton(customId: "player-back", emote: new Emoji("⏪"), style: ButtonStyle.Secondary)
            .WithButton(customId: "player-play", emote: new Emoji("⏯"), style: ButtonStyle.Secondary)
            .WithButton(customId: "player-stop", emote: new Emoji("⏹"), style: ButtonStyle.Secondary)
            .WithButton(customId: "player-forward", emote: new Emoji("⏩"), style: ButtonStyle.Secondary)
            .WithButton(customId: "player-skip", emote: new Emoji("⏭"), style: ButtonStyle.Secondary)
            .WithButton(customId: "player-voldown", emote: new Emoji("🔉"), style: ButtonStyle.Secondary, row: 1)
            .WithButton(customId: "player-volup", emote: new Emoji("🔊"), style: ButtonStyle.Secondary, row: 1)
            .WithButton(customId: "player-leave", emote: new Emoji("⏏"), style: ButtonStyle.Secondary, row: 1)
            .WithButton(customId: "player-loop", emote: new Emoji("🔁"), style: ButtonStyle.Secondary, row: 1)
            .WithButton(customId: "player-shuffle", emote: new Emoji("🔀"), style: ButtonStyle.Secondary, row: 1)
            .WithButton(customId: "player-queue", label: locale["resp.player.queue.title"], style: ButtonStyle.Secondary, row: 2)
            .WithButton(customId: "player-close", label: "🗙", style: ButtonStyle.Secondary, row: 2)
            .Build();
    }

    static async Task UpdateControls(IAudioService audio, IStringLocalizer locale, BotData botData, IGuild guild, IUserMessage message) {
        Thread.CurrentThread.CurrentUICulture = botData.GetOrNewGuild(guild).GetLocale();
        try {
            var (_, embed) = BuildControlsEmbed(audio, guild, locale);

            if (!message.Embeds.First().Description.Equals(embed.Description)) {
                await message.ModifyAsync(m =>
                {
                    m.Embed = Optional.Create(embed);
                });
            }
        }
        catch (HttpException) {
            ControlsMessages.Remove(guild.Id, out _);
        }
    }

    public static void RunControlsUpdateIfExists(IAudioService audio, IStringLocalizer locale, BotData botData, IGuild guild) {
        if (!ControlsMessages.TryGetValue(guild.Id, out var message)) return;
        Task.Run(() => UpdateControls(audio, locale, botData, guild, message));
    }

    public static async Task UpdateAllControls(IAudioService audio, IStringLocalizer locale, BotData botData) {
        foreach (var (_, message) in ControlsMessages) {
            if (message.Channel is SocketGuildChannel channel) {
                await UpdateControls(audio, locale, botData, channel.Guild, message);
            }
        }
    }

    async Task RespondWithControls() {
        if (ControlsMessages.ContainsKey(Context.Guild.Id)) {
            var message = ControlsMessages[Context.Guild.Id];
            await message.DeleteAsync();
            ControlsMessages.Remove(Context.Guild.Id, out _);
        }
        
        var (_, embed) = BuildControlsEmbed(_audio, Context.Guild, _locale);
        
        await RespondAsync(
            embed: embed,
            components: BuildControlsButtons(_locale));

        ControlsMessages.TryAdd(Context.Guild.Id, await GetOriginalResponseAsync());
    }

    bool CheckIfTroll() {
        var player = _audio.GetPlayer<QueuedLavalinkPlayer>(Context.Guild.Id);
        
        if (player == null) return false;
        if (Context.User is not SocketGuildUser user) return false;
        if (user.VoiceState == null) return true;
        return user.VoiceState.Value.VoiceChannel.Id != player.VoiceChannelId;
    }

    async Task<bool> DenyTroll() {
        if (!CheckIfTroll()) return false;
        
        await RespondAsync(_locale["resp.player.wrongvoicechannel"], ephemeral: true);
        return true;
    }
    
    [SlashCommand("controls", "Displays controls for the player")]
    public async Task Controls() {
        await RespondWithControls();
    }

    [SlashCommand("volume", "Sets playback volume")]
    public async Task SetVolume([Summary(description: "Volume to set 0-150")] int volume) {
        if (await DenyTroll()) return;
        
        volume = Math.Max(0, Math.Min(150, volume));
        
        var player = _audio.GetPlayer<QueuedLavalinkPlayer>(Context.Guild.Id);
        if (player != null) {
            await player.SetVolumeAsync(volume / 100.0f);
            await RespondAsync(_locale["resp.player.volume.set", volume], ephemeral: true);
        }
        else {
            await RespondAsync(_locale["resp.player.controls.noplayer"], ephemeral: true);
        }
    }

    [SlashCommand("togglepause", "Toggles playback of current track")]
    [ComponentInteraction("player-play", true)]
    public async Task PlayPause() {
        if (await DenyTroll()) return;
        
        var player = _audio.GetPlayer<QueuedLavalinkPlayer>(Context.Guild.Id);
        if (player != null) {
            try {
                if (player.State == PlayerState.Playing) {
                    await player.PauseAsync();
                }
                else {
                    await player.ResumeAsync();
                }
                
                await DeferAsync();
            }
            catch (InvalidOperationException) {
                await RespondAsync(_locale["resp.player.controls.notrack"], ephemeral: true);
            }
        }
        else {
            await RespondAsync(_locale["resp.player.controls.noplayer"], ephemeral: true);
        }
    }
    
    [SlashCommand("skip", "Skips current playing track")]
    [ComponentInteraction("player-skip", true)]
    public async Task Skip() {
        if (await DenyTroll()) return;
        
        var player = _audio.GetPlayer<QueuedLavalinkPlayer>(Context.Guild.Id);
        if (player != null) {
            if (player.Queue.Count > 0) {
                await player.SkipAsync();
                await RespondAsync(_locale["resp.player.controls.skipped"], ephemeral: true);
            }
            else {
                await RespondAsync(_locale["resp.player.controls.emptyqueue"], ephemeral: true);
            }
        }
        else {
            await RespondAsync(_locale["resp.player.controls.noplayer"], ephemeral: true);
        }
    }
    
    [SlashCommand("stop", "Stops playback of current song")]
    [ComponentInteraction("player-stop", true)]
    public async Task Stop() {
        if (await DenyTroll()) return;
        
        var player = _audio.GetPlayer<QueuedLavalinkPlayer>(Context.Guild.Id);
        if (player != null) {
            await player.StopAsync();
            await RespondAsync(_locale["resp.player.controls.stop"], ephemeral: true);
        }
        else {
            await RespondAsync(_locale["resp.player.controls.noplayer"], ephemeral: true);
        }
    }

    const float VolumeIncrement = 0.05f;
    
    [ComponentInteraction("player-volup", true)]
    public async Task VolumeUp() {
        if (await DenyTroll()) return;
        
        var player = _audio.GetPlayer<QueuedLavalinkPlayer>(Context.Guild.Id);
        if (player != null) {
            var newVolume = Math.Min(1.5f, player.Volume + VolumeIncrement);
            await player.SetVolumeAsync(newVolume);
            await DeferAsync();
        }
        else {
            await RespondAsync(_locale["resp.player.controls.noplayer"], ephemeral: true);
        }
    }
    
    [ComponentInteraction("player-voldown", true)]
    public async Task VolumeDown() {
        if (await DenyTroll()) return;
        
        var player = _audio.GetPlayer<QueuedLavalinkPlayer>(Context.Guild.Id);
        if (player != null) {
            var newVolume = Math.Max(0f, player.Volume - VolumeIncrement);
            await player.SetVolumeAsync(newVolume);
            await DeferAsync();
        }
        else {
            await RespondAsync(_locale["resp.player.controls.noplayer"], ephemeral: true);
        }
    }
    
    const float SeekIncrement = 10f;
    
    [ComponentInteraction("player-forward", true)]
    public async Task SeekForward() {
        if (await DenyTroll()) return;
        
        var player = _audio.GetPlayer<QueuedLavalinkPlayer>(Context.Guild.Id);
        if (player != null) {
            try {
                await player.SeekPositionAsync(player.Position.Position + TimeSpan.FromSeconds(SeekIncrement));
                await DeferAsync();
            } catch (InvalidOperationException) {
                await RespondAsync(_locale["resp.player.controls.notrack"], ephemeral: true);
            }
        }
        else {
            await RespondAsync(_locale["resp.player.controls.noplayer"], ephemeral: true);
        }
    }
    
    [ComponentInteraction("player-back", true)]
    public async Task SeekBack() {
        if (await DenyTroll()) return;
        
        var player = _audio.GetPlayer<QueuedLavalinkPlayer>(Context.Guild.Id);
        if (player != null) {
            try {
                await player.SeekPositionAsync(player.Position.Position - TimeSpan.FromSeconds(SeekIncrement));
                await DeferAsync();
            } catch (InvalidOperationException) {
                await RespondAsync(_locale["resp.player.controls.notrack"], ephemeral: true);
            }
        }
        else {
            await RespondAsync(_locale["resp.player.controls.noplayer"], ephemeral: true);
        }
    }
    
    [SlashCommand("leave", "Stops playback and leaves the voice channel")]
    [ComponentInteraction("player-leave", true)]
    public async Task Leave() {
        if (await DenyTroll()) return;
        
        var player = _audio.GetPlayer<QueuedLavalinkPlayer>(Context.Guild.Id);
        if (player != null) {
            await player.StopAsync(true);
            await RespondAsync(_locale["resp.player.controls.leave"], ephemeral: true);
        }
        else {
            await RespondAsync(_locale["resp.player.controls.noplayer"], ephemeral: true);
        }
    }
    
    [SlashCommand("loop", "Toggle looping of the playback")]
    [ComponentInteraction("player-loop", true)]
    public async Task Loop() {
        if (await DenyTroll()) return;
        
        var player = _audio.GetPlayer<QueuedLavalinkPlayer>(Context.Guild.Id);
        if (player != null) {
            player.IsLooping = !player.IsLooping;
            await RespondAsync(_locale[player.IsLooping ? "resp.player.controls.looped" : "resp.player.controls.unlooped"], ephemeral: true);
        }
        else {
            await RespondAsync(_locale["resp.player.controls.noplayer"], ephemeral: true);
        }
    }
    
    [SlashCommand("shuffle", "Shuffles player queue")]
    [ComponentInteraction("player-shuffle", true)]
    public async Task Shuffle() {
        if (await DenyTroll()) return;
        
        var player = _audio.GetPlayer<QueuedLavalinkPlayer>(Context.Guild.Id);
        if (player != null) {
            if (player.Queue.Count > 0) {
                player.Queue.Shuffle();
                await RespondAsync(_locale["resp.player.controls.shuffled"], ephemeral: true);
            }
            else {
                await RespondAsync(_locale["resp.player.controls.emptyqueue"], ephemeral: true);
            }
        }
        else {
            await RespondAsync(_locale["resp.player.controls.noplayer"], ephemeral: true);
        }
    }
    
    [ComponentInteraction("player-close", true)]
    public async Task CloseControls() {
        if (ControlsMessages.TryRemove(Context.Guild.Id, out var message)) {
            await message.DeleteAsync();
        }

        await DeferAsync();
    }

    [SlashCommand("play", "Attempts to enqueue specified query")]
    [SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
    public async Task Search([Summary(description: "Song to look up")] string query) {
        var searchResponse = query.Contains("https://") || query.Contains("http://") ?
            await _audio.LoadTracksAsync(query) :
            await _audio.LoadTracksAsync(query, SearchMode.YouTube);

        var searchResult = (searchResponse.Tracks ?? Enumerable.Empty<LavalinkTrack>()).ToList();
        
        var resultCount = searchResult.Count;
        
        if ((query.Contains("https://") || query.Contains("http://")) && searchResponse.PlaylistInfo?.Name != null) {
            await PlayLink(query);
            return;
        }

        switch (resultCount) {
            case <= 0:
                await RespondAsync(embed: new EmbedBuilder
                {
                    Title = $"\"{query}\"",
                    Description = _locale["resp.player.play.noresults"],
                    Color = Color.Red
                }.Build(), ephemeral: true);
                break;
            case 1:
                await PlayLink(searchResult.First().Uri!.ToString());
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
                    
                    embed.AddField($"{index}. {track.Title}", $"({Util.FormatTimeSpan(track.Duration)}) - by {track.Author}");
                    buttons.WithButton(customId: $"player-playlink:{track.Uri}", label: index.ToString(), style: ButtonStyle.Secondary);

                    index++;
                }

                await RespondAsync(embed: embed.Build(), components: buttons.Build(), ephemeral: true);
                break;
            }
        }
    }
    
    [ComponentInteraction("player-playlink:*", true)]
    // ReSharper disable once MemberCanBePrivate.Global
    public async Task PlayLink(string link) {
        await DeferAsync();
        
        var player = _audio.GetPlayer<QueuedLavalinkPlayer>(Context.Guild.Id);
        
        if (player == null) {
            var user = Context.User as SocketGuildUser;

            if (user!.VoiceChannel != null) {
                player = await _audio.JoinAsync<QueuedLavalinkPlayer>(Context.Guild.Id, user.VoiceChannel.Id, true);
                await player.SetVolumeAsync(0.2f);
            }
            else {
                await RespondAsync(_locale["resp.player.novoicechannel"], ephemeral: true);
                
                return;
            }
        }

        var response = await _audio.LoadTracksAsync(link);

        if (response.PlaylistInfo?.Name != null) {
            var playlist = response.PlaylistInfo;
            var tracks = (response.Tracks ?? Enumerable.Empty<LavalinkTrack>()).ToList();

            if (tracks.Count <= 0) {
                await RespondAsync(_locale["resp.playlist.invalid"], ephemeral: true);
                return;
            }

            var embed = new EmbedBuilder
            {
                Color = Color.Purple,
                Title = _locale["resp.player.play.enqueued"]
            };

            embed.AddField(_locale["resp.playlist.name"], playlist.Name ?? "null");
            embed.AddField(_locale["resp.playlist.trackcount.noparam"], tracks.Count);
            embed.AddField(_locale["resp.player.play.link"], link);
            
            if (playlist.SelectedTrack >= 0 && playlist.SelectedTrack < tracks.Count) {
                var selectedTrack = tracks[playlist.SelectedTrack];

                for (var i = playlist.SelectedTrack; i < tracks.Count; i++) {
                    await player.PlayAsync(tracks[i], true);
                }

                for (var i = 0; i < playlist.SelectedTrack; i++) {
                    await player.PlayAsync(tracks[i], true);
                }

                var thumbnail = await _artwork.ResolveAsync(selectedTrack);

                if (thumbnail != null) {
                    embed.ImageUrl = thumbnail.ToString();
                }
            }
            else {
                foreach (var track in tracks) {
                    await player.PlayAsync(track, true);
                }
            }

            var buttons = new ComponentBuilder()
                .WithButton(customId: $"player-playlink:{link}", emote: new Emoji("🔁"), style: ButtonStyle.Secondary)
                .Build();

            await RespondAsync(embed: embed.Build(), components: buttons);
        }
        else {
            var track = response.Tracks?.FirstOrDefault();
            
            if (track == null) {
                await RespondAsync(_locale["resp.player.play.invalidlink"], ephemeral: true);
                return;
            }

            await player.PlayAsync(track, true);

            var embed = new EmbedBuilder
            {
                Color = Color.Purple,
                Title = _locale["resp.player.play.enqueued"]
            };

            embed.AddField(track.Title, track.Author);
            embed.AddField(_locale["resp.player.play.duration"], Util.FormatTimeSpan(track.Duration));
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
    }

    [ComponentInteraction("player-queue", true)]
    [SlashCommand("queue", "Displays player queue")]
    public async Task ViewQueue() {
        var player = _audio.GetPlayer<QueuedLavalinkPlayer>(Context.Guild.Id);
        
        if (player != null) {
            var embed = new EmbedBuilder
            {
                Title = _locale["resp.player.queue.title"],
                Color = Color.Purple
            };

            if (player.CurrentTrack != null) {
                embed.AddField(_locale["resp.player.queue.nowplaying", player.CurrentTrack.Title], $"({Util.FormatTimeSpan(player.CurrentTrack.Duration)}) by {player.CurrentTrack.Author}");
            }
            
            if (player.Queue.Count <= 0) {
                embed.Description = _locale["resp.player.queue.empty"];
            }

            foreach (var track in player.Queue.Take(20)) {
                embed.AddField(track.Title, $"({Util.FormatTimeSpan(track.Duration)}) - by {track.Author}");
            }

            await RespondAsync(embed: embed.Build(), ephemeral: true);
        }
        else {
            await RespondAsync(_locale["resp.player.controls.noplayer"], ephemeral: true);
        }
    }
}