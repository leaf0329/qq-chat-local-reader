using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using QQChatLocalReader.Application.Sync;
using QQChatLocalReader.Core.Models;
using QQChatLocalReader.Infrastructure.Security;

namespace QQChatLocalReader.App;

internal sealed class McpSyncRequestAuthorizer(
    McpAuthorizationProfileStore store,
    McpAuthorizationProfile? profile) : ISyncRequestAuthorizer
{
    public async Task<bool> AuthorizeAsync(SyncRequest request, CancellationToken cancellationToken)
    {
        if (profile is not null && store.Read(profile.Id).IsTrusted)
        {
            return true;
        }

        var operation = System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var window = new SyncApprovalWindow(request, profile is not null);
            window.ShowDialog();
            if (window.Decision == SyncApprovalDecision.TrustAndAllow && profile is not null)
            {
                store.SetTrusted(profile.Id, true);
            }

            return window.Decision is SyncApprovalDecision.AllowOnce or SyncApprovalDecision.TrustAndAllow;
        }, DispatcherPriority.Send, cancellationToken);
        return await operation.Task.ConfigureAwait(false);
    }
}

internal enum SyncApprovalDecision
{
    Reject,
    AllowOnce,
    TrustAndAllow,
}

internal sealed class SyncApprovalWindow : Window
{
    private readonly DispatcherTimer timeout;

    public SyncApprovalWindow(SyncRequest request, bool canTrust)
    {
        Title = "QQ 聊天同步确认";
        Width = 560;
        Height = 360;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        var conversationText = string.Join("\n", request.Conversations.Select(item =>
            $"• {(item.Type == ConversationType.Group ? "群聊" : "私聊")} {item.DisplayName}（{item.Id}）"));
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock
        {
            Text = "本机 MCP 客户端请求同步以下 QQ 聊天到加密索引：",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"\n账号：{request.AccountId}\n{conversationText}\n\n时间：{request.Range.StartUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss} 至 {request.Range.EndUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}\n\n聊天内容只在本机处理。120 秒内未选择将自动拒绝。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(55, 65, 81)),
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
        buttons.Children.Add(CreateButton("拒绝", SyncApprovalDecision.Reject, false));
        buttons.Children.Add(CreateButton("仅本次允许", SyncApprovalDecision.AllowOnce, true));
        if (canTrust) buttons.Children.Add(CreateButton("信任此注册并允许", SyncApprovalDecision.TrustAndAllow, true));
        panel.Children.Add(buttons);
        Content = panel;
        timeout = new DispatcherTimer(TimeSpan.FromSeconds(120), DispatcherPriority.Normal, (_, _) => Close(), Dispatcher);
        Loaded += (_, _) => timeout.Start();
        Closed += (_, _) => timeout.Stop();
    }

    public SyncApprovalDecision Decision { get; private set; }

    private Button CreateButton(string text, SyncApprovalDecision decision, bool primary)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(8, 0, 0, 0),
            IsDefault = decision == SyncApprovalDecision.AllowOnce,
            IsCancel = decision == SyncApprovalDecision.Reject,
        };
        if (primary)
        {
            button.Background = new SolidColorBrush(Color.FromRgb(21, 101, 192));
            button.Foreground = Brushes.White;
        }

        button.Click += (_, _) => { Decision = decision; Close(); };
        return button;
    }
}
