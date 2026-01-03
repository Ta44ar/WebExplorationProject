using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace WebExplorationProject.Analysis
{
    /// <summary>
    /// Service for generating topic-specific dictionaries using OpenAI.
    /// Creates specialist terms and emotional/propaganda words based on search query.
    /// </summary>
    public class DictionaryGeneratorService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _model;
        private const string ApiUrl = "https://api.openai.com/v1/chat/completions";

        public DictionaryGeneratorService(HttpClient httpClient, string apiKey, string model = "gpt-4o-mini")
        {
            _httpClient = httpClient;
            _apiKey = apiKey;
            _model = model;
        }

        /// <summary>
        /// Generates dictionaries for a given search query/topic.
        /// </summary>
        public async Task<GeneratedDictionaries?> GenerateDictionariesAsync(string searchQuery)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                Log.Warning("OpenAI API key not configured. Using default dictionaries.");
                return null;
            }

            try
            {
                Log.Information("Generating topic-specific dictionaries for: {query}", searchQuery);

                var prompt = BuildPrompt(searchQuery);
                var response = await CallOpenAiAsync(prompt);
                var dictionaries = ParseResponse(response);

                if (dictionaries != null)
                {
                    Log.Information("Generated dictionaries: {specialist} specialist terms, {emotional} emotional words, {propaganda} propaganda phrases",
                        dictionaries.SpecialistTerms.Count,
                        dictionaries.EmotionalWords.Count,
                        dictionaries.PropagandaPhrases.Count);
                }

                return dictionaries;
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to generate dictionaries: {error}. Using defaults.", ex.Message);
                return null;
            }
        }

        private string BuildPrompt(string searchQuery)
        {
            return $@"You are an expert in media literacy and content analysis. 
For the following search query/topic, generate dictionaries of terms that would help analyze the credibility of web content.

Search Query: ""{searchQuery}""

Generate JSON with the following structure:
{{
    ""topic_keywords"": [""keyword1"", ""keyword2"", ...],
    ""specialist_terms"": {{
        ""general"": [""term1"", ""term2"", ...],
        ""topic_specific"": [""term1"", ""term2"", ...]
    }},
    ""emotional_words"": [""word1"", ""word2"", ...],
    ""propaganda_phrases"": [""phrase1"", ""phrase2"", ...]
}}

Guidelines:
1. **topic_keywords**: 5-10 keywords that identify this topic
2. **specialist_terms.general**: 15-25 scientific/academic terms (e.g., ""peer-review"", ""clinical study"", ""meta-analysis"")
3. **specialist_terms.topic_specific**: 15-25 terms specific to this topic domain
4. **emotional_words**: 20-30 emotionally loaded words that suggest bias (e.g., ""shocking"", ""scandal"", ""conspiracy"")
5. **propaganda_phrases**: 15-25 phrases that indicate manipulation (e.g., ""they don't want you to know"", ""hidden truth"")

Include terms in the same language as the query (if Polish query, use Polish terms).
Respond ONLY with valid JSON, no markdown or explanation.";
        }

        private async Task<string> CallOpenAiAsync(string prompt)
        {
            var request = new
            {
                model = _model,
                messages = new[]
                {
                    new { role = "system", content = "You are a linguistic expert. Respond only with valid JSON." },
                    new { role = "user", content = prompt }
                },
                temperature = 0.3,
                max_tokens = 2000
            };

            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
            requestMessage.Headers.Add("Authorization", $"Bearer {_apiKey}");
            requestMessage.Content = JsonContent.Create(request);

            var response = await _httpClient.SendAsync(requestMessage);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<OpenAiResponse>();
            return json?.Choices?.FirstOrDefault()?.Message?.Content ?? "";
        }

        private GeneratedDictionaries? ParseResponse(string response)
        {
            try
            {
                // Clean up response
                response = response.Trim();
                if (response.StartsWith("```json"))
                    response = response.Substring(7);
                if (response.StartsWith("```"))
                    response = response.Substring(3);
                if (response.EndsWith("```"))
                    response = response.Substring(0, response.Length - 3);
                response = response.Trim();

                var parsed = JsonSerializer.Deserialize<DictionaryResponse>(response, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (parsed == null)
                    return null;

                // Combine general and topic-specific terms
                var allSpecialistTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                
                if (parsed.SpecialistTerms?.General != null)
                    foreach (var term in parsed.SpecialistTerms.General)
                        allSpecialistTerms.Add(term);
                
                if (parsed.SpecialistTerms?.TopicSpecific != null)
                    foreach (var term in parsed.SpecialistTerms.TopicSpecific)
                        allSpecialistTerms.Add(term);

                return new GeneratedDictionaries
                {
                    TopicKeywords = parsed.TopicKeywords ?? new List<string>(),
                    SpecialistTerms = allSpecialistTerms,
                    EmotionalWords = new HashSet<string>(parsed.EmotionalWords ?? new List<string>(), StringComparer.OrdinalIgnoreCase),
                    PropagandaPhrases = new HashSet<string>(parsed.PropagandaPhrases ?? new List<string>(), StringComparer.OrdinalIgnoreCase)
                };
            }
            catch (JsonException ex)
            {
                Log.Warning("Failed to parse dictionary response: {error}", ex.Message);
                return null;
            }
        }

        // Response DTOs
        private class OpenAiResponse
        {
            [JsonPropertyName("choices")]
            public List<Choice>? Choices { get; set; }
        }

        private class Choice
        {
            [JsonPropertyName("message")]
            public Message? Message { get; set; }
        }

        private class Message
        {
            [JsonPropertyName("content")]
            public string? Content { get; set; }
        }

        private class DictionaryResponse
        {
            [JsonPropertyName("topic_keywords")]
            public List<string>? TopicKeywords { get; set; }

            [JsonPropertyName("specialist_terms")]
            public SpecialistTermsDto? SpecialistTerms { get; set; }

            [JsonPropertyName("emotional_words")]
            public List<string>? EmotionalWords { get; set; }

            [JsonPropertyName("propaganda_phrases")]
            public List<string>? PropagandaPhrases { get; set; }
        }

        private class SpecialistTermsDto
        {
            [JsonPropertyName("general")]
            public List<string>? General { get; set; }

            [JsonPropertyName("topic_specific")]
            public List<string>? TopicSpecific { get; set; }
        }
    }

    /// <summary>
    /// Generated dictionaries for content analysis.
    /// </summary>
    public class GeneratedDictionaries
    {
        public List<string> TopicKeywords { get; set; } = new();
        public HashSet<string> SpecialistTerms { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> EmotionalWords { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> PropagandaPhrases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
