namespace DeckHost.Core.Plugins;

/// <summary>
/// Moves plugin-held state along with the editor's bindings, so duplicating a profile or dragging a
/// control keeps the plugins' own settings (Stream Deck plugins store theirs outside the profile).
/// </summary>
public interface IPluginStateTransfer
{
    void CopyProfileSettings(string sourceProfileId, string targetProfileId);

    void MoveControlSettings(string deviceId, string profileId, string sourceControlId, string targetControlId, bool copy);
}
