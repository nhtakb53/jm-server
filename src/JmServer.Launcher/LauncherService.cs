using System.Security.Cryptography;
using JmServer.Contracts;
using JmServer.GameIntegration;
using JmServer.Protocol;

namespace JmServer.Launcher;

public enum LauncherProgressKind
{
    Information,
    Success,
    Warning
}

public sealed record LauncherProgress(
    string Message,
    LauncherProgressKind Kind = LauncherProgressKind.Information);

public enum PvpPlayRole
{
    Host,
    Guest
}

public sealed record LauncherAccountSnapshot(
    AuthenticateResponse Identity,
    IReadOnlyList<CharacterSummary> Characters);

public sealed record LauncherPlayResult(
    long Revision,
    int LoaderExitCode,
    string? QuarantineDirectory);

public sealed record LauncherRecoveryResult(
    bool Recovered,
    long? Revision,
    string Message,
    string? QuarantineDirectory = null);

public sealed class LauncherService
{
    public bool HasRecoveryProfile => File.Exists(ProfileLeaseFile.Path);

    public async Task ProbeAsync(
        ClientProfile profile,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection(profile);
        using var timeout = CreateTimeout(cancellationToken, TimeSpan.FromSeconds(30));
        await connection.ConnectAsync(profile.Server, profile.Port, timeout.Token);
    }

    public async Task<LauncherAccountSnapshot> GetCharactersAsync(
        ClientProfile profile,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection(profile);
        using var timeout = CreateTimeout(cancellationToken, TimeSpan.FromSeconds(30));
        await connection.ConnectAsync(profile.Server, profile.Port, timeout.Token);
        var identity = await connection.AuthenticateAsync(
            profile.DeviceId,
            profile.Token,
            timeout.Token);
        var characters = await connection.ListCharactersAsync(timeout.Token);
        return new LauncherAccountSnapshot(identity, characters.Characters);
    }

    public async Task<GetCharacterCreationPolicyResponse> GetCharacterCreationPolicyAsync(
        ClientProfile profile,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection(profile);
        using var timeout = CreateTimeout(cancellationToken, TimeSpan.FromSeconds(30));
        await connection.ConnectAsync(profile.Server, profile.Port, timeout.Token);
        _ = await connection.AuthenticateAsync(profile.DeviceId, profile.Token, timeout.Token);
        return await connection.GetCharacterCreationPolicyAsync(timeout.Token);
    }

    public async Task<CharacterSummary> CreateCharacterAsync(
        ClientProfile profile,
        string name,
        PlayableCharacterClass characterClass,
        JmServer.Contracts.CharacterCreationPreset preset,
        CancellationToken cancellationToken = default)
    {
        if (HasRecoveryProfile)
        {
            throw new InvalidOperationException(
                "복구 대기 중인 프로필이 있으면 캐릭터를 생성할 수 없습니다.");
        }

        await using var connection = CreateConnection(profile);
        using var timeout = CreateTimeout(cancellationToken, TimeSpan.FromSeconds(30));
        await connection.ConnectAsync(profile.Server, profile.Port, timeout.Token);
        _ = await connection.AuthenticateAsync(profile.DeviceId, profile.Token, timeout.Token);
        var response = await connection.CreateCharacterAsync(
            new CreateCharacterRequest(name, characterClass, preset),
            timeout.Token);
        return response.Character;
    }

    public async Task<GetCharacterManagementResponse> GetCharacterManagementAsync(
        ClientProfile profile,
        Guid characterId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection(profile);
        using var timeout = CreateTimeout(cancellationToken, TimeSpan.FromSeconds(30));
        await connection.ConnectAsync(profile.Server, profile.Port, timeout.Token);
        _ = await connection.AuthenticateAsync(profile.DeviceId, profile.Token, timeout.Token);
        return await connection.GetCharacterManagementAsync(characterId, timeout.Token);
    }

    public async Task<IReadOnlyList<DeletedCharacterSummary>> GetDeletedCharactersAsync(
        ClientProfile profile,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection(profile);
        using var timeout = CreateTimeout(cancellationToken, TimeSpan.FromSeconds(30));
        await connection.ConnectAsync(profile.Server, profile.Port, timeout.Token);
        _ = await connection.AuthenticateAsync(profile.DeviceId, profile.Token, timeout.Token);
        return (await connection.ListDeletedCharactersAsync(timeout.Token)).Characters;
    }

    public async Task<CharacterSummary> RenameCharacterAsync(
        ClientProfile profile,
        Guid characterId,
        string newName,
        CancellationToken cancellationToken = default)
    {
        EnsureCharacterManagementAvailable();
        await using var connection = CreateConnection(profile);
        using var timeout = CreateTimeout(cancellationToken, TimeSpan.FromSeconds(30));
        await connection.ConnectAsync(profile.Server, profile.Port, timeout.Token);
        _ = await connection.AuthenticateAsync(profile.DeviceId, profile.Token, timeout.Token);
        return (await connection.RenameCharacterAsync(characterId, newName, timeout.Token)).Character;
    }

    public async Task<ResetCharacterStatsResponse> ResetCharacterStatsAsync(
        ClientProfile profile,
        Guid characterId,
        CancellationToken cancellationToken = default)
    {
        EnsureCharacterManagementAvailable();
        await using var connection = CreateConnection(profile);
        using var timeout = CreateTimeout(cancellationToken, TimeSpan.FromSeconds(30));
        await connection.ConnectAsync(profile.Server, profile.Port, timeout.Token);
        _ = await connection.AuthenticateAsync(profile.DeviceId, profile.Token, timeout.Token);
        return await connection.ResetCharacterStatsAsync(characterId, timeout.Token);
    }

    public async Task DeleteCharacterAsync(
        ClientProfile profile,
        Guid characterId,
        CancellationToken cancellationToken = default)
    {
        EnsureCharacterManagementAvailable();
        await using var connection = CreateConnection(profile);
        using var timeout = CreateTimeout(cancellationToken, TimeSpan.FromSeconds(30));
        await connection.ConnectAsync(profile.Server, profile.Port, timeout.Token);
        _ = await connection.AuthenticateAsync(profile.DeviceId, profile.Token, timeout.Token);
        _ = await connection.DeleteCharacterAsync(characterId, timeout.Token);
    }

    public async Task<CharacterSummary> RestoreCharacterAsync(
        ClientProfile profile,
        Guid characterId,
        CancellationToken cancellationToken = default)
    {
        EnsureCharacterManagementAvailable();
        await using var connection = CreateConnection(profile);
        using var timeout = CreateTimeout(cancellationToken, TimeSpan.FromSeconds(30));
        await connection.ConnectAsync(profile.Server, profile.Port, timeout.Token);
        _ = await connection.AuthenticateAsync(profile.DeviceId, profile.Token, timeout.Token);
        return (await connection.RestoreCharacterAsync(characterId, timeout.Token)).Character;
    }

    public async Task PurgeDeletedCharacterAsync(
        ClientProfile profile,
        Guid characterId,
        CancellationToken cancellationToken = default)
    {
        EnsureCharacterManagementAvailable();
        await using var connection = CreateConnection(profile);
        using var timeout = CreateTimeout(cancellationToken, TimeSpan.FromSeconds(30));
        await connection.ConnectAsync(profile.Server, profile.Port, timeout.Token);
        _ = await connection.AuthenticateAsync(profile.DeviceId, profile.Token, timeout.Token);
        _ = await connection.PurgeDeletedCharacterAsync(characterId, timeout.Token);
    }

    public Task<LoaderInstallationResult> PrepareClientAsync(
        string loaderArchivePath,
        string gameDirectory,
        CancellationToken cancellationToken = default) =>
        D2RLoaderInstaller.InstallAsync(
            System.IO.Path.GetFullPath(loaderArchivePath),
            System.IO.Path.GetFullPath(gameDirectory),
            cancellationToken);

    public Task<LoaderVerificationResult> VerifyClientAsync(
        string gameDirectory,
        CancellationToken cancellationToken = default) =>
        D2RLoaderInstaller.VerifyInstallationAsync(
            System.IO.Path.GetFullPath(gameDirectory),
            cancellationToken);

    public async Task<IReadOnlyList<PvpRoomInfo>> GetPvpRoomsAsync(
        ClientProfile profile,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection(profile);
        using var timeout = CreateTimeout(cancellationToken, TimeSpan.FromSeconds(30));
        await connection.ConnectAsync(profile.Server, profile.Port, timeout.Token);
        _ = await connection.AuthenticateAsync(profile.DeviceId, profile.Token, timeout.Token);
        return (await connection.ListPvpRoomsAsync(timeout.Token)).Rooms;
    }

    public async Task<PvpRoomInfo> CreatePvpRoomAsync(
        ClientProfile profile,
        Guid characterId,
        string hostAddress,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection(profile);
        using var timeout = CreateTimeout(cancellationToken, TimeSpan.FromSeconds(30));
        await connection.ConnectAsync(profile.Server, profile.Port, timeout.Token);
        _ = await connection.AuthenticateAsync(profile.DeviceId, profile.Token, timeout.Token);
        return (await connection.CreatePvpRoomAsync(
            new CreatePvpRoomRequest(characterId, hostAddress),
            timeout.Token)).Room;
    }

    public async Task<PvpRoomInfo> JoinPvpRoomAsync(
        ClientProfile profile,
        Guid roomId,
        Guid characterId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection(profile);
        using var timeout = CreateTimeout(cancellationToken, TimeSpan.FromSeconds(30));
        await connection.ConnectAsync(profile.Server, profile.Port, timeout.Token);
        _ = await connection.AuthenticateAsync(profile.DeviceId, profile.Token, timeout.Token);
        return (await connection.JoinPvpRoomAsync(
            new JoinPvpRoomRequest(roomId, characterId),
            timeout.Token)).Room;
    }

    public Task<LauncherPlayResult> PlayAsync(
        ClientProfile profile,
        Guid characterId,
        string gameDirectory,
        D2RDisplaySettings displaySettings,
        IProgress<LauncherProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        PlayCoreAsync(
            profile,
            characterId,
            gameDirectory,
            displaySettings,
            null,
            progress,
            cancellationToken);

    public async Task<LauncherPlayResult> PlayPvpAsync(
        ClientProfile profile,
        Guid characterId,
        string gameDirectory,
        D2RDisplaySettings displaySettings,
        PvpRoomInfo room,
        PvpPlayRole role,
        IProgress<LauncherProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        D2RTcpIpRelay? relay = null;
        try
        {
            relay = role switch
            {
                PvpPlayRole.Host => D2RTcpIpRelay.StartHost(
                    room.HostPort,
                    warning => Report(progress, warning, LauncherProgressKind.Warning)),
                PvpPlayRole.Guest => D2RTcpIpRelay.StartGuest(
                    room.HostAddress,
                    room.HostPort,
                    warning => Report(progress, warning, LauncherProgressKind.Warning)),
                _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
            };

            Report(
                progress,
                role == PvpPlayRole.Host
                    ? $"게임 포트 중계 시작 · TCP {room.HostPort} → 로컬 TCP {D2RTcpIpRelay.LocalGamePort}"
                    : $"게임 포트 중계 시작 · 로컬 TCP {D2RTcpIpRelay.LocalGamePort} → {room.HostAddress}:{room.HostPort}",
                LauncherProgressKind.Success);

            return await PlayCoreAsync(
                profile,
                characterId,
                gameDirectory,
                displaySettings,
                room.RoomId,
                progress,
                cancellationToken);
        }
        finally
        {
            if (relay is not null)
            {
                await relay.DisposeAsync();
            }

            await TryLeavePvpRoomAsync(profile, room.RoomId, progress);
        }
    }

    private async Task<LauncherPlayResult> PlayCoreAsync(
        ClientProfile profile,
        Guid characterId,
        string gameDirectory,
        D2RDisplaySettings displaySettings,
        Guid? pvpRoomId,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken)
    {
        gameDirectory = System.IO.Path.GetFullPath(gameDirectory);
        ArgumentNullException.ThrowIfNull(displaySettings);
        displaySettings.Validate();
        if (File.Exists(ProfileLeaseFile.Path))
        {
            throw new InvalidOperationException(
                "이전에 체크아웃한 프로필이 남아 있습니다. 먼저 복구 체크인을 실행하세요.");
        }

        using (var verificationTimeout = CreateTimeout(
                   cancellationToken,
                   TimeSpan.FromSeconds(30)))
        {
            Report(progress, "인게임 모드 파일을 최신 상태로 맞추는 중입니다.");
            var synchronization = await D2RLoaderInstaller.SynchronizeSupplyModAsync(
                gameDirectory,
                verificationTimeout.Token);
            if (!synchronization.IsValid)
            {
                throw new InvalidDataException(synchronization.Message);
            }

            var installation = await VerifyClientAsync(gameDirectory, verificationTimeout.Token);
            if (!installation.IsValid)
            {
                throw new InvalidDataException(installation.Message);
            }

            Report(progress, installation.Message, LauncherProgressKind.Success);
        }

        D2RLoaderInstaller.EnsurePrivateSessionProcessesAreStopped();
        await using var connection = CreateConnection(profile);
        ProfileLeaseFile lease;

        using (var connectTimeout = CreateTimeout(cancellationToken, TimeSpan.FromSeconds(30)))
        {
            Report(progress, "정만서버에 연결하고 캐릭터 프로필을 가져오는 중입니다.");
            await connection.ConnectAsync(profile.Server, profile.Port, connectTimeout.Token);
            var identity = await connection.AuthenticateAsync(
                profile.DeviceId,
                profile.Token,
                connectTimeout.Token);
            var settingsDownload = await DeviceSettingsSynchronizer.DownloadAsync(
                connection,
                installLocally: !D2RModSettings.HasLocalSettings(),
                cancellationToken: connectTimeout.Token);
            Report(
                progress,
                settingsDownload.Downloaded
                    ? $"개인 설정을 서버에서 불러왔습니다. (리비전 {settingsDownload.Revision})"
                    : settingsDownload.ServerCopyExists
                        ? "현재 PC의 개인 설정을 유지합니다. 게임 종료 후 서버 저장본도 갱신됩니다."
                        : "서버에 저장된 개인 설정이 없어 현재 PC 설정을 사용합니다.",
                settingsDownload.Downloaded
                    ? LauncherProgressKind.Success
                    : LauncherProgressKind.Information);
            var characters = await connection.ListCharactersAsync(connectTimeout.Token);
            var summary = characters.Characters.SingleOrDefault(
                              character => character.CharacterId == characterId)
                          ?? throw new InvalidDataException(
                              $"캐릭터 '{characterId}'를 현재 계정에서 찾을 수 없습니다.");

            var checkout = await connection.CheckoutProfileAsync(
                characterId,
                connectTimeout.Token);
            ValidateProfilePayload(checkout);
            var installed = await ProfileDirectoryManager.InstallCheckoutAsync(
                checkout.Binary,
                cancellationToken: connectTimeout.Token);
            var registeredOnServer = characters.Characters
                .Select(character => character.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!registeredOnServer.SetEquals(installed.RegisteredCharacterNames))
            {
                throw new InvalidDataException(
                    "서버 프로필 파일과 등록된 캐릭터 목록이 일치하지 않습니다.");
            }

            var localSavePath = D2RClientLayout.GetCharacterSavePath(summary.Name);
            var metadata = D2SaveMetadataReader.Read(
                await File.ReadAllBytesAsync(localSavePath, connectTimeout.Token));
            if (!string.Equals(metadata.Name, summary.Name, StringComparison.Ordinal) ||
                !string.Equals(
                    metadata.CharacterClass,
                    summary.CharacterClass,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "서버 캐릭터 정보와 D2 세이브 데이터가 일치하지 않습니다.");
            }

            lease = new ProfileLeaseFile(
                profile.Server,
                profile.Port,
                profile.UseTls,
                profile.CertificateSha256,
                characterId,
                checkout.Metadata.LeaseId,
                checkout.Metadata.Revision,
                checkout.Metadata.LeaseExpiresAt,
                installed.RegisteredCharacterNames);
            await lease.SaveAsync(connectTimeout.Token);

            Report(
                progress,
                $"{identity.Username} 계정의 {metadata.Name} 프로필을 가져왔습니다.",
                LauncherProgressKind.Success);
            if (installed.QuarantineDirectory is not null)
            {
                Report(
                    progress,
                    $"기존 로컬 세이브를 격리했습니다: {installed.QuarantineDirectory}",
                    LauncherProgressKind.Warning);
            }
        }

        try
        {
            Report(
                progress,
                displaySettings.WindowMode == D2RWindowMode.Windowed
                    ? $"화면 설정 적용 · 창 모드 {displaySettings.WindowResolution}"
                    : "화면 설정 적용 · 전체 화면(Windows 바탕화면 해상도)",
                LauncherProgressKind.Success);
            Report(progress, "D2RLoader를 실행했습니다. 게임에서는 TCP/IP만 사용하세요.");
            var exitCode = await D2RGameRunner.RunAsync(
                gameDirectory,
                async heartbeatCancellation =>
                {
                    using var heartbeatTimeout = CreateTimeout(
                        heartbeatCancellation,
                        TimeSpan.FromSeconds(20));
                    var renewal = await connection.RenewProfileLeaseAsync(
                        lease.LeaseId,
                        heartbeatTimeout.Token);
                    if (pvpRoomId is { } roomId)
                    {
                        _ = await connection.RenewPvpRoomAsync(
                            roomId,
                            heartbeatTimeout.Token);
                    }

                    lease = lease with { LeaseExpiresAt = renewal.LeaseExpiresAt };
                    await lease.SaveAsync(heartbeatTimeout.Token);
                    Report(progress, $"프로필 임대를 {renewal.LeaseExpiresAt.LocalDateTime:t}까지 갱신했습니다.");
                },
                warning => Report(progress, warning, LauncherProgressKind.Warning),
                cancellationToken,
                displaySettings);

            if (exitCode != 0)
            {
                Report(
                    progress,
                    $"D2RLoader가 코드 {exitCode}로 종료됐지만 프로필 저장을 계속합니다.",
                    LauncherProgressKind.Warning);
            }

            Report(progress, "게임 프로필을 정만서버에 저장하는 중입니다.");
            var collected = ProfileDirectoryManager.CollectForCheckin(
                lease.RegisteredCharacterNames);
            if (collected.QuarantineDirectory is not null)
            {
                Report(
                    progress,
                    $"등록되지 않은 캐릭터를 격리했습니다: {collected.QuarantineDirectory}",
                    LauncherProgressKind.Warning);
            }

            using var checkinTimeout = CreateTimeout(
                cancellationToken,
                TimeSpan.FromSeconds(30));
            var checkin = await CheckinProfileAsync(
                connection,
                lease,
                collected.BundleData,
                checkinTimeout.Token);

            File.Delete(ProfileLeaseFile.Path);
            ProfileDirectoryManager.RemoveCheckedInProfile();
            await TryUploadDeviceSettingsAsync(
                connection,
                progress,
                CancellationToken.None);
            Report(
                progress,
                $"프로필 리비전 {checkin.Revision} 저장을 완료했습니다.",
                LauncherProgressKind.Success);
            return new LauncherPlayResult(
                checkin.Revision,
                exitCode,
                collected.QuarantineDirectory);
        }
        catch
        {
            if (File.Exists(ProfileLeaseFile.Path))
            {
                Report(
                    progress,
                    "로컬 프로필을 복구용으로 보존했습니다. 다시 플레이하기 전에 복구 체크인을 실행하세요.",
                    LauncherProgressKind.Warning);
            }

            throw;
        }
    }

    private static async Task TryLeavePvpRoomAsync(
        ClientProfile profile,
        Guid roomId,
        IProgress<LauncherProgress>? progress)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var connection = CreateConnection(profile);
            await connection.ConnectAsync(profile.Server, profile.Port, timeout.Token);
            _ = await connection.AuthenticateAsync(
                profile.DeviceId,
                profile.Token,
                timeout.Token);
            _ = await connection.LeavePvpRoomAsync(roomId, timeout.Token);
            Report(progress, "PK 방 참가 상태를 정리했습니다.", LauncherProgressKind.Success);
        }
        catch (RemoteServerException exception)
            when (exception.Error.Code == ErrorCode.PvpRoomNotFound)
        {
            // The room already expired or the host closed it.
        }
        catch (Exception exception)
        {
            Report(
                progress,
                "PK 방 상태 정리에 실패했습니다. 방은 자동 만료됩니다. " + exception.Message,
                LauncherProgressKind.Warning);
        }
    }

    public async Task<LauncherRecoveryResult> RecoverProfileAsync(
        ClientProfile profile,
        IProgress<LauncherProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(ProfileLeaseFile.Path))
        {
            return new LauncherRecoveryResult(
                false,
                null,
                "복구할 로컬 프로필이 없습니다.");
        }

        D2RLoaderInstaller.EnsurePrivateSessionProcessesAreStopped();
        await D2RSettingsSession.RecoverInterruptedAsync(
            cancellationToken: cancellationToken);
        var lease = await ProfileLeaseFile.LoadAsync(cancellationToken);
        using var timeout = CreateTimeout(cancellationToken, TimeSpan.FromSeconds(30));
        await using var connection = new LauncherConnection(
            lease.UseTls,
            lease.Server,
            lease.CertificateSha256);

        Report(progress, "보존된 프로필을 정만서버에 복구하는 중입니다.");
        await connection.ConnectAsync(lease.Server, lease.Port, timeout.Token);
        _ = await connection.AuthenticateAsync(
            profile.DeviceId,
            profile.Token,
            timeout.Token);
        var collected = ProfileDirectoryManager.CollectForCheckin(
            lease.RegisteredCharacterNames);
        CheckinProfileResponse result;
        try
        {
            result = await CheckinProfileAsync(
                connection,
                lease,
                collected.BundleData,
                timeout.Token);
        }
        catch (RemoteServerException exception)
            when (exception.Error.Code == ErrorCode.CharacterLeaseInvalid)
        {
            var quarantineDirectory = ProfileDirectoryManager.QuarantineManagedProfile(
                additionalPaths: [ProfileLeaseFile.Path]);
            var staleLeaseMessage =
                "서버 프로필이 이미 갱신되어 오래된 로컬 복구본을 체크인하지 않았습니다. " +
                "로컬 파일은 격리 백업했고 서버 상태를 유지합니다.";
            Report(progress, staleLeaseMessage, LauncherProgressKind.Warning);
            return new LauncherRecoveryResult(
                true,
                null,
                staleLeaseMessage,
                quarantineDirectory);
        }
        File.Delete(ProfileLeaseFile.Path);
        ProfileDirectoryManager.RemoveCheckedInProfile();
        await TryUploadDeviceSettingsAsync(
            connection,
            progress,
            CancellationToken.None);

        var message = $"프로필 리비전 {result.Revision} 복구를 완료했습니다.";
        Report(progress, message, LauncherProgressKind.Success);
        return new LauncherRecoveryResult(
            true,
            result.Revision,
            message,
            collected.QuarantineDirectory);
    }

    private void EnsureCharacterManagementAvailable()
    {
        if (HasRecoveryProfile)
        {
            throw new InvalidOperationException(
                "복구 대기 중인 프로필이 있으면 캐릭터를 관리할 수 없습니다.");
        }
    }

    private static LauncherConnection CreateConnection(ClientProfile profile) =>
        new(profile.UseTls, profile.Server, profile.CertificateSha256);

    private static CancellationTokenSource CreateTimeout(
        CancellationToken cancellationToken,
        TimeSpan timeout)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(timeout);
        return source;
    }

    private static void ValidateProfilePayload(
        BinaryPayload<CheckoutProfileResponse> result)
    {
        if (result.Metadata.BundleLength != result.Binary.Length)
        {
            throw new InvalidDataException("서버가 잘못된 길이의 프로필을 반환했습니다.");
        }

        var actualHash = Convert.ToHexString(SHA256.HashData(result.Binary));
        if (!string.Equals(
                actualHash,
                result.Metadata.Sha256Hex,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("서버 프로필의 체크섬이 일치하지 않습니다.");
        }
    }

    private static async Task<CheckinProfileResponse> CheckinProfileAsync(
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

    private static async Task TryUploadDeviceSettingsAsync(
        LauncherConnection connection,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CreateTimeout(cancellationToken, TimeSpan.FromSeconds(15));
            var upload = await DeviceSettingsSynchronizer.UploadAsync(
                connection,
                timeout.Token);
            if (upload.Uploaded)
            {
                Report(
                    progress,
                    $"개인 설정을 서버에 저장했습니다. (리비전 {upload.Revision})",
                    LauncherProgressKind.Success);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Report(
                progress,
                "개인 설정 서버 저장 시간이 초과되었습니다. 캐릭터 저장은 계속 진행합니다.",
                LauncherProgressKind.Warning);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Report(
                progress,
                "개인 설정을 서버에 저장하지 못했습니다. 캐릭터 저장은 계속 진행합니다. " +
                exception.Message,
                LauncherProgressKind.Warning);
        }
    }

    private static void Report(
        IProgress<LauncherProgress>? progress,
        string message,
        LauncherProgressKind kind = LauncherProgressKind.Information) =>
        progress?.Report(new LauncherProgress(message, kind));
}
