using System.Diagnostics;
using System.Runtime.CompilerServices;
using OpenQA.Selenium.Chrome;

namespace Selenium.UndetectedChromeDriver;

public class UndetectedChromeDriver : ChromeDriver
{
    private readonly UndetectedChromeDriverConfiguration _configuration;

    private readonly ChromeDriverService _service;

    /// <summary>
    /// Please use the <see cref="UndetectedChromeDriverFactory"/>
    /// for creating <see cref="UndetectedChromeDriver"/> instances.
    /// </summary>
    /// <param name="configuration"><see cref="UndetectedChromeDriverConfiguration"/></param>
    /// <param name="service"><see cref="ChromeDriverService"/></param>
    /// <param name="options"><see cref="ChromeOptions"/></param>
    /// <param name="commandTimeout"><see cref="TimeSpan"/></param>
    public UndetectedChromeDriver(UndetectedChromeDriverConfiguration configuration, ChromeDriverService service,
        ChromeOptions options, TimeSpan commandTimeout) : base(service, options, commandTimeout)
    {
        Console.CancelKeyPress += (_, _) => Dispose(true);
        _configuration = configuration;
        _service = service;
        if (!_configuration.Headless)
        {
            Manage().Window.Maximize();
        }
    }

    internal bool Headless { get; init; }

    public int BrowserProcessId { get; init; }
    
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        var browserName = Path.GetFileNameWithoutExtension(_configuration.BrowserExecutableFilePath);
        var driverName = Path.GetFileNameWithoutExtension(_configuration.DriverExecutableFilePath);
        
        KillChildProcessesAndChildrenByName(driverName);
        KillChildProcessesAndChildrenByName(browserName);
        
        if (!_configuration.KeepUserDataDirectory && Directory.Exists(_configuration.UserDataDirectory))
            DeleteUserDataDirectory();
        _service.Dispose();
        return;

        static void KillChildProcessesAndChildrenByName(string target, int retries = 5, int msTimeout = 200)
        {
            for (var i = 0; i < retries; i++)
            {
                foreach (var processId in GetChildProcessIds(Environment.ProcessId))
                {
                    try
                    {
                        var process = Process.GetProcessById(processId);
                        var processName = process.ProcessName;
                        if (processName.StartsWith(target))
                            Terminate(process);
                    }
                    catch
                    {
                        // ignored.
                    }
                }
                Thread.Sleep(msTimeout);
            }
        }
        
        static void Terminate(Process process)
        {
            foreach (var childProcessId in GetChildProcessIds(process.Id))
            {
                try
                {
                    var childProcess = Process.GetProcessById(childProcessId);
                    Terminate(childProcess);
                }
                catch
                {
                    // ignored.
                }
            }

            try
            {
                process.Kill();
            }
            catch
            {
                // ignored.
            }
        }

        static IEnumerable<int> GetChildProcessIds(int parentProcessId)
        {
            // "wmic process where (ParentProcessId={parentProcessId}) get ProcessId"
            using var wmicProcess = Process.Start(new ProcessStartInfo
            {
                ArgumentList =
                {
                    "process",
                    "where",
                    $"(ParentProcessId={parentProcessId})",
                    "get",
                    "ProcessId"
                },
                CreateNoWindow = true,
                FileName = @"C:\Windows\System32\wbem\wmic.exe",
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
            });
            if (wmicProcess == null)
                return [];

            wmicProcess.WaitForExit();
            var output = wmicProcess.StandardOutput.ReadToEnd();
            return output.Trim()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Skip(1)
                .Select(int.Parse);
        }
    }

    ~UndetectedChromeDriver() => Dispose(true);

    private void DeleteUserDataDirectory()
    {
        for (var retries = 3; retries-- > 0; Thread.Sleep(250))
            try
            {
                Directory.Delete(_configuration.UserDataDirectory, true);
            }
            catch
            {
                // ignored.
            }
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Stop")]
    private static extern void Stop(ChromeDriverService chromeDriverService);

    public void Reconnect(TimeSpan? startDelay = null)
    {
        Stop(_service);
        Thread.Sleep((int)(startDelay?.TotalMilliseconds ?? 100));
        _service.Start();
    }

    public void GoTo(string url)
    {
        if (_configuration.Headless)
        {
            if (ExecuteScript("return navigator.webdriver") is null)
                return;

            ExecuteCdpCommand(
                "Page.addScriptToEvaluateOnNewDocument",
                new Dictionary<string, object>
                {
                    {
                        "source", """
                                  Object.defineProperty(window, ""
                                  navigator "", {
                                    Object.defineProperty(window, ""
                                      navigator "", {
                                        value: new Proxy(navigator, {
                                          has: (target, key) => (key === ""
                                            webdriver "" ? false : key in target),
                                          get: (target, key) => key === ""
                                          webdriver "" ? false : typeof target[key] === ""
                                          function "" ? target[key].bind(target) : target[key],
                                        }),
                                      });
                                  """
                    }
                });

            ExecuteCdpCommand(
                "Network.setUserAgentOverride",
                new Dictionary<string, object>
                {
                    ["userAgent"] = ((string)ExecuteScript("return navigator.userAgent")).Replace("Headless",
                        _configuration.UserAgent)
                });

            ExecuteCdpCommand(
                "Page.addScriptToEvaluateOnNewDocument",
                new Dictionary<string, object>
                {
                    ["source"] =
                        """
                        Object.defineProperty(navigator, 'maxTouchPoints', {
                          get: () => 1
                        });
                        Object.defineProperty(navigator.connection, 'rtt', {
                          get: () => 100
                        });
                        // https://github.com/microlinkhq/browserless/blob/master/packages/goto/src/evasions/chrome-runtime.js
                        window.chrome = {
                          app: {
                            isInstalled: false,
                            InstallState: {
                              DISABLED: 'disabled',
                              INSTALLED: 'installed',
                              NOT_INSTALLED: 'not_installed'
                            },
                            RunningState: {
                              CANNOT_RUN: 'cannot_run',
                              READY_TO_RUN: 'ready_to_run',
                              RUNNING: 'running'
                            }
                          },
                          runtime: {
                            OnInstalledReason: {
                              CHROME_UPDATE: 'chrome_update',
                              INSTALL: 'install',
                              SHARED_MODULE_UPDATE: 'shared_module_update',
                              UPDATE: 'update'
                            },
                            OnRestartRequiredReason: {
                              APP_UPDATE: 'app_update',
                              OS_UPDATE: 'os_update',
                              PERIODIC: 'periodic'
                            },
                            PlatformArch: {
                              ARM: 'arm',
                              ARM64: 'arm64',
                              MIPS: 'mips',
                              MIPS64: 'mips64',
                              X86_32: 'x86-32',
                              X86_64: 'x86-64'
                            },
                            PlatformNaclArch: {
                              ARM: 'arm',
                              MIPS: 'mips',
                              MIPS64: 'mips64',
                              X86_32: 'x86-32',
                              X86_64: 'x86-64'
                            },
                            PlatformOs: {
                              ANDROID: 'android',
                              CROS: 'cros',
                              LINUX: 'linux',
                              MAC: 'mac',
                              OPENBSD: 'openbsd',
                              WIN: 'win'
                            },
                            RequestUpdateCheckStatus: {
                              NO_UPDATE: 'no_update',
                              THROTTLED: 'throttled',
                              UPDATE_AVAILABLE: 'update_available'
                            }
                          }
                        }
                        // https://github.com/microlinkhq/browserless/blob/master/packages/goto/src/evasions/navigator-permissions.js
                        if (!window.Notification) {
                          window.Notification = {
                            permission: 'denied'
                          }
                        }
                        const originalQuery = window.navigator.permissions.query
                        window.navigator.permissions.__proto__.query = parameters => parameters.name === 'notifications' ? Promise.resolve({
                          state: window.Notification.permission
                        }) : originalQuery(parameters)
                        const oldCall = Function.prototype.call

                        function call() {
                          return oldCall.apply(this, arguments)
                        }
                        Function.prototype.call = call
                        const nativeToStringFunctionString = Error.toString().replace(/Error/g, 'toString')
                        const oldToString = Function.prototype.toString

                        function functionToString() {
                          if (this === window.navigator.permissions.query) {
                            return 'function query() { [native code] }'
                          }
                          if (this === functionToString) {
                            return nativeToStringFunctionString
                          }
                          return oldCall.call(oldToString, this)
                        }
                        // eslint-disable-next-line
                        Function.prototype.toString = functionToString
                        """
                });
        }

        Navigate().GoToUrl(url);
    }

    public void StartSession()
    {
        base.StartSession(Capabilities);
    }
}