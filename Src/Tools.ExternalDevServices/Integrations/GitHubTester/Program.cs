using Tools.ExternalDevServices.Integrations.GitHub;

namespace GitHubTester;

internal class Program
{
    private static async Task Main()
    {
        Console.WriteLine("Tester started");
        
        string? pta = null;
        do
        {
            Console.WriteLine("Enter PTA:");
            pta = Console.ReadLine()?.Trim();
        } while (string.IsNullOrWhiteSpace(pta));

        using var apiClient = new GitHubRestApiClient(pta, "https://github.azc.ext.hp.com/api/v3", "Indigo-RnD");
        var prDetails = await apiClient.GetPullRequestDetailsAsync("S6-PC", 1239);
        var body = prDetails.Body;
        Console.WriteLine(body);
        /*var diff = await apiClient.GetFileDiffsAsync("S6-PC", 1205,
            "Src/Press/App/UIServer/PressControl/JobsManagement/JobPropertiesEntityManager.cs");*/

        /*var comment = "This is a test comment";
        var commentId = await apiClient.AddCommentToPullRequestAsync("S6-PC", 1239, comment);
        Console.WriteLine("Comment added, check PR then press enter");
        Console.ReadLine();
        var updatedComment = "This is an updated test comment";
        await apiClient.UpdateComment("S6-PC", commentId, updatedComment);
        Console.WriteLine("Comment updated, check PR then press enter");
        Console.ReadLine();*/
    }
}