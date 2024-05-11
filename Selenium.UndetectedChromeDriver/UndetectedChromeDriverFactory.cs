using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using Selenium.UndetectedChromeDriver.Exceptions;

namespace Selenium.UndetectedChromeDriver;

public static partial class UndetectedChromeDriverFactory
{
    public static UndetectedChromeDriver Create(UndetectedChromeDriverConfiguration configuration)
    {
        const string debugHost = "127.0.0.1";
        var debugPort = FindFreePort();
        var options = new ChromeOptions
        {
            AcceptInsecureCertificates = true,
            EnableDownloads = false,
            UnhandledPromptBehavior = UnhandledPromptBehavior.Dismiss,
            BinaryLocation = configuration.BrowserExecutableFilePath,
            DebuggerAddress = $"{debugHost}:{debugPort}"
        };
        var argumentList = configuration.UseDefaultArguments ? new List<string>([
            "--max_old_space_size=2048",
            $"--user-agent={configuration.UserAgent}",
            "--mute-audio",
            "--disable-notifications",
            "--disable-dev-shm-usage",
            $"--lang={configuration.Language}",
            "--no-default-browser-check",
            "--no-first-run",
            "--test-type",
            $"--window-size={configuration.WindowSize}",
            "--start-maximized",
            $"--log-level={configuration.LogLevel}",
            $"--user-data-dir={configuration.UserDataDirectory}",
            $"--remote-debugging-host={debugHost}",
            $"--remote-debugging-port={debugPort}"
        ]) : [];
        argumentList.AddRange(configuration.Arguments);
        
        if (!string.IsNullOrEmpty(configuration.Proxy))
        {
            if (!configuration.Proxy.Contains('@'))
            {
                var proxy = new Proxy
                {
                    Kind = ProxyKind.Manual,
                    IsAutoDetect = false,
                    HttpProxy = configuration.Proxy,
                    SslProxy = configuration.Proxy
                };
                foreach (var proxyBypassAddress in configuration.ProxyBypassAddresses)
                    proxy.AddBypassAddress(proxyBypassAddress);
                options.Proxy = proxy;
            }
            argumentList.Add($"--proxy-server={configuration.Proxy}");
            argumentList.Add("--ignore-certificate-errors");
        }
        options.AddArguments(argumentList.Distinct());

        if (string.Join(',', configuration.Extensions) is { Length: > 0 } extensionString)
            options.AddArgument($"--load-extension={extensionString}");
        
        if (configuration.Headless)
            options.AddArgument("--headless=new");

        FixPreferencesExitType(configuration.UserDataDirectory);

        var arguments = string.Join(" ",
            options.Arguments.Select(argument => argument.Contains(' ') ? $"\"{argument}\"" : argument));
        var browserProcessStartInfo = new ProcessStartInfo(options.BinaryLocation, arguments)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        var process = Process.Start(browserProcessStartInfo);
        if (process == null)
            throw new ProcessStartException(options.BinaryLocation);

        var start = DateTime.UtcNow;
        while (!configuration.Headless && (process.MainWindowHandle == IntPtr.Zero || string.IsNullOrEmpty(process.MainWindowTitle)))
        {
            if ((DateTime.UtcNow - start).TotalSeconds > 30)
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                    // ignored.
                }
                throw new ProcessStartException("Failed to start new chrome browser process.");
            }
            Thread.Sleep(25);
        }

        var service = ChromeDriverService.CreateDefaultService(
            Path.GetDirectoryName(configuration.DriverExecutableFilePath),
            Path.GetFileName(configuration.DriverExecutableFilePath));
        service.DriverProcessStarted += (_, args) => configuration.DriverProcessId = args.ProcessId;
        service.InitializationTimeout = TimeSpan.FromSeconds(60);
        service.HideCommandPromptWindow = true;
        service.SuppressInitialDiagnosticInformation = true;
        var driver = new UndetectedChromeDriver(configuration, service, options, TimeSpan.FromSeconds(30))
        {
            BrowserProcessId = process.Id,
            Headless = configuration.Headless
        };
        return driver;
    }

    private static void FixPreferencesExitType(string userDataDirectory)
    {
        var preferencesFilePath = Path.Combine(userDataDirectory, "Default/Preferences");
        if (!File.Exists(preferencesFilePath))
            return;

        var preferences = File.ReadAllText(preferencesFilePath, Encoding.Latin1);
        var exitType = ExitTypeRegex().Match(preferences).Value;
        if (exitType is { Length: 0 } or "null")
            return;

        File.WriteAllText(preferencesFilePath, ExitTypeRegex().Replace(preferences, "null"), Encoding.Latin1);
    }

    [GeneratedRegex(@"(?<=exit_type"":)(.*?)(?=,)")]
    private static partial Regex ExitTypeRegex();

    private static int FindFreePort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var localEndpoint = new IPEndPoint(IPAddress.Any, 0);
        socket.Bind(localEndpoint);
        var freeEndpoint = (IPEndPoint?)socket.LocalEndPoint;
        if (freeEndpoint == null)
            throw new Exception("Not found free port.");
        return freeEndpoint.Port;
    }
}