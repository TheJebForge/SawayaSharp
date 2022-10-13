using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using FuzzySharp;
using Lavalink4NET;
using Lavalink4NET.Artwork;
using Lavalink4NET.Player;
using Lavalink4NET.Rest;
using SawayaSharp.Data;
using SawayaSharp.Utils;
using PlaylistInfo = SawayaSharp.Data.PlaylistInfo;

// ReSharper disable MemberCanBePrivate.Global
#pragma warning disable CS1998

namespace SawayaSharp.Modules;

[Group("playlist", "A list of commands for managing music playlists")]
public class PlaylistModule: InteractionModuleBase
{
    readonly DiscordSocketClient _discord;
    readonly SharedLocale _locale;
    readonly IAudioService _audio;
    readonly ArtworkService _artwork;
    readonly BotData _botData;
    
    public PlaylistModule(SharedLocale locale, IAudioService audio, BotData botData, DiscordSocketClient discord, ArtworkService artwork) {
        _locale = locale;
        _audio = audio;
        _botData = botData;
        _discord = discord;
        _artwork = artwork;
    }

    async Task ListItems<T>(
        IReadOnlyCollection<T> items, 
        Func<T, ComponentBuilder, EmbedBuilder, int, Task> itemProcess, 
        string localizationPrefix, 
        EmbedBuilder baseEmbed, 
        int page, 
        string pageIdFormat,
        Func<ComponentBuilder, Task>? postProcess = null,
        bool ephemeral = true) {
        
        var count = items.Count;

        // Returning if no playlists
        if (count <= 0) {
            await RespondAsync(_locale[localizationPrefix + ".empty"], ephemeral: true);

            return;
        }

        // Returning if page is wrong
        if (page < 0 || page * 5 > count) {
            await RespondAsync(_locale[localizationPrefix + ".wrongpage"], ephemeral: true);

            return;
        }

        var buttons = new ComponentBuilder();

        if (count > 5) {
            baseEmbed.Footer = new EmbedFooterBuilder().WithText(_locale[localizationPrefix + ".page", page]);
        }

        var index = page * 5;

        foreach (var item in items.Skip(page * 5).Take(5)) {
            await itemProcess.Invoke(item, buttons, baseEmbed, index);
            index++;
        }
        
        if (page > 0) {
            buttons.WithButton(customId: string.Format(pageIdFormat, page - 1), emote: new Emoji("⬅"), row: 1, style: ButtonStyle.Secondary);
        }
        
        if ((page + 1) * 5 < count) {
            buttons.WithButton(customId: string.Format(pageIdFormat, page + 1), emote: new Emoji("➡"), row: 1, style: ButtonStyle.Secondary);
        }

        if (postProcess != null) await postProcess.Invoke(buttons);

        await RespondAsync(embed: baseEmbed.Build(), components: buttons.Build(), ephemeral: ephemeral);
    }

    string GetUniqueId() {
        var guid = Guid.NewGuid().ToString();

        return _botData.Playlists.Any(p => p.Id.Equals(guid)) ? GetUniqueId() : guid;
    }

    async Task<string> FetchUserUsername(ulong id) {
        var guildUser = await Context.Guild.GetUserAsync(id);

        if (guildUser != null) {
            return guildUser.DisplayName;
        }

        var user = await _discord.GetUserAsync(id);

        var username = user?.Username ?? "Deleted User";
        var discriminator = user?.Discriminator ?? "0000";

        return $"{username}#{discriminator}";
    }

    async Task ProcessPlaylist(PlaylistInfo playlist, ComponentBuilder buttons, EmbedBuilder embed, int index) {
        var user = await FetchUserUsername(playlist.Owner);
        embed.AddField($"{index + 1}. {playlist.Name}", $"by {user}");
        buttons.WithButton(customId: $"playlist-show:{playlist.Id},0", label: $"{index + 1}", style: ButtonStyle.Secondary, row: 0);
    }

    [SlashCommand("create", "Creates a new playlist")]
    public async Task PlaylistCreate([Summary(description: "Name of the playlist")] string name) {
        if(_botData.Playlists.Any(p => p.Owner.Equals(Context.User.Id) && p.Name.Equals(name))) {
            await RespondAsync(_locale["resp.playlist.create.exists"], ephemeral: true);
            return;
        }

        var newPlaylist = new PlaylistInfo()
        {
            Id = GetUniqueId(),
            Name = name,
            Owner = Context.User.Id
        };
        
        _botData.Playlists.Add(newPlaylist);
        _botData.SaveData();

        await PlaylistShow(newPlaylist.Id);
    }

    [ComponentInteraction("playlist-list:*,*", true)]
    [SlashCommand("list", "Lists playlists")]
    public async Task PlaylistList([Summary(description: "To show your playlists or all playlists")] bool mine = true, [Summary(description: "Number of the page, starts with 0")] int page = 0) {
        var playlists = mine ? 
            _botData.Playlists.Where(p => p.Owner == Context.User.Id).ToList() : 
            _botData.Playlists;

        await ListItems(
            playlists,
            ProcessPlaylist,
            "resp.playlist",
            new EmbedBuilder
            {
                Title = _locale[mine ? "resp.playlist.mine.title" : "resp.playlist.shared.title"],
                Color = Color.Purple
            },
            page,
            $"playlist-list:{mine},{{0}}");
    }
    
    [SlashCommand("search", "Search playlists")]
    public async Task PlaylistSearch([Summary(description: "What playlist to search for")] string query, [Summary(description: "To show your playlists or all playlists")] bool mine = false) {
        await PlaylistSearchComponent(mine, 0, query);
    }

    [ComponentInteraction("playlist-search:*,*,*", true)]
    public async Task PlaylistSearchComponent(bool mine, int page, string query) {
        var playlists = mine ? 
            _botData.Playlists.Where(p => p.Owner == Context.User.Id) : 
            _botData.Playlists.AsEnumerable();

        var searchResults = Process.ExtractSorted(new PlaylistInfo { Name = query }, playlists, p => p.Name);

        await ListItems(
            searchResults.Select(r => r.Value).ToList(),
            ProcessPlaylist,
            "resp.playlist",
            new EmbedBuilder
            {
                Title = _locale["resp.playlist.search.title", query],
                Description = _locale[mine ? "resp.playlist.search.mine" : "resp.playlist.search.shared"],
                Color = Color.Purple
            },
            page,
            $"playlist-search:{mine},{{0}},{query}");
    }

    async Task PlaylistActions(PlaylistInfo playlist, ComponentBuilder buttons, int page, int row = 2) {
        if (playlist.Contributors.Contains(Context.User.Id) || playlist.Owner == Context.User.Id) {
            buttons.WithButton(customId: $"playlist-search-track:{playlist.Id}", label: _locale["resp.playlist.controls.add"], style: ButtonStyle.Success, row: row);
        }

        if (playlist.Tracks.Count > 0) {
            buttons.WithButton(customId: $"playlist-play:{playlist.Id}", label: _locale["resp.playlist.controls.play"], style: ButtonStyle.Secondary, row: row)
                .WithButton(customId: $"playlist-random:{playlist.Id}", label: _locale["resp.playlist.controls.random"], style: ButtonStyle.Secondary, row: row);
        }
        
        buttons.WithButton(customId: $"playlist-show:{playlist.Id},{page}", label: _locale["resp.playlist.controls.refresh"], style: ButtonStyle.Secondary, row: row);

        if (playlist.Owner == Context.User.Id) {
            buttons.WithButton(customId: $"playlist-select-contributor:{playlist.Id},True", label: _locale["resp.playlist.controls.contributor.add"], style: ButtonStyle.Secondary, row: row + 1)
                .WithButton(customId: $"playlist-select-contributor:{playlist.Id},False", label: _locale["resp.playlist.controls.contributor.remove"], style: ButtonStyle.Secondary, row: row + 1)
                .WithButton(customId: $"playlist-start-rename:{playlist.Id}", label: _locale["resp.playlist.controls.rename"], style: ButtonStyle.Secondary, row: row + 1)
                .WithButton(customId: $"playlist-delete:{playlist.Id}", label: _locale["resp.playlist.controls.delete"], style: ButtonStyle.Danger, row: row + 1);
        }
    }

    static async Task ProcessTrack(PlaylistInfo playlist, LavalinkTrack track, ComponentBuilder buttons, EmbedBuilder embed, int index) {
        embed.AddField($"{index + 1}. {track.Title}", $"({Util.FormatTimeSpan(track.Duration)}) - by {track.Author}");
        buttons.WithButton(customId: $"playlist-track:{playlist.Id},{index}", label: $"{index + 1}", style: ButtonStyle.Secondary, row: 0);
    }
    
    [ComponentInteraction("playlist-show:*,*", true)]
    [SlashCommand("show", "Displays information about the playlist")]
    public async Task PlaylistShow([Summary(description: "ID of the playlist")] string id, [Summary(description: "Number of the page, starts with 0")] int page = 0) {
        var playlist = _botData.Playlists.FirstOrDefault(p => p.Id.Equals(id));

        if (playlist == null) {
            await RespondAsync(_locale["resp.playlist.id.notexist", id], ephemeral: true);

            return;
        }
        var owner = _locale["resp.playlist.owner", await FetchUserUsername(playlist.Owner)];
        var contributors = await Task.WhenAll(playlist.Contributors.Select(FetchUserUsername));
        var contributorsText = contributors.Length > 0 ? _locale["resp.playlist.contributors", string.Join(", ", contributors)] + "\n" : "";
        var trackCount = _locale["resp.playlist.trackcount", playlist.Tracks.Count];
        
        var embed = new EmbedBuilder
        {
            Title = _locale["resp.playlist.title", playlist.Name],
            Description = $"ID: {id}\n{owner}\n{contributorsText}{trackCount}",
            Color = Color.Purple
        };

        if (playlist.Tracks.Count <= 0) {
            var buttons = new ComponentBuilder();

            await PlaylistActions(playlist, buttons, page, 0);

            await RespondAsync(embed: embed.Build(), components: buttons.Build(), ephemeral: true);
        }
        else {
            await ListItems(
                playlist.Tracks,
                (track, buttons, embedBuilder, index) => ProcessTrack(playlist, track, buttons, embedBuilder, index),
                "resp.playlist",
                embed,
                page,
                $"playlist-show:{playlist.Id},{{0}}",
                buttons => PlaylistActions(playlist, buttons, page, playlist.Tracks.Count <= 5 ? 1 : 2));
        }
    }
    
    public class SearchModal : IModal
    {
        public string Title => "Track search";

        [InputLabel("Query")]
        [ModalTextInput("query", placeholder: "Search query or link to track")]
        public string Query { get; set; } = "";
    }

    [ComponentInteraction("playlist-search-track:*", true)]
    [SlashCommand("add", "Adds a track to specified playlist")]
    public async Task PlaylistSearchTrack([Summary(description: "ID of the playlist")] string id) {
        var playlist = _botData.Playlists.FirstOrDefault(p => p.Id.Equals(id));

        if (playlist == null) {
            await RespondAsync(_locale["resp.playlist.id.notexist", id], ephemeral: true);

            return;
        }
        
        if (Context.User.Id != playlist.Owner && !playlist.Contributors.Contains(Context.User.Id)) {
            await RespondAsync(_locale["resp.playlist.not.contributor"], ephemeral: true);

            return;
        }

        await RespondWithModalAsync<SearchModal>($"playlist-track-search-modal:{playlist.Id}");
    }

    [ModalInteraction("playlist-track-search-modal:*", true)]
    public async Task PlaylistAddTrackResults(string id, SearchModal modal) {
        var playlist = _botData.Playlists.FirstOrDefault(p => p.Id.Equals(id));

        if (playlist == null) {
            await RespondAsync(_locale["resp.playlist.id.notexist", id], ephemeral: true);

            return;
        }
        
        var query = modal.Query;
        
        var search = query.Contains("https://") || query.Contains("http://") ?
            await _audio.GetTracksAsync(query) :
            await _audio.GetTracksAsync(query, SearchMode.YouTube);

        var results = search.ToList();

        switch (results.Count) {
            case <= 0:
                await RespondAsync(embed: new EmbedBuilder
                {
                    Title = $"\"{query}\"",
                    Description = _locale["resp.player.play.noresults"],
                    Color = Color.Red
                }.Build(), ephemeral: true);
                break;
            case 1:
                await PlaylistAddTrack(id, results.First().Uri!.ToString());
                break;
            default:
            {
                var embed = new EmbedBuilder
                {
                    Title = $"\"{query}\"",
                    Description = _locale["resp.playlist.adding", playlist.Name],
                    Color = Color.Purple
                };

                var buttons = new ComponentBuilder();

                var index = 1;
                foreach (var track in results.Take(5)) {
                    
                    embed.AddField($"{index}. {track.Title}", $"({Util.FormatTimeSpan(track.Duration)}) - by {track.Author}");
                    buttons.WithButton(customId: $"playlist-add-track:{playlist.Id},{track.Uri}", label: index.ToString(), style: ButtonStyle.Secondary);

                    index++;
                }

                await RespondAsync(embed: embed.Build(), components: buttons.Build(), ephemeral: true);
                break;
            }
        }
    }

    [ComponentInteraction("playlist-add-track:*,*", true)]
    public async Task PlaylistAddTrack(string id, string link) {
        var playlist = _botData.Playlists.FirstOrDefault(p => p.Id.Equals(id));

        if (playlist == null) {
            await RespondAsync(_locale["resp.playlist.id.notexist", id], ephemeral: true);

            return;
        }

        if (Context.User.Id != playlist.Owner && !playlist.Contributors.Contains(Context.User.Id)) {
            await RespondAsync(_locale["resp.playlist.not.contributor"], ephemeral: true);

            return;
        }
        
        var track = await _audio.GetTrackAsync(link);

        if (track == null) {
            await RespondAsync(_locale["resp.player.play.invalidlink"], ephemeral: true);
            return;
        }
        
        playlist.Tracks.Add(track);
        _botData.SaveData();

        await RespondAsync(_locale["resp.playlist.added"], ephemeral: true);
    }

    [ComponentInteraction("playlist-track:*,*", true)]
    public async Task PlaylistTrack(string id, int position) {
        var playlist = _botData.Playlists.FirstOrDefault(p => p.Id.Equals(id));

        if (playlist == null) {
            await RespondAsync(_locale["resp.playlist.id.notexist", id], ephemeral: true);

            return;
        }

        var track = playlist.Tracks.ElementAtOrDefault(position);

        if (track == null) {
            await RespondAsync(_locale["resp.playlist.track.notexist", id], ephemeral: true);

            return;
        }

        var positionText = _locale["resp.playlist.track.position", position];

        var embed = new EmbedBuilder
        {
            Title = track.Title,
            Description = $"by {track.Author}\n{track.Uri}\n\n{positionText}",
            Color = Color.Purple
        };
        
        var artwork = await _artwork.ResolveAsync(track);

        if (artwork != null) {
            embed.ImageUrl = artwork.ToString();
        }

        var buttons = new ComponentBuilder()
            .WithButton(customId: $"player-playlink:{track.Uri}", label: _locale["resp.playlist.track.play"], style: ButtonStyle.Secondary);

        if (Context.User.Id == playlist.Owner) {
            buttons.WithButton(customId: $"playlist-delete-track:{playlist.Id},{position}", label: _locale["resp.playlist.track.delete"], style: ButtonStyle.Danger);
        }

        await RespondAsync(embed: embed.Build(), components: buttons.Build(), ephemeral: true);
    }

    async Task ShowConfirmation(string text, string button, ButtonStyle buttonStyle, string confirmId) {
        var embed = new EmbedBuilder
        {
            Title = _locale["resp.confirmation.title"],
            Color = Color.Red,
            Description = text
        };

        var buttons = new ComponentBuilder()
            .WithButton(customId: confirmId, label: button, style: buttonStyle);

        await RespondAsync(embed: embed.Build(), components: buttons.Build(), ephemeral: true);
    }

    [SlashCommand("delete", "Deletes the playlist")]
    [ComponentInteraction("playlist-delete:*", true)]
    public async Task PlaylistDelete(string id) {
        var playlist = _botData.Playlists.FirstOrDefault(p => p.Id.Equals(id));

        if (playlist == null) {
            await RespondAsync(_locale["resp.playlist.id.notexist", id], ephemeral: true);

            return;
        }
        
        if (Context.User.Id != playlist.Owner) {
            await RespondAsync(_locale["resp.playlist.not.owner"], ephemeral: true);

            return;
        }

        await ShowConfirmation(
            _locale["resp.playlist.delete.text", playlist.Name],
            _locale["resp.playlist.controls.delete"],
            ButtonStyle.Danger,
            $"playlist-confirm-delete:{playlist.Id}");
    }
    
    [ComponentInteraction("playlist-confirm-delete:*", true)]
    public async Task PlaylistConfirmDelete(string id) {
        var playlist = _botData.Playlists.FirstOrDefault(p => p.Id.Equals(id));

        if (playlist == null) {
            await RespondAsync(_locale["resp.playlist.id.notexist", id], ephemeral: true);

            return;
        }
        
        if (Context.User.Id != playlist.Owner) {
            await RespondAsync(_locale["resp.playlist.not.owner"], ephemeral: true);

            return;
        }

        _botData.Playlists.Remove(playlist);
        _botData.SaveData();

        await RespondAsync(_locale["resp.playlist.delete.done"], ephemeral: true);
    }
    
    [ComponentInteraction("playlist-delete-track:*,*", true)]
    public async Task PlaylistDeleteTrack(string id, int position) {
        var playlist = _botData.Playlists.FirstOrDefault(p => p.Id.Equals(id));

        if (playlist == null) {
            await RespondAsync(_locale["resp.playlist.id.notexist", id], ephemeral: true);

            return;
        }
        
        if (Context.User.Id != playlist.Owner) {
            await RespondAsync(_locale["resp.playlist.not.owner"], ephemeral: true);

            return;
        }

        var track = playlist.Tracks.ElementAtOrDefault(position);
        if (track == null) {
            await RespondAsync(_locale["resp.playlist.track.notexist"], ephemeral: true);

            return;
        }

        await ShowConfirmation(
            _locale["resp.playlist.track.delete.text", playlist.Name],
            _locale["resp.playlist.track.delete"],
            ButtonStyle.Danger,
            $"playlist-confirm-delete-track:{playlist.Id},{position},{playlist.Tracks.Count},{track.Uri}");
    }
    
    [ComponentInteraction("playlist-confirm-delete-track:*,*,*,*", true)]
    public async Task PlaylistConfirmDelete(string id, int position, int count, string link) {
        var playlist = _botData.Playlists.FirstOrDefault(p => p.Id.Equals(id));

        if (playlist == null) {
            await RespondAsync(_locale["resp.playlist.id.notexist", id], ephemeral: true);

            return;
        }
        
        if (Context.User.Id != playlist.Owner) {
            await RespondAsync(_locale["resp.playlist.not.owner"], ephemeral: true);

            return;
        }
        
        var track = playlist.Tracks.ElementAtOrDefault(position);
        if (track == null || playlist.Tracks.Count != count || !Equals(track.Uri?.ToString(), link)) {
            await RespondAsync(_locale["resp.playlist.track.notexist"], ephemeral: true);

            return;
        }

        playlist.Tracks.RemoveAt(position);
        _botData.SaveData();

        await RespondAsync(_locale["resp.playlist.track.delete.done"], ephemeral: true);
    }
    
    public class RenameModal : IModal
    {
        public string Title => "Rename playlist";

        [InputLabel("Name")]
        [ModalTextInput("new-name", placeholder: "New name for the playlist")]
        public string NewName { get; set; } = "";
    }
    
    [ComponentInteraction("playlist-start-rename:*", true)]
    [SlashCommand("rename", "Rename a playlist")]
    public async Task PlaylistStartRename([Summary(description: "ID of the playlist")] string id) {
        var playlist = _botData.Playlists.FirstOrDefault(p => p.Id.Equals(id));

        if (playlist == null) {
            await RespondAsync(_locale["resp.playlist.id.notexist", id], ephemeral: true);

            return;
        }
        
        if (Context.User.Id != playlist.Owner) {
            await RespondAsync(_locale["resp.playlist.not.owner"], ephemeral: true);

            return;
        }

        await RespondWithModalAsync<RenameModal>($"playlist-rename-modal:{playlist.Id}");
    }

    [ModalInteraction("playlist-rename-modal:*", true)]
    public async Task PlaylistRename(string id, RenameModal modal) {
        var playlist = _botData.Playlists.FirstOrDefault(p => p.Id.Equals(id));

        if (playlist == null) {
            await RespondAsync(_locale["resp.playlist.id.notexist", id], ephemeral: true);

            return;
        }
        
        if (Context.User.Id != playlist.Owner) {
            await RespondAsync(_locale["resp.playlist.not.owner"], ephemeral: true);

            return;
        }

        playlist.Name = modal.NewName;
        _botData.SaveData();

        await RespondAsync(_locale["resp.playlist.rename.done"], ephemeral: true);
    }
}