using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace WebExplorationProject.Analysis
{
    /// <summary>
    /// AI analyzer using OpenAI API for content analysis.
    /// Analyzes sentiment, credibility, and propaganda techniques.
    /// </summary>
    public class OpenAiAnalyzer : IAiAnalyzer
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _model;
        private readonly int _delayMs;
        private const string ApiUrl = "https://api.openai.com/v1/chat/completions";

        public OpenAiAnalyzer(HttpClient httpClient, string apiKey, string model = "gpt-4o-mini", int delayMs = 1000)
        {
            _httpClient = httpClient;
            _apiKey = apiKey;
            _model = model;
            _delayMs = delayMs;
        }

        public async Task<AiAnalysisResult> AnalyzeContentAsync(string content, string? context = null)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                return new AiAnalysisResult
                {
                    Success = false,
                    ErrorMessage = "OpenAI API key not configured"
                };
            }

            try
            {
                var prompt = BuildAnalysisPrompt(content, context);
                var response = await CallOpenAiAsync(prompt);
                
                // Throttle to avoid rate limiting (429 errors)
                await Task.Delay(_delayMs);
                
                return ParseAnalysisResponse(response);
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("429"))
            {
                Log.Warning("Rate limited by OpenAI, waiting 10 seconds...");
                await Task.Delay(10000);
                
                // Retry once
                try
                {
                    var prompt = BuildAnalysisPrompt(content, context);
                    var response = await CallOpenAiAsync(prompt);
                    await Task.Delay(_delayMs * 2); // Double delay after retry
                    return ParseAnalysisResponse(response);
                }
                catch (Exception retryEx)
                {
                    Log.Warning("AI analysis retry failed: {error}", retryEx.Message);
                    return new AiAnalysisResult
                    {
                        Success = false,
                        ErrorMessage = $"Rate limited: {retryEx.Message}"
                    };
                }
            }
            catch (Exception ex)
            {
                Log.Warning("AI analysis failed: {error}", ex.Message);
                return new AiAnalysisResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private string BuildAnalysisPrompt(string content, string? context)
        {
            var contextInfo = string.IsNullOrEmpty(context) ? "" : $"Context/Topic: {context}\n\n";
            
            // Truncate content if too long
            if (content.Length > 3500)
            {
                content = content.Substring(0, 3500) + "...";
            }

            return $@"{contextInfo}Analyze the following text for credibility and emotional manipulation. Respond ONLY with valid JSON in this exact format:
                    {{
                        ""sentiment_score"": <number from -1.0 to 1.0, where -1=very negative, 0=neutral, 1=very positive>,
                        ""credibility_score"": <number from 0.0 to 1.0, where 0=not credible, 1=highly credible>,
                        ""detected_techniques"": [<list of detected propaganda/persuasion techniques>],
                        ""emotional_words"": [<list of emotional/loaded words found in text>],
                        ""explanation"": ""<brief explanation of the analysis>""
                    }}

                    Propaganda/persuasion techniques to look for:
                    - Appeal to fear/emotion
                    - False dichotomy
                    - Loaded language
                    - Appeal to authority (without evidence)
                    - Bandwagon
                    - Cherry-picking data
                    - Conspiracy theories
                    - Ad hominem
                    - Straw man arguments
                    - Sensationalism

                    Text to analyze:
                    {content}";
        }

        private async Task<string> CallOpenAiAsync(string prompt)
        {
            var request = new
            {
                model = _model,
                messages = new[]
                {
                    new { role = "system", content = "You are an expert in media literacy and propaganda analysis. Analyze text objectively and respond only with valid JSON." },
                    new { role = "user", content = prompt }
                },
                temperature = 0.3,
                max_tokens = 500
            };

            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
            requestMessage.Headers.Add("Authorization", $"Bearer {_apiKey}");
            requestMessage.Content = JsonContent.Create(request);

            var response = await _httpClient.SendAsync(requestMessage);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<OpenAiResponse>();
            return json?.Choices?.FirstOrDefault()?.Message?.Content ?? "";
        }

        private AiAnalysisResult ParseAnalysisResponse(string response)
        {
            try
            {
                // Clean up response (remove markdown code blocks if present)
                response = response.Trim();
                if (response.StartsWith("```json"))
                    response = response.Substring(7);
                if (response.StartsWith("```"))
                    response = response.Substring(3);
                if (response.EndsWith("```"))
                    response = response.Substring(0, response.Length - 3);
                response = response.Trim();

                var parsed = JsonSerializer.Deserialize<AiAnalysisResponse>(response, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (parsed == null)
                {
                    return new AiAnalysisResult { Success = false, ErrorMessage = "Failed to parse AI response" };
                }

                return new AiAnalysisResult
                {
                    Success = true,
                    SentimentScore = Math.Clamp(parsed.SentimentScore, -1.0, 1.0),
                    CredibilityScore = Math.Clamp(parsed.CredibilityScore, 0.0, 1.0),
                    DetectedTechniques = parsed.DetectedTechniques ?? new List<string>(),
                    EmotionalWords = parsed.EmotionalWords ?? new List<string>(),
                    Explanation = parsed.Explanation ?? ""
                };
            }
            catch (JsonException ex)
            {
                Log.Warning("Failed to parse AI response: {error}", ex.Message);
                return new AiAnalysisResult
                {
                    Success = false,
                    ErrorMessage = $"JSON parsing error: {ex.Message}"
                };
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

        private class AiAnalysisResponse
        {
            [JsonPropertyName("sentiment_score")]
            public double SentimentScore { get; set; }

            [JsonPropertyName("credibility_score")]
            public double CredibilityScore { get; set; }

            [JsonPropertyName("detected_techniques")]
            public List<string>? DetectedTechniques { get; set; }

            [JsonPropertyName("emotional_words")]
            public List<string>? EmotionalWords { get; set; }

            [JsonPropertyName("explanation")]
            public string? Explanation { get; set; }
        }
    }
}
