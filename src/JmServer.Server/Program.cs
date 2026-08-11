using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using D2SSharp.Model;
using JmServer.Domain;
using JmServer.GameIntegration;
using JmServer.Persistence;
using JmServer.Protocol;
using JmServer.Server;
using Microsoft.Extensions.Logging;
using Npgsql;
using SuperSocket;
using SuperSocket.Server.Abstractions;
using SuperSocket.Server.Host;

var configuration = JmConfiguration.Load();

if (args is ["provision-database", var databaseName, var roleName])
{
    var administrationConnectionString = configuration["JM_ADMIN_DATABASE"];
    if (string.IsNullOrWhiteSpace(administrationConnectionString))
    {
        Console.Error.WriteLine(
            "JM_ADMIN_DATABASE must contain a PostgreSQL administrator connection string.");
        return 2;
    }

    await using var administrationDataSource = NpgsqlDataSource.Create(administrationConnectionString);
    var provisioned = await new DatabaseProvisioner(administrationDataSource)
        .ProvisionAsync(databaseName, roleName);
    await using var provisionedDataSource = NpgsqlDataSource.Create(provisioned.ConnectionString);
    await new DatabaseMigrator(provisionedDataSource).RunAsync();

    Console.WriteLine($"Created database: {provisioned.DatabaseName}");
    Console.WriteLine($"Created role:     {provisioned.RoleName}");
    Console.WriteLine($"Password:         {provisioned.Password}");
    Console.WriteLine($"JM_DATABASE:      {provisioned.ConnectionString}");
    Console.WriteLine("Store the password now. It cannot be recovered by 정만서버.");
    return 0;
}

var connectionString = configuration["JM_DATABASE"];
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("JM_DATABASE must contain the PostgreSQL connection string.");
    return 2;
}

await using var dataSource = NpgsqlDataSource.Create(connectionString);
var migrator = new DatabaseMigrator(dataSource);
await migrator.RunAsync();

if (args.Length > 0)
{
    return await RunAdministrationCommandAsync(args, dataSource);
}

var deviceStore = new NpgsqlDeviceStore(dataSource);
var characterSaveFactory = new D2CharacterSaveFactory();
var characterStore = new NpgsqlCharacterStore(dataSource, characterSaveFactory);
var vault = new CharacterVaultService(
    characterStore,
    characterSaveFactory,
    new D2CharacterSaveEditor());
var profileStore = new NpgsqlProfileStore(dataSource);
var profileVault = new ProfileVaultService(profileStore);
var pvpRooms = new PvpRoomService(characterStore);
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "yyyy-MM-dd HH:mm:ss zzz ";
    });
});
var dispatcher = new PacketDispatcher(
    deviceStore,
    vault,
    profileVault,
    pvpRooms,
    loggerFactory.CreateLogger<PacketDispatcher>());

var listenIp = configuration["JM_LISTEN_IP"] ?? "127.0.0.1";
var port = int.TryParse(configuration["JM_LISTEN_PORT"], out var configuredPort)
    ? configuredPort
    : 15570;
var certificatePath = configuration["JM_TLS_CERTIFICATE"];
X509Certificate2? serverCertificate = null;
if (!string.IsNullOrWhiteSpace(certificatePath))
{
    certificatePath = Path.GetFullPath(certificatePath);
    var keyStorageFlags = OperatingSystem.IsWindows()
        ? X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet
        : X509KeyStorageFlags.EphemeralKeySet;
    serverCertificate = X509CertificateLoader.LoadPkcs12FromFile(
        certificatePath,
        configuration["JM_TLS_PASSWORD"],
        keyStorageFlags);
    Console.WriteLine(
        $"TLS certificate loaded with {keyStorageFlags}; private key: {serverCertificate.HasPrivateKey}.");
}

if (!IsLoopback(listenIp) && serverCertificate is null)
{
    Console.Error.WriteLine(
        "Refusing a non-loopback listener without TLS. Set JM_TLS_CERTIFICATE to a trusted PFX file.");
    return 2;
}

var listener = new ListenOptions
{
    Ip = listenIp,
    Port = port,
    NoDelay = true,
    AuthenticationOptions = serverCertificate is null
        ? null
        : new ServerAuthenticationOptions
        {
            ServerCertificate = serverCertificate,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
        }
};

var host = SuperSocketHostBuilder
    .Create<WirePacket, WirePipelineFilter>(args)
    .UseSession<JmSession>()
    .UsePackageHandler((session, packet) =>
        dispatcher.DispatchAsync((JmSession)session, packet))
    .ConfigureSuperSocket(options =>
    {
        options.Name = "정만서버";
        options.MaxPackageLength = JmServer.Contracts.ProtocolConstants.MaxPacketBodyLength
                                   + WirePacketCodec.HeaderLength;
        options.IdleSessionTimeOut = 180;
        options.ClearIdleSessionInterval = 30;
        options.Listeners = [listener];
    })
    .ConfigureLogging(builder =>
    {
        builder.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "yyyy-MM-dd HH:mm:ss zzz ";
        });
    })
    .UseWindowsService(options => options.ServiceName = "정만서버")
    .Build();

Console.WriteLine(
    $"정만서버 listening on {listenIp}:{port} ({(serverCertificate is null ? "local plaintext" : "TLS")}).");
await host.RunAsync();
serverCertificate?.Dispose();
return 0;

static bool IsLoopback(string value) =>
    IPAddress.TryParse(value, out var address) && IPAddress.IsLoopback(address);

static async Task<int> RunAdministrationCommandAsync(string[] arguments, NpgsqlDataSource dataSource)
{
    var administration = new AdministrationStore(dataSource);
    switch (arguments[0])
    {
        case "migrate":
            Console.WriteLine("Database migrations are current.");
            return 0;

        case "rename-account" when arguments.Length == 3:
            {
                var accountId = await administration.RenameAccountAsync(arguments[1], arguments[2]);
                Console.WriteLine(
                    $"Renamed account {accountId} from '{arguments[1]}' to '{arguments[2]}'.");
                return 0;
            }

        case "bootstrap-device" when arguments.Length == 3:
            {
                var device = await administration.ProvisionDeviceAsync(arguments[1], arguments[2]);
                Console.WriteLine($"AccountId: {device.AccountId}");
                Console.WriteLine($"DeviceId:  {device.DeviceId}");
                Console.WriteLine($"Username:  {device.Username}");
                Console.WriteLine($"Device:    {device.DisplayName}");
                Console.WriteLine($"Token:     {device.Token}");
                Console.WriteLine("Store the token now. It cannot be recovered from the database.");
                return 0;
            }

        case "import-character" when arguments.Length == 5:
            {
                var savePath = Path.GetFullPath(arguments[4]);
                var saveData = await File.ReadAllBytesAsync(savePath);
                var characterId = await administration.ImportCharacterAsync(
                    arguments[1],
                    arguments[2],
                    arguments[3],
                    saveData);
                Console.WriteLine($"Imported character {characterId} from {savePath}.");
                return 0;
            }

        case "import-stash" when arguments.Length == 3:
            {
                var stashPath = Path.GetFullPath(arguments[2]);
                var stashData = await File.ReadAllBytesAsync(stashPath);
                await administration.ImportSharedStashAsync(
                    arguments[1],
                    Path.GetFileName(stashPath),
                    stashData);
                Console.WriteLine($"Imported shared stash from {stashPath}.");
                return 0;
            }

        case "set-stash-gold" when arguments.Length is 2 or 3:
            {
                var goldPerTab = D2SharedStashGold.MaximumGoldPerTab;
                if (arguments.Length == 3 &&
                    (!uint.TryParse(arguments[2], out goldPerTab) ||
                     goldPerTab > D2SharedStashGold.MaximumGoldPerTab))
                {
                    Console.Error.WriteLine(
                        $"Gold must be between 0 and {D2SharedStashGold.MaximumGoldPerTab} per tab.");
                    return 2;
                }

                var stored = await administration.ReadSoftcoreSharedStashAsync(arguments[1]);
                var fileName = stored?.FileName ?? D2SharedStashGold.SoftcoreFileName;
                var stashData = stored is null
                    ? D2SharedStashGold.CreateModernSoftcore(goldPerTab)
                    : D2SharedStashGold.SetNormalTabGold(stored.Data, goldPerTab);
                await administration.ImportSharedStashAsync(
                    arguments[1],
                    fileName,
                    stashData);
                Console.WriteLine(
                    $"Set shared stash gold to {goldPerTab:N0} per normal tab for '{arguments[1]}'.");
                return 0;
            }

        case "refill-stash-materials" when arguments.Length == 2:
            {
                var stored = await administration.ReadSoftcoreSharedStashAsync(arguments[1]);
                var fileName = stored?.FileName ?? D2SharedStashGold.SoftcoreFileName;
                var stashData = stored is null
                    ? D2SharedStashGold.CreateModernSoftcore(
                        D2SharedStashGold.MaximumGoldPerTab)
                    : D2SharedStashGold.RefillMaterialStacks(stored.Data);
                await administration.ImportSharedStashAsync(
                    arguments[1],
                    fileName,
                    stashData);
                Console.WriteLine(
                    $"Refilled {D2SharedStashGold.InitialAdvancedStashItemCodes.Count} advanced stash stacks " +
                    $"to {D2SharedStashGold.MaximumAdvancedStackSize} for '{arguments[1]}'.");
                return 0;
            }

        case "reset-shared-stash" when arguments.Length == 2:
            {
                var stashData = D2SharedStashGold.CreateModernSoftcore(
                    D2SharedStashGold.MaximumGoldPerTab);
                await administration.ImportSharedStashAsync(
                    arguments[1],
                    D2SharedStashGold.SoftcoreFileName,
                    stashData);
                Console.WriteLine(
                    $"Reset the shared stash for '{arguments[1]}': " +
                    $"5 empty normal tabs with {D2SharedStashGold.MaximumGoldPerTab:N0} gold each, " +
                    $"and {D2SharedStashGold.InitialAdvancedStashItemCodes.Count} material stacks at " +
                    $"{D2SharedStashGold.MaximumAdvancedStackSize} each.");
                Console.WriteLine("The previous full profile was preserved in account_vault_versions.");
                return 0;
            }

        case "inspect-unique-charms" when arguments.Length == 2:
            {
                var files = await administration.ReadVaultFilesAsync(arguments[1]);
                var locations = files
                    .SelectMany(file => D2UniqueCharmInspector.Inspect(file.FileName, file.Data))
                    .ToArray();
                Console.WriteLine(
                    $"Unique charms for '{arguments[1]}': {locations.Length}");
                foreach (var location in locations)
                {
                    Console.WriteLine(
                        $"{location.ItemName} | {location.RelativePath} | {location.Container} | " +
                        $"{location.StorePage} ({location.X},{location.Y})");
                }

                return 0;
            }

        case "inspect-stash-layout" when arguments.Length == 2:
            {
                var stored = await administration.ReadSoftcoreSharedStashAsync(arguments[1]);
                if (stored is null)
                {
                    Console.WriteLine($"No softcore shared stash exists for '{arguments[1]}'.");
                    return 0;
                }

                var stash = D2StashSave.Read(stored.Data);
                Console.WriteLine(
                    $"Shared stash for '{arguments[1]}': {stored.FileName}, {stash.Count} tabs.");
                for (var index = 0; index < stash.Count; index++)
                {
                    var tab = stash[index];
                    Console.WriteLine(
                        $"[{index}] {tab.TabType} | format={tab.StashFormat} | " +
                        $"items={tab.Items.Count} | gold={tab.Gold:N0}");
                }

                return 0;
            }

        case "upgrade-character-era" when arguments.Length == 3:
            {
                var expectedFileName = arguments[2] + ".d2s";
                var files = await administration.ReadVaultFilesAsync(arguments[1]);
                var characterFile = files.SingleOrDefault(file => string.Equals(
                    file.FileName,
                    expectedFileName,
                    StringComparison.OrdinalIgnoreCase));
                if (characterFile is null)
                {
                    throw new InvalidOperationException(
                        $"Character file '{expectedFileName}' does not exist for '{arguments[1]}'.");
                }

                var editor = new D2CharacterSaveEditor();
                var previousEra = editor.ReadGameVersion(characterFile.Data);
                var updatedData = editor.UpgradeToReignOfTheWarlock(characterFile.Data);
                var updated = await administration.ReplaceActiveCharacterSaveAsync(
                    arguments[1],
                    arguments[2],
                    updatedData,
                    "character.era.upgrade");
                Console.WriteLine(
                    $"Upgraded '{updated.Name}' from {previousEra} to ReignOfTheWarlock. " +
                    $"Character revision={updated.CharacterRevision}, " +
                    $"vault revision={updated.VaultRevision}.");
                return 0;
            }

        case "rotate-device-token" when arguments.Length == 2 &&
                                               Guid.TryParse(arguments[1], out var deviceId):
            {
                var rotated = await administration.RotateDeviceTokenAsync(deviceId);
                Console.WriteLine($"DeviceId: {rotated.DeviceId}");
                Console.WriteLine($"Username: {rotated.Username}");
                Console.WriteLine($"Device:   {rotated.DisplayName}");
                Console.WriteLine($"Token:    {rotated.Token}");
                Console.WriteLine("The previous token is invalid. Store the new token now.");
                return 0;
            }

        default:
            Console.Error.WriteLine("Administration commands:");
            Console.Error.WriteLine("  provision-database <database-name> <role-name>");
            Console.Error.WriteLine("  migrate");
            Console.Error.WriteLine("  rename-account <current-username> <new-username>");
            Console.Error.WriteLine("  bootstrap-device <username> <device-name>");
            Console.Error.WriteLine("  import-character <username> <character-name> <class> <save-path>");
            Console.Error.WriteLine("  import-stash <username> <shared-stash-path>");
            Console.Error.WriteLine("  set-stash-gold <username> [gold-per-tab]");
            Console.Error.WriteLine("  refill-stash-materials <username>");
            Console.Error.WriteLine("  reset-shared-stash <username>");
            Console.Error.WriteLine("  inspect-unique-charms <username>");
            Console.Error.WriteLine("  inspect-stash-layout <username>");
            Console.Error.WriteLine("  upgrade-character-era <username> <character-name>");
            Console.Error.WriteLine("  rotate-device-token <device-id>");
            return 2;
    }
}
