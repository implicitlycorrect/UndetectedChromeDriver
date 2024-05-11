# UndetectedChromeDriver  

This is a C# implementation of [undetected_chromedriver](https://github.com/ultrafunkamsterdam/undetected-chromedriver).

It optimizes Selenium chromedriver to avoid being detected by anti-bot services.

Example usage:
```csharp
var driverPath = await ChromeHelper.GetOrInstallDriverAsync(cancellationToken: default);
var driverConfig = new UndetectedChromeDriverConfiguration(driverPath) {
  Arguments = [
      "--no-first-run",
      "--mute-audio",
      "--disable-infobars",
      "--disable-gpu",
      "--disable-dev-shm-usage",
      "--disable-cloud-management",
      "--disable-extensions",
      "--ignore-certificate-errors",
      "--allow-running-insecure-content",
      "--disable-popup-blocking",
      "--disable-blink-features",
      "--disable-blink-features=AutomationControlled",
      "--disable-logging",
      "--disable-default-apps",
      "--disable-notifications",
      "--disable-plugins-discovery",
      "--disable-hang-monitor",
      "--disable-prompt-on-repost",
      "--silent"
    ],
    UserAgent = "Mozilla/5.0 (Windows NT 5.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/119.1.8190.97 Safari/537.36",
    Headless = true
};
using var driver = UndetectedChromeDriverFactory.Create(driverConfig);
driver.GoTo("https://www.google.com/");
```
