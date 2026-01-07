namespace Tools.ExternalDevServices.Utils;

public static class FileUtils
{
    public static string NormalizeFileName(string title)
    {
        // Replace invalid chars with underscore
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", title.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));

        // Optionally trim and limit length (e.g. 100 chars)
        sanitized = sanitized.Trim().TrimEnd('.');
        return sanitized.Length > 100 ? sanitized[..100] : sanitized;
    }
}