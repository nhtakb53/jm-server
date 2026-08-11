namespace JmServer.GameIntegration;

public static class D2RClientLayout
{
    public const string ModName = "JMServer";
    public const string SupportedGameVersion = "3.2.92777";

    public static string GetBaseSaveDirectory()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(profile))
        {
            throw new InvalidOperationException("The current Windows user profile could not be located.");
        }

        return Path.Combine(
            profile,
            "Saved Games",
            "Diablo II Resurrected");
    }

    public static string GetModSaveDirectory() => Path.Combine(
        GetBaseSaveDirectory(),
        "Mods",
        ModName);

    public static string GetCharacterSavePath(string characterName)
    {
        if (string.IsNullOrWhiteSpace(characterName) ||
            characterName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            !string.Equals(characterName, Path.GetFileName(characterName), StringComparison.Ordinal))
        {
            throw new InvalidDataException("The character name cannot be used as a save file name.");
        }

        return Path.Combine(GetModSaveDirectory(), characterName + ".d2s");
    }
}
