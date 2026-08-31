using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Reflection;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using QQChatLocalReader.Application;
using QQChatLocalReader.Application.Mcp;
using QQChatLocalReader.Application.Sync;
using QQChatLocalReader.Application.Updates;
using QQChatLocalReader.Core.Models;
using QQChatLocalReader.Infrastructure.Exporting;
using QQChatLocalReader.Infrastructure.Indexing;
using QQChatLocalReader.Infrastructure.QqData;
using QQChatLocalReader.Infrastructure.Security;

namespace QQChatLocalReader.App;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };
    private readonly ApplicationRuntime runtime = ApplicationRuntime.OpenDefault();
    private readonly List<ConversationChoice> allConversations = [];
    private readonly DispatcherTimer jobTimer;
    private Guid? activeJobId;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        jobTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, PollJob, Dispatcher);
    }

    public ObservableCollection<ConversationChoice> VisibleConversations { get; } = [];

    public ObservableCollection<MessageRow> Messages { get; } = [];

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshAccountsAsync();
        await CheckForUpdatesAsync();
    }

    private void Window_Closed(object? sender, EventArgs e) => runtime.Dispose();

    private async void RefreshAccounts_Click(object sender, RoutedEventArgs e) => await RefreshAccountsAsync();

    private async Task RefreshAccountsAsync() => await RunBusyAsync("正在发现本机 QQ 账号……", async () =>
    {
        var accounts = await runtime.Catalog.ListAccountsAsync(CancellationToken.None);
        AccountBox.ItemsSource = accounts;
        AccountBox.SelectedIndex = accounts.Count == 1 ? 0 : -1;
        StatusText.Text = accounts.Count == 0 ? "未发现可用的 QQ 本地数据库。" : $"已发现 {accounts.Count} 个账号。";
    });

    private void AccountBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        allConversations.Clear();
        VisibleConversations.Clear();
        Messages.Clear();
        ContextBox.Clear();
    }

    private async void ReadConversations_Click(object sender, RoutedEventArgs e)
    {
        if (AccountBox.SelectedItem is not AccountDescriptor account)
        {
            Inform("请先选择账号。");
            return;
        }

        await RunBusyAsync("正在创建只读快照并读取会话，Windows 可能请求管理员确认……", async () =>
        {
            var conversations = await runtime.Catalog.ListConversationsAsync(account.Id, CancellationToken.None);
            allConversations.Clear();
            allConversations.AddRange(conversations.Select(item => new ConversationChoice(item)));
            ApplyConversationFilter();
            StatusText.Text = $"已读取 {conversations.Count} 个会话。请明确勾选需要操作的会话。";
        });
    }

    private void ConversationFilterBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyConversationFilter();

    private void ApplyConversationFilter()
    {
        var filter = ConversationFilterBox.Text.Trim();
        var visible = allConversations.Where(item => filter.Length == 0 ||
            item.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            item.Descriptor.Id.Contains(filter, StringComparison.Ordinal));
        VisibleConversations.Clear();
        foreach (var item in visible) VisibleConversations.Add(item);
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in VisibleConversations) item.IsSelected = true;
    }

    private void RangeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StartDatePicker is null) return;
        var custom = (RangeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "custom";
        StartDatePicker.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        EndDatePicker.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SyncSelected_Click(object sender, RoutedEventArgs e)
    {
        if (!TrySelection(out var account, out var conversations, out var range)) return;
        if (conversations.Count > 1)
        {
            Inform("当前版本按会话创建同步任务，请一次只选择一个会话。");
            return;
        }

        activeJobId = runtime.SyncJobs.Start(new SyncRequest(account.Id, conversations, range));
        jobTimer.Start();
        StatusText.Text = $"同步任务已创建：{activeJobId}。Windows 可能请求管理员确认。";
    }

    private void PollJob(object? sender, EventArgs e)
    {
        if (activeJobId is not Guid jobId) return;
        var job = runtime.SyncJobs.Get(jobId);
        StatusText.Text = job.State switch
        {
            SyncJobState.AwaitingAuthorization => "同步等待确认……",
            SyncJobState.Running => "正在同步并写入本机加密索引……",
            SyncJobState.Completed => $"同步完成，共处理 {job.MessageCount ?? 0} 条消息。",
            SyncJobState.Canceled => "同步已取消。",
            SyncJobState.Rejected => "同步未获授权。",
            _ => $"同步失败（{job.ErrorCode ?? "unknown"}）。请确认 QQ 正在运行且版本受支持。",
        };
        if (job.State is SyncJobState.Completed or SyncJobState.Canceled or SyncJobState.Rejected or SyncJobState.Failed)
        {
            jobTimer.Stop();
            activeJobId = null;
        }
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        if (!TrySelection(out var account, out var conversations, out var range)) return;
        await RunBusyAsync("正在搜索本机加密索引……", () =>
        {
            var page = runtime.Index.SearchMessages(new MessageSearchRequest(
                account.Id, conversations, range, KeywordBox.Text, SenderBox.Text, 500));
            Messages.Clear();
            foreach (var message in page.Messages) Messages.Add(new MessageRow(message));
            StatusText.Text = page.NextCursor is null
                ? $"找到 {Messages.Count} 条消息。"
                : $"已显示前 {Messages.Count} 条消息，请缩小范围或使用命令行/MCP 分页继续。";
            return Task.CompletedTask;
        });
    }

    private void MessageList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (MessageList.SelectedItem is not MessageRow selected) return;
        var context = runtime.Index.ReadContext(selected.Conversation, selected.Message.MessageId, 20, 20);
        var builder = new StringBuilder();
        for (var index = 0; index < context.Messages.Count; index++)
        {
            var item = McpMessageFormatter.Create(context.Messages[index]);
            builder.Append(index == context.AnchorIndex ? "▶ " : "  ")
                .Append(item.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture))
                .Append("  ").Append(item.SenderName ?? item.SenderId).AppendLine()
                .AppendLine(item.Text).AppendLine();
        }

        ContextBox.Text = builder.ToString();
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (!TrySelection(out _, out var conversations, out var range)) return;
        var dialog = new OpenFolderDialog { Title = "选择导出目录" };
        if (dialog.ShowDialog(this) != true) return;
        var privacy = Enum.Parse<MessageExportPrivacy>(SelectedTag(ExportPrivacyBox));
        var warning = privacy == MessageExportPrivacy.Raw
            ? "将导出原始明文聊天记录。导出文件不再受本地索引加密保护，是否继续？"
            : "将导出基础脱敏的明文聊天记录。导出文件不再受本地索引加密保护，是否继续？";
        if (MessageBox.Show(this, warning, "确认导出", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await RunBusyAsync("正在导出……", async () =>
        {
            var messages = conversations.SelectMany(item => runtime.Index.ReadMessages(item, range)).ToArray();
            var result = await MessageExporter.ExportAsync(
                messages, dialog.FolderName, Enum.Parse<MessageExportFormat>(SelectedTag(ExportFormatBox)), privacy);
            StatusText.Text = $"已导出 {result.MessageCount} 条消息：{result.FilePath}";
        });
    }

    private void CopyMcpConfig_Click(object sender, RoutedEventArgs e)
    {
        var profile = McpAuthorizationProfileStore.OpenDefault().Create("通用 MCP 配置");
        var document = new
        {
            mcpServers = new Dictionary<string, object>
            {
                ["qq-chat-local-reader"] = new { command = Environment.ProcessPath, args = new[] { "mcp", "--profile", profile.Id.ToString("D") } },
            },
        };
        Clipboard.SetText(JsonSerializer.Serialize(document, IndentedJson));
        StatusText.Text = "通用 MCP 配置已复制。配置包含本机程序路径，不包含 QQ 数据或密钥。";
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => new SettingsWindow(ClearLocalIndex) { Owner = this }.ShowDialog();

    private void ClearLocalIndex()
    {
        if (MessageBox.Show(
                this,
                "这会永久删除本机加密聊天索引、搜索数据和同步任务记录，不会修改 QQ 原始聊天记录。删除后程序将退出，是否继续？",
                "确认清除本地索引",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        jobTimer.Stop();
        runtime.Dispose();
        EncryptedMessageIndex.DeleteDefault();
        MessageBox.Show(this, "本地加密索引已清除。QQ 原始聊天记录未被修改。", "清除完成", MessageBoxButton.OK, MessageBoxImage.Information);
        System.Windows.Application.Current.Shutdown();
    }

    private async Task CheckForUpdatesAsync()
    {
        if (!ApplicationPreferencesStore.Read().CheckForUpdates) return;
        try
        {
            var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            var update = await GitHubReleaseUpdateChecker.CheckAsync(current);
            if (update?.IsNewer == true && MessageBox.Show(
                this,
                $"发现新版本 {update.VersionTag}。是否打开 GitHub Release 页面？",
                "发现更新",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo(update.ReleasePage.AbsoluteUri) { UseShellExecute = true });
            }
        }
        catch (HttpRequestException)
        {
            StatusText.Text = "暂时无法检查更新；本地读取、搜索和导出不受影响。";
        }
        catch (TaskCanceledException)
        {
            StatusText.Text = "更新检查超时；本地功能不受影响。";
        }
    }

    private bool TrySelection(out AccountDescriptor account, out IReadOnlyList<ConversationDescriptor> conversations, out TimeRange range)
    {
        if (AccountBox.SelectedItem is not AccountDescriptor selectedAccount)
        {
            Inform("请先选择账号。");
            account = null!; conversations = []; range = null!; return false;
        }

        var selected = allConversations.Where(item => item.IsSelected).Select(item => item.Descriptor).ToArray();
        if (selected.Length == 0)
        {
            Inform("请明确勾选至少一个群聊或私聊。");
            account = null!; conversations = []; range = null!; return false;
        }

        try
        {
            account = selectedAccount; conversations = selected; range = SelectedRange(); return true;
        }
        catch (ArgumentException exception)
        {
            Inform(exception.Message); account = null!; conversations = []; range = null!; return false;
        }
    }

    private TimeRange SelectedRange()
    {
        var tag = SelectedTag(RangeBox);
        if (int.TryParse(tag, out var days)) return TimeRange.ForLastNaturalDays(DateTimeOffset.Now, TimeZoneInfo.Local, days);
        if (StartDatePicker.SelectedDate is not DateTime start || EndDatePicker.SelectedDate is not DateTime end)
            throw new ArgumentException("请选择完整的开始和结束日期。");
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(start.Date, DateTimeKind.Unspecified), TimeZoneInfo.Local);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(end.Date.AddDays(1), DateTimeKind.Unspecified), TimeZoneInfo.Local);
        return new TimeRange(startUtc, endUtc);
    }

    private static string SelectedTag(ComboBox box) => ((ComboBoxItem)box.SelectedItem).Tag!.ToString()!;

    private void Inform(string message) => MessageBox.Show(this, message, "提示", MessageBoxButton.OK, MessageBoxImage.Information);

    private async Task RunBusyAsync(string status, Func<Task> action)
    {
        IsEnabled = false;
        StatusText.Text = status;
        try { await action(); }
        catch (Exception exception)
        {
            StatusText.Text = "操作失败。";
            MessageBox.Show(this, exception.Message, "操作失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsEnabled = true; }
    }
}

public sealed class ConversationChoice(ConversationDescriptor descriptor) : INotifyPropertyChanged
{
    private bool isSelected;
    public ConversationDescriptor Descriptor { get; } = descriptor;
    public string DisplayName => Descriptor.DisplayName;
    public string Detail => $"{(Descriptor.Type == ConversationType.Group ? "群聊" : "私聊")} · {Descriptor.Id}";
    public bool IsSelected
    {
        get => isSelected;
        set { if (isSelected == value) return; isSelected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class MessageRow(QqMessageRecord source)
{
    public McpMessageDto Message { get; } = McpMessageFormatter.Create(source);
    public ConversationDescriptor Conversation { get; } = new(source.AccountId, source.ConversationType, source.ConversationId, source.ConversationDisplayName);
    public string LocalTime => Message.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
    public string ConversationName => Message.ConversationName;
    public string Sender => Message.SenderName ?? Message.SenderId;
    public string Text => Message.Text;
}
