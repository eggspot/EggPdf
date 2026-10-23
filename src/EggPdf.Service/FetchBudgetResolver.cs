using System.Diagnostics;
using EggPdf.Core;
using EggPdf.Core.Resources;

namespace EggPdf.Service;

/// <summary>
/// Caps one render's external fetches, so a single request to the public service can't keep it busy
/// fetching remote images one after another: at most <see cref="MaxFetches"/> fetches within
/// <see cref="TotalTime"/> of the request starting. Past either limit every fetch resolves to null and the
/// render gets one LIMIT_EXCEEDED warning. Create one per request.
/// </summary>
internal sealed class FetchBudgetResolver : IResourceResolver
{
    public const int MaxFetches = 50;
    public static readonly TimeSpan TotalTime = TimeSpan.FromSeconds(20);

    private readonly IResourceResolver _inner;
    private readonly WarningCollector? _warnings;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private int _fetches;
    private int _warned;

    public FetchBudgetResolver(IResourceResolver inner, WarningCollector? warnings)
    {
        _inner = inner;
        _warnings = warnings;
    }

    public async Task<ResourceResult?> ResolveAsync(string url, ResourceType type, CancellationToken ct = default)
    {
        var remaining = TotalTime - _clock.Elapsed;
        if (Interlocked.Increment(ref _fetches) > MaxFetches || remaining <= TimeSpan.Zero)
        {
            WarnOnce();
            return null;
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(remaining);
        try
        {
            return await _inner.ResolveAsync(url, type, budget.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            WarnOnce();
            return null;
        }
    }

    private void WarnOnce()
    {
        if (_warnings == null || Interlocked.Exchange(ref _warned, 1) == 1) return;
        lock (_warnings)
            _warnings.Add(WarningCodes.LimitExceeded,
                $"External resources skipped: a render may fetch at most {MaxFetches} resources within {TotalTime.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}s");
    }
}
