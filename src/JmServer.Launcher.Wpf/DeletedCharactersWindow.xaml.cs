using System.Windows;
using System.Windows.Controls;
using JmServer.Contracts;

namespace JmServer.Launcher.Wpf;

public partial class DeletedCharactersWindow : Window
{
    public DeletedCharactersWindow(IReadOnlyList<DeletedCharacterSummary> characters)
    {
        InitializeComponent();
        DeletedCharacterList.ItemsSource = characters.Select(character => new DeletedCharacterRow(
            character.CharacterId,
            character.Name,
            character.CharacterClass,
            character.DeletedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm"))).ToArray();
        DeletedCharacterList.SelectedIndex = characters.Count > 0 ? 0 : -1;
        UpdateButtonState();
    }

    public Guid? SelectedCharacterId { get; private set; }

    public bool PurgeRequested { get; private set; }

    private void DeletedCharacterList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateButtonState();

    private void UpdateButtonState()
    {
        var selected = DeletedCharacterList.SelectedItem is DeletedCharacterRow;
        RestoreButton.IsEnabled = selected;
        PurgeButton.IsEnabled = selected;
    }

    private void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (DeletedCharacterList.SelectedItem is not DeletedCharacterRow selected)
        {
            return;
        }

        if (MessageBox.Show(
                this,
                $"{selected.Name} 캐릭터를 서버 프로필로 복구할까요?",
                "캐릭터 복구",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        SelectedCharacterId = selected.CharacterId;
        DialogResult = true;
    }

    private void PurgeButton_Click(object sender, RoutedEventArgs e)
    {
        if (DeletedCharacterList.SelectedItem is not DeletedCharacterRow selected)
        {
            return;
        }

        if (MessageBox.Show(
                this,
                $"{selected.Name} 캐릭터와 모든 이전 버전을 영구 삭제할까요? 이 작업은 복구할 수 없습니다.",
                "캐릭터 영구 삭제",
                MessageBoxButton.YesNo,
                MessageBoxImage.Stop) != MessageBoxResult.Yes)
        {
            return;
        }

        SelectedCharacterId = selected.CharacterId;
        PurgeRequested = true;
        DialogResult = true;
    }

    private sealed record DeletedCharacterRow(
        Guid CharacterId,
        string Name,
        string CharacterClass,
        string DeletedAt);
}
