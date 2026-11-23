using System.Reflection;

namespace WorkingTimeChecker.Core;

public static class EmbeddedResourceChecker
{
    private const string PythonScriptsResourceName =
        "WorkingTimeChecker.Resources.python-scripts.zip";

    public static long GetPythonScriptsSize()
    {
        var assembly = typeof(
            EmbeddedResourceChecker
        ).Assembly;

        using var resourceStream =
            assembly.GetManifestResourceStream(
                PythonScriptsResourceName
            );

        if (resourceStream is null)
        {
            var availableResources =
                assembly.GetManifestResourceNames();

            throw new InvalidOperationException(
                "Pythonスクリプトの組み込みZIPが" +
                "見つかりません。" +
                Environment.NewLine +
                "検索した名前: " +
                PythonScriptsResourceName +
                Environment.NewLine +
                "利用可能なリソース: " +
                string.Join(", ", availableResources)
            );
        }

        return resourceStream.Length;
    }
}