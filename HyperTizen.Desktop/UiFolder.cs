namespace HyperTizen.Desktop;

public static class UiFolder
{
    // The nearest HyperTizenUI folder (with an index.html) in the given directory or its parents.
    public static string? Find(string startDirectory)
    {
        for (DirectoryInfo? directory = new DirectoryInfo(startDirectory); directory != null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "HyperTizenUI");
            if (File.Exists(Path.Combine(candidate, "index.html"))) return candidate;
        }
        return null;
    }
}
