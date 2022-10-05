// See https://aka.ms/new-console-template for more information

using Discord;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;
using Lavalink4NET;
using Lavalink4NET.Artwork;
using Lavalink4NET.DiscordNet;
using Lavalink4NET.Logging.Microsoft;
using Lavalink4NET.Player;
using Lavalink4NET.Tracking;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SawayaSharp;
using SawayaSharp.Modules;
using System.Globalization;
using System.Reflection;
#pragma warning disable CS4014

// Getting configuration
var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json")
    .Build();

var botData = BotData.LoadData();

var discordConfig = new DiscordSocketConfig
{
    GatewayIntents = GatewayIntents.Guilds | 
        GatewayIntents.GuildVoiceStates |
        GatewayIntents.GuildMembers
};

var interactionConfig = new InteractionServiceConfig();

var lavalinkOptions = new LavalinkNodeOptions
{
    RestUri = "http://localhost:2333/",
    WebSocketUri = "ws://localhost:2333",
    Password = "youshallnotpass",
    AllowResuming = false
};

// Adding services
var services = new ServiceCollection()
    .AddSingleton(botData)
    .AddSingleton(discordConfig)
    .AddSingleton(interactionConfig)
    .AddSingleton<DiscordSocketClient>()
    .AddSingleton<InteractionService>()
    .AddSingleton(lavalinkOptions)
    .AddSingleton<IDiscordClientWrapper, DiscordClientWrapper>()
    .AddSingleton<IAudioService, LavalinkNode>()
    .AddSingleton(new InactivityTrackingOptions())
    .AddSingleton<InactivityTrackingService>()
    .AddLogging(b => b.AddConsole())
    .AddLocalization(o => o.ResourcesPath = "Resources")
    .AddSingleton<MicrosoftExtensionsLogger>()
    .AddSingleton<ArtworkService>()
    .AddSingleton<SharedLocale>();

// Building service provider
var provider = services.BuildServiceProvider();

// Enabling localization
interactionConfig.LocalizationManager = new ResxLocalizationManager("SawayaSharp.Resources.locale", Assembly.GetExecutingAssembly(), CultureInfo.GetCultureInfo("ru"));

// Getting required services
var socketClient = provider.GetRequiredService<DiscordSocketClient>();
var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
var logger = loggerFactory.CreateLogger<Program>();
var audioService = provider.GetRequiredService<IAudioService>();
var inactivityTracker = provider.GetRequiredService<InactivityTrackingService>();
var interactionService = provider.GetRequiredService<InteractionService>();
var locale = provider.GetRequiredService<SharedLocale>();

// Starting Discord connection
await socketClient.LoginAsync(TokenType.Bot, config["Token"]);
await socketClient.StartAsync();

// Loading all modules
foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
    await interactionService.AddModulesAsync(assembly, provider);
}

// On Discord connection ready
socketClient.Ready += async () =>
{
    logger.LogInformation("Discord connected");

    // Initializing lavalink
    await audioService.InitializeAsync();
    
    // Starts inactivity tracker
    if(!inactivityTracker.IsTracking)
        inactivityTracker.BeginTracking();

    // Registering commands
    interactionService.RegisterCommandsGloballyAsync();
    
    foreach (var guild in socketClient.Guilds) {
        await guild.DeleteApplicationCommandsAsync();
    }
};

// Executing interactions
socketClient.InteractionCreated += async i =>
{
    var ctx = new SocketInteractionContext(socketClient, i);
    
    if (i is SocketMessageComponent component) {
        logger.LogInformation("id: {}", component.Data.CustomId);        
    }
    
    // Setting locale
    Thread.CurrentThread.CurrentUICulture = botData.GetOrNewGuild(ctx.Guild).GetLocale();
    
    await interactionService.ExecuteCommandAsync(ctx, provider);
};

// Logging all exceptions
AppDomain.CurrentDomain.FirstChanceException += (_, eventArgs) =>
{
    logger.LogError("Exception: {}", eventArgs.Exception);
};

// Update player controls
Task.Run(async () =>
{
    while (true) {
        await PlayerModule.UpdateAllControls(audioService, locale, botData);
        await Task.Delay(2500);
    }
});

audioService.TrackEnd += (_, eventArgs) =>
{
    var guild = socketClient.GetGuild(eventArgs.Player.GuildId);
    PlayerModule.RunControlsUpdateIfExists(audioService, locale, botData, guild);
    return Task.CompletedTask;
};

// Process exit procedure
AppDomain.CurrentDomain.ProcessExit += async (_, _) =>
{
    await socketClient.LogoutAsync();
    audioService.Dispose();
};

// Wait indefinitely on current task
await Task.Delay(-1);