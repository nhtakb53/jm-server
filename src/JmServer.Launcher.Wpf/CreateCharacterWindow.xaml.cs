using System.Windows;
using System.Windows.Controls;
using JmServer.Contracts;

namespace JmServer.Launcher.Wpf;

public partial class CreateCharacterWindow : Window
{
    private readonly GetCharacterCreationPolicyResponse _policy;
    private readonly CharacterPresetOption _preset;

    public CreateCharacterWindow(
        GetCharacterCreationPolicyResponse policy,
        int currentCharacterCount)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _policy = policy;
        _preset = policy.Presets.SingleOrDefault()
                  ?? throw new ArgumentException("서버의 캐릭터 생성 정책이 올바르지 않습니다.", nameof(policy));
        InitializeComponent();
        CapacityText.Text =
            $"현재 {currentCharacterCount}명 · 계정당 최대 {policy.MaxCharactersPerAccount}명";
        NameRuleText.Text =
            $"글자로 시작하는 {policy.MinimumNameLength}~{policy.MaximumNameLength}자 · 글자, 숫자, '-' 또는 '_' 사용 가능";
        ClassComboBox.ItemsSource = policy.Classes;
        ClassComboBox.SelectedIndex = 0;
        PresetDescriptionText.Text = _preset.Description;
        PresetPointsText.Text =
            $"레벨 {_preset.Level} · 미사용 스탯 {_preset.UnspentStatPoints} · 미사용 스킬 {_preset.UnspentSkillPoints}";
        UpdateSelectionState();
        Loaded += (_, _) => NameTextBox.Focus();
    }

    public CharacterCreationSelection? Selection { get; private set; }

    private void SelectionChanged(object sender, RoutedEventArgs e) =>
        UpdateSelectionState();

    private void UpdateSelectionState()
    {
        if (ClassDescriptionText is null || PresetDescriptionText is null)
        {
            return;
        }

        var selectedClass = ClassComboBox.SelectedItem as CharacterClassOption;
        ClassDescriptionText.Text = selectedClass?.RequiresWarlockExpansion == true
            ? "Reign of the Warlock DLC가 설치된 PC에서만 플레이할 수 있습니다."
            : "Diablo II: Resurrected 확장 캐릭터로 생성됩니다.";
        var nameLength = NameTextBox.Text.Trim().EnumerateRunes().Count();
        CreateButton.IsEnabled = selectedClass is not null &&
                                 nameLength >= _policy.MinimumNameLength &&
                                 nameLength <= _policy.MaximumNameLength;
    }

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        if (ClassComboBox.SelectedItem is not CharacterClassOption selectedClass)
        {
            return;
        }

        var name = NameTextBox.Text.Trim();
        if (MessageBox.Show(
                this,
                $"{name} / {selectedClass.DisplayName} / {_preset.DisplayName}\n\n서버 전용 캐릭터로 생성할까요?",
                "캐릭터 생성 확인",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        Selection = new CharacterCreationSelection(
            name,
            selectedClass.CharacterClass,
            _preset.Preset);
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;
}

public sealed record CharacterCreationSelection(
    string Name,
    PlayableCharacterClass CharacterClass,
    CharacterCreationPreset Preset);
