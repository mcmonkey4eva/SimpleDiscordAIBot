# SimpleDiscordAIBot

This is a dirt-simple Discord bot to do AI stuff (TM).

This is a personal project, not an important or public-intended one, and as such will not be maintained.

Use it if you want. Yoink the code if you want. Do whatever you want.

It's hardcoded to use the tools I prefer (eg text-gen-webui), so, if you want different ones, feel free to fork and change the code for yourself.

# Usage

- Install requirements and configure them (see below)
- Add the bot to guilds you want it in
- In a terminal `cd`'d to the SimpleDiscordAIBot folder, `dotnet run` to run it. Maybe put it in a `screen` or something.
- You can now `@YourBotNameHere hi` on Discord to chat with it.
    - You can also reply to the bot's messages to continue a conversation.
    - It will only see messages in the current reply chain. If you `@` without a reply that automatically is a new chain.
- You can start a message with `[nopreprompt]` to hijack and disable the preprompt to directly muck with the LLM.
- It will try to block any form of URLs or etc.
- It will only reply in guilds, and only ones you allowed in the config. DMs and such don't work.

# Requirements

- A [registered Discord bot](https://discord.com/developers/applications) with message content intent enabled.
- [DotNET SDK 7.0](https://dotnet.microsoft.com/en-us/download/dotnet/7.0)
- [Text-Generation-WebUI](https://github.com/oobabooga/text-generation-webui) for running the LLM (Language Model) backend part, with a model of your choosing installed on it and working. Launch it with `--api --api-blocking-port 7861` (feel free to choose your own port, just don't reuse the main UI's port. Also don't expose it to the open internet)

# Config

- Make a folder named `config/` and make a file in it named `config.fds`, inside that put this content:

```yml
textgen_url: http://127.0.0.1:7861
discord_token: abc123
max_new_tokens: 1000
stopping_strings:
- \n###
user_name_default: Human
bot_name: Assistant
prefix: \x### \x

pre_prompts:
    example:
    - ### System: Conversation log between an online user interested in AI technology, and an experienced AI developer named Llama trying their best to help. Llama uses markdown syntax to add helpeful emphasis. Llama never uses URLs. Llama tries to be extremely kind and professional.
    - (Date: {{date}})
    - {{user}}: How are you today?
    - {{bot}}: I'm doing great today! I'm excited to help with your AI questions!
    - {{user}}: My name is {{username}}, Who are you?
    - {{bot}}: Nice to meet you {{username}}! I'm Llama, an AI helper!

guilds:
    123:
        preprompt: example
```

- Fill in textgen_url with the URL to the blocking API for your text-gen-webui server.
- Fill in Discord token with, yknow, a Discord bot token.
- Fill in pre_prompts with any preprompts you want to be available. You can use `{{date}}` for the current date, `{{user}}` for the user prefix, `{{bot}}` for the bot prefix, `{{username}}` for the actual Discord username.
    - I highly recommend a much longer preprompt than the example. Give it a lot of example conversation logs to ensure it stays on-topic and functional.
- Fill in guilds with a mapping of Guild ID numbers (use Discord dev mode to get those) to settings. Mostly just the reference the preprompt ID.
    - You can set a guild ID of `*` as a catch-all for any unlisted guilds the bot is in.
- Configure the other things how you want. The example above configured for the format that [StableBeluga](https://huggingface.co/stabilityai/StableBeluga2) uses.

OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
