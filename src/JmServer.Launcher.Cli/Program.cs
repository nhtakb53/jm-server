using System.Security.Cryptography;
using JmServer.Contracts;
using JmServer.GameIntegration;
using JmServer.Launcher;
using JmServer.Protocol;

if (args.Length == 0)
{
    PrintUsage();
    return 2;
}

if (args[0] == "build-supply-mod" && args.Length == 3)
{
    try
    {
        var result = await InGameSupplyModBuilder.BuildAsync(
            Path.GetFullPath(args[1]),
            Path.GetFullPath(args[2]));
        Console.WriteLine(
            $"Built in-game supply mod: {result.UniqueItemCount} uniques, " +
            $"{result.SetItemCount} sets, {result.SelectorCount} selectors, " +
            $"{result.BaseSelectorCount} bases, {result.MaterialSelectorCount} materials, " +
            $"{result.CharmSelectorCount} charm selectors, {result.ControlTokenCount} control tokens, " +
            $"{result.QuickCraftRecipeCount} quick crafts, " +
            $"{result.WorkbenchRecipeCount} workbench recipes, " +
            $"{result.FileCount} files.");
        Console.WriteLine(result.OutputDirectory);
        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(exception);
        return 1;
    }
}

ClientProfile? storedProfile;
try
{
    storedProfile = await ClientProfileStore.TryLoadAsync();
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Could not load encrypted client profile: {exception.Message}");
    return 1;
}

var defaultServer = Environment.GetEnvironmentVariable("JM_SERVER_HOST") ??
                    storedProfile?.Server ??
                    "127.0.0.1";
var defaultPort = int.TryParse(Environment.GetEnvironmentVariable("JM_SERVER_PORT"), out var configuredPort)
    ? configuredPort
    : storedProfile?.Port ?? 15570;
var tlsEnvironmentValue = Environment.GetEnvironmentVariable("JM_SERVER_TLS");
var defaultUseTls = tlsEnvironmentValue is not null
    ? IsEnabled(tlsEnvironmentValue)
    : storedProfile?.UseTls ?? false;
var defaultCertificateSha256 = Environment.GetEnvironmentVariable("JM_SERVER_CERT_SHA256") ??
                               storedProfile?.CertificateSha256;

try
{
    if (args[0] == "play" &&
        args.Length == 3 &&
        Guid.TryParse(args[1], out var playCharacterId))
    {
        return await PlayAsync(
            playCharacterId,
            Path.GetFullPath(args[2]),
            defaultServer,
            defaultPort,
            defaultUseTls,
            defaultCertificateSha256,
            storedProfile);
    }

    if (args[0] == "recover-checkin" && args.Length == 1)
    {
        return await RecoverProfileAsync(storedProfile);
    }

    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));

    if (args[0] == "save-profile" && args.Length == 1)
    {
        var (profileDeviceId, profileToken) = ReadDeviceCredentials(profile: null);
        await ClientProfileStore.SaveAsync(
            new ClientProfile(
                defaultServer,
                defaultPort,
                defaultUseTls,
                defaultCertificateSha256,
                profileDeviceId,
                profileToken),
            cancellation.Token);
        Console.WriteLine($"Encrypted client profile saved to {ClientProfileStore.ProfilePath}.");
        return 0;
    }

    if (args[0] == "profile-status" && args.Length == 1)
    {
        if (storedProfile is null)
        {
            Console.WriteLine("No encrypted client profile is configured.");
            return 1;
        }

        Console.WriteLine($"Profile: {ClientProfileStore.ProfilePath}");
        Console.WriteLine($"Server:  {storedProfile.Server}:{storedProfile.Port} (TLS pinned)");
        Console.WriteLine($"Device:  {storedProfile.DeviceId}");
        return 0;
    }

    if (args[0] == "set-endpoint" &&
        args.Length == 3 &&
        int.TryParse(args[2], out var endpointPort) &&
        endpointPort is >= 1 and <= 65535)
    {
        if (storedProfile is null)
        {
            throw new InvalidOperationException(
                "No encrypted client profile is configured.");
        }

        var endpointHost = args[1].Trim();
        if (endpointHost.Length == 0)
        {
            throw new ArgumentException("The server host cannot be empty.");
        }

        await ClientProfileStore.SaveAsync(
            storedProfile with { Server = endpointHost, Port = endpointPort });
        Console.WriteLine(
            $"Encrypted client endpoint updated to {endpointHost}:{endpointPort}.");
        return 0;
    }

    if (args[0] == "probe")
    {
        await using var probe = new LauncherConnection(
            defaultUseTls,
            defaultServer,
            defaultCertificateSha256);
        await probe.ConnectAsync(defaultServer, defaultPort, cancellation.Token);
        Console.WriteLine($"Protocol handshake succeeded with {defaultServer}:{defaultPort}.");
        return 0;
    }

    if (args[0] == "verify-loader" && args.Length == 2)
    {
        var result = await D2RLoaderVerifier.VerifyReleaseArchiveAsync(
            Path.GetFullPath(args[1]),
            cancellation.Token);
        Console.WriteLine(result.Message);
        return result.IsValid ? 0 : 1;
    }

    if (args[0] == "prepare-client" && args.Length == 3)
    {
        var result = await D2RLoaderInstaller.InstallAsync(
            Path.GetFullPath(args[1]),
            Path.GetFullPath(args[2]),
            cancellation.Token);
        Console.WriteLine(
            $"Prepared restricted 정만서버 client: D2R {result.GameVersion}, " +
            $"D2RLoader {result.LoaderVersion}, {result.SupplyUniqueItemCount} unique and " +
            $"{result.SupplySetItemCount} set catalog items, {result.BaseSelectorCount} bases, " +
            $"{result.MaterialSelectorCount} materials, {result.CharmSelectorCount} charm selectors, " +
            $"{result.QuickCraftRecipeCount} quick crafts and " +
            $"{result.WorkbenchRecipeCount} workbench recipes.");
        if (result.BackupDirectory is not null)
        {
            Console.WriteLine($"Replaced files were backed up to {result.BackupDirectory}.");
        }

        return 0;
    }

    if (args[0] == "verify-client" && args.Length == 2)
    {
        var result = await D2RLoaderInstaller.VerifyInstallationAsync(
            Path.GetFullPath(args[1]),
            cancellation.Token);
        Console.WriteLine(result.Message);
        return result.IsValid ? 0 : 1;
    }

    if (args[0] == "inspect-save" && args.Length == 2)
    {
        var savePath = Path.GetFullPath(args[1]);
        var saveData = await File.ReadAllBytesAsync(savePath, cancellation.Token);
        var metadata = D2SaveMetadataReader.Read(saveData);
        Console.WriteLine($"Version: {metadata.Version}");
        Console.WriteLine($"Name:    {metadata.Name}");
        Console.WriteLine($"Class:   {metadata.CharacterClass}");
        Console.WriteLine($"Level:   {metadata.Level}");
        return 0;
    }

    var (deviceId, token) = ReadDeviceCredentials(storedProfile);
    await using var connection = new LauncherConnection(
        defaultUseTls,
        defaultServer,
        defaultCertificateSha256);
    await connection.ConnectAsync(defaultServer, defaultPort, cancellation.Token);
    var identity = await connection.AuthenticateAsync(deviceId, token, cancellation.Token);

    switch (args[0])
    {
        case "list" when args.Length == 1:
            {
                var result = await connection.ListCharactersAsync(cancellation.Token);
                Console.WriteLine($"Authenticated as {identity.Username}.");
                foreach (var character in result.Characters)
                {
                    var state = character.IsLeased
                        ? $"leased until {character.LeaseExpiresAt:O}"
                        : "available";
                    Console.WriteLine(
                        $"{character.CharacterId}  {character.Name}  {character.CharacterClass}  " +
                        $"revision={character.Revision}  {state}");
                }

                return 0;
            }

        case "create" when args.Length is 3 or 4 &&
                                        Enum.TryParse<PlayableCharacterClass>(
                                            args[2], ignoreCase: true, out var createClass):
            {
                var createPreset = CharacterCreationPreset.PvpReady;
                var result = await connection.CreateCharacterAsync(
                    new CreateCharacterRequest(args[1], createClass, createPreset),
                    cancellation.Token);
                Console.WriteLine(
                    $"Created {result.Character.CharacterId}  {result.Character.Name}  " +
                    $"{result.Character.CharacterClass}  revision={result.Character.Revision}.");
                return 0;
            }

        case "manage" when args.Length == 2 && Guid.TryParse(args[1], out var managedCharacterId):
            {
                var result = await connection.GetCharacterManagementAsync(
                    managedCharacterId,
                    cancellation.Token);
                Console.WriteLine(
                    $"{result.Character.CharacterId}  {result.Character.Name}  " +
                    $"{result.Character.CharacterClass}  revision={result.Character.Revision}");
                Console.WriteLine(
                    $"level={result.Stats.Level} str={result.Stats.Strength} dex={result.Stats.Dexterity} " +
                    $"vit={result.Stats.Vitality} ene={result.Stats.Energy} " +
                    $"unspent={result.Stats.UnspentStatPoints}");
                return 0;
            }

        case "deleted" when args.Length == 1:
            {
                var result = await connection.ListDeletedCharactersAsync(cancellation.Token);
                foreach (var character in result.Characters)
                {
                    Console.WriteLine(
                        $"{character.CharacterId}  {character.Name}  {character.CharacterClass}  " +
                        $"deleted={character.DeletedAt:O}");
                }

                return 0;
            }

        case "rename" when args.Length == 3 && Guid.TryParse(args[1], out var renameCharacterId):
            {
                var result = await connection.RenameCharacterAsync(
                    renameCharacterId,
                    args[2],
                    cancellation.Token);
                Console.WriteLine(
                    $"Renamed character to {result.Character.Name}; revision={result.Character.Revision}.");
                return 0;
            }

        case "reset-stats" when args.Length == 2 &&
                                             Guid.TryParse(args[1], out var resetCharacterId):
            {
                var result = await connection.ResetCharacterStatsAsync(
                    resetCharacterId,
                    cancellation.Token);
                Console.WriteLine(
                    $"Reset {result.Character.Name}; unspent={result.Stats.UnspentStatPoints}; " +
                    $"revision={result.Character.Revision}.");
                return 0;
            }

        case "delete" when args.Length == 2 && Guid.TryParse(args[1], out var deleteCharacterId):
            {
                _ = await connection.DeleteCharacterAsync(deleteCharacterId, cancellation.Token);
                Console.WriteLine($"Moved character {deleteCharacterId} to server trash.");
                return 0;
            }

        case "restore" when args.Length == 2 && Guid.TryParse(args[1], out var restoreCharacterId):
            {
                var result = await connection.RestoreCharacterAsync(
                    restoreCharacterId,
                    cancellation.Token);
                Console.WriteLine(
                    $"Restored {result.Character.Name}; revision={result.Character.Revision}.");
                return 0;
            }

        case "purge" when args.Length == 2 && Guid.TryParse(args[1], out var purgeCharacterId):
            {
                _ = await connection.PurgeDeletedCharacterAsync(
                    purgeCharacterId,
                    cancellation.Token);
                Console.WriteLine($"Permanently deleted character {purgeCharacterId}.");
                return 0;
            }

        case "rooms" when args.Length == 1:
            {
                var result = await connection.ListPvpRoomsAsync(cancellation.Token);
                Console.WriteLine($"Authenticated as {identity.Username}.");
                foreach (var room in result.Rooms)
                {
                    Console.WriteLine(
                        $"{room.RoomId}  {room.RoomCode}  {room.HostUsername}/{room.HostCharacterName}  " +
                        $"{room.Status}  {room.HostAddress}:{room.HostPort}  expires={room.ExpiresAt:O}");
                }

                return 0;
            }

        case "room-create" when args.Length == 3 && Guid.TryParse(args[1], out var roomCharacterId):
            {
                var result = await connection.CreatePvpRoomAsync(
                    new CreatePvpRoomRequest(roomCharacterId, args[2]),
                    cancellation.Token);
                Console.WriteLine(
                    $"Created {result.Room.RoomId}  {result.Room.RoomCode}  " +
                    $"{result.Room.HostAddress}:{result.Room.HostPort}");
                return 0;
            }

        case "room-leave" when args.Length == 2 && Guid.TryParse(args[1], out var roomId):
            {
                _ = await connection.LeavePvpRoomAsync(roomId, cancellation.Token);
                Console.WriteLine($"Left PK room {roomId}.");
                return 0;
            }

        default:
            PrintUsage();
            return 2;
    }
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Operation canceled or timed out. Any checked-out local save was kept for recovery.");
    return 1;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}

static async Task<int> PlayAsync(
    Guid characterId,
    string gameDirectory,
    string server,
    int port,
    bool useTls,
    string? certificateSha256,
    ClientProfile? storedProfile)
{
    var (deviceId, token) = ReadDeviceCredentials(storedProfile);
    using var commandCancellation = new CancellationTokenSource();
    ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        commandCancellation.Cancel();
    };
    Console.CancelKeyPress += cancelHandler;

    try
    {
        if (File.Exists(ProfileLeaseFile.Path))
        {
            throw new InvalidOperationException(
                "A previous 정만서버 profile is waiting for recovery. Run 'recover-checkin' first.");
        }

        using (var startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                   commandCancellation.Token))
        {
            startupCancellation.CancelAfter(TimeSpan.FromSeconds(30));
            var installation = await D2RLoaderInstaller.VerifyInstallationAsync(
                gameDirectory,
                startupCancellation.Token);
            if (!installation.IsValid)
            {
                throw new InvalidDataException(installation.Message);
            }
        }

        D2RLoaderInstaller.EnsurePrivateSessionProcessesAreStopped();
        await using var connection = new LauncherConnection(useTls, server, certificateSha256);
        ProfileLeaseFile lease;
        using (var connectCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                   commandCancellation.Token))
        {
            connectCancellation.CancelAfter(TimeSpan.FromSeconds(30));
            await connection.ConnectAsync(server, port, connectCancellation.Token);
            var identity = await connection.AuthenticateAsync(
                deviceId,
                token,
                connectCancellation.Token);
            var characters = await connection.ListCharactersAsync(connectCancellation.Token);
            var summary = characters.Characters.SingleOrDefault(
                              character => character.CharacterId == characterId)
                          ?? throw new InvalidDataException(
                              $"Character '{characterId}' is not available to this account.");

            var checkout = await connection.CheckoutProfileAsync(
                characterId,
                connectCancellation.Token);
            ValidateProfilePayload(checkout);
            var installed = await ProfileDirectoryManager.InstallCheckoutAsync(
                checkout.Binary,
                cancellationToken: connectCancellation.Token);
            var registeredOnServer = characters.Characters
                .Select(character => character.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!registeredOnServer.SetEquals(installed.RegisteredCharacterNames))
            {
                throw new InvalidDataException(
                    "Server profile files do not match the registered character list.");
            }

            var localSavePath = D2RClientLayout.GetCharacterSavePath(summary.Name);
            var metadata = D2SaveMetadataReader.Read(
                await File.ReadAllBytesAsync(localSavePath, connectCancellation.Token));
            if (!string.Equals(metadata.Name, summary.Name, StringComparison.Ordinal) ||
                !string.Equals(metadata.CharacterClass, summary.CharacterClass, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Server character metadata does not match the D2 save payload.");
            }

            lease = new ProfileLeaseFile(
                server,
                port,
                useTls,
                certificateSha256,
                characterId,
                checkout.Metadata.LeaseId,
                checkout.Metadata.Revision,
                checkout.Metadata.LeaseExpiresAt,
                installed.RegisteredCharacterNames);
            await lease.SaveAsync(connectCancellation.Token);
            Console.WriteLine(
                $"Checked out the 정만서버 profile revision {checkout.Metadata.Revision} for {metadata.Name}.");
            if (installed.QuarantineDirectory is not null)
            {
                Console.WriteLine($"Existing local 정만서버 saves were quarantined at '{installed.QuarantineDirectory}'.");
            }

            Console.WriteLine("Starting D2RLoader. Use TCP/IP only; do not open the Online character tab.");
            Console.WriteLine($"Authenticated as {identity.Username}.");
        }

        var exitCode = await D2RGameRunner.RunAsync(
            gameDirectory,
            async heartbeatCancellation =>
            {
                using var heartbeatTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                    heartbeatCancellation);
                heartbeatTimeout.CancelAfter(TimeSpan.FromSeconds(20));
                var renewal = await connection.RenewProfileLeaseAsync(
                    lease.LeaseId,
                    heartbeatTimeout.Token);
                lease = lease with { LeaseExpiresAt = renewal.LeaseExpiresAt };
                await lease.SaveAsync(heartbeatTimeout.Token);
                Console.WriteLine($"Lease renewed until {renewal.LeaseExpiresAt:O}.");
            },
            warning => Console.Error.WriteLine("WARNING: " + warning),
            commandCancellation.Token);

        if (exitCode != 0)
        {
            Console.Error.WriteLine($"WARNING: D2RLoader exited with code {exitCode}; saving the profile anyway.");
        }

        var collected = ProfileDirectoryManager.CollectForCheckin(
            lease.RegisteredCharacterNames);
        if (collected.QuarantineDirectory is not null)
        {
            Console.WriteLine(
                $"Unregistered local character files were quarantined at '{collected.QuarantineDirectory}'.");
        }

        using var checkinCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            commandCancellation.Token);
        checkinCancellation.CancelAfter(TimeSpan.FromSeconds(30));
        var checkin = await CheckinProfileAsync(
            connection,
            lease,
            collected.BundleData,
            checkinCancellation.Token);

        File.Delete(ProfileLeaseFile.Path);
        ProfileDirectoryManager.RemoveCheckedInProfile();
        Console.WriteLine(
            $"Checked in profile revision {checkin.Revision}; local 정만서버 character and stash files were removed.");
        return 0;
    }
    catch
    {
        if (File.Exists(ProfileLeaseFile.Path))
        {
            Console.Error.WriteLine(
                "The complete local 정만서버 profile was retained for recovery. Run 'recover-checkin' before playing again.");
        }

        throw;
    }
    finally
    {
        Console.CancelKeyPress -= cancelHandler;
    }
}

static async Task<int> RecoverProfileAsync(ClientProfile? storedProfile)
{
    if (!File.Exists(ProfileLeaseFile.Path))
    {
        Console.WriteLine("No local 정만서버 profile is waiting for recovery.");
        return 0;
    }

    D2RLoaderInstaller.EnsurePrivateSessionProcessesAreStopped();
    var lease = await ProfileLeaseFile.LoadAsync(CancellationToken.None);
    var (deviceId, token) = ReadDeviceCredentials(storedProfile);
    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    await using var connection = new LauncherConnection(
        lease.UseTls,
        lease.Server,
        lease.CertificateSha256);
    await connection.ConnectAsync(lease.Server, lease.Port, cancellation.Token);
    _ = await connection.AuthenticateAsync(deviceId, token, cancellation.Token);
    var collected = ProfileDirectoryManager.CollectForCheckin(lease.RegisteredCharacterNames);
    CheckinProfileResponse result;
    try
    {
        result = await CheckinProfileAsync(
            connection,
            lease,
            collected.BundleData,
            cancellation.Token);
    }
    catch (RemoteServerException exception)
        when (exception.Error.Code == ErrorCode.CharacterLeaseInvalid)
    {
        var quarantineDirectory = ProfileDirectoryManager.QuarantineManagedProfile(
            additionalPaths: [ProfileLeaseFile.Path]);
        Console.WriteLine(
            "The server profile was already updated. The stale local profile was not checked in.");
        Console.WriteLine($"The stale local profile was quarantined at '{quarantineDirectory}'.");
        return 0;
    }
    File.Delete(ProfileLeaseFile.Path);
    ProfileDirectoryManager.RemoveCheckedInProfile();
    Console.WriteLine($"Recovered and checked in profile revision {result.Revision}.");
    if (collected.QuarantineDirectory is not null)
    {
        Console.WriteLine(
            $"Unregistered local character files were quarantined at '{collected.QuarantineDirectory}'.");
    }

    return 0;
}

static void ValidateProfilePayload(BinaryPayload<CheckoutProfileResponse> result)
{
    if (result.Metadata.BundleLength != result.Binary.Length)
    {
        throw new InvalidDataException("Server returned a profile with a mismatched length.");
    }

    var actualHash = Convert.ToHexString(SHA256.HashData(result.Binary));
    if (!string.Equals(actualHash, result.Metadata.Sha256Hex, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException("Server returned a profile with a mismatched checksum.");
    }
}

static async Task<CheckinProfileResponse> CheckinProfileAsync(
    LauncherConnection connection,
    ProfileLeaseFile lease,
    byte[] bundleData,
    CancellationToken cancellationToken)
{
    var sha256Hex = Convert.ToHexString(SHA256.HashData(bundleData));
    return await connection.CheckinProfileAsync(
        new CheckinProfileRequest(
            lease.LeaseId,
            lease.Revision,
            bundleData.Length,
            sha256Hex),
        bundleData,
        cancellationToken);
}

static (Guid DeviceId, string Token) ReadDeviceCredentials(ClientProfile? profile)
{
    var deviceIdText = Environment.GetEnvironmentVariable("JM_DEVICE_ID") ??
                       profile?.DeviceId.ToString();
    var token = Environment.GetEnvironmentVariable("JM_DEVICE_TOKEN") ?? profile?.Token;
    if (!Guid.TryParse(deviceIdText, out var deviceId) || string.IsNullOrWhiteSpace(token))
    {
        throw new InvalidOperationException("JM_DEVICE_ID and JM_DEVICE_TOKEN are required.");
    }

    return (deviceId, token);
}

static void PrintUsage()
{
    Console.Error.WriteLine("정만서버");
    Console.Error.WriteLine("  save-profile        (imports JM_* environment variables into Windows DPAPI)");
    Console.Error.WriteLine("  profile-status");
    Console.Error.WriteLine("  set-endpoint <server-host> <server-port>");
    Console.Error.WriteLine("  probe");
    Console.Error.WriteLine("  inspect-save <save-path>");
    Console.Error.WriteLine("  verify-loader <D2RLoader-release.zip>");
    Console.Error.WriteLine("  prepare-client <D2RLoader-release.zip> <D2R-game-directory>");
    Console.Error.WriteLine("  verify-client <D2R-game-directory>");
    Console.Error.WriteLine("  build-supply-mod <pinned-D2R-data-directory> <output-directory>");
    Console.Error.WriteLine("  list");
    Console.Error.WriteLine("  create <name> <class>  (always creates the server's level-99 build)");
    Console.Error.WriteLine("  manage <character-id>");
    Console.Error.WriteLine("  deleted");
    Console.Error.WriteLine("  rename <character-id> <new-name>");
    Console.Error.WriteLine("  reset-stats <character-id>");
    Console.Error.WriteLine("  delete <character-id>");
    Console.Error.WriteLine("  restore <character-id>");
    Console.Error.WriteLine("  purge <deleted-character-id>");
    Console.Error.WriteLine("  rooms");
    Console.Error.WriteLine("  room-create <character-id> <host-ipv4>");
    Console.Error.WriteLine("  room-leave <room-id>");
    Console.Error.WriteLine("  play <character-id> <D2R-game-directory>");
    Console.Error.WriteLine("  recover-checkin");
}

static bool IsEnabled(string? value) =>
    value is not null &&
    (value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
     value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
     value.Equals("yes", StringComparison.OrdinalIgnoreCase));
