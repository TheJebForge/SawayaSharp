using Discord.Interactions;
using Discord.WebSocket;
using Lavalink4NET;
using Lavalink4NET.DiscordNet;
using Lavalink4NET.Players;
using Lavalink4NET.Rest.Entities.Tracks;
using Microsoft.Extensions.Logging;

namespace SawayaSharp.Modules;

public class TestModule: InteractionModuleBase
{
   ILogger<TestModule> _logger;
   readonly IAudioService _audio;
   readonly SharedLocale _locale;

   public TestModule(ILogger<TestModule> logger, IAudioService audio, SharedLocale locale) {
      _logger = logger;
      _audio = audio;
      _locale = locale;
   }

   [SlashCommand("test", "test command")]
   public async Task Test() {
      var user = Context.User as SocketGuildUser;

      if (user?.VoiceState == null) {
         await RespondAsync(_locale["resp.test.failure"]);
         return;
      }
      
      var channel = user.VoiceState.Value.VoiceChannel;

      var player = await _audio.Players.RetrieveAsync(Context, 
	      PlayerFactory.Queued, 
	      new PlayerRetrieveOptions(PlayerChannelBehavior.Join));

      if (!player.IsSuccess) return;
      
      await player.Player.SetVolumeAsync(0.2f);

      var link = new Random().Next(0, 17) switch
      {
         0 => "https://www.youtube.com/watch?v=POb02mjj2zE",
         1 => "https://www.youtube.com/watch?v=Fbdq9WCkKiA",
         2 => "https://www.youtube.com/watch?v=6AEuWkFSwGc",
         3 => "https://youtu.be/dvZ0B4wt02g",
         4 => "https://youtu.be/JHZQlznBcSo",
         5 => "https://youtu.be/lBw-pWTuq28",
         6 => "https://youtu.be/PdPMyknz3OM",
         7 => "https://youtu.be/IXFjJGZlzEE",
         8 => "https://youtu.be/wVro3s6A8z8",
         9 => "https://youtu.be/ZQEM_vKH4gE",
         10 => "https://youtu.be/2BrenE71KEk",
         11 => "https://youtu.be/NdO6d1WLQKE",
         12 => "https://youtu.be/c9Yl9B5cP6U",
         13 => "https://youtu.be/ZaNAbMtvYGQ",
         14 => "https://youtu.be/ql9TiOhGx0s",
         15 => "https://youtu.be/4EKtjx4AAoQ",
         16 => "https://www.youtube.com/watch?v=mzFa1rmqeeU",
         _ => throw new ArgumentOutOfRangeException()
      };

      var track = await _audio.Tracks.LoadTrackAsync(link, TrackSearchMode.None);

      if (track == null) {
         await RespondAsync(_locale["resp.test.failure"]);
         return;
      }

      await player.Player.PlayAsync(track);
      await RespondAsync(_locale["resp.test.success"]);
   }
}