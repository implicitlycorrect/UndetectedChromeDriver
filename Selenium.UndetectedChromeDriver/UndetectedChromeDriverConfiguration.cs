namespace Selenium.UndetectedChromeDriver;

public class UndetectedChromeDriverConfiguration(string driverExecutableFilePath)
{
    public IEnumerable<string> Arguments { get; set; } = [];

    public bool UseDefaultArguments { get; set; } = true;
    
    public string? Proxy { get; set; }

    public IEnumerable<string> ProxyBypassAddresses = [];

    public string UserAgent { get; set; } = "Mozilla/5.0 (Windows NT 5.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/119.1.8190.97 Safari/537.36";

    public IEnumerable<string> Extensions { get; set; } = Enumerable.Empty<string>();

    private string? _userDataDirectory;
    public string DriverExecutableFilePath { get; } = driverExecutableFilePath;

    public string BrowserExecutableFilePath { get; } = ChromeHelper.GetChromeExecutableFilePath();

    public int DriverProcessId { get; set; } = 0;

    public bool KeepUserDataDirectory { get; set; } = true;

    public string UserDataDirectory
    {
        get
        {
            if (!string.IsNullOrEmpty(_userDataDirectory))
                return _userDataDirectory;

            KeepUserDataDirectory = false;
            return _userDataDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        }
        set => _userDataDirectory = value;
    }

    public int LogLevel { get; set; } = 4;

    public bool Headless { get; set; }

    public string Language { get; } = "nb-no";

    public string WindowSize { get; } = "1920,1080";
}