namespace InfoID.Desktop.Features.Trial.Services;

/// <summary>
/// How InfoID behaves when it is NOT activated.
///
/// OfflineWatermarkTrial = true  (current: license server not deployed yet)
///     Print without logging in and without the server. Every card carries the
///     "INFOID TRIAL VERSION" watermark. No card limit.
///
/// OfflineWatermarkTrial = false (once the License API is live)
///     The normal trial: log in, the server counts the cards (e.g. 10), watermark on.
///
/// An activated license always prints without a watermark, whatever this is set to.
/// </summary>
public static class TrialPolicy
{
    public const bool OfflineWatermarkTrial = true;
}
