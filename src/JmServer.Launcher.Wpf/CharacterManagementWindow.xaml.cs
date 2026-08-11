using System.Windows;
using JmServer.Contracts;

namespace JmServer.Launcher.Wpf;

public partial class CharacterManagementWindow : Window
{
    private readonly GetCharacterManagementResponse _snapshot;

    public CharacterManagementWindow(GetCharacterManagementResponse snapshot)
    {
        _snapshot = snapshot;
        InitializeComponent();
        CharacterSummaryText.Text =
            $"{snapshot.Character.Name} · {snapshot.Character.CharacterClass} · 서버 리비전 {snapshot.Character.Revision}";
        LevelText.Text = snapshot.Stats.Level.ToString();
        StrengthDexterityText.Text = $"{snapshot.Stats.Strength} / {snapshot.Stats.Dexterity}";
        VitalityEnergyText.Text = $"{snapshot.Stats.Vitality} / {snapshot.Stats.Energy}";
        UnspentStatsText.Text = snapshot.Stats.UnspentStatPoints.ToString();
        NameTextBox.Text = snapshot.Character.Name;
        NameTextBox.SelectAll();
    }

    public CharacterManagementAction Action { get; private set; }

    public string? NewName { get; private set; }

    private void RenameButton_Click(object sender, RoutedEventArgs e)
    {
        var name = NameTextBox.Text.Trim();
        if (string.Equals(name, _snapshot.Character.Name, StringComparison.Ordinal))
        {
            MessageBox.Show(this, "현재 이름과 같습니다.", "이름 변경");
            return;
        }

        Action = CharacterManagementAction.Rename;
        NewName = name;
        DialogResult = true;
    }

    private void ResetStatsButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                this,
                "현재 배분된 능력치를 직업 기본값으로 되돌리고 모든 포인트를 미사용 상태로 환급할까요?",
                "스탯 초기화",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        Action = CharacterManagementAction.ResetStats;
        DialogResult = true;
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                this,
                $"{_snapshot.Character.Name} 캐릭터를 서버 휴지통으로 이동할까요? 나중에 복구할 수 있습니다.",
                "캐릭터 삭제",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        Action = CharacterManagementAction.Delete;
        DialogResult = true;
    }
}

public enum CharacterManagementAction
{
    None,
    Rename,
    ResetStats,
    Delete
}
