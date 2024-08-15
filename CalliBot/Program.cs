using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Townsharp.Infrastructure.Configuration;
using Townsharp.Infrastructure.Consoles;
using Townsharp.Infrastructure.Subscriptions;
using Townsharp.Infrastructure.WebApi;
using Microsoft.Win32.SafeHandles;

namespace DiscordBot
{
    class Program
    {
        private DiscordSocketClient _client;
        public IConsoleClient consoleClient;  
        public string accessToken = "";

        static async Task Main(string[] args)
            => await new Program().MainAsync();

        public async Task MainAsync()
        {
            var config = new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.All,
                MessageCacheSize = 1000
            };

            _client = new DiscordSocketClient(config);

            _client.Log += Log;
            _client.UserJoined += UserJoinedAsync;
            _client.MessageDeleted += MessageDeletedAsync;
            _client.MessageReceived += MessageReceivedAsync;

            string token = "";  

            bool serverConnBool = await ConnectToServer();

            if (!serverConnBool)
            {
                Console.WriteLine("Error occurred when connecting to the server");
            }
            else
            {
                Console.WriteLine("Server connected smoothly.");
            }

            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();
            await ChangeStatus("ElderToad is great!");

            await Task.Delay(-1);
        }

        private async Task ChangeStatus(string status)
        {
            await _client.SetCustomStatusAsync(status);
        }

        public async Task<bool> ConnectToServer()
        {
            try
            {
                var botCreds = BotCredential.FromEnvironmentVariables();
                var webApiClient = new WebApiBotClient(botCreds); // Api Client
                var consoleClientFactory = new ConsoleClientFactory(); // Used to connect to consoles
                var subscriptionMultiplexerFactory = new SubscriptionMultiplexerFactory(botCreds); // Used to create a subscription multiplexer, this mechanism may change in the future.
                var subscriptionMultiplexer = subscriptionMultiplexerFactory.Create(2); // how many concurrent connections do you need?  Rule of thumb is 200 servers per connection
                var accessRequestResult = await webApiClient.RequestConsoleAccessAsync(430116864);
                accessToken = accessRequestResult.Content.token!;
                var endpointUri = accessRequestResult.Content.BuildConsoleUri();
                Channel<ConsoleEvent> eventChannel = Channel.CreateUnbounded<ConsoleEvent>();
                consoleClient = consoleClientFactory.CreateClient(endpointUri, accessToken, eventChannel.Writer);  // Assigned to field
                CancellationTokenSource cancellationTokenSource = new CancellationTokenSource(); // used to end the session.
                await consoleClient.ConnectAsync(cancellationTokenSource.Token); // Connect the client to the console endpoint.
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error connecting to server: {ex.Message}");
                return false;
            }
        }

        private Task Log(LogMessage msg)
        {
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        }

        private async Task MessageReceivedAsync(SocketMessage message)
        {
            if (!(message is SocketUserMessage userMessage) || userMessage.Author.IsBot)
                return;

            var context = new SocketCommandContext(_client, userMessage);
            int argPos = 0;
            if (!userMessage.HasStringPrefix("!", ref argPos))
                return;

            var command = userMessage.Content.Substring(argPos).Split(' ')[0].ToLower();
            var embedBuilder = new EmbedBuilder()
                .WithColor(Color.Blue)
                .WithAuthor(userMessage.Author)
                .WithCurrentTimestamp();

            switch (command)
            {
                case "test":
                    embedBuilder.WithDescription($"It works, {userMessage.Author.Mention}!");
                    break;

                case "cmd":
                    if (userMessage.Author is SocketGuildUser guildUser && !HasRole(guildUser, "CorbotAccess"))
                    {
                        embedBuilder.WithDescription("You don't have Corbot Access!")
                                    .WithColor(Color.Red);
                        break;
                    }

                    var commandToExecute = userMessage.Content.Replace("!cmd ", "");
                    Console.WriteLine(commandToExecute);
                    try
                    {
                        await consoleClient.RunCommandAsync(commandToExecute);
                        embedBuilder.WithDescription($"Sent command `{commandToExecute}` successfully!");
                    }
                    catch
                    {
                        embedBuilder.WithDescription("Error occurred when executing the command. Attempting to restart...")
                                    .WithColor(Color.Orange);

                        bool serverConnBool = await ConnectToServer();
                        if (!serverConnBool)
                        {
                            embedBuilder.WithDescription("Server offline or error occurred.")
                                        .WithColor(Color.Red);
                        }
                        else
                        {
                            try
                            {
                                await consoleClient.RunCommandAsync(commandToExecute);
                                embedBuilder.WithDescription($"Server connected successfully. Sent command `{commandToExecute}`!")
                                            .WithColor(Color.Green);
                            }
                            catch (Exception ex)
                            {
                                embedBuilder.WithDescription($"Error executing command after reconnection: {ex.Message}")
                                            .WithColor(Color.Red);
                            }
                        }
                    }
                    break;

   




                default:
                    embedBuilder.WithDescription("Unknown command.")
                                .WithColor(Color.Red);
                    break;
            }

            await context.Channel.SendMessageAsync(embed: embedBuilder.Build());
        }

        private bool HasRole(SocketGuildUser user, string roleName)
        {
            return user.Roles.Any(r => r.Name == roleName);
        }

        private async Task UserJoinedAsync(SocketGuildUser user)
        {
            Console.WriteLine(user.Username);

            var embed = new EmbedBuilder()
                .WithTitle("Welcome!")
                .WithDescription($"Hello {user.Mention}, welcome to {user.Guild.Name}!")
                .WithThumbnailUrl(user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                .WithColor(Color.Green)
                .WithTimestamp(DateTimeOffset.Now)
                .AddField("Account Created", user.CreatedAt.ToString("f"), true)
                .AddField("Joined Server", user.JoinedAt?.ToString("f"), true)
                .WithFooter(footer => footer.Text = "We're glad to have you here!");

            await _client.SetCustomStatusAsync($"Welcome {user.DisplayName}!");

            var channel = _client.GetChannel(1263921182072373248) as IMessageChannel;
            if (channel != null)
            {
                await channel.SendMessageAsync(embed: embed.Build());
            }
        }

        private async Task MessageDeletedAsync(Cacheable<IMessage, ulong> cachedMessage, Cacheable<IMessageChannel, ulong> cachedChannel)
        {
            try
            {
                var channel = await cachedChannel.GetOrDownloadAsync();
                if (channel == null)
                {
                    Console.WriteLine("Unable to retrieve the channel information.");
                    return;
                }

                if (!cachedMessage.HasValue)
                {
                    Console.WriteLine("Unable to retrieve the deleted message information.");

                    var logChannel1 = _client.GetChannel(1273168654128644149) as IMessageChannel;
                    if (logChannel1 != null && !cachedMessage.HasValue && channel.Name != "online-players")
                    {
                        var embed = new EmbedBuilder()
                            .WithColor(Color.Red)
                            .WithTitle("Message Deleted")
                            .WithDescription($"A message was deleted in `{channel.Name}` but the message content was unable to be retrieved. Check the Audit Log for more info.")
                            .WithCurrentTimestamp()
                            .Build();

                        await logChannel1.SendMessageAsync(embed: embed);
                    }

                    return;
                }

                var message = await cachedMessage.GetOrDownloadAsync();
                string messageContent = message.Content;
                var author = message.Author;

                Console.WriteLine($"A message was deleted in {channel.Name}. Content: {messageContent}");

                var logChannel = _client.GetChannel(1273168654128644149) as IMessageChannel;
                if (logChannel != null && channel.Name != "online-players")
                {
                    var embed = new EmbedBuilder()
                        .WithColor(Color.Red)
                        .WithTitle("Message Deleted")
                        .WithDescription($"A message was deleted in `{channel.Name}`")
                        .AddField("Content", messageContent)
                        .AddField("Author", author.Username)
                        .WithThumbnailUrl(author.GetAvatarUrl() ?? author.GetDefaultAvatarUrl())
                        .WithCurrentTimestamp()
                        .Build();

                    await logChannel.SendMessageAsync(embed: embed);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Error occurred: " + e.Message);
            }
        }
    }
}
