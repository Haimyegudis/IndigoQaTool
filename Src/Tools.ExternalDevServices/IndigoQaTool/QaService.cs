using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.AI; // חובה עבור IChatClient ו-GetResponseAsync
using Azure.AI.OpenAI;
using Azure.Identity;
using OpenAI;
using Newtonsoft.Json;
using Tools.ExternalDevServices.AI.Orchestration.Flows.Jira;
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
        private readonly IChatClient _chatClient;
        private readonly JiraDefectInformationAndRequirementsFlow _jiraFlow;

        // הגדרות מערכת
        private const string JiraUrl = "https://hp-jira.external.hp.com";
        private const string ConfluenceUrl = "https://v-indigo-confluence.inr.rd.hpicorp.net:6443";

        // מלא את הפרטים שלך
        private string _jiraToken = "YOUR_JIRA_TOKEN";
        private string _confluenceToken = "YOUR_CONFLUENCE_TOKEN";
        private string _userEmail = "YOUR_EMAIL";

        public QaService()
        {
            // 1. התחברות ל-Azure OpenAI הארגוני
            string azureEndpoint = "https://YOUR-ORG-INSTANCE.openai.azure.com/";
            string deploymentName = "gpt-4o";

            var azureClient = new AzureOpenAIClient(
                new Uri(azureEndpoint),
                new DefaultAzureCredential());

            // --- תיקון 1: שימוש ב-AsChatClient במקום new OpenAIChatClient ---
            // פונקציה זו הופכת את הקליינט של Azure ל-IChatClient סטנדרטי
            _chatClient = azureClient.AsChatClient(deploymentName);

            // 2. יצירת הלקוחות ל-Jira ול-Confluence
            var jiraClient = new JiraRestApiClient(JiraUrl, "2", _userEmail, _jiraToken);
            var confluenceClient = new ConfluenceRestApiClient(ConfluenceUrl, _confluenceToken);

            // 3. אתחול ה-Flow
            _jiraFlow = new JiraDefectInformationAndRequirementsFlow(
                jiraClient,
                confluenceClient,
                _chatClient,
                null
            );
        }

        public async Task<string> GeneratePlanAsync(string jiraKey, string manualLinks, QaOptions options, string instructions)
        {
            try
            {
                // שלב א: שליפת מידע
                var defectInfo = await _jiraFlow.GetDefectInformationAndRequirementsAsync(
                    jiraKey,
                    System.Threading.CancellationToken.None
                );

                string contextData = JsonConvert.SerializeObject(defectInfo, Formatting.Indented);

                // שלב ב: בניית הפרומפט
                string prompt = BuildPrompt(contextData, options, instructions);

                // --- תיקון 2: שימוש ב-GetResponseAsync במקום CompleteAsync ---
                var response = await _chatClient.GetResponseAsync(prompt);

                // חילוץ הטקסט מתוך התשובה
                return response.Message.Text;
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}\nStack: {ex.StackTrace}";
            }
        }

        private string BuildPrompt(string content, QaOptions ops, string instructions)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Act as a Senior QA Automation Engineer.");
            sb.AppendLine("Based on the provided Defect Information and Requirements, create a Test Plan (Table: Test Name | Type | Steps | Expected Result).");

            if (ops.Sanity) sb.AppendLine("- Include Sanity Tests");
            if (ops.Negative) sb.AppendLine("- Include Negative Tests");
            if (ops.Scenarios) sb.AppendLine("- Include Business Scenarios");
            if (ops.Ui) sb.AppendLine("- Include UI Tests");

            if (!string.IsNullOrWhiteSpace(instructions))
                sb.AppendLine($"User Instructions: {instructions}");

            sb.AppendLine("\n--- DATA CONTEXT ---\n" + content);
            return sb.ToString();
        }
    }
}