using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Azure.AI.OpenAI;
using Azure.Identity;
using Newtonsoft.Json;
using Tools.ExternalDevServices.AI.Orchestration.Flows.Jira;
using Tools.ExternalDevServices.Integrations.Jira;
using Tools.ExternalDevServices.Integrations.Confluence;

// שימוש ב-alias כדי להפריד בין שני ה-ChatMessage
using AzureChatMessage = OpenAI.Chat.ChatMessage;
using AzureChatClient = OpenAI.Chat.ChatClient;
using AzureUserMessage = OpenAI.Chat.UserChatMessage;

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
        private readonly AzureChatClient _azureChatClient;
        private readonly IChatClient _iChatClient;
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

            // שני קליינטים נפרדים
            _azureChatClient = azureClient.GetChatClient(deploymentName);
            _iChatClient = _azureChatClient.AsIChatClient();

            // 2. יצירת הלקוחות ל-Jira ול-Confluence
            var jiraClient = new JiraRestApiClient(JiraUrl, "2", _userEmail, _jiraToken);
            var confluenceClient = new ConfluenceRestApiClient(ConfluenceUrl, _confluenceToken);

            // 3. אתחול ה-Flow
            _jiraFlow = new JiraDefectInformationAndRequirementsFlow(
                jiraClient,
                confluenceClient,
                _iChatClient,
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

                // שימוש ב-Azure OpenAI ישירות עם הטיפוסים שלו
                var messages = new List<AzureChatMessage>
                {
                    new AzureUserMessage(prompt)
                };

                var completion = await _azureChatClient.CompleteChatAsync(messages);

                // חילוץ הטקסט
                return completion.Value.Content[0].Text ?? string.Empty;
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