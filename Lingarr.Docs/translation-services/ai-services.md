# AI services

How to set up an AI translation service in Lingarr.

All AI services follow the same pattern: select the service, provide an API key, and choose a model.

## How to set up

1. In the Lingarr web interface, go to **Settings > Services**.
2. Select your AI service in the primary service dropdown, or click **Add fallback service** to use it as a fallback.
3. Click the gear icon on the service row to configure its credentials.
4. Enter the API key and choose a model. Self-hosted services such as [LocalAI](#localai) and [Custom](#custom) also ask for an endpoint.

    When a service asks for an endpoint, LocalAI accepts a **base URL** or a full completion path, and [Custom](#custom) accepts a base URL or the full path of its selected protocol (`/v1/chat/completions` or `/v1/responses`).
    
    Choose a protocol in the LocalAI settings:
    
    - **OpenAI Chat Completions** posts to `{base}/chat/completions` (adds `/v1` when the base does not already end with it). Example base: `http://ollama:11434` or `https://opencode.ai/zen/go/v1`.
    - **Ollama Generate** posts to `{base}/api/generate`. Example base: `http://ollama:11434`.
    - **Auto (from URL)** keeps the old behaviour for full paths: an endpoint ending in `completions` uses chat, and other explicit paths are posted as-is. A bare host such as `http://ollama:11434` (optionally with `/v1`) is completed to `{base}/api/generate`. Prefer an explicit protocol for OpenAI-compatible gateways.
    
    Use **Test connection** to fetch the model list (`/v1/models` or Ollama `/api/tags`) and then select a model, or type a model id.

5. Optionally configure a prompt, a default one is automatically set if left empty. Customization is available by clicking the `Open Request Settings` button below the configuration.
6. Set your source and target languages at the bottom of the page.

Every setting can also be provided as an environment variable, listed per service below. See [Configuration](/getting-started/configuration) for more details.

## Untagged source subtitles

Release groups sometimes ship `Movie.mp4` plus `Movie.srt` with no language tag in the filename. Lingarr labels those files `unknown`.

Traditional services (Yandex, Google, DeepL, LibreTranslate, Bing, Microsoft) have no `unknown` language pair and are skipped. AI services detect the language from the subtitle text — these files are typically English — and translate to the target language.

Put an AI service in the translation chain, as primary or as a fallback after Yandex or Google, so untagged files can be translated. Automatic and bulk jobs also fall back to an untagged sidecar when no configured source language is present.

## Prompts

Each request is built from two templates, and both apply to every AI service.

`AI_PROMPT` is the system prompt. It carries the standing instruction on how to translate, and is sent as the system message. Lingarr seeds a default on first run. Clearing it sends an empty system message, leaving the service without translation instructions, so keep a value set.

`AI_USER_PROMPT` is the user message. It carries the line being translated, and is sent as the user message. The default is `{lineToTranslate}`, the subtitle line on its own. When it is left empty, the line is sent unchanged.

A recommended layout when using context frames the line with its neighbours and keeps the instructions closest to the answer:

```
[Context-Before]
{contextBefore}

[Context-After]
{contextAfter}

[Target-to-Translate]
{lineToTranslate}

Translate only the text under [Target-to-Translate]. The lines under [Context-Before] and [Context-After] are neighbouring subtitle lines for context only: use them to keep names, terms and tone consistent, and never translate or repeat them.
```

The two context sections are empty while the context settings are `0`.

Both templates accept the same placeholders:

| **Placeholder** | **Value** |
|-----------------|-----------|
| `{lineToTranslate}` | The subtitle line being translated. |
| `{contextBefore}` | The lines preceding it, as many as the context setting allows. Empty when that setting is `0`. Plain lines by default, JSON objects with **Structured context with translations** enabled (see below). |
| `{contextAfter}` | The lines following it, as many as the context setting allows. Empty when that setting is `0`. Same form as `{contextBefore}`, without translations. |
| `{sourceLanguage}` | The language being translated from. For untagged files (`unknown` / `und`) this becomes `the source language (detect it automatically; typically English)`, or `auto` when language-code format is enabled. |
| `{targetLanguage}` | The language being translated to. |
| `{model}` | The configured model. |

With **Structured context with translations** enabled, each context line is one JSON object with `position`, `line` and, once the line has been translated, `translation`, for example `{"position":12,"line":"Hold it!","translation":"Wacht even!"}`. If you enable it, tell the model in your prompt to follow those earlier translations, for example: `When a [Context-Before] entry includes a "translation" field, follow that earlier translation.`

Use the user prompt to frame a single line, for example to surround it with its context so the service can see where the line sits.

Batch translation does not use the user prompt. The batch is sent as the user message instead, so only `AI_PROMPT` applies when batching is enabled.

## Proofreading

Proofreading re-examines a completed translation. For each subtitle line it sends the source line and the existing translation together to the AI service and asks for a corrected translation, without translating from scratch.

Only services that implement proofreading offer it: OpenAI, Anthropic, Gemini, DeepSeek, Mistral, xAI, LocalAI and Custom. LibreTranslate, DeepL, Google, Bing, Microsoft and Yandex do not support proofreading.

You can run it two ways:

- **Whole request.** On the translations list, a `Completed` request with a translated subtitle shows a Proofread action beside Retry, Resume and Remove. This queues a job that proofreads every line and rewrites the translated subtitle file in place once it finishes. Lingarr keeps no copy of the pre-proofread text, in the file or in the database, so there is nothing to revert to afterwards.
- **Single line.** On the translation detail page, each line has its own Proofread button. It fetches a suggested correction for that line, shows it inline, and you apply or dismiss it.

A whole-request proofread calls the AI service once per subtitle line, the same as a non-batch translation, batching does not apply. Proofreading a long subtitle track costs roughly as much as translating it again.

The subtitle file is rewritten only after every line has been checked, so cancelling a proofread, or restarting Lingarr while one is running, leaves the existing translation and its file exactly as they were. The request returns to `Completed` and can be proofread again.

Proofreading uses two more templates, `proofread_prompt` (the system prompt) and `proofread_user_prompt` (the user message), configured on the same Request Settings panel as `AI_PROMPT` and `AI_USER_PROMPT`, or through the `PROOFREAD_PROMPT` and `PROOFREAD_USER_PROMPT` environment variables.

Both accept the placeholders already listed above, plus two more:

| **Placeholder** | **Value** |
|-----------------|-----------|
| `{sourceLine}` | The original subtitle line. |
| `{translatedLine}` | The existing translation being checked. |

`{lineToTranslate}`, `{contextBefore}` and `{contextAfter}` are sent as empty strings during a proofread call, so a request template that already contains them still renders correctly.

## Request settings

These two settings apply to every AI service and are configured once under **Settings > Services > Request Settings**, or through environment variables. They are not listed again in each provider table below.

| **Environment Variable** | **Description** |
|--------------------------|-----------------|
| `AI_MAX_TOKENS` | Maximum tokens for one completion (thinking plus the answer). Default `0` omits the field so the provider or request-template default is kept. OpenAI receives `max_completion_tokens`; LocalAI and other chat providers receive `max_tokens`; Custom over Responses receives `max_output_tokens`; Gemini receives `generationConfig.maxOutputTokens`. Anthropic's template default is `8192` when the field is omitted. LocalAI generate (Ollama `/api/generate`) is not injected. |
| `AI_THINKING` | Chain-of-thought control. Default `auto`. Applied only to OpenAI-compatible chat, which is LocalAI chat and Custom over `openai_chat`. `auto` sends no `thinking` field. `off` writes `thinking.disabled` (needed for OpenCode Go / MiMo). `on` writes `thinking.enabled`. |

## Environment variables
### OpenAI

| **Environment Variable** | **Description**                                             |
|--------------------------|-------------------------------------------------------------|
| `OPENAI_MODEL` | The model to use for OpenAI translations. Example: `gpt-4`. |
| `OPENAI_API_KEY` | The API key for authenticating with OpenAI.                 |
| `AI_PROMPT` | The system prompt template.                                 |
| `AI_USER_PROMPT` | The user message template.                                  |

### Anthropic

| **Environment Variable** | **Description**                                |
|--------------------------|------------------------------------------------|
| `ANTHROPIC_MODEL` | The model to use for Anthropic translations.   |
| `ANTHROPIC_API_KEY` | The API key for authenticating with Anthropic. |
| `ANTHROPIC_VERSION` | The version of the Anthropic API to use.       |
| `AI_PROMPT` | The system prompt template.                    |
| `AI_USER_PROMPT` | The user message template.                     |

### Gemini

| **Environment Variable** | **Description**                                                        |
|--------------------------|------------------------------------------------------------------------|
| `GEMINI_MODEL` | The model to use for Gemini translations. Example: `gemini-2.0-flash`. |
| `GEMINI_API_KEY` | The API key for authenticating with Gemini.                            |
| `AI_PROMPT` | The system prompt template.                                            |
| `AI_USER_PROMPT` | The user message template.                                             |

### DeepSeek

| **Environment Variable** | **Description**                                                       |
|--------------------------|-----------------------------------------------------------------------|
| `DEEPSEEK_MODEL` | The model to use for DeepSeek translations. Example: `deepseek-chat`. |
| `DEEPSEEK_API_KEY` | The API key for authenticating with DeepSeek.                         |
| `AI_PROMPT` | The system prompt template.                                           |
| `AI_USER_PROMPT` | The user message template.                                            |

### Mistral

| **Environment Variable** | **Description**                                                             |
|--------------------------|-----------------------------------------------------------------------------|
| `MISTRAL_MODEL` | The model to use for Mistral translations. Example: `mistral-large-latest`. |
| `MISTRAL_API_KEY` | The API key for authenticating with Mistral.                                |
| `AI_PROMPT` | The system prompt template.                                                 |
| `AI_USER_PROMPT` | The user message template.                                                  |

### xAI

| **Environment Variable** | **Description**                                          |
|--------------------------|----------------------------------------------------------|
| `XAI_MODEL` | The model to use for xAI translations. Example: `grok-4.5`. |
| `XAI_API_KEY` | The API key for authenticating with xAI.                 |
| `AI_PROMPT` | The system prompt template.                              |
| `AI_USER_PROMPT` | The user message template.                               |

### LocalAI

LocalAI works with Ollama or any other OpenAI-compatible model or router.

| **Environment Variable** | **Description**                                                                                                           |
|--------------------------|---------------------------------------------------------------------------------------------------------------------------|
| `LOCAL_AI_MODEL` | The model to use for LocalAI translations.                                                                                |
| `LOCAL_AI_API_KEY` | The API key for authenticating with LocalAI. This is optional, and only needed if the deployment requires authentication. |
| `LOCAL_AI_ENDPOINT` | Base URL or full completion path. Example: `http://ollama:11434` or `https://opencode.ai/zen/go/v1`.                       |
| `LOCAL_AI_PROTOCOL` | `auto`, `openai_chat`, or `ollama_generate`. Default `auto` preserves the old URL-suffix behaviour.                       |
| `AI_PROMPT` | The system prompt template.                                                                                               |
| `AI_USER_PROMPT` | The user message template.                                                                                                |

### Custom

Custom talks to your own gateway over one explicitly selected, non-streaming protocol: **OpenAI Chat Completions** (`openai_chat`, the default) or **OpenAI Responses** (`openai_responses`). It is a separate provider, not a mode of LocalAI: address, key, model, protocol and per-protocol request template live under their own `custom_*` settings, so both providers can be configured and used in the same translation chain without sharing any of them. Existing `localai` configurations keep working unchanged — nothing is migrated automatically — while new OpenAI-compatible gateways belong on Custom.

| **Environment Variable** | **Description**                                                                                                           |
|--------------------------|---------------------------------------------------------------------------------------------------------------------------|
| `CUSTOM_PROTOCOL` | `openai_chat` or `openai_responses`. Default `openai_chat`. Switching keeps the same address, key and model, but the gateway has to serve the selected protocol and every protocol keeps its own request template. |
| `CUSTOM_ENDPOINT` | Base URL or a full endpoint path of the selected protocol. Example: `http://localhost:8000/v1` or `https://gateway.example.com/v1/responses`. |
| `CUSTOM_MODEL` | The model to use. Load the gateway's model list with **Test connection**, or type a model id.                               |
| `CUSTOM_API_KEY` | Bearer token, optional. Only needed when the gateway requires authentication. Stored encrypted.                            |
| `CUSTOM_CHAT_REQUEST_TEMPLATE` | Request template used by the `openai_chat` protocol, stored separately from the LocalAI and OpenAI templates.              |
| `CUSTOM_RESPONSES_REQUEST_TEMPLATE` | Request template used by the `openai_responses` protocol, stored independently from the chat template, so switching protocols never overwrites the other one's edits. |
| `AI_PROMPT` | The system prompt template.                                                                                               |
| `AI_USER_PROMPT` | The user message template.                                                                                                |

As everywhere else, each variable also accepts the `_FILE` variant, see [Configuration](/getting-started/configuration).

**Protocols.** Lingarr never guesses the protocol. Pick it in the Custom settings and only that protocol's endpoint is called; a full path of the other protocol is still recognised as an API path and replaced, so switching protocols does not require retyping the address.

- `openai_chat` posts to the chat completions endpoint and parses `choices[0].message.content`, with structured output requested through `response_format` for batch jobs.
- `openai_responses` posts the same prompts as `input` messages to the responses endpoint and reads `output[].content[].text`; batch jobs request structured output through `text.format`. A response only counts as success when it completed with text — refusals, `incomplete`/`failed` responses, non-final states and empty output fail the candidate instead of being written back as a translation.

**Addresses.** Custom looks at what you enter and resolves the URL of the selected protocol from it:

- a base URL such as `http://localhost:8000` becomes `http://localhost:8000/v1/chat/completions`;
- a URL that already ends in `/v1`, such as `http://localhost:8000/v1`, becomes the same `.../v1/chat/completions`;
- a full path such as `https://gateway.example.com/v1/chat/completions` is used as it is;
- with `openai_responses` the same rules apply with `/v1/responses` (or `/responses` on a URL that already ends in `/v1`) as the target path.

Other known API paths (`/chat/completions`, `/responses`, `/completions`, `/models`, and Ollama's `/api/generate` and `/api/tags`) are recognised and replaced by the selected protocol's path, so a URL copied from another client does not have to be trimmed by hand. Custom never streams: every request is a non-streaming call. A request template that sets `"stream": true` is rejected with a configuration error before it is sent, and a gateway that answers with an event stream (`text/event-stream` or a `data:` body) is reported as unsupported instead of being parsed as a partial or empty translation.

**Authentication.** `CUSTOM_API_KEY` is optional. When it is set, the request carries `Authorization: Bearer <key>`; when it is empty, the request is sent without one and Lingarr warns that gateways requiring authentication will reject it.

**Models.** **Test connection** requests the model catalogue at the `/v1/models` path of the same address, with the same bearer token, and offers the result in the model dropdown. Gateways without a `/models` endpoint are supported: the model field accepts a typed id, and a failed model list does not mean the gateway cannot translate.

**Request template.** Each protocol has its own default body, stored independently from the other protocol and from `local_ai_chat_request_template` and `openai_request_template`: `custom_chat_request_template` for `openai_chat` (`model` + `messages`) and `custom_responses_request_template` for `openai_responses` (`model` + `input`). **Settings > Services > Request Settings** follows `custom_protocol`, so the editor always shows the template that is actually in use.

**Translation chain.** A chain entry is one provider plus an optional model, so `custom` can appear more than once with a different model on every line. When a gateway answers `401`, Lingarr treats the credentials as wrong for that gateway and skips its remaining models before trying the next provider in the chain; a missing model, a rate limit or a timeout moves on after the normal retries. When the lines of one subtitle end up translated by more than one model, the translator info and the per-line view report the mixed sources.
