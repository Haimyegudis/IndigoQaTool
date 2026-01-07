using Tools.ExternalDevServices.Integrations.Jenkins;

namespace JenkinsTester
{
    internal class Program
    {
        private static async Task Main()
        {
            Console.WriteLine("Enter Jenkins Pipeline Url:");
            var url = Console.ReadLine();
            if (string.IsNullOrEmpty(url))
                throw new ArgumentNullException(nameof(url), "Url is empty");

            var pipeline = await JenkinsPipelineParser.ParseUrl(url);
            Directory.CreateDirectory(@"c:\GitHub\JenkinsPipelines");
            var fileName = $"{pipeline.BuildTag}.md";
            fileName = Path.Combine(@"c:\GitHub\JenkinsPipelines", fileName);
            await File.WriteAllTextAsync(fileName, pipeline.ToMarkdown());
            Console.WriteLine($"Pipeline markdown saved to {fileName}");
        }
    }
}
