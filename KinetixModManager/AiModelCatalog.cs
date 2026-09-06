using System;
using System.Collections.Generic;

namespace KinetixModManager;

/// <summary>
/// Which models a provider's catalog is allowed to offer, and what their ids are once tidied up — the decisions
/// <see cref="AiService"/> makes about a model list, separated from the HTTP that fetches one.
///
/// <para>
/// These rules decide what appears in the model dropdown. Getting them wrong does not fail loudly: the list comes
/// back, it is simply the wrong list, and the user cannot select the model they are paying for. A mutation
/// campaign inverted the reasoning-model test, required an id to start with both "gpt" and "chatgpt" at once, and
/// shortened the Gemini prefix strip by a character so every id kept a leading slash — and all three passed the
/// whole suite, because nothing here was reachable from a test.
/// </para>
/// </summary>
internal static class AiModelCatalog
{
	/// <summary>The prefix Gemini puts on every model name it lists — "models/gemini-2.0-flash".</summary>
	private const string GeminiNamePrefix = "models/";

	/// <summary>
	/// Model ids that are not chat models. OpenAI's own catalog lists embeddings, speech, images and the rest
	/// alongside the models that can hold a conversation.
	/// </summary>
	private static readonly string[] NotChatModels =
	{
		"embedding", "whisper", "tts", "dall-e", "audio", "realtime", "image", "moderation", "transcribe",
		"search", "davinci", "babbage", "codex",
	};

	/// <summary>
	/// Whether <paramref name="apiBase"/> is OpenAI's own endpoint. It matters because api.openai.com returns
	/// non-chat models too and has to be filtered, while a custom endpoint's catalog is assumed to be chat models
	/// already — filtering someone's self-hosted list against OpenAI's naming would empty it.
	/// </summary>
	internal static bool IsOfficialOpenAi(string apiBase) =>
		apiBase.Contains("api.openai.com", StringComparison.OrdinalIgnoreCase);

	/// <summary>Whether a listed id should be offered to the user at all.</summary>
	internal static bool ShouldOffer(string id, bool officialOpenAi) =>
		id.Length > 0 && (!officialOpenAi || IsOpenAiChatModel(id));

	/// <summary>
	/// True for OpenAI ids that are usable chat/completions models. The gpt-* family and the o-series reasoning
	/// models (o1, o3, o4) are chat models; everything on <see cref="NotChatModels"/> is not.
	/// </summary>
	internal static bool IsOpenAiChatModel(string idRaw)
	{
		string id = idRaw.ToLowerInvariant();
		foreach (string b in NotChatModels) if (id.Contains(b)) return false;
		if (id.StartsWith("gpt") || id.StartsWith("chatgpt")) return true;
		return id.Length >= 2 && id[0] == 'o' && char.IsDigit(id[1]); // o1 / o3 / o4 reasoning models
	}

	/// <summary>
	/// The id to use for a Gemini model, from the name its catalog lists. Leaving the prefix on produces an id
	/// with a slash in it, which every later request is then built from and every one of which the API rejects.
	/// </summary>
	internal static string GeminiModelId(string name) =>
		name.StartsWith(GeminiNamePrefix, StringComparison.OrdinalIgnoreCase)
			? name.Substring(GeminiNamePrefix.Length)
			: name;

	/// <summary>
	/// Whether there is no conversation to send. A missing list and an empty one are the same answer, and both
	/// have to be caught here so the caller can say so plainly rather than dereferencing nothing.
	/// </summary>
	internal static bool NothingToSend<T>(IReadOnlyList<T>? turns) => turns == null || turns.Count == 0;
}
