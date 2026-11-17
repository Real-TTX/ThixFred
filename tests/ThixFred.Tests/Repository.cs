namespace ThixFred.Tests;

/// <summary>Where the sources are, for the tests that read them: the folder of ThixFred.slnx above the test run.</summary>
internal static class Repository
{
    public static string Root { get; } = Find();

    private static string Find()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "ThixFred.slnx")))
            {
                return folder.FullName;
            }
        }

        throw new DirectoryNotFoundException("ThixFred.slnx was not found above " + AppContext.BaseDirectory);
    }
}
