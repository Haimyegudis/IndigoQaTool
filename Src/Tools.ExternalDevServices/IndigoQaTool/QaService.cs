using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.AI; // חובה בשביל IChatClient
using OpenAI; // חובה בשביל ה-Client הקונקרטי

// וודא שה-Namespaces האלו נכונים לפי ה-References שלך
using Tools.ExternalDevServices.Integrations.Jira;
using Tools.ExternalDevServices.Integrations.Confluence;

namespace IndigoQaClient
{
    public class QaOptions
    {
        public bool Sanity { get; set; }
        public bool Negative { get; set; }
        public bool Scenarios { get; set; }
        public bool Ui { get; set; }
        public bool Values { get; set; }
        public bool Events { get; set; }
    }

    public class QaService
    {
        private const string JiraUrl = "https://hp-jira.external.hp.com";
        private const string ConfluenceUrl = "https://v-indigo-confluence.inr.rd.hpicorp.net:6443";

        // --- הכנס את הערכים שלך ---
        private string _jiraToken = "YOUR_JIRA_TOKEN";
        private string _confluenceToken = "YOUR_CONFLUENCE_TOKEN";
        private string _user = "YOUR_EMAIL";
        private string _aiKey = "YOUR_OPENAI_KEY";

        private readonly IChatClient _chatClient;

        public QaService()
        {
            // יצירת הקליינט לפי הספרייה Microsoft.Extensions.AI.OpenAI
            // אם השורה הזו נותנת שגיאה, וודא שהתקנת את החבילה: Microsoft.Extensions.AI.OpenAI
            _chatClient = new OpenAIChatClient(new OpenAIClient(_aiKey), "gpt-4o");
        }

        public async Task<string> GeneratePlanAsync(string jiraKey, string manualLinksText, QaOptions options, string instructions)
        {
            var sbDocs = new StringBuilder();
            List<string> urlsToProcess = new List<string>();

            // 1. לינקים ידניים
            if (!string.IsNullOrWhiteSpace(manualLinksText))
            {
                var links = manualLinksText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var link in links) if (link.Trim().StartsWith("http")) urlsToProcess.Add(link.Trim());
            }

            // 2. Jira
            if (!string.IsNullOrWhiteSpace(jiraKey))
            {
                try
                {
                    using var jira = new JiraRestApiClient(JiraUrl, "2", _user, _jiraToken);
                    var issue = await IssueInfo.GetIssueInformationAsync(jira, jiraKey);

                    if (issue != null)
                    {
                        string link = ExtractLink(issue.Description);
                        if (!string.IsNullOrEmpty(link)) urlsToProcess.Add(link);
                    }
                }
                catch (Exception ex) { return $"Error fetching from Jira: {ex.Message}"; }
            }

            // 3. Confluence
            if (urlsToProcess.Count > 0)
            {
                try
                {
                    using var conf = new ConfluenceRestApiClient(ConfluenceUrl, _confluenceToken);
                    foreach (var url in urlsToProcess.Distinct())
                    {
                        try
                        {
                            var content = await conf.GetDocumentAsMarkdownByUrlAsync(url);
                            sbDocs.AppendLine($"\n--- Doc: {url} ---\n{content}");
                        }
                        catch { sbDocs.AppendLine($"Error reading {url}"); }
                    }
                }
                catch (Exception ex) { return $"Error Confluence: {ex.Message}"; }
            }

            if (sbDocs.Length == 0) return "No content found.";

            // 4. AI
            string prompt = BuildPrompt(sbDocs.ToString(), options, instructions);
            try
            {
                var response = await _chatClient.CompleteAsync(prompt);
                return response.Message.Text;
            }
            catch (Exception ex) { return $"AI Error: {ex.Message}"; }
        }

        private string ExtractLink(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            int idx = text.IndexOf("https://");
            if (idx == -1) return null;
            int end = text.IndexOfAny(new[] { ' ', '\n', '\r', '"', '<' }, idx);
            if (end == -1) end = text.Length;
            return text.Substring(idx, end - idx);
        }

        private string BuildPrompt(string content, QaOptions ops, string instructions)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Act as a Senior QA Automation Engineer.");
            sb.AppendLine("Create a Test Plan (Table: Test Name | Type | Steps | Expected Result).");

            if (ops.Sanity) sb.AppendLine("- Include Sanity");
            if (ops.Negative) sb.AppendLine("- Include Negative");
            if (ops.Scenarios) sb.AppendLine("- Include Business Scenarios");
            if (ops.Ui) sb.AppendLine("- Include UI Tests");
            if (ops.Values) sb.AppendLine("- Include Value Validation");
            if (ops.Events) sb.AppendLine("- Include System Events");

            if (!string.IsNullOrWhiteSpace(instructions))
                sb.AppendLine($"Instructions: {instructions}");

            sb.AppendLine("\nRequirements:\n" + content);
            return sb.ToString();
        }
    }
}