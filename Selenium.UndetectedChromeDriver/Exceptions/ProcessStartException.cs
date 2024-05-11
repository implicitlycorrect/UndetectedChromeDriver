namespace Selenium.UndetectedChromeDriver.Exceptions;

public sealed class ProcessStartException(string processName) : Exception($"Failed to start process \"{processName}\"");