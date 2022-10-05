using Discord.Interactions;
using Discord.WebSocket;
using Lavalink4NET;
using Lavalink4NET.Player;
using Microsoft.Extensions.Logging;

namespace SawayaSharp.Modules;

public class TestModule: InteractionModuleBase
{
   ILogger<TestModule> _logger;
   IAudioService _audio;
   SharedLocale _locale;

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

      var player = _audio.GetPlayer<QueuedLavalinkPlayer>(user.Guild.Id);
      if (player == null) {
         player = await _audio.JoinAsync<QueuedLavalinkPlayer>(Context.Guild.Id, channel.Id, true);
         await player.SetVolumeAsync(0.1f);
      }
      
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

      var track = await _audio.GetTrackAsync(link);

      if (track == null) {
         await RespondAsync(_locale["resp.test.failure"]);
         return;
      }

      await player.PlayAsync(track, true);
      await RespondAsync(_locale["resp.test.success"]);
   }
}