namespace Tmds.DBus.Protocol;

public record ClientSetupResult(string address)
{
    public string ConnectionAddress { get; init; } =
        address ?? throw new ArgumentNullException(nameof(address));

    public object? TeardownToken { get; init; }

    public string? UserId { get; init; }

    public bool SupportsFdPassing { get; init; }
}