namespace CommunityFrontier.Core;

public sealed class LaunchCoordinator(ILobbyConfigurationWriter writer, ILobbyConfigurationFormatter formatter,
    IGameRunner runner, IGameInstallationValidator validator)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task PlayAsync(DetectedGameInstallation game, PlayMode mode, string? developmentIdentifier, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            if (!validator.IsValid(game.InstallationPath)) throw new FriendlyException("Red Dead moved or is unavailable. Choose Find Red Dead again.");
            runner.EnsureStopped();
            switch (mode)
            {
                case PlayMode.Normal: await writer.RestoreAsync(game.InstallationPath, token: token); break;
                case PlayMode.Solo: await writer.ApplyAsync(game.InstallationPath, formatter.Format(StartupMetaFormatter.NewSoloIdentifier()), mode, token); break;
                case PlayMode.Community:
                    if (string.IsNullOrWhiteSpace(developmentIdentifier)) throw new FriendlyException("Fair Play is unavailable in this preview. Choose Original Settings to play with your original game setup.");
                    await writer.ApplyAsync(game.InstallationPath, formatter.Format(developmentIdentifier), mode, token);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(mode));
            }
            // No online service is consulted by Solo, Normal, or restoration.
            await runner.LaunchAsync(game, token);
        }
        finally { gate.Release(); }
    }
}
