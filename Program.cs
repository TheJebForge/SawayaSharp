// See https://aka.ms/new-console-template for more information

using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Lavalink4NET.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SawayaSharp;
using SawayaSharp.Data;
using System.Globalization;
using System.Reflection;
using Lavalink4NET;
using Lavalink4NET.Artwork;
using Lavalink4NET.InactivityTracking.Extensions;
using Lavalink4NET.InactivityTracking.Trackers.Idle;
using Lavalink4NET.InactivityTracking.Trackers.Users;
using SawayaSharp.Modules;


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

var interactionConfig = new InteractionServiceConfig
{
    UseCompiledLambda = true
};

// Adding services
var services = new ServiceCollection()
    .AddSingleton(botData)
    .AddSingleton(discordConfig)
    .AddSingleton(interactionConfig)
    .AddSingleton<DiscordSocketClient>()
    .AddSingleton<InteractionService>()
    .AddLogging(b => b.AddConsole())
    .AddLocalization(o => o.ResourcesPath = "Resources")
    .AddSingleton<SharedLocale>()
    .AddLavalink()
    .AddInactivityTracking()
    .AddSingleton<ArtworkService>()
    .ConfigureLavalink(options => {
	    options.Passphrase = "youshallnotpass";
    })
    .ConfigureInactivityTracking(options => {})
    .Configure<UsersInactivityTrackerOptions>(options => {
	    options.Timeout = TimeSpan.FromMinutes(5);
    })
    .Configure<IdleInactivityTrackerOptions>(options => {
	    options.Timeout = TimeSpan.FromMinutes(5);
    });

// Building service provider
var provider = services.BuildServiceProvider();

// Enabling localization
interactionConfig.LocalizationManager = new ResxLocalizationManager("SawayaSharp.Resources.locale", Assembly.GetExecutingAssembly(), CultureInfo.GetCultureInfo("ru"));

// Getting required services
var socketClient = provider.GetRequiredService<DiscordSocketClient>();
var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
var logger = loggerFactory.CreateLogger<Program>();
var audioService = provider.GetRequiredService<IAudioService>();
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

    await audioService.StartAsync();
    
    logger.LogInformation("Music initialized");
    
    // Registering commands
    var debug = config["DebugGuild"];

    if (debug != null) {
        interactionService.RegisterCommandsToGuildAsync(ulong.Parse(debug));
    }
    else {
        interactionService.RegisterCommandsGloballyAsync();        
    }
};

// Executing interactions
socketClient.InteractionCreated += async i =>
{
    var ctx = new SocketInteractionContext(socketClient, i);
    
    if (i is SocketMessageComponent component) {
        logger.LogInformation("component id: {}", component.Data.CustomId);        
    }
    
    if (i is SocketModal modal) {
        logger.LogInformation("modal id: {}", modal.Data.CustomId);        
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

audioService.TrackEnded += (_, eventArgs) =>
{
    var guild = socketClient.GetGuild(eventArgs.Player.GuildId);
    PlayerModule.RunControlsUpdateIfExists(audioService, locale, botData, guild);
    return Task.CompletedTask;
};

// Process exit procedure
AppDomain.CurrentDomain.ProcessExit += async (_, _) =>
{
    await socketClient.LogoutAsync();
};

// Wait indefinitely on current task
await Task.Delay(-1);