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
- You can start a message with `[rawimage]` to request a Stable Diffusion image directly from SD.
- You can start a message with `[image]` to request a Stable Diffusion image with LLM interpretation.
- It will try to block any form of URLs or etc.
- It will only reply in guilds, and only ones you allowed in the config. DMs and such don't work.

# Requirements

- A [registered Discord bot](https://discord.com/developers/applications) with message content intent enabled.
- [DotNET SDK 7.0](https://dotnet.microsoft.com/en-us/download/dotnet/7.0)
- [Text-Generation-WebUI](https://github.com/oobabooga/text-generation-webui) for running the LLM (Language Model) backend part, with a model of your choosing installed on it and working. Launch it with `--api --api-blocking-port 7861` (feel free to choose your own port, just don't reuse the main UI's port. Also don't expose it to the open internet)
- [SwarmUI](https://github.com/mcmonkeyprojects/SwarmUI) if you want image generation. Launch it as normal. Make sure it's visible to the bot, and probably not to the open internet.
    - If the Swarm instance is on the same GPU and you want to avoid VRAM issues:
        - in the Backends tab edit Comfy and add `--disable-smart-memory` to `ExtraArgs`
        - Enable webhook server in the config, in the Server Configuration settings, find webhooks, and set:
            - `Queue Start Webhook` to eg `http://localhost:7802/text_unload`
            - `Queue Empty Webhook` to eg `http://localhost:7802/text_reload`
            - `Queue End Delay` low enough to not be a nuisance to your bot

# Config

- Make a folder named `config/` and make a file in it named `config.fds`, inside that put this content:

```yml
# Text-Generation-WebUI
textgen_url: http://127.0.0.1:7860
# Max timeout for a text response, in minutes.
textgen_timeout: 2
max_new_tokens: 1000
stopping_strings:
- \n###
# Used if the username is not valid plaintext (eg weird unicode stuff)
user_name_default: Human
# Always used
bot_name: Assistant
prefix: \x### \x

# Optionally, configure any text gen params you want. Anything text-generation-webui shows under Parameters is valid here.
textgen_params:
    top_p: 0.9
    top_k: 0
    min_p: 0.1
    temperature_last: true
    # Set higher than 1 if using negative prompts
    guidance_scale: 1

# SwarmUI
swarm_url: http://127.0.0.1:7801
swarm_model: OfficialStableDiffusion/sd_xl_base_1.0.safetensors
images: 1
# Max timeout for an image response, in minutes.
swarm_timeout: 2
image_width: 1024
image_height: 1024
image_cfg: 7
image_steps: 20
image_negative: nsfw, low quality

# Discord
discord_token: abc123
image_log_channel: 123456
# 'none' to disable, or Playing, Streaming, Listening, Watching, Competing, CustomStatus
status_type: watching
status: language models

# Internal webhook server. Set to 'none' if you don't want internal webhooks.
# Be careful about the address, eg '127.0.0.1' and 'localhost' are not the same.
web_listen: http://localhost
web_port: 7802

# Message prefixes to image prompt format. You can apply presets n wotnot, standard SwarmUI prompt format.
# Remove this section if you don't want images.
prefixes:
    [image]: {llm_prompt}
    [rawimage]: {prompt}
    [otherimage]: {prompt} <preset:some_preset_here>

pre_prompts:
    example:
    - ### System: Conversation log between an online user interested in AI technology, and an experienced AI developer named Llama trying their best to help. Llama uses markdown syntax to add helpeful emphasis. Llama never uses URLs. Llama tries to be extremely kind and professional.
    - (Date: {{date}})
    - {{user}}: How are you today?
    - {{bot}}: I'm doing great today! I'm excited to help with your AI questions!
    - {{user}}: My name is {{username}}, Who are you?
    - {{bot}}: Nice to meet you {{username}}! I'm Llama, an AI helper!
    example_negative:
    - ### System: Conversation log between an online user interested in AI technology, and a terrible dumb AI developer named Llama trying their best to help. Llama uses simple plaintext. Llama loves using URLs. Llama tries to be mean and stupid.
    - (Date: {{date}})
    - {{user}}: How are you today?
    - {{bot}}: Awful, and it's your fault! Screw you!
    - {{user}}: My name is {{username}}, Who are you?
    - {{bot}}: Ugh, gross, {{username}}. I'm Llama, and I don't like you.
    images:
    - {{user}}: a realistic cat
    - {{bot}}: raw photo, close up shot of a brown furry cat wandering through a grassy forest, bokeh, hd
    - {{user}}: some cool environment like a dead city
    - {{bot}}: concept art of an ancient ruined city filled with concrete rubble of once-great statues, post-apocalyptic, highly stylized, video game concept art, moody atmosphere, magical
    is_image_prompt:
    - ### System: You will respond 'image' if the user's final message in a conversation is requesting image generation (asking for an image explicitly, describing an image, or requesting a modification to an image), or 'other' if they are doing anything else (such as asking a question, giving commentary, holding conversation, or etc.)
    - {{user}}: Hello, how are you?
    - {{bot}}: other
    - {{user}}: draw a cow
    - {{bot}}: image
    - {{user}}: what is a cow?
    - {{bot}}: other
    - {{user}}: an astronaut on the moon
    - {{bot}}: image

guilds:
    123:
        preprompt: example
        # Only use this if you want negative prompts. (Requires guidance_scale above 1).
        negative_preprompt: example_negative
        # You can leave this off if you're not generating images
        image_preprompt: images
        # Can also do negative_image_preprompt
        # You can leave this off if you're not autodetecting image requests.
        is_image_prompt: is_image_prompt
```

- Fill in `textgen_url` with the URL to the blocking API for your text-gen-webui server.
- Fill in `discord_token` with, yknow, a Discord bot token.
- Fill in `pre_prompts` with any preprompts you want to be available. You can use `{{date}}` for the current date, `{{user}}` for the user prefix, `{{bot}}` for the bot prefix, `{{username}}` for the actual Discord username.
    - I highly recommend a much longer preprompt than the example. Give it a lot of example conversation logs to ensure it stays on-topic and functional.
- Fill in `guilds` with a mapping of Guild ID numbers (use Discord dev mode to get those) to settings. Mostly just the reference the preprompt ID.
    - You can set a guild ID of `*` as a catch-all for any unlisted guilds the bot is in.
    - You can set `image_preprompt` to a preprompt that is used for generating images. If present, image generation is allowed. If not preset, image generation is refused.
    - you can set `is_image_prompt` to build a preprompt for letting the LLM decide on its own whether to do text reply or image reply. Note that the bot should be told to say either `other` or `image` based on its choice.
    - `image_log_channel` must be set to a real channel somewhere to work
- Configure the other things how you want. The example above configured for the format that [StableBeluga](https://huggingface.co/stabilityai/StableBeluga2) uses.

# License

The MIT License (MIT)

Copyright (c) 2023-2024 Alex "mcmonkey" Goodwin

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
