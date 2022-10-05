// See https://aka.ms/new-console-template for more information

using Discord;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SawayaSharp;
using SawayaSharp.Modules;
using System.Globalization;
using System.Reflection;
using Victoria;
using Victoria.Enums;
using Victoria.Responses.Search;
#pragma warning disable CS4014

// Getting configuration
var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json")
    .Build();

var botData = BotData.LoadData();

var discordConfig = new DiscordSocketConfig()
{
    GatewayIntents = GatewayIntents.Guilds | 
        GatewayIntents.GuildVoiceStates |
        GatewayIntents.GuildMembers
};

var interactionConfig = new InteractionServiceConfig();

// Adding services
var services = new ServiceCollection()
    .AddSingleton(botData)
    .AddSingleton(discordConfig)
    .AddSingleton(interactionConfig)
    .AddSingleton<DiscordSocketClient>()
    .AddSingleton<InteractionService>()
    .AddLavaNode(x =>
    {
        x.SelfDeaf = true;
    })
    .AddLogging(b => b.AddConsole())
    .AddLocalization(o => o.ResourcesPath = "Resources")
    .AddSingleton<SharedLocale>();

// Building service provider
var provider = services.BuildServiceProvider();

// Enabling localization
interactionConfig.LocalizationManager = new ResxLocalizationManager("SawayaSharp.Resources.locale", Assembly.GetExecutingAssembly(), CultureInfo.GetCultureInfo("ru"));

// Getting required services
var socketClient = provider.GetRequiredService<DiscordSocketClient>();
var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
var logger = loggerFactory.CreateLogger<Program>();
var lavaNode = provider.GetRequiredService<LavaNode>();
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

    if (!lavaNode.IsConnected) {
        await lavaNode.ConnectAsync();
    }

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
AppDomain.CurrentDomain.FirstChanceException += (sender, eventArgs) =>
{
    logger.LogError("Exception: {}", eventArgs.Exception);
};

// Update player controls
Task.Run(async () =>
{
    while (true) {
        await PlayerModule.UpdateAllControls(lavaNode, locale, botData);
        await Task.Delay(500);
    }
});

// Leave on track end
lavaNode.OnTrackEnded += async eventArgs =>
{
    if (eventArgs.Player.Queue.Count > 0) {
        await eventArgs.Player.SkipAsync();
        await eventArgs.Player.UpdateVolumeAsync((ushort)eventArgs.Player.Volume);
        return;
    }
    if (eventArgs.Reason is TrackEndReason.Replaced or TrackEndReason.Stopped) {
        await eventArgs.Player.UpdateVolumeAsync((ushort)eventArgs.Player.Volume);
        return;
    }

    await lavaNode.LeaveAsync(eventArgs.Player.VoiceChannel);
};

// Wait indefinitely on current task
await Task.Delay(-1);