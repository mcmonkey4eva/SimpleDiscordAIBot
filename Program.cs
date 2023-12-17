using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;
using Discord;
using Discord.WebSocket;
using FreneticUtilities.FreneticToolkit;
using FreneticUtilities.FreneticDataSyntax;
using FreneticUtilities.FreneticExtensions;
using System.Net.Http.Headers;
using Discord.Rest;
using ISImage = SixLabors.ImageSharp.Image;

namespace SimpleDiscordAIBot;

public static class ConfigHandler
{
    public static FDSSection Config = FDSUtility.ReadFile("config/config.fds");
}

public static class Util
{
    public static ByteArrayContent JSONContent(JObject jobj)
    {
        ByteArrayContent content = new(jobj.ToString(Formatting.None).EncodeUTF8());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    public static JObject ParseToJson(this string input)
    {
        try
        {
            return JObject.Parse(input);
        }
        catch (JsonReaderException ex)
        {
            throw new JsonReaderException($"Failed to parse JSON `{input.Replace("\n", "  ")}`: {ex.Message}");
        }
    }

    /// <summary>Sends a JSON object post and receives a JSON object back.</summary>
    public static async Task<JObject> PostJson(this HttpClient client, string url, JObject data)
    {
        return (await (await client.PostAsync(url, JSONContent(data))).Content.ReadAsStringAsync()).ParseToJson();
    }
}

public record class LLMParams
{
    public int max_new_tokens = 1000;
    public bool do_sample = true;
    public float temperature = 0.7f;
    public float top_p = 0.9f;
    public float typical_p = 1;
    public float min_p = 0.1f;
    public float repetition_penalty = 1.3f;
    public float encoder_repetition_penalty = 1.0f;
    public int repetition_penalty_range = 30;
    public int top_k = 0;
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

public static class SwarmAPI
{
    public static HttpClient Client = new();

    public static string Session = "";

    public static string Address => ConfigHandler.Config.GetString("swarm_url");

    static SwarmAPI()
    {
        Client.DefaultRequestHeaders.Add("user-agent", "SimpleDiscordAIBot/1.0");
        Client.Timeout = TimeSpan.FromMinutes(ConfigHandler.Config.GetFloat("swarm_timeout", 2).Value);
    }

    public class SessionInvalidException : Exception
    {
    }

    public static async Task GetSession()
    {
        JObject sessData = await Client.PostJson($"{Address}/API/GetNewSession", new());
        Session = sessData["session_id"].ToString();
    }

    public static async Task<List<(byte[], string)>> SendRequest(string prompt)
    {
        string model = ConfigHandler.Config.GetString("swarm_model");
        return await RunWithSession(async () =>
        {
            JObject request = new()
            {
                ["images"] = ConfigHandler.Config.GetInt("images", 1),
                ["session_id"] = Session,
                ["donotsave"] = true,
                ["prompt"] = prompt,
                ["negativeprompt"] = ConfigHandler.Config.GetString("image_negative"),
                ["model"] = model,
                ["width"] = ConfigHandler.Config.GetInt("image_width"),
                ["height"] = ConfigHandler.Config.GetInt("image_height"),
                ["cfgscale"] = ConfigHandler.Config.GetDouble("image_cfg"),
                ["steps"] = ConfigHandler.Config.GetInt("image_steps"),
                ["seed"] = -1
            };
            if (ConfigHandler.Config.GetBool("use_aitemplate", false).Value)
            {
                request["enableaitemplate"] = true;
            }
            JObject generated = await Client.PostJson($"{Address}/API/GenerateText2Image", request);
            if (generated.TryGetValue("error_id", out JToken errorId) && errorId.ToString() == "invalid_session_id")
            {
                throw new SessionInvalidException();
            }
            List<(byte[], string)> images = generated["images"].Select(img =>
            {
                string type = img.ToString().After("data:").Before(";");
                string ext = type == "image/gif" ? "gif" : "jpg";
                byte[] data = Convert.FromBase64String(img.ToString().After(";base64,"));
                return (data, ext);
            }).ToList();
            Console.WriteLine($"Generate {images.Count} images");
            if (images.Count == 0)
            {
                Console.WriteLine($"Raw response was: {generated}");
            }
            return images;
        });
    }

    public static async Task<T> RunWithSession<T>(Func<Task<T>> call)
    {
        if (string.IsNullOrWhiteSpace(Session))
        {
            await GetSession();
        }
        try
        {
            return await call();
        }
        catch (SessionInvalidException)
        {
            await GetSession();
            return await call();
        }
    }
}

public static class TextGenAPI
{
    public static HttpClient Client = new();

    static TextGenAPI()
    {
        Client.DefaultRequestHeaders.Add("user-agent", "SimpleDiscordAIBot/1.0");
        Client.Timeout = TimeSpan.FromMinutes(ConfigHandler.Config.GetFloat("textgen_timeout", 2).Value);
    }

    public static async Task<string> SendRequest(string prompt, LLMParams llmParam)
    {
        JObject jData = new()
        {
            ["prompt"] = prompt,
            ["max_tokens"] = llmParam.max_new_tokens,
            ["do_sample"] = llmParam.do_sample,
            ["temperature"] = llmParam.temperature,
            ["top_p"] = llmParam.top_p,
            ["min_p"] = llmParam.min_p,
            ["typical_p"] = llmParam.typical_p,
            ["repetition_penalty"] = llmParam.repetition_penalty,
            ["repetition_penalty_range"] = llmParam.repetition_penalty_range,
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
            ["custom_stopping_strings"] = JArray.FromObject(llmParam.stopping_strings)
        };
        FDSSection otherParams = ConfigHandler.Config.GetSection("textgen_params");
        if (otherParams is not null)
        {
            foreach (string key in otherParams.GetRootKeys())
            {
                FDSData val = otherParams.GetRootData(key);
                jData[key] = val.Internal switch
                {
                    string str => str,
                    int i => i,
                    long l => l,
                    float f => f,
                    double d => d,
                    bool b => b,
                    _ => throw new Exception($"Unknown type {val.Internal.GetType()} for {key}")
                };
            }
        }
        string serialized = JsonConvert.SerializeObject(jData);
        Console.WriteLine($"will send: {serialized}");
        HttpResponseMessage response = await Client.PostAsync($"{ConfigHandler.Config.GetString("textgen_url")}/v1/completions", new StringContent(serialized, StringConversionHelper.UTF8Encoding, "application/json"));
        string responseText = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"Response {(int)response.StatusCode} {response.StatusCode} text: {responseText}");
        string result = JObject.Parse(responseText)["choices"][0]["text"].ToString();
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
        string content = message.Content;
        if (message.Embeds is not null && message.Embeds.Count == 1 && !string.IsNullOrWhiteSpace(message.Embeds.First().Footer?.Text))
        {
            content = message.Embeds.First().Footer.Value.Text;
        }
        CachedMessage cache = new(content, message.Reference?.MessageId.GetValueOrDefault(0) ?? 0, message.Author?.Id ?? 0, message.Author?.Username ?? "");
        MessageCache[id] = cache;
        return cache;
    }

    public record struct MessageHolder(bool IsBot, string Name, string Text);

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
                string prefix = ConfigHandler.Config.GetString("prefix");
                string user = prefix + ConfigHandler.Config.GetString("user_name_default");
                string botName = prefix + ConfigHandler.Config.GetString("bot_name");
                bool isSelfRef = message.Content.Contains($"<@{Client.CurrentUser.Id}>") || message.Content.Contains($"<@!{Client.CurrentUser.Id}>");
                List<MessageHolder> priors = new();
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
                        if (count++ > 20)
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
                        priors.Add(new(true, botName, refMessage.Content));
                        priors.Add(new(false, $"{prefix}{aname}", msgRef));
                        refMessage = ref2.RefId == 0 ? null : await GetMessageCached(message.Channel.Id, ref2.RefId);
                    }
                }
                if (!isSelfRef)
                {
                    return;
                }
                string prior = priors.Select(m => $"{m.Name}: {m.Text}\n").Reverse().JoinString("");
                string input = message.Content.Replace($"<@{Client.CurrentUser.Id}>", "").Replace($"<@!{Client.CurrentUser.Id}>", "").Trim();
                Console.WriteLine($"Got input: {prior} {input}");
                bool doImage = false;
                string imagePrompt = null;
                string prePrompt = "";
                string FillPromptTags(string prompt)
                {
                    return prompt.Replace("{{user}}", user).Replace("{{username}}", rawUser).Replace("{{char}}", botName).Replace("{{bot}}", botName).Replace("{{date}}", DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm"));
                }
                if (input.StartsWith("[nopreprompt]"))
                {
                    input = input["[nopreprompt]".Length..].Trim();
                }
                else
                {
                    input = input.Replace("\n", " ");
                    string promptType = "preprompt";
                    FDSSection imagePrefixSection = ConfigHandler.Config.GetSection("prefixes");
                    if (imagePrefixSection is not null)
                    {
                        foreach (string imgPrefix in imagePrefixSection.GetRootKeys())
                        {
                            if (input.StartsWith(imgPrefix))
                            {
                                imagePrompt = imagePrefixSection.GetString(imgPrefix);
                                doImage = true;
                                promptType = "image_preprompt";
                                input = input[imgPrefix.Length..].Trim();
                                break;
                            }
                        }
                    }
                    if (!doImage && input.StartsWith("[text]"))
                    {
                        input = input["[text]".Length..].Trim();
                    }
                    else if (!doImage)
                    {
                        string isImagePrompt = ConfigHandler.Config.GetString($"guilds.{guildChannel.GuildId}.is_image_prompt");
                        if (isImagePrompt is not null)
                        {
                            string isImagePromptText = FillPromptTags(ConfigHandler.Config.GetStringList($"pre_prompts.{isImagePrompt}").JoinString("\n") + "\n");
                            string priorShort = priors.Take(2).Select(m => $"{(m.IsBot ? "Bot" : "User")}: {m.Text}\n").Reverse().JoinString("");
                            LLMParams paramsToUse = llmParams with { max_new_tokens = 5, repetition_penalty = 1 };
                            string isImageAnswer = await TextGenAPI.SendRequest($"{isImagePromptText}{priorShort}User: {input}\n{botName}:", paramsToUse);
                            Console.WriteLine($"Got is image answer for '{input}': {isImageAnswer}");
                            if (isImageAnswer.ToLowerFast().Trim().Contains("image"))
                            {
                                doImage = true;
                                promptType = "image_preprompt";
                                imagePrompt = ConfigHandler.Config.GetString("prefixes.[image]", "{llm_prompt}");
                            }
                        }
                    }
                    prePrompt = ConfigHandler.Config.GetString($"guilds.{guildChannel.GuildId}.{promptType}");
                    if (prePrompt is null)
                    {
                        prePrompt = ConfigHandler.Config.GetString($"guilds.*.{promptType}");
                        if (prePrompt is null)
                        {
                            Console.WriteLine("Bad guild");
                            return;
                        }
                    }
                    prePrompt = FillPromptTags(ConfigHandler.Config.GetStringList($"pre_prompts.{prePrompt}").JoinString("\n") + "\n");
                }
                using (message.Channel.EnterTypingState())
                {
                    LLMParams paramsToUse = doImage ? llmParams with { max_new_tokens = Math.Min(llmParams.max_new_tokens, 256) } : llmParams;
                    string res = (doImage && !imagePrompt.Contains("{llm_prompt}")) ? input : await TextGenAPI.SendRequest($"{prePrompt}{prior}{user}: {input}\n{botName}:", paramsToUse);
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
                    if (!doImage)
                    {
                        await (message as IUserMessage).ReplyAsync(res, allowedMentions: AllowedMentions.None);
                        return;
                    }
                    EmbedBuilder embedded = new EmbedBuilder() { Description = "(Please wait, generating...)" }.WithFooter(res);
                    string actualPrompt = imagePrompt.Replace(imagePrompt.Contains("{llm_prompt}") ? "{llm_prompt}" : "{prompt}", res.Replace("<", "").Replace(':', '_'));
                    IUserMessage botMessage = await (message as IUserMessage).ReplyAsync(embed: embedded.Build(), allowedMentions: AllowedMentions.None);
                    List<(byte[], string)> imgs = await SwarmAPI.SendRequest(actualPrompt);
                    if (imgs.Count == 0)
                    {
                        embedded.Description = "Failed to generate :(";
                    }
                    else
                    {
                        if (imgs.Count > 1)
                        {
                            ISImage[] isImgs = imgs.Select(i => ISImage.Load(i.Item1)).ToArray();
                            int sqrt = (int)Math.Ceiling(Math.Sqrt(isImgs.Length));
                            int width = 0, height = 0;
                            for (int i = 0; i < isImgs.Length; i++)
                            {
                                width = Math.Max(width, isImgs[i].Width);
                                height = Math.Max(height, isImgs[i].Height);
                            }
                            int rows = (int)Math.Ceiling((double)isImgs.Length / sqrt);
                            using Image<Rgba32> img = new(width * sqrt, height * rows);
                            for (int i = 0; i < isImgs.Length; i++)
                            {
                                int x = (i % sqrt) * width, y = (i / sqrt) * height;
                                img.Mutate(m => m.DrawImage(isImgs[i], new Point(x, y), 1));
                            }
                            using MemoryStream imgStream2 = new();
                            img.SaveAsJpeg(imgStream2);
                            imgStream2.Position = 0;
                            imgs = new() { (imgStream2.ToArray(), "jpg") };
                        }
                        embedded.Description = $"<@{message.Author.Id}>'s AI-generated image";
                        ulong logChan = ConfigHandler.Config.GetUlong("image_log_channel").Value;
                        using MemoryStream imgStream = new(imgs[0].Item1);
                        RestUserMessage msg = await (Client.GetChannel(logChan) as SocketTextChannel).SendFileAsync(imgStream, $"generated_img_for_{message.Author.Id}.{imgs[0].Item2}", text: botMessage.GetJumpUrl());
                        embedded.ImageUrl = msg.Attachments.First().Url;
                    }
                    await botMessage.ModifyAsync(m => m.Embed = embedded.Build());
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
