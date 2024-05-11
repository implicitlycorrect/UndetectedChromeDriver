using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Selenium.UndetectedChromeDriver;

public static partial class ChromeHelper
{
    private static readonly string[] WindowsChromeParentDirectoryCandidates =
        ["PROGRAMFILES", "PROGRAMFILES(X86)", "LOCALAPPDATA", "PROGRAMW6432"];

    private static readonly string[] WindowsChromeChildDirectoryCandidates =
        [@"Google\Chrome\Application", @"Google\Chrome Beta\Application", @"Google\Chrome Canary\Application"];

    private static readonly string[] LinuxChromeExecutableCandidates =
        ["google-chrome", "chromium", "chromium-browser", "chrome", "google-chrome-stable"];

    public static async Task PatchDriverExecutableAsync(string driverExecutableFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driverExecutableFilePath);

        FileStream? driverFileStream = null;
        StreamReader? streamReader = null;

        try
        {
            driverFileStream = new FileStream(driverExecutableFilePath, FileMode.Open, FileAccess.ReadWrite);
            streamReader = new StreamReader(driverFileStream, Encoding.Latin1);

            var content = await streamReader.ReadToEndAsync(cancellationToken);

            if (CdcRegex().Match(content) is not { Success: true, Value: { } cdc })
                return;

            driverFileStream.Seek(0, SeekOrigin.Begin);
            var bytes = Encoding.Latin1.GetBytes(content.Replace(cdc,
                "{console.log(\"undetected chromedriver 1337!\")}".PadRight(cdc.Length, ' ')));
            await driverFileStream.WriteAsync(bytes, cancellationToken);
            await driverFileStream.FlushAsync(cancellationToken);
        }
        finally
        {
            driverFileStream?.Close();
            streamReader?.Dispose();
            if (driverFileStream is not null)
            {
                driverFileStream.Close();
                await driverFileStream.DisposeAsync();
            }
        }
    }

    public static async Task<string> GetOrInstallDriverAsync(string? chromeExecutableFilePath = null,
        CancellationToken cancellationToken = default)
    {
        var version = GetVersion(chromeExecutableFilePath);

        var isPlatformWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var platformName = isPlatformWindows ? "win32" :
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux64" : throw new PlatformNotSupportedException();
        
        var extension = isPlatformWindows ? ".exe" : string.Empty;

        var driverPath = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Path.GetTempPath(), $"chromedriver_{version}{extension}"));
        if (File.Exists(driverPath))
            return driverPath;

        using var httpClient = new HttpClient(new HttpClientHandler
        {
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        }, true);

        var latestBuildsInfo = await httpClient.GetStringAsync(
            "https://googlechromelabs.github.io/chrome-for-testing/latest-patch-versions-per-build.json", cancellationToken);

        var chromeDriverVersion = Regex.Match(latestBuildsInfo, $$"""
                                                                  "{{version}}":{"version":"(.*?)"
                                                                  """).Groups[1].Value;

        await using var responseStream = await httpClient.GetStreamAsync(
            $"https://storage.googleapis.com/chrome-for-testing-public/{chromeDriverVersion}/{platformName}/chromedriver-{platformName}.zip",
            cancellationToken);

        using var zipArchive = new ZipArchive(responseStream, ZipArchiveMode.Read);

        FileStream? fileStream = null;

        try
        {
            fileStream = new FileStream(driverPath, FileMode.Create);
            
            var zipArchiveEntry =
                zipArchive.GetEntry(
                    $"chromedriver-{platformName}/chromedriver{extension}");
            await using var stream = zipArchiveEntry?.Open();
            if (stream is null)
                throw new InvalidOperationException("Failed to read downloaded chromedriver zip archive");

            await stream.CopyToAsync(fileStream, cancellationToken);
            await fileStream.FlushAsync(cancellationToken);
            await fileStream.DisposeAsync();
            try
            {
                fileStream.Close();
            }
            catch
            {
                // ignored.
            }

            await PatchDriverExecutableAsync(driverPath, cancellationToken);
            return driverPath;
        }
        finally
        {
            if (fileStream is not null)
            {
                await fileStream.DisposeAsync();
            }
        }
    }

    private static string GetVersion(string? chromeExecutableFilePath = null)
    {
        chromeExecutableFilePath ??= GetChromeExecutableFilePath();
        var versionString = FileVersionInfo.GetVersionInfo(chromeExecutableFilePath).FileVersion ??
                            throw new PlatformNotSupportedException("Failed to get chrome version");
        return versionString.Count(x => x is '.') is 3
            ? string.Join('.', versionString.Split('.', 4)[..3])
            : versionString;
    }

    public static string GetChromeExecutableFilePath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return GetWindowsChromeExecutableCandidates().First(File.Exists);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return GetLinuxChromeExecutableCandidates().First(File.Exists);

        throw new PlatformNotSupportedException("Failed to get chrome executable path");
    }

    private static IEnumerable<string> GetWindowsChromeExecutableCandidates()
    {
        return from parentDirectoryVariableName in WindowsChromeParentDirectoryCandidates
            from baseDirectory in WindowsChromeChildDirectoryCandidates
            let parentDirectory = Environment.GetEnvironmentVariable(parentDirectoryVariableName)
            select Path.Combine(parentDirectory, baseDirectory, "chrome.exe");
    }

    private static IEnumerable<string> GetLinuxChromeExecutableCandidates()
    {
        return from pathEntry in Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator)
            from executableName in LinuxChromeExecutableCandidates
            select Path.Combine(pathEntry, executableName);
    }

    [GeneratedRegex(@"\{window\.cdc.*?;\}")]
    private static partial Regex CdcRegex();
}