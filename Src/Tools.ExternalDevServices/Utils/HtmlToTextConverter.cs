using System.Text;
using HtmlAgilityPack;

namespace Tools.ExternalDevServices.Utils;

public static class HtmlToTextConverter
{
    private enum FormatMode
    {
        PlainText,
        Markdown
    }

    public static string ConvertConfluenceHtmlToPlainText(string html) =>
        ConvertConfluenceHtml(html, FormatMode.PlainText);

    public static string ConvertConfluenceHtmlToMarkdown(string html, bool includeMetaTables = true) =>
        ConvertConfluenceHtml(html, FormatMode.Markdown, includeMetaTables);

    private static string ConvertConfluenceHtml(string html, FormatMode mode, bool includeMetaTables = true)
    {
        var doc = new HtmlDocument { OptionEmptyCollection = true };
        doc.LoadHtml(html);

        // Remove unnecessary tags
        string[] tagsToRemove = { "script", "style", "noscript", "svg", "video", "audio" };
        foreach (var tag in tagsToRemove)
            doc.DocumentNode.SelectNodes($"//{tag}").ToList().ForEach(n => n.Remove());

        // Remove Confluence macros and empty images
        doc.DocumentNode.SelectNodes("//*[starts-with(name(), 'ac:')]")
            .ToList().ForEach(n => n.Remove());

        doc.DocumentNode.SelectNodes("//img[not(@alt)]")
            .ToList().ForEach(n => n.Remove());

        var sb = new StringBuilder();

        void ExtractText(HtmlNode node, int indent = 0, int? index = null)
        {
            string indentStr = new string(' ', indent * 2);

            switch (node.Name)
            {
                case "h1":
                case "h2":
                case "h3":
                case "h4":
                case "h5":
                case "h6":
                    int level = int.Parse(node.Name.Substring(1));
                    var headingPrefix = mode == FormatMode.Markdown
                        ? new string('#', level) + " "
                        : indentStr;
                    sb.AppendLine($"{headingPrefix}{RenderInline(node).Trim()}\n");
                    break;

                case "p":
                    sb.AppendLine($"{indentStr}{RenderInline(node).Trim()}\n");
                    break;

                case "ul":
                    foreach (var li in node.Elements("li"))
                        ExtractText(li, indent + 1);
                    break;

                case "ol":
                    var i = 1;
                    foreach (var li in node.Elements("li"))
                        ExtractText(li, indent + 1, i++);
                    break;

                case "li":
                    var bullet = index.HasValue ? $"{index}. " : "- ";
                    sb.AppendLine($"{indentStr}{bullet}{RenderInline(node).Trim()}");
                    break;

                case "blockquote":
                    sb.AppendLine($"{indentStr}> {RenderInline(node).Trim()}\n");
                    break;

                case "table":
                    if (!includeMetaTables && IsMetaTable(node)) break;
                    var rows = node.SelectNodes(".//tr");
                    bool headerDone = false;
                    foreach (var row in rows)
                    {
                        var cells = row.Elements("th").Concat(row.Elements("td"))
                            .Select(cell => RenderInline(cell).Trim()).ToList();

                        sb.AppendLine($"{indentStr}| {string.Join(" | ", cells)} |");

                        if (mode == FormatMode.Markdown && !headerDone && row.Elements("th").Any())
                        {
                            sb.AppendLine($"{indentStr}| {string.Join(" | ", cells.Select(_ => "---"))} |");
                            headerDone = true;
                        }
                    }
                    sb.AppendLine();
                    break;

                case "pre":
                    sb.AppendLine("```");
                    sb.AppendLine(node.InnerText.Trim());
                    sb.AppendLine("```\n");
                    break;

                case "hr":
                    sb.AppendLine(mode == FormatMode.Markdown ? "---\n" : "--------------------\n");
                    break;

                case "details":
                    sb.AppendLine($"{indentStr}--- Expandable Section ---\n");
                    foreach (var child in node.ChildNodes)
                        ExtractText(child, indent + 1);
                    break;

                default:
                    foreach (var child in node.ChildNodes)
                        ExtractText(child, indent);
                    break;
            }
        }

        string RenderInline(HtmlNode node)
        {
            if (node.NodeType == HtmlNodeType.Text)
                return node.InnerText;

            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (node.Name == "a" && node.Attributes["href"] != null)
            {
                var href = node.Attributes["href"].Value.Trim();
                var text = string.Concat(node.ChildNodes.Select(RenderInline)).Trim();
                return mode == FormatMode.Markdown
                    ? $"[{text}]({href})"
                    : string.IsNullOrEmpty(text) ? href : $"{text} ({href})";
            }

            if (node.Name == "img")
            {
                var alt = node.GetAttributeValue("alt", "").Trim();
                return string.IsNullOrEmpty(alt) ? "" :
                    mode == FormatMode.Markdown ? $"![{alt}]" : $"[Image: {alt}]";
            }

            if (node.Name == "code")
            {
                var inner = string.Concat(node.ChildNodes.Select(RenderInline)).Trim();
                return $"`{inner}`";
            }

            return string.Concat(node.ChildNodes.Select(RenderInline));
        }

        ExtractText(doc.DocumentNode);
        return HtmlEntity.DeEntitize(sb.ToString().Trim());
    }

    private static bool IsMetaTable(HtmlNode node)
    {
        try
        {
            // Get first row's cell texts
            var headerRow = node.SelectSingleNode(".//tr");
            var headerCells = headerRow.Elements("th").Concat(headerRow.Elements("td")).ToList();

            var headerTexts = headerCells.Select(cell => cell.InnerText.Trim().ToLower()).ToList();
            return headerTexts.Contains("owner") ||
                   headerTexts.Contains("status") ||
                   headerTexts.Contains("version") ||
                   headerTexts.Contains("published") ||
                   headerTexts.Contains("changed by") ||
                   headerTexts.Contains("comment");
        }
        catch (Exception)
        {
            return false;
        }
    }
}