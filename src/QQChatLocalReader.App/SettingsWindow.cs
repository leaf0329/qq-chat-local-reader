using System.Windows;
using System.Windows.Controls;
using QQChatLocalReader.Infrastructure.Security;

namespace QQChatLocalReader.App;

internal sealed class SettingsWindow : Window
{
    private readonly CheckBox updateCheckBox;
    private readonly ListBox profileList;
    private readonly McpAuthorizationProfileStore profileStore = McpAuthorizationProfileStore.OpenDefault();

    public SettingsWindow(Action clearIndex)
    {
        ArgumentNullException.ThrowIfNull(clearIndex);
        Title = "设置";
        Width = 560;
        Height = 490;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(20) };
        updateCheckBox = new CheckBox
        {
            Content = "启动时检查 GitHub Release 新版本（不发送聊天内容、QQ 账号或设备标识）",
            IsChecked = ApplicationPreferencesStore.Read().CheckForUpdates,
            Margin = new Thickness(0, 0, 0, 20),
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var save = new Button { Content = "保存", Padding = new Thickness(18, 8, 18, 8), IsDefault = true };
        save.Click += (_, _) => { ApplicationPreferencesStore.Write(new ApplicationPreferences(updateCheckBox.IsChecked == true)); DialogResult = true; };
        buttons.Children.Add(save);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        var content = new StackPanel();
        content.Children.Add(updateCheckBox);
        content.Children.Add(new TextBlock { Text = "MCP 注册信任", FontWeight = FontWeights.SemiBold, FontSize = 15 });
        content.Children.Add(new TextBlock { Text = "每项独立管理。撤销后，下次 AI 发起同步会重新弹出本地确认。", Margin = new Thickness(0, 4, 0, 8), TextWrapping = TextWrapping.Wrap });
        profileList = new ListBox { Height = 220, DisplayMemberPath = nameof(ProfileRow.Label) };
        content.Children.Add(profileList);
        var revoke = new Button { Content = "撤销所选项的信任", Padding = new Thickness(12, 7, 12, 7), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        revoke.Click += (_, _) => RevokeSelected();
        content.Children.Add(revoke);
        var clear = new Button
        {
            Content = "清除本地聊天索引",
            Padding = new Thickness(12, 7, 12, 7),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 20, 0, 0),
        };
        clear.Click += (_, _) => clearIndex();
        content.Children.Add(clear);
        root.Children.Add(content);
        Content = root;
        ReloadProfiles();
    }

    private void RevokeSelected()
    {
        if (profileList.SelectedItem is not ProfileRow row) return;
        profileStore.SetTrusted(row.Id, false);
        ReloadProfiles();
    }

    private void ReloadProfiles()
    {
        profileList.ItemsSource = profileStore.List()
            .Select(item => new ProfileRow(item.Id, $"{item.DisplayName} · {(item.IsTrusted ? "已信任" : "每次确认")} · {item.Id:D}"))
            .ToArray();
    }

    private sealed record ProfileRow(Guid Id, string Label);
}
