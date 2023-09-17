using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;
using Discord;
using Discord.WebSocket;
using FreneticUtilities.FreneticToolkit;
using FreneticUtilities.FreneticDataSyntax;
using FreneticUtilities.FreneticExtensions;

namespace SimpleDiscordAIBot;

public static class ConfigHandler
{
    public static FDSSection Config = FDSUtility.ReadFile("config/config.fds");
}

public class LLMParams
{
    public int max_new_tokens = 1000;
    public bool do_sample = true;
    public float temperature = 0.7f;
    public float top_p = 0.1f;
    public float typical_p = 1;
    public float repetition_penalty = 1.18f;
    public float encoder_repetition_penalty = 1.0f;
    public int top_k = 40;
    public int min_length = 0;
    public int no_repeat_ngram_size = 0;
    public int num_beams = 1;
    public float penalty_alpha = 0;
    public int length_penalty = 1;
    public bool early_stopping = false;
    public int seed = -1;
    public bool add_bos_token = false;
    public bool skip_special_tokens = true;
    public string[] stopping_strings = Array.Empty<string>();
}

public static class TextGenAPI
{
    public static HttpClient Client = new();

    static TextGenAPI()
    {
        Client.DefaultRequestHeaders.Add("user-agent", "SimpleDiscordAIBot/1.0");
    }

    public static async Task<string> SendRequest(string prompt, LLMParams llmParam)
    {
        JObject jData = new()
        {
            ["prompt"] = prompt,
            ["max_new_tokens"] = llmParam.max_new_tokens,
            ["do_sample"] = llmParam.do_sample,
            ["temperature"] = llmParam.temperature,
            ["top_p"] = llmParam.top_p,
            ["typical_p"] = llmParam.typical_p,
            ["repetition_penalty"] = llmParam.repetition_penalty,
            ["encoder_repetition_penalty"] = llmParam.encoder_repetition_penalty,
            ["top_k"] = llmParam.top_k,
            ["min_length"] = llmParam.min_length,
            ["no_repeat_ngram_size"] = llmParam.no_repeat_ngram_size,
            ["num_beams"] = llmParam.num_beams,
            ["penalty_alpha"] = llmParam.penalty_alpha,
            ["length_penalty"] = llmParam.length_penalty,
            ["early_stopping"] = llmParam.early_stopping,
            ["seed"] = llmParam.seed,
            ["add_bos_token"] = llmParam.add_bos_token,
            ["skip_special_tokens"] = llmParam.skip_special_tokens,
            ["stopping_strings"] = JToken.FromObject(llmParam.stopping_strings)
        };
        string serialized = JsonConvert.SerializeObject(jData);
        Console.WriteLine($"will send: {serialized}");
        HttpResponseMessage response = await Client.PostAsync($"{ConfigHandler.Config.GetString("textgen_url")}/api/v1/generate", new StringContent(serialized, StringConversionHelper.UTF8Encoding, "application/json"));
        string responseText = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"Response {(int)response.StatusCode} {response.StatusCode} text: {responseText}");
        string result = JObject.Parse(responseText)["results"][0]["text"].ToString();
        return result;
    }
}

public static class Program
{
    public static DiscordSocketClient Client;

    public static AsciiMatcher AlphanumericMatcher = new(AsciiMatcher.BothCaseLetters + AsciiMatcher.Digits);

    public record class CachedMessage(string Content, ulong RefId, ulong Author, string AuthorName);

    public static Dictionary<ulong, CachedMessage> MessageCache = new();

    public static async Task<CachedMessage> GetMessageCached(ulong channel, ulong id)
    {
        if (MessageCache.TryGetValue(id, out CachedMessage res))
        {
            return res;
        }
        IMessage message = await (Client.GetChannel(channel) as SocketTextChannel).GetMessageAsync(id);
        Console.WriteLine($"Must fill cache on message {message.Id}");
        if (message is null)
        {
            MessageCache[id] = null;
            return null;
        }
        CachedMessage cache = new(message.Content, message.Reference?.MessageId.GetValueOrDefault(0) ?? 0, message.Author?.Id ?? 0, message.Author?.Username ?? "");
        MessageCache[id] = cache;
        return cache;
    }

    public static void Main()
    {
        Console.WriteLine("Starting...");
        DiscordSocketConfig config = new()
        {
            MessageCacheSize = 50,
            AlwaysDownloadUsers = true,
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
        };
        Client = new DiscordSocketClient(config);
        Client.Ready += () =>
        {
            Console.WriteLine("Bot ready.");
            return Task.CompletedTask;
        };
        LLMParams llmParams = new()
        {
            stopping_strings = ConfigHandler.Config.GetStringList("stopping_strings").Select(s => s.Replace("\\n", "\n")).ToArray(),
            max_new_tokens = ConfigHandler.Config.GetInt("max_new_tokens", 1000).Value
        };
        Client.MessageReceived += async (message) =>
        {
            try
            {
                if (message.Content is null || message.Author.IsBot || message.Author.IsWebhook || message is not IUserMessage userMessage || message.Channel is not IGuildChannel guildChannel)
                {
                    return;
                }
                string rawUser = AlphanumericMatcher.TrimToMatches(message.Author.Username);
                string user = rawUser;
                if (user.Length < 3)
                {
                }
                ulong guild = guildChannel.GuildId;
                string prePrompt = ConfigHandler.Config.GetString($"guilds.{guild}.preprompt");
                if (prePrompt is null)
                {
                    prePrompt = ConfigHandler.Config.GetString($"guilds.*.preprompt");
                    if (prePrompt is null)
                    {
                        Console.WriteLine("Bad guild");
                        return;
                    }
                }
                prePrompt = ConfigHandler.Config.GetStringList($"pre_prompts.{prePrompt}").JoinString("\n");
                string prefix = ConfigHandler.Config.GetString("prefix");
                user = prefix + ConfigHandler.Config.GetString("user_name_default");
                string botName = prefix + ConfigHandler.Config.GetString("bot_name");
                string prior = "";
                bool isSelfRef = message.Content.Contains($"<@{Client.CurrentUser.Id}>") || message.Content.Contains($"<@!{Client.CurrentUser.Id}>");
                if (message.Reference is not null && message.Reference.ChannelId == message.Channel.Id)
                {
                    CachedMessage cache = await GetMessageCached(message.Channel.Id, message.Reference.MessageId.Value);
                    if (cache is null)
                    {
                        return;
                    }
                    CachedMessage refMessage = await GetMessageCached(message.Channel.Id, message.Reference.MessageId.Value);
                    int count = 0;
                    while (refMessage is not null)
                    {
                        if (count++ > 50)
                        {
                            break;
                        }
                        isSelfRef = true;
                        if (refMessage.Author != Client.CurrentUser.Id || refMessage.RefId == 0)
                        {
                            return;
                        }
                        CachedMessage ref2 = await GetMessageCached(message.Channel.Id, refMessage.RefId);
                        if (ref2 is null)
                        {
                            return;
                        }
                        string aname = AlphanumericMatcher.TrimToMatches(ref2.AuthorName);
                        if (aname.Length < 3)
                        {
                            aname = ConfigHandler.Config.GetString("user_name_default");
                        }
                        string msgRef = ref2.Content.Replace($"<@{Client.CurrentUser.Id}>", "").Replace($"<@!{Client.CurrentUser.Id}>", "").Trim();
                        prior = $"{prefix}{aname}: {msgRef}\n{botName}: {refMessage.Content}\n{prior}";
                        refMessage = ref2.RefId == 0 ? null : await GetMessageCached(message.Channel.Id, ref2.RefId);
                    }
                }
                if (!isSelfRef)
                {
                    return;
                }
                prePrompt = prePrompt.Replace("{{user}}", user).Replace("{{username}}", rawUser).Replace("{{bot}}", botName).Replace("{{date}}", DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm")) + "\n";
                string input = message.Content.Replace($"<@{Client.CurrentUser.Id}>", "").Replace($"<@!{Client.CurrentUser.Id}>", "").Trim();
                Console.WriteLine($"Got input: {prior} {input}");
                if (input.StartsWith("[nopreprompt]"))
                {
                    prePrompt = "";
                    input = input["[nopreprompt]".Length..].Trim();
                }
                else
                {
                    input = input.Replace("\n", " ");
                }
                using (message.Channel.EnterTypingState())
                {
                    string res = await TextGenAPI.SendRequest($"{prePrompt}{prior}{user}: {input}\n{botName}:", llmParams);
                    int line = res.IndexOf("\n#");
                    if (line != -1)
                    {
                        res = res[..line];
                    }
                    if (res.Length > 1900)
                    {
                        res = res[0..1900] + "...";
                    }
                    Console.WriteLine($"\n\n{user}: {input}\n{botName}:{res}\n\n");
                    res = res.Replace("\\", "\\\\").Replace("<", "\\<").Replace(">", "\\>").Replace("@", "\\@ ")
                        .Replace("http://", "").Replace("https://", "").Trim();
                    if (string.IsNullOrWhiteSpace(res))
                    {
                        res = "[Error]";
                    }
                    await (message as IUserMessage).ReplyAsync(res, allowedMentions: AllowedMentions.None);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed: {ex}");
            }
        };
        Console.WriteLine("Logging in to Discord...");
        Client.LoginAsync(TokenType.Bot, ConfigHandler.Config.GetString("discord_token")).Wait();
        Console.WriteLine("Connecting to Discord...");
        Client.StartAsync().Wait();
        Console.WriteLine("Running Discord!");
        while (true)
        {
            string input = Console.ReadLine();
            if (input is null)
            {
                return;
            }
            input = input.Replace("\n", "   ");
            string fullPrompt = $"User: {input}\nBot: ";
            string res = TextGenAPI.SendRequest(fullPrompt, llmParams).Result;
            Console.WriteLine($"AI says back: {res}");
            int line = res.IndexOf('\n');
            if (line != -1)
            {
                res = res[..line];
            }
            Console.WriteLine($"\n\nUser: {input}\nBot: {res}\n\n");
        }
    }
}
