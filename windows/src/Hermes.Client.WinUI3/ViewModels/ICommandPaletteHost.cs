using Hermes.Client.Core;

namespace Hermes.Client.WinUI3.ViewModels;

/// <summary>
/// Shell services the command palette needs. Implemented by
/// <see cref="MainWindow"/>: it owns navigation, the composer, and the
/// skill cache (GET /v1/skills 500s today — the cache degrades to empty).
/// </summary>
public interface ICommandPaletteHost
{
    Task<IReadOnlyList<SkillInfo>> GetSkillsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Session>> GetSessionsAsync(CancellationToken cancellationToken = default);
    Task PerformEffectAsync(PaletteEffect effect);
}
