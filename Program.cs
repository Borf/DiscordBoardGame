using Discord;
using Discord.WebSocket;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace DiscordBoardGame
{
    public class Program
    {
        private static DiscordSocketClient client;

        public static List<Player> Players = new List<Player>();
        public static int Round = 0;
        public static bool Busy = false;
        public static string QuestionCat = "hard";
        public static int Question = -1;

        public static Random random = new Random();

        public static JsonElement questions;

        public static List<int> QuestionTiles = new List<int>() { 7, 11, 14, 20, 24 };
        public static List<int> DrinkTiles = new List<int>() { 1, 3, 8, 16, 19,23 };

        //                                                      G  Q  G  Q  G  Q  G, 
        public static List<int> RiggedRolls = new List<int>() { 3, 4, 1, 6, 2, 4, 4, 5 };
        public static string riggedname = "borf";
        public static int riggedQuestionIndex = 0;
        public static List<int> QuestionsAsked = new List<int>();

        public async static Task Main(string[] args)
        {
            questions = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText("questions.json"));
            client = new DiscordSocketClient(new DiscordSocketConfig
            {
                AlwaysDownloadUsers = true
            });
            client.Log += Log;
            client.MessageReceived += MessageReceived;
            await client.LoginAsync(Discord.TokenType.Bot, File.ReadAllText("bottoken.txt"));
            await client.StartAsync();
            CreateHostBuilder(args).Build().Run();
        }

        private static Task Log(LogMessage arg)
        {
            return Task.CompletedTask;
        }

        private static async Task MessageReceived(SocketMessage msg)
        {
            if (msg.Author.IsBot || msg.Author.Id == client.CurrentUser.Id)
                return;
            if (msg.Channel.Name != "monopoly")
                return;

            if(msg.Content.ToLower() == "!join")
            {
                if(Round > 0)
                {
                    await msg.Channel.SendMessageAsync("Sorry the game has already started");
                    return;
                }
                if (Players.Any(p => p.Name == msg.Author.Username))
                {
                    await msg.Channel.SendMessageAsync("You're already in the game, wait for the game to start");
                    return;
                }
                var p = new Player()
                {
                    Avatar = msg.Author.GetAvatarUrl(),
                    Name = msg.Author.Username,
                    Position = 0
                };
                Players.Add(p);
                await msg.Channel.SendMessageAsync("You have joined");
                await Startup.Send(new { msg = "spawn", player = p });
            }
            if(msg.Content.ToLower() == "!start" && Round == 0)
            {
                QuestionsAsked = new List<int>();
                Round = 1;
                riggedQuestionIndex = 0;
                foreach (var player in Players)
                    player.Rolled = false;
                await msg.Channel.SendMessageAsync("The game has started!");
            }
            if (msg.Content.ToLower() == "!roll")
            {
                if(Round == 0)
                {
                    await msg.Channel.SendMessageAsync("The game hasn't started yet");
                    return;
                }
                if (Busy || Question != -1)
                {
                    await msg.Channel.SendMessageAsync("Hold on, wait for the previous player to finish their turn");
                    return;
                }

                if (!Players.Any(p => p.Name == msg.Author.Username))
                {
                    await msg.Channel.SendMessageAsync("You're not in the game");
                    return;
                }
                var player = Players.First(p => p.Name == msg.Author.Username);
                if (player.Rolled)
                {
                    await msg.Channel.SendMessageAsync("You rolled already, please wait");
                    return;
                }
                Busy = true;
                var newmsg = await msg.Channel.SendMessageAsync("Rolling......");


                int value = random.Next(6) + 1;

                if(msg.Author.Username.ToLower() == riggedname.ToLower())
                    value = RiggedRolls[Round - 1];
                else
                {
                    var riggedPlayer = Players.FirstOrDefault(p => p.Name.ToLower() == riggedname);
                    if (riggedPlayer != null)
                    {
                        int riggedPosition = riggedPlayer.Position;
                        if (player.Position + value >= 25)
                            value = 1;
                        if (player.Position > riggedPosition)
                            value = random.Next(1) + 1;
                    }
                }

                await Startup.Send(new { msg = "roll", Roll = value, Name = msg.Author.Username });
                player.Position += value;

                await Task.Delay(4000);
                await newmsg.ModifyAsync(mp =>
                {
                    mp.Content = "You rolled a " + value;
                });
                await Task.Delay(500 * value);
                player.Rolled = true;
                Busy = false;

                if(player.Position >= 25)
                {
                    await msg.Channel.SendMessageAsync("Player " + msg.Author.Mention + " has won!");
                    Round = 0;
                    Players.Clear();
                    return;
                }

                if(DrinkTiles.Contains(player.Position))
                {
                    await Startup.Send(new { msg = "incorrect", Answer = "" });
                    await Task.Delay(10000);
                    await Startup.Send(new { msg = "incorrecthide" });
                }

                if (QuestionTiles.Contains(player.Position))
                {
                    if (msg.Author.Username.ToLower() == riggedname.ToLower())
                    {
                        QuestionCat = "hard";
                        Question = riggedQuestionIndex;
                        riggedQuestionIndex++;
                    }
                    else
                    {
                        QuestionCat = "easy";

                        do
                        {
                            Question = random.Next(questions.GetProperty(QuestionCat).GetArrayLength());
                        } while (QuestionsAsked.Contains(Question));
                        QuestionsAsked.Add(Question);
                        if (QuestionsAsked.Count >= questions.GetProperty(QuestionCat).GetArrayLength())
                            QuestionsAsked = new List<int>();


                    }

                    string q = questions.GetProperty(QuestionCat)[Question].GetProperty("q").GetString();
                    await msg.Channel.SendMessageAsync("You landed on a question: " + q);
                    await Startup.Send(new { msg = "question", Text = q });


                    var t = Task.Run(async () =>
                    {
                        await Task.Delay(20000);
                        if (Question > -1)
                        {
                            await msg.Channel.SendMessageAsync("Too bad, you didn't get the answer, time for a drink!");
                            await Startup.Send(new { msg = "questionhide" });

                            await Startup.Send(new { msg = "incorrect", Answer = "The correct answer was: " + questions.GetProperty(QuestionCat)[Question].GetProperty("a")[0].GetString() });
                            Question = -1;
                            await Task.Delay(10000);
                            await Startup.Send(new { msg = "incorrecthide" });

                            await CheckForNextRound(msg.Channel);
                        }
                    });
                }
                else
                    await CheckForNextRound(msg.Channel);

            }
            if (Question > -1)
            {
                if (!Players.Any(p => p.Name == msg.Author.Username))
                    return;
                var player = Players.First(p => p.Name == msg.Author.Username);

                var answers = questions.GetProperty(QuestionCat)[Question].GetProperty("a");
                foreach(var answer in answers.EnumerateArray())
                {
                    if(answer.GetString().ToLower() == msg.Content.ToLower())
                    {
                        await msg.Channel.SendMessageAsync("That is correct!");
                        Question = -1;
                        await CheckForNextRound(msg.Channel);
                        await Startup.Send(new { msg = "questionhide" });
                        await Startup.Send(new { msg = "correct" });
                        await Task.Delay(10000);
                        await Startup.Send(new { msg = "correcthide" });
                    }
                }

            }



        }

        private static async Task CheckForNextRound(ISocketMessageChannel channel)
        {
            if (Players.Any(p => !p.Rolled))
                return;
            await channel.SendMessageAsync("That it for this round! time for the next round");

            Round++;
            foreach (var player in Players)
                player.Rolled = false;
            await Startup.Send(new { msg = "round", Round = Round });

        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
    }
}
