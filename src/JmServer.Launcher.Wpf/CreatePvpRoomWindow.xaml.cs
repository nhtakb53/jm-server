using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;

namespace JmServer.Launcher.Wpf;

public partial class CreatePvpRoomWindow : Window
{
    public CreatePvpRoomWindow(string characterName, string? previousHostAddress)
    {
        InitializeComponent();
        CharacterText.Text = $"선택 캐릭터 · {characterName}";
        HostAddressTextBox.Text = string.IsNullOrWhiteSpace(previousHostAddress)
            ? FindSuggestedAddress()
            : previousHostAddress;
        UpdateButtonState();
        Loaded += (_, _) =>
        {
            HostAddressTextBox.Focus();
            HostAddressTextBox.SelectAll();
        };
    }

    public string? HostAddress { get; private set; }

    private void HostAddressTextBox_TextChanged(object sender, TextChangedEventArgs e) =>
        UpdateButtonState();

    private void UpdateButtonState()
    {
        if (CreateButton is null)
        {
            return;
        }

        CreateButton.IsEnabled = IsValidAddress(HostAddressTextBox.Text);
    }

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        var address = HostAddressTextBox.Text.Trim();
        if (!IsValidAddress(address))
        {
            return;
        }

        HostAddress = IPAddress.Parse(address).ToString();
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;

    private static bool IsValidAddress(string value) =>
        IPAddress.TryParse(value.Trim(), out var address) &&
        address.AddressFamily == AddressFamily.InterNetwork &&
        !address.Equals(IPAddress.Any) &&
        !address.Equals(IPAddress.Broadcast) &&
        !IPAddress.IsLoopback(address) &&
        address.GetAddressBytes()[0] < 224;

    private static string FindSuggestedAddress()
    {
        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up &&
                              adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses.Select(address => new
            {
                Address = address.Address,
                HasGateway = adapter.GetIPProperties().GatewayAddresses.Any(gateway =>
                    gateway.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !gateway.Address.Equals(IPAddress.Any))
            }))
            .Where(item => item.Address.AddressFamily == AddressFamily.InterNetwork &&
                           !IPAddress.IsLoopback(item.Address) &&
                           !item.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
            .OrderByDescending(item => item.HasGateway)
            .Select(item => item.Address.ToString())
            .ToArray();
        return candidates.FirstOrDefault() ?? string.Empty;
    }
}
