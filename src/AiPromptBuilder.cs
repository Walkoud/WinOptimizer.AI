using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WinOptimizer.AI
{
    /// <summary>
    /// AI decision result
    /// </summary>
    public class AiDecision
    {
        public string Target { get; set; }
        public string Action { get; set; } // kill, disable, whitelist
        public string Reason { get; set; }
        public string RiskLevel { get; set; }
        
        // For Services - recommended values from AI
        public string RecommendedStartup { get; set; }
        public string RecommendedState { get; set; }
    }

    /// <summary>
    /// Result containing both summary and specific decisions
    /// </summary>
    public class AiAnalysisResult
    {
        public string Summary { get; set; }
        public List<AiDecision> Decisions { get; set; } = new List<AiDecision>();
    }

    /// <summary>
    /// Builds AI prompts for different system components
    /// </summary>
    public static class AiPromptBuilder
    {
        public static string BuildServicePrompt(List<ServiceData> services, bool noXbox = false, bool noStore = false)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Analyze this Windows services list for a high-performance gaming PC.");
            sb.AppendLine("Generate a valid JSON object.");
            sb.AppendLine("STRICT RULE: EXCLUDE any service that is already in its optimal state.");
            sb.AppendLine("Structure:");
            sb.AppendLine("{");
            sb.AppendLine($"  \"summary\": \"(Brief general advice/observation in {LanguageManager.CurrentLanguage})\",");
            sb.AppendLine("  \"recommendations\": [");
            sb.AppendLine("    {");
            sb.AppendLine("      \"Name\": \"...\",");
            sb.AppendLine("      \"Display\": \"...\",");
            sb.AppendLine("      \"Current_Startup\": \"...\",");
            sb.AppendLine("      \"Current_State\": \"...\",");
            sb.AppendLine("      \"Recommended_Startup\": \"Automatic|Manual|Disabled\",");
            sb.AppendLine("      \"Recommended_State\": \"Running|Stopped\",");
            sb.AppendLine($"      \"Reason\": \"(Short explanation in {LanguageManager.CurrentLanguage})\"");
            sb.AppendLine("    }");
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("Guidelines:");
            sb.AppendLine("1. Target: OEM bloatware, unused VPN/Network services, non-essential background tasks.");
            sb.AppendLine("2. If a service is essential, do NOT include it.");
            sb.AppendLine("3. Output ONLY the raw JSON object.");
            
            // Add exclusions section if any checked
            if (noXbox || noStore)
            {
                sb.AppendLine();
                sb.AppendLine("Explicit Exclusions:");
                if (noXbox) sb.AppendLine("- I don't use Xbox services.");
                if (noStore) sb.AppendLine("- I don't use Microsoft Store apps.");
            }
            
            sb.AppendLine();
            sb.AppendLine("List:");
            sb.AppendLine("[");
            
            var items = services.Select(s => s.GetAiPromptData());
            sb.AppendLine(string.Join(",\n", items));
            
            sb.AppendLine("]");
            
            return sb.ToString();
        }

        public static string BuildTaskPrompt(List<TaskData> tasks)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Analyze this Windows scheduled tasks list for a high-performance gaming PC.");
            sb.AppendLine("Generate a valid JSON object.");
            sb.AppendLine("STRICT RULE: EXCLUDE any task that is already in its optimal state.");
            sb.AppendLine("Structure:");
            sb.AppendLine("{");
            sb.AppendLine($"  \"summary\": \"(Brief general advice/observation in {LanguageManager.CurrentLanguage})\",");
            sb.AppendLine("  \"recommendations\": [");
            sb.AppendLine("    {");
            sb.AppendLine("      \"Name\": \"...\",");
            sb.AppendLine("      \"Current_State\": \"...\",");
            sb.AppendLine("      \"Current_Status\": \"...\",");
            sb.AppendLine("      \"Recommended_State\": \"Ready|Disabled|Delete\",");
            sb.AppendLine("      \"Recommended_Status\": \"Enabled|Disabled\",");
            sb.AppendLine($"      \"Reason\": \"(Short explanation in {LanguageManager.CurrentLanguage})\"");
            sb.AppendLine("    }");
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("Guidelines:");
            sb.AppendLine("1. Target: Telemetry, updaters for unused software, non-essential maintenance.");
            sb.AppendLine("2. Keep essential Windows tasks.");
            sb.AppendLine("3. Output ONLY the raw JSON object.");
            sb.AppendLine();
            sb.AppendLine("List:");
            sb.AppendLine("[");
            
            var items = tasks.Select(t => t.GetAiPromptData());
            sb.AppendLine(string.Join(",\n", items));
            
            sb.AppendLine("]");
            
            return sb.ToString();
        }

        public static string BuildAutorunPrompt(List<AutorunData> autoruns)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Analyze this Windows startup items (autoruns) list for a high-performance gaming PC.");
            sb.AppendLine("Generate a valid JSON object.");
            sb.AppendLine("STRICT RULE: EXCLUDE any autorun that is already in its optimal state.");
            sb.AppendLine("Structure:");
            sb.AppendLine("{");
            sb.AppendLine($"  \"summary\": \"(Brief general advice/observation in {LanguageManager.CurrentLanguage})\",");
            sb.AppendLine("  \"recommendations\": [");
            sb.AppendLine("    {");
            sb.AppendLine("      \"Name\": \"...\",");
            sb.AppendLine("      \"Current_Status\": \"...\",");
            sb.AppendLine("      \"Recommended_Status\": \"Enabled|Disabled\",");
            sb.AppendLine($"      \"Reason\": \"(Short explanation in {LanguageManager.CurrentLanguage})\"");
            sb.AppendLine("    }");
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("Guidelines:");
            sb.AppendLine("1. Target: Unnecessary startup programs, bloatware, updaters.");
            sb.AppendLine("2. Keep essential security/drivers.");
            sb.AppendLine("3. Output ONLY the raw JSON object.");
            sb.AppendLine();
            sb.AppendLine("List:");
            sb.AppendLine("[");
            
            var items = autoruns.Select(a => a.GetAiPromptData());
            sb.AppendLine(string.Join(",\n", items));
            
            sb.AppendLine("]");
            
            return sb.ToString();
        }

        public static string BuildProcessPrompt(List<ProcessData> processes)
        {
            var sb = new StringBuilder();
            sb.AppendLine(LanguageManager.GetString("PromptIntro"));
            sb.AppendLine();
            sb.AppendLine(LanguageManager.GetString("PromptRule"));
            sb.AppendLine();
            sb.AppendLine(LanguageManager.GetString("ProcessPrompt"));
            sb.AppendLine();
            sb.AppendLine("### PROCESSES DATA:");
            sb.AppendLine("[");
            
            var items = processes.Select(p => p.GetAiPromptData());
            sb.AppendLine(string.Join(",\n", items));
            
            sb.AppendLine("]");
            sb.AppendLine();
            sb.AppendLine($"IMPORTANT: The 'reason' field MUST be in {LanguageManager.CurrentLanguage}.");
            sb.AppendLine();
            sb.AppendLine("### OUTPUT FORMAT:");
            sb.AppendLine(@"{ ""actions"": [ { ""target"": ""ProcessName"", ""action"": ""kill|whitelist"", ""reason"": ""..."" } ] }");
            
            return sb.ToString();
        }
    }

    /// <summary>
    /// Parses AI responses and extracts decisions
    /// </summary>
    public static class AiResponseParser
    {
        public static AiAnalysisResult ParseAnalysis(string response)
        {
            var result = new AiAnalysisResult();
            
            if (string.IsNullOrWhiteSpace(response))
                return result;

            var json = ExtractJson(response);
            if (string.IsNullOrEmpty(json))
                return result;

            try
            {
                // Try parsing as Object (new format)
                if (json.TrimStart().StartsWith("{"))
                {
                    try
                    {
                        var obj = Newtonsoft.Json.Linq.JObject.Parse(json);
                        
                        // Extract Summary
                        result.Summary = obj["summary"]?.ToString();
                        
                        // Extract Recommendations/Actions
                        var items = obj["recommendations"] as Newtonsoft.Json.Linq.JArray ?? 
                                    obj["actions"] as Newtonsoft.Json.Linq.JArray;
                        
                        if (items != null)
                        {
                            result.Decisions = ParseDecisionsFromArray(items);
                        }
                        return result;
                    }
                    catch { /* Fallback to array parsing */ }
                }

                // Try parsing as Array (legacy format)
                if (json.TrimStart().StartsWith("["))
                {
                    try
                    {
                        var array = Newtonsoft.Json.Linq.JArray.Parse(json);
                        result.Decisions = ParseDecisionsFromArray(array);
                    }
                    catch { /* Ignore */ }
                }
            }
            catch { /* Ignore all errors */ }

            return result;
        }

        private static List<AiDecision> ParseDecisionsFromArray(Newtonsoft.Json.Linq.JArray array)
        {
            var decisions = new List<AiDecision>();
            foreach (var item in array)
            {
                var name = item["Name"]?.ToString() ?? item["name"]?.ToString() ?? item["target"]?.ToString();
                var reason = item["Reason"]?.ToString() ?? item["reason"]?.ToString();
                
                // Old format support
                var actionStr = item["action"]?.ToString();
                
                // New format support
                var recStartup = item["Recommended_Startup"]?.ToString() ?? "";
                var recState = item["Recommended_State"]?.ToString() ?? "";
                var recStatus = item["Recommended_Status"]?.ToString() ?? "";
                
                // Determine action
                var action = actionStr;
                if (string.IsNullOrEmpty(action))
                {
                    if (recStartup.ToLower() == "disabled" || recStatus.ToLower() == "disabled")
                        action = "disable";
                    else if (recStartup.ToLower() == "manual")
                        action = "manual";
                    else if (recStartup.ToLower() == "automatic" || recStatus.ToLower() == "enabled")
                        action = "enable";
                    else
                        action = "disable"; // Default for recommendations list
                }
                
                if (!string.IsNullOrEmpty(name))
                {
                    decisions.Add(new AiDecision
                    {
                        Target = name,
                        Action = action,
                        Reason = reason ?? $"Recommended: {recStartup} {recStatus}",
                        RiskLevel = item["risk_level"]?.ToString() ?? "low",
                        RecommendedStartup = recStartup,
                        RecommendedState = recState
                    });
                }
            }
            return decisions;
        }

        public static List<AiDecision> ParseDecisions(string response)
        {
            return ParseAnalysis(response).Decisions;
        }

        /// <summary>
        /// Parses AI response and extracts ServiceData directly from JSON array
        /// </summary>
        public static List<ServiceData> ParseServiceDataFromJson(string response)
        {
            var services = new List<ServiceData>();
            
            if (string.IsNullOrWhiteSpace(response))
                return services;

            // Try to extract JSON from the response
            var json = ExtractJson(response);
            if (string.IsNullOrEmpty(json))
                return services;

            try
            {
                var array = Newtonsoft.Json.Linq.JArray.Parse(json);
                foreach (var item in array)
                {
                    var name = item["Name"]?.ToString() ?? item["name"]?.ToString();
                    var display = item["Display"]?.ToString() ?? item["DisplayName"]?.ToString() ?? name;
                    var reason = item["Reason"]?.ToString() ?? item["reason"]?.ToString();
                    
                    var currentStartup = item["Current_Startup"]?.ToString() ?? "";
                    var currentState = item["Current_State"]?.ToString() ?? "";
                    var recStartup = item["Recommended_Startup"]?.ToString() ?? "";
                    var recState = item["Recommended_State"]?.ToString() ?? "";
                    
                    if (!string.IsNullOrEmpty(name))
                    {
                        var service = new ServiceData
                        {
                            ServiceName = name,
                            DisplayName = display,
                            CurrentStartup = currentStartup,
                            CurrentState = currentState,
                            UserSelectedStartup = recStartup,  // Pre-select with AI recommendation
                            UserSelectedState = recState,      // Pre-select with AI recommendation
                            AiReason = reason,
                            AiAction = recStartup.ToLower() == "disabled" ? "disable" : "manual",
                            IsRecommended = true
                        };
                        services.Add(service);
                    }
                }
            }
            catch { /* Ignore parsing errors */ }

            return services;
        }

        /// <summary>
        /// Parses AI response and extracts TaskData directly from JSON array
        /// </summary>
        public static List<TaskData> ParseTaskDataFromJson(string response)
        {
            var tasks = new List<TaskData>();
            
            if (string.IsNullOrWhiteSpace(response))
                return tasks;

            var json = ExtractJson(response);
            if (string.IsNullOrEmpty(json))
                return tasks;

            try
            {
                var array = Newtonsoft.Json.Linq.JArray.Parse(json);
                foreach (var item in array)
                {
                    var name = item["Name"]?.ToString() ?? item["name"]?.ToString();
                    var reason = item["Reason"]?.ToString() ?? item["reason"]?.ToString();
                    var state = item["State"]?.ToString() ?? item["state"]?.ToString();
                    var isEnabled = item["Enabled"]?.ToString() ?? item["enabled"]?.ToString();
                    var lastRun = item["Last_Run"]?.ToString() ?? item["last_run"]?.ToString();
                    
                    if (!string.IsNullOrEmpty(name))
                    {
                        var task = new TaskData
                        {
                            TaskName = name,
                            State = state,
                            IsEnabled = isEnabled?.ToLower() == "true",
                            LastRunTime = lastRun,
                            AiReason = reason,
                            AiAction = "disable",
                            IsRecommended = true
                        };
                        tasks.Add(task);
                    }
                }
            }
            catch { /* Ignore parsing errors */ }

            return tasks;
        }

        /// <summary>
        /// Parses AI response and extracts AutorunData directly from JSON array
        /// </summary>
        public static List<AutorunData> ParseAutorunDataFromJson(string response)
        {
            var autoruns = new List<AutorunData>();
            
            if (string.IsNullOrWhiteSpace(response))
                return autoruns;

            var json = ExtractJson(response);
            if (string.IsNullOrEmpty(json))
                return autoruns;

            try
            {
                var array = Newtonsoft.Json.Linq.JArray.Parse(json);
                foreach (var item in array)
                {
                    var name = item["Name"]?.ToString() ?? item["name"]?.ToString();
                    var reason = item["Reason"]?.ToString() ?? item["reason"]?.ToString();
                    var command = item["Command"]?.ToString() ?? item["command"]?.ToString();
                    var location = item["Location"]?.ToString() ?? item["location"]?.ToString();
                    
                    if (!string.IsNullOrEmpty(name))
                    {
                        var autorun = new AutorunData
                        {
                            EntryName = name,
                            Command = command,
                            Location = location,
                            AiReason = reason,
                            AiAction = "disable",
                            IsRecommended = true
                        };
                        autoruns.Add(autorun);
                    }
                }
            }
            catch { /* Ignore parsing errors */ }

            return autoruns;
        }

        private static string ExtractJson(string text)
        {
            // Look for JSON in code blocks
            var codeBlockStart = text.IndexOf("```json");
            if (codeBlockStart >= 0)
            {
                var contentStart = codeBlockStart + 7;
                var contentEnd = text.IndexOf("```", contentStart);
                if (contentEnd > contentStart)
                    return text.Substring(contentStart, contentEnd - contentStart).Trim();
            }

            // Look for JSON arrays [ ... ]
            var arrayStart = text.IndexOf('[');
            var arrayEnd = text.LastIndexOf(']');
            if (arrayStart >= 0 && arrayEnd > arrayStart)
            {
                // Check if there's a { before the [, if so prefer the object, otherwise use array
                var objectStart = text.IndexOf('{');
                if (objectStart < 0 || objectStart > arrayStart)
                    return text.Substring(arrayStart, arrayEnd - arrayStart + 1);
            }

            // Look for first { to last } (object format)
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start >= 0 && end > start)
                return text.Substring(start, end - start + 1);

            return null;
        }
    }
}
