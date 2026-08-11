using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using JmServer.Contracts;
using JmServer.GameIntegration;
using JmServer.Launcher;
using Microsoft.Win32;

namespace JmServer.Launcher.Wpf;

public partial class MainWindow : Window
{
    private static readonly DisplayModeOption[] DisplayModeOptions =
    [
        new(D2RWindowMode.Fullscreen, "전체 화면 (모니터 해상도)"),
        new(D2RWindowMode.Windowed, "창 모드")
    ];

    private static readonly ResolutionOption[] ResolutionOptions =
    [
        new(1280, 720, "1280 × 720 (HD)"),
        new(1366, 768, "1366 × 768"),
        new(1600, 900, "1600 × 900"),
        new(1920, 1080, "1920 × 1080 (FHD)"),
        new(1920, 1200, "1920 × 1200 (16:10)"),
        new(2560, 1080, "2560 × 1080 (울트라와이드)"),
        new(2560, 1440, "2560 × 1440 (QHD)"),
        new(2560, 1600, "2560 × 1600 (16:10)"),
        new(3440, 1440, "3440 × 1440 (울트라와이드)"),
        new(3840, 2160, "3840 × 2160 (4K UHD)")
    ];

    private readonly LauncherService _launcher = new();
    private readonly ObservableCollection<CharacterRow> _characters = [];
    private readonly ObservableCollection<PvpRoomRow> _pvpRooms = [];
    private CancellationTokenSource? _activeOperation;
    private string? _pvpHostAddress;
    private bool _isBusy;
    private bool _allowClose;
    private bool _isInitialized;

    public MainWindow()
    {
        InitializeComponent();
        CharacterList.ItemsSource = _characters;
        PvpCharacterComboBox.ItemsSource = _characters;
        PvpRoomList.ItemsSource = _pvpRooms;
        WindowModeComboBox.ItemsSource = DisplayModeOptions;
        ResolutionComboBox.ItemsSource = ResolutionOptions;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        UpdateRecoveryState();
        UpdateButtonState();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        try
        {
            await LoadSettingsAsync();
            if (HasUsableProfileFields())
            {
                await RunBusyAsync("서버 상태 확인 중", RefreshCharactersCoreAsync);
            }
        }
        catch (Exception exception)
        {
            HandleError("초기 설정을 불러오지 못했습니다.", exception);
        }
    }

    private async Task LoadSettingsAsync()
    {
        var profile = await ClientProfileStore.TryLoadAsync();
        if (profile is not null)
        {
            ServerTextBox.Text = profile.Server;
            PortTextBox.Text = profile.Port.ToString();
            UseTlsCheckBox.IsChecked = profile.UseTls;
            CertificateTextBox.Text = profile.CertificateSha256 ?? string.Empty;
            DeviceIdTextBox.Text = profile.DeviceId.ToString();
            TokenPasswordBox.Password = profile.Token;
            EndpointText.Text = $"{profile.Server}:{profile.Port} · TLS";
        }
        else
        {
            ServerTextBox.Text = "192.168.0.10";
            PortTextBox.Text = "15570";
            MainTabs.SelectedIndex = 2;
        }

        var preferences = await LauncherPreferencesStore.TryLoadAsync();
        GameDirectoryTextBox.Text = preferences?.GameDirectory ?? FindDefaultGameDirectory();
        LoaderArchiveTextBox.Text = preferences?.LoaderArchivePath ?? FindDefaultLoaderArchive();
        _pvpHostAddress = preferences?.PvpHostAddress;
        var windowMode = preferences?.WindowMode ?? D2RWindowMode.Fullscreen;
        WindowModeComboBox.SelectedItem = DisplayModeOptions.Single(
            option => option.Mode == windowMode);
        var windowWidth = preferences?.WindowWidth ?? 1920;
        var windowHeight = preferences?.WindowHeight ?? 1080;
        ResolutionComboBox.SelectedItem = ResolutionOptions.FirstOrDefault(
                                              option => option.Width == windowWidth &&
                                                        option.Height == windowHeight)
                                          ?? ResolutionOptions.Single(
                                              option => option.Width == 1920 &&
                                                        option.Height == 1080);
        UpdateDisplayModeState();
        SettingsStatusText.Text = profile is null
            ? "서버에서 발급한 장치 ID와 토큰을 입력하세요."
            : $"암호화된 연결 설정을 불러왔습니다: {ClientProfileStore.ProfilePath}";
        UpdateRecoveryState();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) =>
        await RunBusyAsync("캐릭터 목록 새로고침 중", RefreshCharactersCoreAsync);

    private async void CreateCharacterButton_Click(object sender, RoutedEventArgs e)
    {
        if (_launcher.HasRecoveryProfile)
        {
            MessageBox.Show(
                this,
                "복구 대기 중인 프로필이 있습니다. 먼저 복구 체크인을 실행하세요.",
                "캐릭터 생성",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        GetCharacterCreationPolicyResponse? policy = null;
        await RunBusyAsync(
            "캐릭터 생성 정책 확인 중",
            async cancellationToken =>
            {
                policy = await _launcher.GetCharacterCreationPolicyAsync(
                    ReadProfileFromForm(),
                    cancellationToken);
            });
        if (policy is null)
        {
            return;
        }

        var dialog = new CreateCharacterWindow(policy, _characters.Count)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true || dialog.Selection is null)
        {
            return;
        }

        var selection = dialog.Selection;
        CharacterSummary? created = null;
        await RunBusyAsync(
            $"{selection.Name} 생성 중",
            async cancellationToken =>
            {
                created = await _launcher.CreateCharacterAsync(
                    ReadProfileFromForm(),
                    selection.Name,
                    selection.CharacterClass,
                    selection.Preset,
                    cancellationToken);
                AppendLog(
                    $"서버 전용 캐릭터 생성 완료 · {created.Name} / {created.CharacterClass}",
                    LauncherProgressKind.Success);
                await RefreshCharactersCoreAsync(cancellationToken);
                CharacterList.SelectedItem = _characters.FirstOrDefault(
                    character => character.CharacterId == created.CharacterId);
            });
    }

    private async Task RefreshCharactersCoreAsync(CancellationToken cancellationToken)
    {
        var profile = ReadProfileFromForm();
        var snapshot = await _launcher.GetCharactersAsync(profile, cancellationToken);
        _characters.Clear();
        foreach (var character in snapshot.Characters)
        {
            _characters.Add(new CharacterRow(character));
        }

        var firstAvailable = _characters.FirstOrDefault(character => character.IsAvailable);
        CharacterList.SelectedItem = firstAvailable;
        PvpCharacterComboBox.SelectedItem = firstAvailable;
        await RefreshPvpRoomsCoreAsync(profile, cancellationToken);

        AccountText.Text = snapshot.Identity.Username;
        EndpointText.Text = $"{profile.Server}:{profile.Port} · TLS";
        ServerStatusText.Text = $"연결됨 · {snapshot.Identity.Username}";
        ServerStatusText.Foreground = (System.Windows.Media.Brush)FindResource("SuccessBrush");
        AppendLog($"서버 연결 성공 · 캐릭터 {snapshot.Characters.Count}개", LauncherProgressKind.Success);
        UpdateRecoveryState();
    }

    private async void ManageCharacterButton_Click(object sender, RoutedEventArgs e)
    {
        if (CharacterList.SelectedItem is not CharacterRow selected)
        {
            MessageBox.Show(this, "관리할 캐릭터를 선택하세요.", "캐릭터 관리");
            return;
        }

        GetCharacterManagementResponse? snapshot = null;
        await RunBusyAsync(
            $"{selected.Name} 관리 정보 확인 중",
            async cancellationToken =>
            {
                snapshot = await _launcher.GetCharacterManagementAsync(
                    ReadProfileFromForm(),
                    selected.CharacterId,
                    cancellationToken);
            });
        if (snapshot is null)
        {
            return;
        }

        var dialog = new CharacterManagementWindow(snapshot) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        switch (dialog.Action)
        {
            case CharacterManagementAction.Rename when dialog.NewName is not null:
                await RunBusyAsync(
                    $"{selected.Name} 이름 변경 중",
                    async cancellationToken =>
                    {
                        var renamed = await _launcher.RenameCharacterAsync(
                            ReadProfileFromForm(),
                            selected.CharacterId,
                            dialog.NewName,
                            cancellationToken);
                        AppendLog(
                            $"캐릭터 이름 변경 완료 · {selected.Name} → {renamed.Name}",
                            LauncherProgressKind.Success);
                        await RefreshCharactersCoreAsync(cancellationToken);
                        CharacterList.SelectedItem = _characters.FirstOrDefault(
                            character => character.CharacterId == renamed.CharacterId);
                    });
                break;
            case CharacterManagementAction.ResetStats:
                await RunBusyAsync(
                    $"{selected.Name} 스탯 초기화 중",
                    async cancellationToken =>
                    {
                        var reset = await _launcher.ResetCharacterStatsAsync(
                            ReadProfileFromForm(),
                            selected.CharacterId,
                            cancellationToken);
                        AppendLog(
                            $"스탯 초기화 완료 · 미사용 포인트 {reset.Stats.UnspentStatPoints}",
                            LauncherProgressKind.Success);
                        await RefreshCharactersCoreAsync(cancellationToken);
                        CharacterList.SelectedItem = _characters.FirstOrDefault(
                            character => character.CharacterId == selected.CharacterId);
                    });
                break;
            case CharacterManagementAction.Delete:
                await RunBusyAsync(
                    $"{selected.Name} 휴지통 이동 중",
                    async cancellationToken =>
                    {
                        await _launcher.DeleteCharacterAsync(
                            ReadProfileFromForm(),
                            selected.CharacterId,
                            cancellationToken);
                        AppendLog(
                            $"캐릭터 휴지통 이동 완료 · {selected.Name}",
                            LauncherProgressKind.Success);
                        await RefreshCharactersCoreAsync(cancellationToken);
                    });
                break;
        }
    }

    private async void TrashButton_Click(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<DeletedCharacterSummary>? deleted = null;
        await RunBusyAsync(
            "캐릭터 휴지통 확인 중",
            async cancellationToken =>
            {
                deleted = await _launcher.GetDeletedCharactersAsync(
                    ReadProfileFromForm(),
                    cancellationToken);
            });
        if (deleted is null)
        {
            return;
        }

        var dialog = new DeletedCharactersWindow(deleted) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedCharacterId is not { } characterId)
        {
            return;
        }

        if (dialog.PurgeRequested)
        {
            await RunBusyAsync(
                "삭제 캐릭터 영구 삭제 중",
                async cancellationToken =>
                {
                    await _launcher.PurgeDeletedCharacterAsync(
                        ReadProfileFromForm(),
                        characterId,
                        cancellationToken);
                    AppendLog("삭제 캐릭터를 영구 삭제했습니다.", LauncherProgressKind.Success);
                    await RefreshCharactersCoreAsync(cancellationToken);
                });
        }
        else
        {
            await RunBusyAsync(
                "삭제 캐릭터 복구 중",
                async cancellationToken =>
                {
                    var restored = await _launcher.RestoreCharacterAsync(
                        ReadProfileFromForm(),
                        characterId,
                        cancellationToken);
                    AppendLog(
                        $"캐릭터 복구 완료 · {restored.Name}",
                        LauncherProgressKind.Success);
                    await RefreshCharactersCoreAsync(cancellationToken);
                    CharacterList.SelectedItem = _characters.FirstOrDefault(
                        character => character.CharacterId == restored.CharacterId);
                });
        }
    }

    private async Task RefreshPvpRoomsCoreAsync(
        ClientProfile profile,
        CancellationToken cancellationToken)
    {
        var rooms = await _launcher.GetPvpRoomsAsync(profile, cancellationToken);
        _pvpRooms.Clear();
        foreach (var room in rooms)
        {
            _pvpRooms.Add(new PvpRoomRow(room));
        }

        PvpRoomList.SelectedItem = _pvpRooms.FirstOrDefault(room => room.CanJoin)
                                   ?? _pvpRooms.FirstOrDefault();
    }

    private async void RefreshPvpRoomsButton_Click(object sender, RoutedEventArgs e) =>
        await RunBusyAsync(
            "PK 방 목록 새로고침 중",
            async cancellationToken =>
            {
                await RefreshPvpRoomsCoreAsync(ReadProfileFromForm(), cancellationToken);
                AppendLog($"PK 방 {_pvpRooms.Count}개를 확인했습니다.", LauncherProgressKind.Success);
            });

    private async void TestConnectionButton_Click(object sender, RoutedEventArgs e) =>
        await RunBusyAsync(
            "서버 연결 시험 중",
            async cancellationToken =>
            {
                await RefreshCharactersCoreAsync(cancellationToken);
                SettingsStatusText.Text = "서버 인증과 캐릭터 조회에 성공했습니다.";
            });

    private async void SaveSettingsButton_Click(object sender, RoutedEventArgs e) =>
        await RunBusyAsync(
            "설정 저장 중",
            async cancellationToken =>
            {
                var profile = ReadProfileFromForm();
                var preferences = ReadPreferencesFromForm();
                await ClientProfileStore.SaveAsync(profile, cancellationToken);
                await LauncherPreferencesStore.SaveAsync(preferences, cancellationToken);
                EndpointText.Text = $"{profile.Server}:{profile.Port} · TLS";
                SettingsStatusText.Text = "연결 설정과 게임 경로를 저장했습니다.";
                AppendLog("설정을 안전하게 저장했습니다.", LauncherProgressKind.Success);
            });

    private async void VerifyClientButton_Click(object sender, RoutedEventArgs e) =>
        await RunBusyAsync(
            "D2R 설치 검증 중",
            async cancellationToken =>
            {
                var gameDirectory = ReadRequiredDirectory();
                var result = await _launcher.VerifyClientAsync(gameDirectory, cancellationToken);
                ClientStatusText.Text = result.IsValid ? "설치 정상" : "설치 확인 필요";
                ClientStatusText.Foreground = (System.Windows.Media.Brush)FindResource(
                    result.IsValid ? "SuccessBrush" : "WarningBrush");
                SettingsStatusText.Text = result.Message;
                AppendLog(
                    result.Message,
                    result.IsValid
                        ? LauncherProgressKind.Success
                        : LauncherProgressKind.Warning);
                if (!result.IsValid)
                {
                    throw new InvalidDataException(result.Message);
                }
            });

    private async void PrepareClientButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                this,
                "D2R, Battle.net, Agent를 모두 종료했나요?\n검증된 D2RLoader와 게임 내 아이템 보급 모드를 설치합니다.",
                "정만서버 클라이언트 설치",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        await RunBusyAsync(
            "정만서버 클라이언트 설치 중",
            async cancellationToken =>
            {
                var gameDirectory = ReadRequiredDirectory();
                var loaderArchive = ReadRequiredLoaderArchive();
                var result = await _launcher.PrepareClientAsync(
                    loaderArchive,
                    gameDirectory,
                    cancellationToken);
                ClientStatusText.Text = "설치 정상";
                ClientStatusText.Foreground = (System.Windows.Media.Brush)FindResource("SuccessBrush");
                SettingsStatusText.Text =
                    $"D2R {result.GameVersion}, D2RLoader {result.LoaderVersion}, " +
                    $"유니크 {result.SupplyUniqueItemCount}개·세트 {result.SupplySetItemCount}개, " +
                    $"베이스 {result.BaseSelectorCount}개·재료 {result.MaterialSelectorCount}개, " +
                    $"참 선택 {result.CharmSelectorCount}개·빠른 크래프트 {result.QuickCraftRecipeCount}개, " +
                    $"작업대 {result.WorkbenchRecipeCount}개 " +
                    "게임 내 보급 모드 설치를 완료했습니다.";
                AppendLog(SettingsStatusText.Text, LauncherProgressKind.Success);
                if (result.BackupDirectory is not null)
                {
                    AppendLog($"교체 파일 백업: {result.BackupDirectory}");
                }
            });
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (CharacterList.SelectedItem is not CharacterRow selected)
        {
            MessageBox.Show(this, "플레이할 캐릭터를 선택하세요.", "정만서버");
            return;
        }

        if (_launcher.HasRecoveryProfile)
        {
            MessageBox.Show(
                this,
                "복구 프로필이 남아 있습니다. 먼저 복구 체크인을 실행하세요.",
                "정만서버",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show(
                this,
                $"{selected.Name} 캐릭터로 시작합니다.\nBattle.net, Agent, D2R, D2RLoader를 모두 종료했나요?",
                "플레이 시작",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        await RunBusyAsync(
            $"{selected.Name} 플레이 중",
            async cancellationToken =>
            {
                var profile = ReadProfileFromForm();
                var gameDirectory = ReadRequiredDirectory();
                var displaySettings = ReadDisplaySettingsFromForm();
                var progress = new Progress<LauncherProgress>(item =>
                    AppendLog(item.Message, item.Kind));
                var result = await _launcher.PlayAsync(
                    profile,
                    selected.CharacterId,
                    gameDirectory,
                    displaySettings,
                    progress,
                    cancellationToken);
                AppendLog(
                    $"플레이 종료 · 서버 리비전 {result.Revision}",
                    LauncherProgressKind.Success);
                await RefreshCharactersCoreAsync(cancellationToken);
            });
    }

    private async void CreatePvpRoomButton_Click(object sender, RoutedEventArgs e)
    {
        if (PvpCharacterComboBox.SelectedItem is not CharacterRow selected || !selected.IsAvailable)
        {
            MessageBox.Show(this, "PK에 사용할 캐릭터를 선택하세요.", "PK 방 만들기");
            return;
        }

        if (_launcher.HasRecoveryProfile)
        {
            MessageBox.Show(
                this,
                "복구 프로필이 남아 있습니다. 먼저 복구 체크인을 실행하세요.",
                "PK 방 만들기",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var dialog = new CreatePvpRoomWindow(selected.Name, _pvpHostAddress)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true || dialog.HostAddress is null)
        {
            return;
        }

        var hostAddress = dialog.HostAddress;
        _pvpHostAddress = hostAddress;
        await RunBusyAsync(
            $"{selected.Name} PK 방 호스트 중",
            async cancellationToken =>
            {
                var profile = ReadProfileFromForm();
                var gameDirectory = ReadRequiredDirectory();
                var displaySettings = ReadDisplaySettingsFromForm();
                await LauncherPreferencesStore.SaveAsync(
                    ReadPreferencesFromForm(),
                    cancellationToken);
                var room = await _launcher.CreatePvpRoomAsync(
                    profile,
                    selected.CharacterId,
                    hostAddress,
                    cancellationToken);
                AppendLog(
                    $"PK 방 {room.RoomCode} 생성 · {room.HostAddress}:{room.HostPort}",
                    LauncherProgressKind.Success);
                AppendLog("D2R에서 TCP/IP → 방 만들기를 선택하세요.");

                var progress = new Progress<LauncherProgress>(item =>
                    AppendLog(item.Message, item.Kind));
                var result = await _launcher.PlayPvpAsync(
                    profile,
                    selected.CharacterId,
                    gameDirectory,
                    displaySettings,
                    room,
                    PvpPlayRole.Host,
                    progress,
                    cancellationToken);
                AppendLog(
                    $"PK 호스트 종료 · 서버 리비전 {result.Revision}",
                    LauncherProgressKind.Success);
                await RefreshCharactersCoreAsync(cancellationToken);
            });
    }

    private async void JoinPvpRoomButton_Click(object sender, RoutedEventArgs e)
    {
        if (PvpCharacterComboBox.SelectedItem is not CharacterRow selected || !selected.IsAvailable)
        {
            MessageBox.Show(this, "PK에 사용할 캐릭터를 선택하세요.", "PK 방 참가");
            return;
        }

        if (PvpRoomList.SelectedItem is not PvpRoomRow room || !room.CanJoin)
        {
            MessageBox.Show(this, "참가 가능한 PK 방을 선택하세요.", "PK 방 참가");
            return;
        }

        if (_launcher.HasRecoveryProfile)
        {
            MessageBox.Show(
                this,
                "복구 프로필이 남아 있습니다. 먼저 복구 체크인을 실행하세요.",
                "PK 방 참가",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show(
                this,
                $"{room.HostUsername}의 방 {room.RoomCode}에 {selected.Name} 캐릭터로 참가합니다.\n" +
                $"접속 주소 {room.Endpoint}를 클립보드에 복사하고 D2RLoader를 실행할까요?",
                "PK 방 참가",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        await RunBusyAsync(
            $"{room.RoomCode} PK 방 참가 중",
            async cancellationToken =>
            {
                var profile = ReadProfileFromForm();
                var gameDirectory = ReadRequiredDirectory();
                var displaySettings = ReadDisplaySettingsFromForm();
                var joined = await _launcher.JoinPvpRoomAsync(
                    profile,
                    room.RoomId,
                    selected.CharacterId,
                    cancellationToken);
                TryCopyAddress("127.0.0.1");
                AppendLog(
                    $"PK 방 {joined.RoomCode} 참가 · {joined.HostAddress}:{joined.HostPort}",
                    LauncherProgressKind.Success);
                AppendLog("D2R에서 TCP/IP → 참가를 선택하고 127.0.0.1을 붙여넣으세요.");

                var progress = new Progress<LauncherProgress>(item =>
                    AppendLog(item.Message, item.Kind));
                var result = await _launcher.PlayPvpAsync(
                    profile,
                    selected.CharacterId,
                    gameDirectory,
                    displaySettings,
                    joined,
                    PvpPlayRole.Guest,
                    progress,
                    cancellationToken);
                AppendLog(
                    $"PK 참가 종료 · 서버 리비전 {result.Revision}",
                    LauncherProgressKind.Success);
                await RefreshCharactersCoreAsync(cancellationToken);
            });
    }

    private async void RecoverButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_launcher.HasRecoveryProfile)
        {
            UpdateRecoveryState();
            return;
        }

        if (MessageBox.Show(
                this,
                "D2R과 D2RLoader를 종료한 뒤 복구를 진행해야 합니다. 계속할까요?",
                "복구 체크인",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        await RunBusyAsync(
            "복구 체크인 중",
            async cancellationToken =>
            {
                var profile = ReadProfileFromForm();
                var progress = new Progress<LauncherProgress>(item =>
                    AppendLog(item.Message, item.Kind));
                var result = await _launcher.RecoverProfileAsync(
                    profile,
                    progress,
                    cancellationToken);
                AppendLog(result.Message, LauncherProgressKind.Success);
                UpdateRecoveryState();
                await RefreshCharactersCoreAsync(cancellationToken);
            });
    }

    private void BrowseGameButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Diablo II Resurrected 설치 폴더 선택",
            InitialDirectory = Directory.Exists(GameDirectoryTextBox.Text)
                ? GameDirectoryTextBox.Text
                : null
        };
        if (dialog.ShowDialog(this) == true)
        {
            GameDirectoryTextBox.Text = dialog.FolderName;
        }
    }

    private void WindowModeComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e) => UpdateDisplayModeState();

    private void BrowseLoaderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "D2RLoader 1.0.1-beta 원본 ZIP 선택",
            Filter = "ZIP 압축 파일 (*.zip)|*.zip|모든 파일 (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            LoaderArchiveTextBox.Text = dialog.FileName;
        }
    }

    private void CopyLootFilterButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(JmLootFilterProfile.GetJson());
            AppendLog(
                "정만서버 전리품 필터를 클립보드에 복사했습니다.",
                LauncherProgressKind.Success);
            MessageBox.Show(
                this,
                "D2R에서 옵션 → 전리품 필터를 열고 가져오기 화살표를 누른 뒤 " +
                "Ctrl+V로 붙여 넣으세요. 기본 프로필은 아이템을 숨기지 않습니다.",
                "전리품 필터 복사",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (ExternalException exception)
        {
            HandleError("전리품 필터 복사 실패", exception);
        }
    }

    private void OpenGuideButton_Click(object sender, RoutedEventArgs e)
    {
        var guidePath = Path.Combine(AppContext.BaseDirectory, "guide", "index.html");
        if (!File.Exists(guidePath))
        {
            HandleError(
                "사용 가이드 열기 실패",
                new FileNotFoundException("배포본의 웹 가이드 파일을 찾을 수 없습니다.", guidePath));
            return;
        }

        Process.Start(new ProcessStartInfo(guidePath) { UseShellExecute = true });
        AppendLog("웹 사용 가이드를 기본 브라우저로 열었습니다.", LauncherProgressKind.Success);
    }

    private void CharacterList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateButtonState();

    private void PvpRoomList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateButtonState();

    private void PvpCharacterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateButtonState();

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _activeOperation?.Cancel();
        AppendLog(
            "작업 취소를 요청했습니다. 플레이 중이었다면 복구 프로필이 남을 수 있습니다.",
            LauncherProgressKind.Warning);
    }

    private async Task RunBusyAsync(
        string description,
        Func<CancellationToken, Task> operation)
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        _activeOperation = new CancellationTokenSource();
        BusyText.Text = description;
        BusyProgress.Visibility = Visibility.Visible;
        CancelButton.Visibility = Visibility.Visible;
        UpdateButtonState();
        try
        {
            await operation(_activeOperation.Token);
        }
        catch (OperationCanceledException)
        {
            AppendLog("작업이 취소됐습니다.", LauncherProgressKind.Warning);
        }
        catch (Exception exception)
        {
            HandleError(description + " 실패", exception);
        }
        finally
        {
            _activeOperation.Dispose();
            _activeOperation = null;
            _isBusy = false;
            BusyText.Text = string.Empty;
            BusyProgress.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;
            UpdateRecoveryState();
            UpdateButtonState();
        }
    }

    private ClientProfile ReadProfileFromForm()
    {
        var server = ServerTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(server))
        {
            throw new InvalidDataException("서버 주소를 입력하세요.");
        }

        if (!int.TryParse(PortTextBox.Text, out var port) || port is < 1 or > 65535)
        {
            throw new InvalidDataException("서버 포트가 올바르지 않습니다.");
        }

        if (!Guid.TryParse(DeviceIdTextBox.Text, out var deviceId))
        {
            throw new InvalidDataException("장치 ID가 올바른 GUID가 아닙니다.");
        }

        var token = TokenPasswordBox.Password;
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidDataException("장치 토큰을 입력하세요.");
        }

        var certificate = CertificateTextBox.Text.Trim();
        try
        {
            if (Convert.FromHexString(certificate).Length != 32)
            {
                throw new InvalidDataException("인증서 SHA-256 값은 64자리여야 합니다.");
            }
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("인증서 SHA-256 값이 16진수가 아닙니다.", exception);
        }

        return new ClientProfile(server, port, true, certificate, deviceId, token);
    }

    private LauncherPreferences ReadPreferencesFromForm()
    {
        var displaySettings = ReadDisplaySettingsFromForm();
        return new LauncherPreferences(
            GameDirectoryTextBox.Text.Trim(),
            LoaderArchiveTextBox.Text.Trim(),
            _pvpHostAddress,
            displaySettings.WindowMode,
            displaySettings.WindowWidth,
            displaySettings.WindowHeight);
    }

    private D2RDisplaySettings ReadDisplaySettingsFromForm()
    {
        if (WindowModeComboBox.SelectedItem is not DisplayModeOption mode)
        {
            throw new InvalidDataException("D2R 화면 모드를 선택하세요.");
        }

        if (ResolutionComboBox.SelectedItem is not ResolutionOption resolution)
        {
            throw new InvalidDataException("D2R 창 모드 해상도를 선택하세요.");
        }

        return new D2RDisplaySettings(mode.Mode, resolution.Width, resolution.Height);
    }

    private void UpdateDisplayModeState()
    {
        if (ResolutionComboBox is null || DisplayModeHelpText is null)
        {
            return;
        }

        var isWindowed = WindowModeComboBox.SelectedItem is DisplayModeOption
        {
            Mode: D2RWindowMode.Windowed
        };
        ResolutionComboBox.IsEnabled = isWindowed;
        DisplayModeHelpText.Text = isWindowed
            ? "선택한 크기의 일반 창으로 실행합니다."
            : "전체 화면은 Windows 바탕화면 해상도를 사용합니다. 아래 창 해상도는 창 모드로 바꿀 때 사용할 값으로 저장됩니다.";
    }

    private string ReadRequiredDirectory()
    {
        var directory = GameDirectoryTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException("D2R 설치 폴더를 찾을 수 없습니다.");
        }

        return Path.GetFullPath(directory);
    }

    private string ReadRequiredLoaderArchive()
    {
        var archive = LoaderArchiveTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(archive) || !File.Exists(archive))
        {
            throw new FileNotFoundException("D2RLoader 원본 ZIP을 찾을 수 없습니다.", archive);
        }

        return Path.GetFullPath(archive);
    }

    private bool HasUsableProfileFields() =>
        !string.IsNullOrWhiteSpace(ServerTextBox.Text) &&
        !string.IsNullOrWhiteSpace(DeviceIdTextBox.Text) &&
        !string.IsNullOrWhiteSpace(TokenPasswordBox.Password) &&
        !string.IsNullOrWhiteSpace(CertificateTextBox.Text);

    private void UpdateRecoveryState()
    {
        var hasRecovery = _launcher.HasRecoveryProfile;
        RecoveryStatusText.Text = hasRecovery ? "복구 필요" : "없음";
        RecoveryStatusText.Foreground = (System.Windows.Media.Brush)FindResource(
            hasRecovery ? "WarningBrush" : "SuccessBrush");
        RecoverButton.Visibility = hasRecovery ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateButtonState()
    {
        var selected = CharacterList.SelectedItem as CharacterRow;
        var selectedPvpCharacter = PvpCharacterComboBox.SelectedItem as CharacterRow;
        RefreshButton.IsEnabled = !_isBusy && HasUsableProfileFields();
        CreateCharacterButton.IsEnabled = !_isBusy &&
                                          HasUsableProfileFields() &&
                                          !_launcher.HasRecoveryProfile;
        TrashButton.IsEnabled = !_isBusy &&
                                HasUsableProfileFields() &&
                                !_launcher.HasRecoveryProfile;
        ManageCharacterButton.IsEnabled = !_isBusy &&
                                          selected?.IsAvailable == true &&
                                          !_launcher.HasRecoveryProfile;
        PlayButton.IsEnabled = !_isBusy &&
                                selected?.IsAvailable == true &&
                                !_launcher.HasRecoveryProfile;
        PlayButton.Content = selected switch
        {
            null => "캐릭터를 선택하세요",
            { IsAvailable: false } => "현재 사용 중인 캐릭터",
            _ => $"{selected.Name} 혼자 플레이"
        };
        RefreshPvpRoomsButton.IsEnabled = !_isBusy && HasUsableProfileFields();
        CreatePvpRoomButton.IsEnabled = !_isBusy &&
                                        selectedPvpCharacter?.IsAvailable == true &&
                                        !_launcher.HasRecoveryProfile;
        JoinPvpRoomButton.IsEnabled = !_isBusy &&
                                      selectedPvpCharacter?.IsAvailable == true &&
                                      PvpRoomList.SelectedItem is PvpRoomRow { CanJoin: true } &&
                                      !_launcher.HasRecoveryProfile;
        RecoverButton.IsEnabled = !_isBusy && _launcher.HasRecoveryProfile;
        MainTabs.IsEnabled = !_isBusy;
    }

    private sealed record DisplayModeOption(D2RWindowMode Mode, string Label);

    private sealed record ResolutionOption(int Width, int Height, string Label);

    private void AppendLog(
        string message,
        LauncherProgressKind kind = LauncherProgressKind.Information)
    {
        var marker = kind switch
        {
            LauncherProgressKind.Success => "OK",
            LauncherProgressKind.Warning => "!",
            _ => "·"
        };
        LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {marker} {message}{Environment.NewLine}");
        LogTextBox.ScrollToEnd();
        if (PvpStatusText is not null)
        {
            PvpStatusText.Text = message;
            PvpStatusText.Foreground = (System.Windows.Media.Brush)FindResource(kind switch
            {
                LauncherProgressKind.Success => "SuccessBrush",
                LauncherProgressKind.Warning => "WarningBrush",
                _ => "MutedTextBrush"
            });
        }
    }

    private void HandleError(string title, Exception exception)
    {
        ServerStatusText.Text = "확인 필요";
        ServerStatusText.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
        AppendLog(exception.Message, LauncherProgressKind.Warning);
        MessageBox.Show(
            this,
            exception.Message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private void TryCopyAddress(string address)
    {
        try
        {
            Clipboard.SetText(address);
            AppendLog($"참가 주소 {address}를 클립보드에 복사했습니다.", LauncherProgressKind.Success);
        }
        catch (ExternalException exception)
        {
            AppendLog(
                $"클립보드 복사 실패 · 주소를 직접 입력하세요: {address} ({exception.Message})",
                LauncherProgressKind.Warning);
        }
    }

    private static string FindDefaultGameDirectory()
    {
        var candidate = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Diablo II Resurrected");
        return Directory.Exists(candidate) ? candidate : string.Empty;
    }

    private static string FindDefaultLoaderArchive()
    {
        var besideExecutable = Path.Combine(
            AppContext.BaseDirectory,
            "D2RLoader-1.0.1-beta.zip");
        if (File.Exists(besideExecutable))
        {
            return besideExecutable;
        }

        var downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            "D2RLoader-1.0.1-beta.zip");
        return File.Exists(downloads) ? downloads : string.Empty;
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!_isBusy || _allowClose)
        {
            return;
        }

        if (MessageBox.Show(
                this,
                "작업 중에 종료하면 복구 체크인이 필요할 수 있습니다. 작업을 취소하고 종료할까요?",
                "정만서버 종료",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }

        _allowClose = true;
        _activeOperation?.Cancel();
    }

    private sealed class CharacterRow(CharacterSummary summary)
    {
        public Guid CharacterId { get; } = summary.CharacterId;
        public string Name { get; } = summary.Name;
        public string CharacterClass { get; } = summary.CharacterClass;
        public long Revision { get; } = summary.Revision;
        public bool IsAvailable { get; } = !summary.IsLeased;
        public string Status { get; } = summary.IsLeased
            ? $"사용 중 · {summary.LeaseExpiresAt?.LocalDateTime:g}까지"
            : "플레이 가능";
    }

    private sealed class PvpRoomRow(PvpRoomInfo room)
    {
        public Guid RoomId { get; } = room.RoomId;
        public string RoomCode { get; } = room.RoomCode;
        public string HostUsername { get; } = room.HostUsername;
        public string HostCharacterName { get; } = room.HostCharacterName;
        public string GuestUsername { get; } = room.GuestUsername ?? "-";
        public string Endpoint { get; } = $"{room.HostAddress}:{room.HostPort}";
        public string ExpiresAt { get; } = room.ExpiresAt.LocalDateTime.ToString("HH:mm:ss");
        public bool CanJoin { get; } = room.CanJoin;
        public string Status { get; } = room switch
        {
            { IsHost: true, Status: PvpRoomStatus.Waiting } => "내 방 · 대기",
            { IsHost: true } => "내 방 · 준비",
            { Status: PvpRoomStatus.Waiting } => "참가 가능",
            _ => "2명 준비"
        };
    }
}
