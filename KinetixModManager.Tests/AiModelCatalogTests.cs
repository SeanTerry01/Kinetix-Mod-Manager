using System.Collections.Generic;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// What ends up in the AI model dropdown.
///
/// <para>
/// None of this fails loudly when it is wrong. The catalog request succeeds, a list comes back, and it is simply
/// the wrong list — with the model the user pays for missing from it, or every id malformed so that every later
/// request is rejected. A mutation campaign inverted the reasoning-model test, required an id to start with both
/// "gpt" and "chatgpt" at once, and shortened the Gemini prefix strip by one character. All three passed the
/// whole suite.
/// </para>
/// </summary>
public class AiModelCatalogTests
{
	// ------------------------------------------------------------------- whose catalog is it ----

	[Theory]
	[InlineData("https://api.openai.com/v1", true)]
	[InlineData("https://API.OpenAI.com/v1", true)]
	[InlineData("http://localhost:1234/v1", false)]
	[InlineData("https://openrouter.ai/api/v1", false)]
	public void OnlyOpenAisOwnEndpointGetsItsCatalogFiltered(string apiBase, bool expected)
	{
		// Filtering a self-hosted catalog against OpenAI's naming would empty it.
		Assert.Equal(expected, AiModelCatalog.IsOfficialOpenAi(apiBase));
	}

	[Fact]
	public void ACustomEndpointsModelsAreAllOffered()
	{
		Assert.True(AiModelCatalog.ShouldOffer("llama-3.3-70b", officialOpenAi: false));
		Assert.True(AiModelCatalog.ShouldOffer("whisper-large", officialOpenAi: false));
	}

	[Fact]
	public void AnEmptyIdIsNeverOffered()
	{
		Assert.False(AiModelCatalog.ShouldOffer("", officialOpenAi: false));
		Assert.False(AiModelCatalog.ShouldOffer("", officialOpenAi: true));
	}

	// ------------------------------------------------------------ which OpenAI models are chat ----

	[Theory]
	[InlineData("gpt-4o")]
	[InlineData("gpt-4o-mini")]
	[InlineData("gpt-3.5-turbo")]
	[InlineData("chatgpt-4o-latest")]
	public void TheGptFamilyAreChatModels(string id)
	{
		// Requiring an id to start with "gpt" AND "chatgpt" at once would empty the dropdown almost entirely.
		Assert.True(AiModelCatalog.IsOpenAiChatModel(id));
	}

	[Theory]
	[InlineData("o1")]
	[InlineData("o1-preview")]
	[InlineData("o3-mini")]
	[InlineData("o4-mini")]
	public void TheOSeriesReasoningModelsAreChatModels(string id)
	{
		// These are the models a user is most likely to be paying for, and they match no "gpt" prefix.
		Assert.True(AiModelCatalog.IsOpenAiChatModel(id));
	}

	[Theory]
	[InlineData("text-embedding-3-large")]
	[InlineData("whisper-1")]
	[InlineData("tts-1-hd")]
	[InlineData("dall-e-3")]
	[InlineData("gpt-4o-audio-preview")]      // a gpt id that is still not a chat model
	[InlineData("gpt-4o-realtime-preview")]
	[InlineData("omni-moderation-latest")]
	[InlineData("davinci-002")]
	[InlineData("gpt-image-1")]
	[InlineData("gpt-4o-transcribe")]
	[InlineData("gpt-4o-search-preview")]
	[InlineData("babbage-002")]
	[InlineData("codex-mini-latest")]
	public void NonChatModelsAreNotOffered(string id)
	{
		// One id per entry in the exclusion list, and each chosen to match exactly one of them, so corrupting any
		// single entry fails a case. Five of the thirteen were unasserted until a re-measurement damaged "codex"
		// and nothing noticed. The ids are written out rather than read from the list itself: iterating the
		// production array would only assert that the code agrees with itself.
		Assert.False(AiModelCatalog.IsOpenAiChatModel(id));
	}

	[Fact]
	public void TheExclusionListIsCheckedBeforeTheGptPrefix()
	{
		// "gpt-4o-audio-preview" starts with gpt and is still not a chat model. Order matters here.
		Assert.False(AiModelCatalog.IsOpenAiChatModel("gpt-4o-audio-preview"));
		Assert.True(AiModelCatalog.IsOpenAiChatModel("gpt-4o"));
	}

	[Theory]
	[InlineData("o")]            // too short to carry a digit
	[InlineData("omni")]         // starts with o, second character is not a digit
	[InlineData("")]
	public void AnIdThatMerelyStartsWithOIsNotAReasoningModel(string id)
	{
		Assert.False(AiModelCatalog.IsOpenAiChatModel(id));
	}

	[Fact]
	public void ModelIdsAreMatchedRegardlessOfCase()
	{
		Assert.True(AiModelCatalog.IsOpenAiChatModel("GPT-4o"));
		Assert.False(AiModelCatalog.IsOpenAiChatModel("Whisper-1"));
	}

	// -------------------------------------------------------------------------- Gemini ids ----

	[Theory]
	[InlineData("models/gemini-2.0-flash", "gemini-2.0-flash")]
	[InlineData("models/gemini-1.5-pro", "gemini-1.5-pro")]
	[InlineData("MODELS/gemini-2.0-flash", "gemini-2.0-flash")]
	public void TheCatalogPrefixIsStrippedWholeFromAGeminiId(string listed, string expected)
	{
		// One character short leaves "/gemini-2.0-flash", and every request built from it is rejected.
		Assert.Equal(expected, AiModelCatalog.GeminiModelId(listed));
	}

	[Fact]
	public void AnIdWithoutThePrefixIsLeftAlone()
	{
		Assert.Equal("gemini-2.0-flash", AiModelCatalog.GeminiModelId("gemini-2.0-flash"));
	}

	[Fact]
	public void AnIdThatIsNothingButThePrefixBecomesEmpty()
	{
		// The caller drops empty ids; it must be given an empty one rather than a stray slash.
		Assert.Equal("", AiModelCatalog.GeminiModelId("models/"));
	}

	// ------------------------------------------------------------------ nothing to send ----

	[Fact]
	public void AMissingConversationIsNothingToSend()
	{
		// Not a null dereference: the caller turns this into a sentence the user can read.
		Assert.True(AiModelCatalog.NothingToSend<string>(null));
	}

	[Fact]
	public void AnEmptyConversationIsNothingToSend()
	{
		Assert.True(AiModelCatalog.NothingToSend(new List<string>()));
	}

	[Fact]
	public void AConversationWithATurnIsSomethingToSend()
	{
		Assert.False(AiModelCatalog.NothingToSend(new List<string> { "hello" }));
	}
}
