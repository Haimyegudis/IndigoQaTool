using ModelContextProtocol.Server;
using System.ComponentModel;
using ModelContextProtocol;
using Tools.ExternalDevServices.Integrations.TestRail;

namespace Tools.ExternalDevServices.AI.MCP.TestRailMcp
{
    [McpServerToolType]
    public static class TestRailCasesInformationTool
    {
        public const string TestRailUrl = "https://hp-testrail.external.hp.com";

        [McpServerTool, Description("Retrieves test case details")]
        public static async Task<TestRailRestApiTypes.TestCase> GetTestCase(IMcpServer mcpServer, string id)
        {
            Validate(out var testRailApiKey, out var user, out _);

            using var jiraRestApiClient = new TestRailRestApiClient(TestRailUrl, user, testRailApiKey);

            var response = await jiraRestApiClient.GetTestCaseAsync(id);
            return response;
        }

        [McpServerTool, Description("Retrieves test cases metadata for the specified jira epic id")]
        public static async Task<TestRailRestApiTypes.TestCaseMetadata[]> GetTestCases(IMcpServer mcpServer, string jiraId)
        {
            Validate(out var testRailApiKey, out var user, out var projectId);

            using var testRailRestApiClient = new TestRailRestApiClient(TestRailUrl, user, testRailApiKey);

            var suites = await testRailRestApiClient.GetSuitesAsync(projectId);

            var testCases = new List<TestRailRestApiTypes.TestCaseMetadata>();
            foreach (var suite in suites)
            {
                var response = await testRailRestApiClient.GetTestCasesAsync(projectId, suite.Id, jiraId);
                testCases.AddRange(response);
            }

            return [..testCases];
        }

        [McpServerTool, Description("Retrieves test cases metadata for the specified jira epic id and test suite id")]
        public static async Task<TestRailRestApiTypes.TestCaseMetadata[]> GetTestCasesInSuite(IMcpServer mcpServer, string jiraId, string suiteId)
        {
            Validate(out var testRailApiKey, out var user, out var projectId);

            using var testRailRestApiClient = new TestRailRestApiClient(TestRailUrl, user, testRailApiKey);

            var response = await testRailRestApiClient.GetTestCasesAsync(projectId, suiteId, jiraId);
            return response;
        }

        [McpServerTool, Description("Retrieves all the test suites in the project")]
        public static async Task<TestRailRestApiTypes.TestSuite[]> GetTestSuites(IMcpServer mcpServer)
        {
            Validate(out var testRailApiKey, out var user, out var projectId);
           
            using var testRailRestApiClient = new TestRailRestApiClient(TestRailUrl, user, testRailApiKey);

            var response = await testRailRestApiClient.GetSuitesAsync(projectId);
            return response;
        }

        private static void Validate(out string testRailPersonalApiKey, out string user, out string projectId)
        {
            testRailPersonalApiKey = "";
            user = "";
            projectId = "";

            if (string.IsNullOrWhiteSpace(HostArguments.TestRailPersonalApiKey))
            {
                throw new McpException("TestRail Personal API Key is not set.");
            }
            testRailPersonalApiKey = HostArguments.TestRailPersonalApiKey;

            if (string.IsNullOrWhiteSpace(HostArguments.User))
            {
                throw new McpException("User is not set.");
            }
            user = HostArguments.User;

            if (string.IsNullOrWhiteSpace(HostArguments.ProjectId))
            {
                throw new McpException("ProjectId is not set.");
            }
            projectId = HostArguments.ProjectId;
        }
    }
}
