using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Octokit;

namespace ZapretGui.Services;

public class UpdateService
{
    private const string RepoOwner = "Flowseal";
    private const string RepoName = "zapret-discord-youtube";

    public async Task<(string Version, string DownloadUrl)?> GetLatestReleaseInfoAsync()
    {
        try
        {
            var github = new GitHubClient(new ProductHeaderValue("ZapretGuiApp"));
            var latestRelease = await github.Repository.Release.GetLatest(RepoOwner, RepoName);

            var zipAsset = latestRelease.Assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (zipAsset == null) return null;

            var cleanVersion = latestRelease.TagName.TrimStart('v', 'V');
            return (cleanVersion, zipAsset.BrowserDownloadUrl);
        }
        catch
        {
            return null;
        }
    }

    public async Task DownloadAndExtractAsync(string url, string targetVersionFolder)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ZapretGuiApp");

        byte[] archiveData = await client.GetByteArrayAsync(url);
        string tempZipPath = Path.Combine(Path.GetTempPath(), "zapret_latest.zip");
        await File.WriteAllBytesAsync(tempZipPath, archiveData);

        if (Directory.Exists(targetVersionFolder))
        {
            Directory.Delete(targetVersionFolder, true);
        }
        Directory.CreateDirectory(targetVersionFolder);

        using (var archive = ZipFile.OpenRead(tempZipPath))
        {
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;

                var relativePath = entry.FullName;
                var firstSeparatorIndex = relativePath.IndexOf('/');
                if (firstSeparatorIndex >= 0)
                {
                    relativePath = relativePath.Substring(firstSeparatorIndex + 1);
                }

                if (string.IsNullOrEmpty(relativePath)) continue;

                var destinationPath = Path.Combine(targetVersionFolder, relativePath);
                var directoryName = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(directoryName))
                {
                    Directory.CreateDirectory(directoryName);
                }

                entry.ExtractToFile(destinationPath, overwrite: true);
            }
        }

        if (File.Exists(tempZipPath))
        {
            File.Delete(tempZipPath);
        }
    }
}
