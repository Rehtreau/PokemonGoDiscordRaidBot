﻿using Discord.WebSocket;
using PokemonGoRaidBot.Config;
using PokemonGoRaidBot.Objects;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using System.Reflection;

namespace PokemonGoRaidBot
{
    public class CommandExecutor
    {
        private CommandHandler Handler;
        private SocketUserMessage Message;
        private SocketGuildUser User;
        private SocketGuild Guild;
        private MessageParser Parser;
        private bool IsAdmin;
        private BotConfig Config;

        private string[] Command;
        private string noAccessMessage = "You do not have the necessary permissions to change this setting.  You must be a server moderator or administrator to make this change.";

        public CommandExecutor(CommandHandler handler, SocketUserMessage message, MessageParser parser)
        {
            Handler = handler;
            Message = message;
            Parser = parser;

            Config = Handler.Config;
            Command = Message.Content.ToLowerInvariant().Substring(Config.Prefix.Length).Split(' ');

            User = (SocketGuildUser)Message.Author;
            Guild = ((SocketGuildChannel)Message.Channel).Guild;

            IsAdmin = User.GuildPermissions.Administrator || User.GuildPermissions.ManageGuild;
        }

        public async Task Execute()
        {
            MethodInfo[] methodInfos = GetType()
                           .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance);
            bool found = false;
            foreach (var method in methodInfos)
            {
                var attr = method.GetCustomAttribute<RaidBotCommandAttribute>();
                if (attr != null && attr.Command == Command[0])
                {
                    Task result = (Task)method.Invoke(this, new object[] { });
                    await result;
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                await Handler.MakeCommandMessage(Message.Channel, $"Unknown Command \"{Command[0]}\".  Type {Config.Prefix}help to see valid Commands for this bot.");
            }
        }

        [RaidBotCommand("join")] 
        private async Task Join()
        {
            var post = Handler.Posts.FirstOrDefault(x => x.UniqueId == Command[1]);
            if(post == null)
            {
                await Handler.MakeCommandMessage(Message.Channel, $"Raid post with Id \"{Command[1]}\" does not exist.");
                return;
            }

            int num;
            if(!Int32.TryParse(Command[2], out num))
            {
                await Handler.MakeCommandMessage(Message.Channel, $"Invalid number of raid joiners \"{Command[2]}\".");
                return;
            }

            post.JoinedUsers[Message.Author.Id] = num;
            await Handler.MakePost(post, Parser);
        }

        [RaidBotCommand("unjoin")]
        private async Task UnJoin()
        {

            var post = Handler.Posts.FirstOrDefault(x => x.UniqueId == Command[1]);
            if (post == null)
            {
                await Handler.MakeCommandMessage(Message.Channel, $"Raid post with Id \"{Command[1]}\" does not exist.");
                return;
            }

            post.JoinedUsers.Remove(Message.Author.Id);
            await Handler.MakePost(post, Parser);
        }

        [RaidBotCommand("info")]
        private async Task Info()
        {
            if (Command.Length > 1 && Command[1].Length > 2)
            {
                var info = Parser.ParsePokemon(Command[1], Config, Guild.Id);
                var response = "";

                if (info != null)
                    response += Parser.MakeInfoLine(info, Guild.Id);
                else
                    response += $"'{Command[1]}' did not match any raid boss names or aliases.";

                await Handler.MakeCommandMessage(Message.Channel, response);
            }
            else
            {
                var strings = new List<string>();
                var strInd = 0;

                var tierCommand = 0;

                var list = Config.PokemonInfoList.Where(x => x.CatchRate > 0);

                if (Command.Length > 1 && Int32.TryParse(Command[1], out tierCommand))
                {
                    list = list.Where(x => x.Tier == tierCommand);
                }

                var orderedList = list.OrderByDescending(x => x.Id).OrderByDescending(x => x.Tier);


                var maxBossLength = orderedList.Select(x => x.BossNameFormatted.Length).Max();
                strings.Add("```");
                foreach (var info in orderedList)
                {
                    var lineStr = Parser.MakeInfoLine(info, Guild.Id, maxBossLength);

                    if (strings[strInd].Length + lineStr.Length + 3 < 2000)
                        strings[strInd] += lineStr;
                    else
                    {
                        strings[strInd] += "```";
                        strings.Add("```" + lineStr);
                        strInd++;
                    }

                }
                strings[strInd] += "```";
                foreach (var str in strings)
                    await Message.Channel.SendMessageAsync(str);
            }
        }

        [RaidBotCommand("channel")]
        private async Task Channel()
        {
            if (!IsAdmin)
            {
                await Handler.MakeCommandMessage(Message.Channel, noAccessMessage);
                return;
            }
            if (Command.Length > 1 && !string.IsNullOrEmpty(Command[1]))
            {
                var channel = Guild.Channels.FirstOrDefault(x => x.Name.ToLowerInvariant() == Command[1].ToLowerInvariant());
                if (channel != null)
                {
                    Config.ServerChannels[Guild.Id] = channel.Id;
                    Config.Save();
                    await Handler.MakeCommandMessage(Message.Channel, $"Output channel for {Guild.Name} changed to {channel.Name}");
                }
                else
                {
                    await Handler.MakeCommandMessage(Message.Channel, $"{Guild.Name} does not contain a channel named \"{Command[1]}\"");
                }
            }
            else
            {
                Config.ServerChannels.Remove(Guild.Id);
                Config.Save();
                await Handler.MakeCommandMessage(Message.Channel, $"Output channel override for {Guild.Name} removed, default value \"{Config.OutputChannel}\" will be used.");
            }
        }

        [RaidBotCommand("nochannel")]
        private async Task NoChannel()
        {
            if (!IsAdmin)
            {
                await Handler.MakeCommandMessage(Message.Channel, noAccessMessage);
                return;
            }
            Config.ServerChannels[Guild.Id] = 0;
            Config.Save();
            await Handler.MakeCommandMessage(Message.Channel, "Bot will no longer output to a single channel.  Use !channel to undo this.");
        }

        [RaidBotCommand("alias")]
        private async Task Alias()
        {
            if (!IsAdmin)
            {
                await Handler.MakeCommandMessage(Message.Channel, noAccessMessage);
                return;
            }
            PokemonInfo foundInfo = null;
            foreach (var info in Config.PokemonInfoList)
            {
                if (info.Name.Equals(Command[1], StringComparison.OrdinalIgnoreCase))
                {
                    info.ServerAliases.Add(new KeyValuePair<ulong, string>(Guild.Id, Command[2].ToLowerInvariant()));
                    foundInfo = info;
                    Config.Save();
                    break;
                }
            }
            var resp = $"Pokemon matching '{Command[1]}' not found.";

            if (foundInfo != null)
                resp = $"Alias \"{Command[2].ToLowerInvariant()}\" added to {foundInfo.Name}";

            await Handler.MakeCommandMessage(Message.Channel, resp);
        }
        
        [RaidBotCommand("removealias")]
        private async Task RemoveAlias()
        {
            if (!IsAdmin)
            {
                await Handler.MakeCommandMessage(Message.Channel, noAccessMessage);
                return;
            }
            var aresp = "";
            foreach (var info in Config.PokemonInfoList)
            {
                if (info.Name.Equals(Command[1], StringComparison.OrdinalIgnoreCase))
                {
                    var alias = info.ServerAliases.FirstOrDefault(x => x.Key == Guild.Id && x.Value == Command[2].ToLowerInvariant());

                    if (!alias.Equals(default(KeyValuePair<ulong, string>)))
                    {
                        info.ServerAliases.Remove(alias);
                        Config.Save();
                        aresp = $"Alias \"{alias.Value}\" removed from {info.Name}";
                    }
                    else
                    {
                        aresp = $"Alias \"{alias.Value}\" not found on {info.Name}.  ";
                        var aliases = info.ServerAliases.Where(x => x.Key == Guild.Id);
                        aresp += aliases.Count() > 0 ? "Aliases that can be removed are: " + string.Join(", ", aliases.Select(x => x.Value)) : $"No aliases found for {info.Name}.";
                    }
                    break;
                }
            }
            await Handler.MakeCommandMessage(Message.Channel, aresp);
        }

        [RaidBotCommand("merge")]
        private async Task Merge()
        {
            var post1 = Handler.Posts.FirstOrDefault(x => x.UniqueId == Command[1]);
            if (post1 == null)
            {
                await Handler.MakeCommandMessage(Message.Channel, $"Post with Unique Id \"{Command[1]}\" not found.");
                return;
            }

            var post2 = Handler.Posts.FirstOrDefault(x => x.UniqueId == Command[2]);
            if (post2 == null)
            {
                await Handler.MakeCommandMessage(Message.Channel, $"Post with Unique Id \"{Command[2]}\" not found.");
                return;
            }
            if (post1.UserId != Message.Author.Id && post2.UserId != Message.Author.Id && !IsAdmin)
            {
                await Handler.MakeCommandMessage(Message.Channel, "Only one of the post creators, server mods, or administrators can merge raid posts.");
            }

            if (post1.HasEndDate)
            {
                if (post2.HasEndDate)
                {
                    post1.EndDate = new DateTime(Math.Max(post1.EndDate.Ticks, post2.EndDate.Ticks));
                }
            }
            else if (post2.HasEndDate)
                post1.EndDate = post2.EndDate;

            post1.Responses.AddRange(post2.Responses);

            Handler.DeletePost(post2);
            await Handler.MakePost(post1, Parser);
        }
        
        [RaidBotCommand("delete")]
        private async Task Delete()
        {
            var post = Handler.Posts.FirstOrDefault(x => x.UniqueId == Command[1]);
            if (post == null)
            {
                await Handler.MakeCommandMessage(Message.Channel, $"Post with Unique Id \"{Command[1]}\" not found.");
                return;
            }
            if (post.UserId != Message.Author.Id && !IsAdmin)
            {
                await Handler.MakeCommandMessage(Message.Channel, "Only the creator of the post, server mods, or administrators can delete raid posts.");
            }
            Handler.DeletePost(post);
        }

        [RaidBotCommand("pinall")]
        private async Task PinAll()
        {
            if (!IsAdmin)
            {
                await Handler.MakeCommandMessage(Message.Channel, noAccessMessage);
                return;
            }
            foreach (var pinallChannel in Guild.Channels)
            {
                if (!Config.PinChannels.Contains(pinallChannel.Id))
                {
                    Config.PinChannels.Add(pinallChannel.Id);
                }
            }
            Config.Save();
            await Handler.MakeCommandMessage(Message.Channel, $"All channels added to Pin Channels.");
        }

        [RaidBotCommand("pin")]
        private async Task Pin()
        {
            if (!IsAdmin)
            {
                await Handler.MakeCommandMessage(Message.Channel, noAccessMessage);
                return;
            }
            var pinchannel = Guild.Channels.FirstOrDefault(x => x.Name.ToLowerInvariant() == Command[1].ToLowerInvariant());
            if (pinchannel == null)
            {
                await Handler.MakeCommandMessage(Message.Channel, $"{Guild.Name} does not contain a channel named \"{Command[1]}\"");
                return;
            }
            if (!Config.PinChannels.Contains(pinchannel.Id))
            {
                Config.PinChannels.Add(pinchannel.Id);
                Config.Save();
                await Handler.MakeCommandMessage(Message.Channel, $"{pinchannel.Name} added to Pin Channels.");
            }
            else
            {
                await Handler.MakeCommandMessage(Message.Channel, $"{pinchannel.Name} is already in Pin Channels.");
            }
        }

        [RaidBotCommand("unpinall")]
        private async Task UnPinAll()
        {
            if (!IsAdmin)
            {
                await Handler.MakeCommandMessage(Message.Channel, noAccessMessage);
                return;
            }
            foreach (var pinallChannel in Guild.Channels)
            {
                if (Config.PinChannels.Contains(pinallChannel.Id))
                {
                    Config.PinChannels.Remove(pinallChannel.Id);
                }
            }
            Config.Save();
            await Handler.MakeCommandMessage(Message.Channel, $"All channels removed from Pin Channels.");
        }

        [RaidBotCommand("unpin")]
        private async Task UnPin()
        {
            if (!IsAdmin)
            {
                await Handler.MakeCommandMessage(Message.Channel, noAccessMessage);
                return;
            }
            var unpinchannel = Guild.Channels.FirstOrDefault(x => x.Name.ToLowerInvariant() == Command[1].ToLowerInvariant());
            if (unpinchannel == null)
            {
                await Handler.MakeCommandMessage(Message.Channel, $"{Guild.Name} does not contain a channel named \"{Command[1]}\"");
                return;
            }
            if (!Config.PinChannels.Contains(unpinchannel.Id))
            {
                await Handler.MakeCommandMessage(Message.Channel, $"{unpinchannel.Name} has not been added to Pin Channels.");
                return;
            }
            Config.PinChannels.Remove(unpinchannel.Id);
            Config.Save();
            await Handler.MakeCommandMessage(Message.Channel, $"{unpinchannel.Name} removed from Pin Channels.");
        }

        [RaidBotCommand("pinlist")]
        private async Task PinList()
        {
            var pinstring = "";
            foreach (var channel in Guild.Channels)
            {
                if (Config.PinChannels.Contains(channel.Id))
                    pinstring += $"\n{channel.Name}";
            }

            if (string.IsNullOrEmpty(pinstring)) pinstring = "No channels in Pin List.";
            else pinstring = "Pinned Channels:" + pinstring;

            await Handler.MakeCommandMessage(Message.Channel, pinstring);
        }

        [RaidBotCommand("help")]
        private async Task Help()
        {
            var helpMessage = Parser.GetHelpString(Config);
            await Message.Channel.SendMessageAsync(helpMessage);
        }
    }
}
