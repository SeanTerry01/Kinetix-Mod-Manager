using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>One selectable model in a provider's model dropdown (id sent to the API, display shown to the user).</summary>
public sealed class AiModelOption
{
	public string Id { get; }
	public string Display { get; }
	public AiModelOption(string id, string display) { Id = id; Display = display; }
	public override string ToString() => Display;
}

/// <summary>One turn in an AI conversation. <see cref="IsUser"/> false means the assistant/model reply.</summary>
public sealed class AiTurn
{
	public bool IsUser { get; }
	public string Text { get; }
	public AiTurn(bool isUser, string text) { IsUser = isUser; Text = text; }
}

/// <summary>Describes one AI provider: its display name, a curated fallback model list, and where to get a key.</summary>
public sealed class AiProviderInfo
{
	public string Id { get; }
	public string Display { get; }
	/// <summary>Curated fallback models, shown before (or if) a live refresh isn't available.</summary>
	public IReadOnlyList<AiModelOption> Models { get; }
	public string DefaultModel { get; }
	public string KeyHelp { get; }
	/// <summary>True if this provider needs a user-supplied base URL (an OpenAI-compatible custom endpoint).</summary>
	public bool NeedsBaseUrl { get; }
	public AiProviderInfo(string id, string display, IReadOnlyList<AiModelOption> models, string defaultModel, string keyHelp, bool needsBaseUrl = false)
	{
		Id = id; Display = display; Models = models; DefaultModel = defaultModel; KeyHelp = keyHelp; NeedsBaseUrl = needsBaseUrl;
	}
	public override string ToString() => Display;
}

/// <summary>
/// AI provider registry and request routing for the manager's AI-assisted features (currently log diagnosis).
/// Providers are pluggable — Anthropic (Claude), OpenAI (GPT), Google (Gemini), and any OpenAI-compatible custom
/// endpoint (OpenRouter, local Ollama / LM Studio, etc.) are wired. Uses raw HTTP over the shared
/// <see cref="NexusService.HttpClient"/> (no SDK). The user supplies their own API key per provider (stored
/// encrypted); nothing is bundled. Each provider can also list its live models (<see cref="ListModelsAsync"/>) so
/// the Settings model dropdown reflects the provider's current catalog. A feature just calls
/// <see cref="AskAsync(string,string,int)"/> and never needs to know which provider is active.
/// </summary>
public class AiService
{
	/// <summary>Sentinel model id meaning "use the free-text Custom model box in Settings".</summary>
	public const string CustomModelId = "__custom__";

	/// <summary>Provider id for a user-configured OpenAI-compatible endpoint (needs a base URL).</summary>
	public const string OpenAiCompatibleId = "OpenAICompatible";

	private readonly AppSettings _settings;
	public AiService(AppSettings settings) => _settings = settings;

	/// <summary>Every provider the app knows how to talk to, in dropdown order. Model lists here are curated
	/// fallbacks — the Settings "Refresh model list" button replaces them with the provider's live catalog.</summary>
	public static readonly IReadOnlyList<AiProviderInfo> Providers = new[]
	{
		new AiProviderInfo(
			"Anthropic", "Anthropic (Claude)",
			new AiModelOption[]
			{
				new("claude-haiku-4-5",  "Claude Haiku 4.5 (cheapest, fastest)"),
				new("claude-sonnet-4-6", "Claude Sonnet 4.6 (balanced)"),
				new("claude-opus-4-8",   "Claude Opus 4.8 (most capable)"),
			},
			defaultModel: "claude-haiku-4-5",
			keyHelp: "Create a key at console.anthropic.com under API Keys. Pay-as-you-go, billed to your account. Use “Refresh model list” to load current models."),

		new AiProviderInfo(
			"OpenAI", "OpenAI (GPT)",
			new AiModelOption[]
			{
				new("gpt-4o-mini", "GPT-4o mini (cheap, fast)"),
				new("gpt-4o",      "GPT-4o"),
			},
			defaultModel: "gpt-4o-mini",
			keyHelp: "Create a key at platform.openai.com under API keys. Use “Refresh model list” after entering it to load your account’s current models."),

		new AiProviderInfo(
			"Google", "Google (Gemini)",
			new AiModelOption[]
			{
				new("gemini-2.0-flash", "Gemini 2.0 Flash (cheap, fast)"),
				new("gemini-1.5-pro",   "Gemini 1.5 Pro"),
			},
			defaultModel: "gemini-2.0-flash",
			keyHelp: "Create a key at aistudio.google.com under Get API key. Gemini has a free tier. Use “Refresh model list” to load current models."),

		new AiProviderInfo(
			OpenAiCompatibleId, "OpenAI-compatible / Custom endpoint",
			Array.Empty<AiModelOption>(),
			defaultModel: "",
			keyHelp: "For any service that speaks OpenAI's API — OpenRouter, a local server (Ollama, LM Studio), etc. Enter the endpoint's base URL (usually ending in /v1) and its API key, then use “Refresh model list” or type a model ID under Custom. For local servers that need no key, enter any placeholder such as \"local\".",
			needsBaseUrl: true),
	};

	/// <summary>Looks up a provider by id, or null if unknown.</summary>
	public static AiProviderInfo? FindProvider(string id) =>
		Providers.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

	/// <summary>True when AI features are enabled and the active provider has a model and a saved key.</summary>
	public bool IsConfigured =>
		_settings.AiEnabled &&
		!string.IsNullOrWhiteSpace(_settings.AiModel) &&
		_settings.AiApiKeys.TryGetValue(_settings.AiProvider, out string? k) && !string.IsNullOrWhiteSpace(k) &&
		(!(FindProvider(_settings.AiProvider)?.NeedsBaseUrl ?? false) || !string.IsNullOrWhiteSpace(_settings.AiCustomBaseUrl));

	// -------------------------------------------------------------------------
	// Asking (single-shot and multi-turn; used by diagnosis/chat and the Settings test)
	// -------------------------------------------------------------------------

	/// <summary>Single-shot: sends one user message to the active provider/model and returns the answer.</summary>
	public Task<string> AskAsync(string system, string userContent, int maxTokens = 1024) =>
		ChatAsync(system, new[] { new AiTurn(true, userContent) }, maxTokens);

	/// <summary>Multi-turn: sends a system prompt plus the whole conversation and returns the next answer.</summary>
	public Task<string> ChatAsync(string system, IReadOnlyList<AiTurn> turns, int maxTokens = 1024) =>
		SendConversationAsync(_settings.AiProvider, _settings.AiModel,
			_settings.AiApiKeys.TryGetValue(_settings.AiProvider, out string? k) ? k : "",
			system, turns, maxTokens, _settings.AiCustomBaseUrl);

	/// <summary>
	/// Explicit-parameter single-shot used by the Settings "Test connection" button, where the values come from
	/// the unsaved controls rather than from <see cref="AppSettings"/>. <paramref name="baseUrl"/> is only used
	/// by the OpenAI-compatible custom provider.
	/// </summary>
	public Task<string> AskAsync(string provider, string model, string apiKey, string system, string userContent, int maxTokens, string baseUrl = "") =>
		SendConversationAsync(provider, model, apiKey, system, new[] { new AiTurn(true, userContent) }, maxTokens, baseUrl);

	private async Task<string> SendConversationAsync(string provider, string model, string apiKey, string system, IReadOnlyList<AiTurn> turns, int maxTokens, string baseUrl)
	{
		if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("No API key is set for this provider.");
		if (string.IsNullOrWhiteSpace(model)) throw new InvalidOperationException("No model is selected for this provider.");
		if (turns == null || turns.Count == 0) throw new InvalidOperationException("No message to send.");
		return provider switch
		{
			"Anthropic"        => await AskAnthropicAsync(model, apiKey, system, turns, maxTokens),
			"OpenAI"           => await AskOpenAiAsync(model, apiKey, system, turns, maxTokens, "https://api.openai.com/v1"),
			"Google"           => await AskGeminiAsync(model, apiKey, system, turns, maxTokens),
			OpenAiCompatibleId => await AskOpenAiAsync(model, apiKey, system, turns, maxTokens, RequireBase(baseUrl)),
			_ => throw new NotSupportedException($"Unknown AI provider '{provider}'."),
		};
	}

	private static async Task<string> AskAnthropicAsync(string model, string apiKey, string system, IReadOnlyList<AiTurn> turns, int maxTokens)
	{
		var messages = new JArray();
		foreach (AiTurn t in turns)
			messages.Add(new JObject { ["role"] = t.IsUser ? "user" : "assistant", ["content"] = t.Text });
		var body = new JObject { ["model"] = model, ["max_tokens"] = maxTokens, ["system"] = system, ["messages"] = messages };

		using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
		req.Headers.Add("x-api-key", apiKey);
		req.Headers.Add("anthropic-version", "2023-06-01");
		req.Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");

		JObject json = await SendJsonAsync(req);
		if ((string?)json["stop_reason"] == "refusal")
			throw new InvalidOperationException("The AI declined to answer this request.");

		var content = json["content"] as JArray;
		var sb = new StringBuilder();
		if (content != null)
			foreach (JToken block in content)
				if ((string?)block["type"] == "text") sb.Append((string?)block["text"]);
		return NonEmpty(sb.ToString());
	}

	private static async Task<string> AskOpenAiAsync(string model, string apiKey, string system, IReadOnlyList<AiTurn> turns, int maxTokens, string apiBase)
	{
		var messages = new JArray { new JObject { ["role"] = "system", ["content"] = system } };
		foreach (AiTurn t in turns)
			messages.Add(new JObject { ["role"] = t.IsUser ? "user" : "assistant", ["content"] = t.Text });
		// max_completion_tokens is the current field (older max_tokens is deprecated); accepted by current chat models.
		var body = new JObject { ["model"] = model, ["max_completion_tokens"] = maxTokens, ["messages"] = messages };

		using var req = new HttpRequestMessage(HttpMethod.Post, apiBase + "/chat/completions");
		req.Headers.Add("Authorization", "Bearer " + apiKey);
		req.Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");

		JObject json = await SendJsonAsync(req);
		return NonEmpty((string?)json["choices"]?[0]?["message"]?["content"] ?? "");
	}

	private static async Task<string> AskGeminiAsync(string model, string apiKey, string system, IReadOnlyList<AiTurn> turns, int maxTokens)
	{
		var contents = new JArray();
		foreach (AiTurn t in turns)
			contents.Add(new JObject { ["role"] = t.IsUser ? "user" : "model", ["parts"] = new JArray { new JObject { ["text"] = t.Text } } });
		var body = new JObject
		{
			["system_instruction"] = new JObject { ["parts"] = new JArray { new JObject { ["text"] = system } } },
			["contents"] = contents,
			["generationConfig"] = new JObject { ["maxOutputTokens"] = maxTokens },
		};

		using var req = new HttpRequestMessage(HttpMethod.Post,
			$"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent");
		req.Headers.Add("x-goog-api-key", apiKey);
		req.Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");

		JObject json = await SendJsonAsync(req);
		var parts = json["candidates"]?[0]?["content"]?["parts"] as JArray;
		var sb = new StringBuilder();
		if (parts != null)
			foreach (JToken p in parts) sb.Append((string?)p["text"]);
		return NonEmpty(sb.ToString());
	}

	// -------------------------------------------------------------------------
	// Live model listing (Settings "Refresh model list")
	// -------------------------------------------------------------------------

	/// <summary>
	/// Fetches the provider's current model catalog using <paramref name="apiKey"/>, so the dropdown reflects
	/// models added or retired by the provider. Returns real models only (the UI adds the "Custom" entry).
	/// Throws on network/auth failure so the caller can report it.
	/// </summary>
	public async Task<List<AiModelOption>> ListModelsAsync(string provider, string apiKey, string baseUrl = "")
	{
		if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("No API key is set for this provider.");
		return provider switch
		{
			"Anthropic"        => await ListAnthropicModelsAsync(apiKey),
			"OpenAI"           => await ListOpenAiModelsAsync(apiKey, "https://api.openai.com/v1"),
			"Google"           => await ListGeminiModelsAsync(apiKey),
			OpenAiCompatibleId => await ListOpenAiModelsAsync(apiKey, RequireBase(baseUrl)),
			_ => new List<AiModelOption>(),
		};
	}

	private static async Task<List<AiModelOption>> ListAnthropicModelsAsync(string apiKey)
	{
		using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/v1/models?limit=100");
		req.Headers.Add("x-api-key", apiKey);
		req.Headers.Add("anthropic-version", "2023-06-01");
		JObject json = await SendJsonAsync(req);

		var result = new List<AiModelOption>();
		if (json["data"] is JArray data)
			foreach (JToken m in data)
			{
				string id = (string?)m["id"] ?? "";
				if (string.IsNullOrEmpty(id)) continue;
				result.Add(new AiModelOption(id, (string?)m["display_name"] ?? id));
			}
		return result;
	}

	private static async Task<List<AiModelOption>> ListOpenAiModelsAsync(string apiKey, string apiBase)
	{
		using var req = new HttpRequestMessage(HttpMethod.Get, apiBase + "/models");
		req.Headers.Add("Authorization", "Bearer " + apiKey);
		JObject json = await SendJsonAsync(req);

		var result = new List<AiModelOption>();
		if (json["data"] is JArray data)
			foreach (JToken m in data)
			{
				string id = (string?)m["id"] ?? "";
				// api.openai.com returns non-chat models too, so filter there; a custom endpoint's catalog is
				// assumed to be chat models already, so take everything it lists.
				bool officialOpenAi = apiBase.Contains("api.openai.com", StringComparison.OrdinalIgnoreCase);
				if (id.Length > 0 && (!officialOpenAi || IsOpenAiChatModel(id))) result.Add(new AiModelOption(id, id));
			}
		return result.OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase).ToList();
	}

	private static async Task<List<AiModelOption>> ListGeminiModelsAsync(string apiKey)
	{
		using var req = new HttpRequestMessage(HttpMethod.Get, "https://generativelanguage.googleapis.com/v1beta/models?pageSize=200");
		req.Headers.Add("x-goog-api-key", apiKey);
		JObject json = await SendJsonAsync(req);

		var result = new List<AiModelOption>();
		if (json["models"] is JArray models)
			foreach (JToken m in models)
			{
				var methods = m["supportedGenerationMethods"] as JArray;
				bool canGenerate = methods != null && methods.Any(x => (string?)x == "generateContent");
				if (!canGenerate) continue;
				string name = (string?)m["name"] ?? ""; // e.g. "models/gemini-2.0-flash"
				string id = name.StartsWith("models/", StringComparison.OrdinalIgnoreCase) ? name.Substring(7) : name;
				if (string.IsNullOrEmpty(id)) continue;
				result.Add(new AiModelOption(id, (string?)m["displayName"] ?? id));
			}
		return result.OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase).ToList();
	}

	// -------------------------------------------------------------------------
	// Helpers
	// -------------------------------------------------------------------------

	/// <summary>Validates and normalises a custom base URL (trims trailing slashes); throws if it's missing.</summary>
	private static string RequireBase(string baseUrl)
	{
		if (string.IsNullOrWhiteSpace(baseUrl))
			throw new InvalidOperationException("Enter the endpoint's base URL on the Settings AI tab (for example https://openrouter.ai/api/v1).");
		return baseUrl.Trim().TrimEnd('/');
	}

	/// <summary>Sends a request over the shared client, parses JSON, and turns a non-2xx into a readable exception.</summary>
	private static async Task<JObject> SendJsonAsync(HttpRequestMessage req)
	{
		using var resp = await NexusService.HttpClient.SendAsync(req);
		string text = await resp.Content.ReadAsStringAsync();
		if (!resp.IsSuccessStatusCode)
		{
			string apiMsg = "";
			try { apiMsg = (string?)JObject.Parse(text)["error"]?["message"] ?? ""; }
			catch { /* non-JSON error body */ }
			throw new HttpRequestException($"Request failed (HTTP {(int)resp.StatusCode}). {apiMsg}".Trim());
		}
		return JObject.Parse(text);
	}

	private static string NonEmpty(string s)
	{
		string t = s.Trim();
		if (string.IsNullOrEmpty(t)) throw new InvalidOperationException("The AI returned an empty response.");
		return t;
	}

	/// <summary>True for OpenAI ids that are usable chat/completions models (excludes embeddings, audio, images, etc.).</summary>
	private static bool IsOpenAiChatModel(string idRaw)
	{
		string id = idRaw.ToLowerInvariant();
		string[] bad = { "embedding", "whisper", "tts", "dall-e", "audio", "realtime", "image", "moderation", "transcribe", "search", "davinci", "babbage", "codex" };
		foreach (string b in bad) if (id.Contains(b)) return false;
		if (id.StartsWith("gpt") || id.StartsWith("chatgpt")) return true;
		return id.Length >= 2 && id[0] == 'o' && char.IsDigit(id[1]); // o1 / o3 / o4 reasoning models
	}
}
