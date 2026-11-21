using Hishab.Application.Orders;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Hishab.Infrastructure.Labels;

/// <summary>
/// Prints HTML to PDF with headless Chromium (ADR 0008): real text shaping (Bangla conjuncts), CSS
/// page sizes. One browser per process, started on first use and restarted if it dies; each
/// document gets its own throw-away context with JavaScript off and every request blocked, so a
/// template can neither run script nor reach the network (no SSRF through an address field).
/// At most <see cref="MaxConcurrentRenders"/> documents print at once: Chromium is memory-hungry.
/// </summary>
internal sealed partial class ChromiumPdfRenderer(ILogger<ChromiumPdfRenderer> logger) : IAsyncDisposable
{
    public const int MaxConcurrentRenders = 2;

    private readonly SemaphoreSlim _gate = new(MaxConcurrentRenders);
    private readonly SemaphoreSlim _startLock = new(1);
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public async Task<byte[]> RenderAsync(string html, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var browser = await BrowserAsync(cancellationToken);
            await using var context = await browser.NewContextAsync(new BrowserNewContextOptions { JavaScriptEnabled = false });
            await context.RouteAsync("**/*", route => route.AbortAsync()); // data: URIs (our font) are not requests
            var page = await context.NewPageAsync();
            await page.SetContentAsync(html, new PageSetContentOptions { WaitUntil = WaitUntilState.Load, Timeout = 15_000 });
            return await page.PdfAsync(new PagePdfOptions { PreferCSSPageSize = true, PrintBackground = true });
        }
        catch (PlaywrightException e)
        {
            LogRenderFailed(logger, e.GetType().Name); // message can hold the HTML (personal data): not logged
            throw new PdfRenderingException();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IBrowser> BrowserAsync(CancellationToken cancellationToken)
    {
        if (_browser is { IsConnected: true } running)
        {
            return running;
        }

        await _startLock.WaitAsync(cancellationToken);
        try
        {
            if (_browser is { IsConnected: true } started)
            {
                return started; // another render started it while we waited
            }

            _playwright ??= await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            LogBrowserStarted(logger, _browser.Version);
            return _browser;
        }
        finally
        {
            _startLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync();
        }

        _playwright?.Dispose();
        _gate.Dispose();
        _startLock.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Chromium {Version} started for PDF rendering")]
    private static partial void LogBrowserStarted(ILogger logger, string version);

    [LoggerMessage(Level = LogLevel.Error, Message = "PDF rendering failed with {ExceptionType} (is Chromium installed? see README)")]
    private static partial void LogRenderFailed(ILogger logger, string exceptionType);
}
