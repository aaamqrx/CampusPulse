using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CampusPulse.Core;
using Forms = System.Windows.Forms;
using WpfMessageBox = System.Windows.MessageBox;

namespace CampusPulse.App;

public partial class MainWindow : Window
{
    private readonly bool _isDemo;
    private readonly ControlClient _client = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _pollTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly Forms.NotifyIcon? _tray;
    private ServiceSnapshot? _snapshot;
    private BackgroundState _backgroundState;
    private bool _rendering = true;
    private bool _formDirty;
    private bool _busy;
    private bool _polling;
    private bool _connected;
    private bool _exitRequested;
    private bool _closeExplained;

    public MainWindow(bool isDemo)
    {
        _isDemo = isDemo;
        InitializeComponent();
        VersionText.Text = ProductInfo.Version;
        _rendering = false;
        if (isDemo)
        {
            Title += "（演示，未联网）";
            DemoBanner.Visibility = Visibility.Visible;
            RenderDemo();
        }
        else
        {
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("打开 CampusPulse", null, (_, _) => Dispatcher.Invoke(ShowFromTray));
            menu.Items.Add("退出界面（后台继续）", null, (_, _) => Dispatcher.Invoke(ExitInterface));
            _tray = new Forms.NotifyIcon
            {
                Text = "CampusPulse · 设置与状态",
                Icon = System.Drawing.SystemIcons.Application,
                ContextMenuStrip = menu,
                Visible = true
            };
            _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
            _pollTimer.Tick += async (_, _) => await RefreshAsync();
            Loaded += async (_, _) =>
            {
                await RefreshAsync();
                _pollTimer.Start();
            };
        }
        Closed += (_, _) =>
        {
            _pollTimer.Stop();
            _lifetime.Cancel();
            _tray?.Dispose();
            PasswordInput.Clear();
            System.Windows.Application.Current.Shutdown();
        };
        UpdateControls();
    }

    private async Task RefreshAsync(bool reportFailure = false)
    {
        if (_isDemo) { RenderDemo(); return; }
        if (_polling || _busy || _lifetime.IsCancellationRequested) return;
        _polling = true;
        try
        {
            _backgroundState = await Task.Run(ServiceControl.Query, _lifetime.Token);
            ServiceStateText.Text = DescribeService(_backgroundState);
            if (_backgroundState != BackgroundState.Running)
            {
                ShowDisconnected(_backgroundState == BackgroundState.NotInstalled
                    ? "尚未安装后台服务，请使用正式安装包完成安装。"
                    : "后台未运行，自动重连和防睡眠当前不可用。可点击“启动后台服务”。");
                return;
            }
            var reply = await _client.SendAsync(new ServiceRequest("status"), _lifetime.Token);
            if (!reply.Success || reply.Snapshot is null)
            {
                ShowDisconnected("后台暂未提供有效状态，请稍后刷新。");
                return;
            }
            ApplySnapshot(reply.Snapshot);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            ShowDisconnected(FriendlyError(exception));
            if (reportFailure) Feedback(FriendlyError(exception), true);
        }
        finally
        {
            _polling = false;
            UpdateControls();
        }
    }

    private void ApplySnapshot(ServiceSnapshot snapshot, bool replaceForm = false)
    {
        _snapshot = snapshot;
        _connected = true;
        StateText.Text = DescribeConnection(snapshot.State);
        StateDetail.Text = snapshot.Message;
        StateBadge.Background = Brush(snapshot.State is ConnectionState.Online or ConnectionState.IntranetOnline ? "#DEF0E9"
            : snapshot.State is ConnectionState.AuthenticationRejected or ConnectionState.PortalUnavailable ? "#FFF0DF" : "#E8EFF4");
        LastCheckText.Text = FormatTime(snapshot.LastCheck);
        LastSuccessText.Text = FormatTime(snapshot.LastSuccess, "尚无验证成功记录");
        NextCheckText.Text = FormatTime(snapshot.NextCheck, "未安排自动检查");
        ActualAutostartText.Text = snapshot.ActualStartWithWindows switch
        {
            true => "已开启",
            false => "已关闭（手动启动）",
            null => "无法确认系统配置"
        };
        if (snapshot.ActualStartWithWindows.HasValue && snapshot.ActualStartWithWindows != snapshot.Settings.StartWithWindows)
            ActualAutostartText.Text += " · 与保存设置不一致";
        AwakeText.Text = snapshot.KeepingAwake ? "防睡眠生效中（允许息屏）"
            : snapshot.Settings.UnattendedMode ? "已启用，当前未持有防睡眠请求" : "未启用";
        BlockedText.Visibility = snapshot.Settings.AuthenticationBlocked ? Visibility.Visible : Visibility.Collapsed;
        BlockedText.Text = "自动认证已受保护暂停。请核对并重新保存凭据，或点击“立即重连”手动重试一次。";
        EventsList.ItemsSource = snapshot.RecentEvents.OrderByDescending(entry => entry.Time)
            .Select(entry => new EventRow(FormatTime(entry.Time), entry.Message)).ToArray();
        NoEventsText.Visibility = snapshot.RecentEvents.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PasswordHint.Text = snapshot.HasPassword
            ? "已保存密码。更换账号、运营商或登录地址须重新输入密码。"
            : "尚未保存密码。密码只在本机受保护保存。";

        if (!_formDirty || replaceForm)
        {
            _rendering = true;
            UsernameBox.Text = snapshot.Settings.Username;
            PortalBox.Text = snapshot.Settings.PortalUrl;
            CarrierBox.SelectedValue = snapshot.Settings.Carrier;
            StartWithWindowsBox.IsChecked = snapshot.Settings.StartWithWindows;
            EnabledBox.IsChecked = snapshot.Settings.Enabled;
            UnattendedBox.IsChecked = snapshot.Settings.UnattendedMode;
            _rendering = false;
            if (replaceForm) _formDirty = false;
        }
        UpdateUnsavedText();
        UpdateControls();
    }

    private void ShowDisconnected(string detail)
    {
        _connected = false;
        StateText.Text = _backgroundState == BackgroundState.NotInstalled ? "后台尚未安装" : "后台未连接";
        StateDetail.Text = detail;
        StateBadge.Background = Brush("#FFF0DF");
        NextCheckText.Text = "当前无法确认";
        ActualAutostartText.Text = "当前无法确认";
        PasswordHint.Text = "后台未连接，暂无法确认已保存凭据状态。";
        AwakeText.Text = _backgroundState is BackgroundState.Stopped or BackgroundState.NotInstalled ? "未生效（后台未运行）" : "当前无法确认";
        if (_snapshot is null) UnsavedText.Text = "后台连接后可编辑设置。";
        UpdateControls();
    }

    private void FieldChanged(object sender, RoutedEventArgs e)
    {
        if (_rendering) return;
        _formDirty = true;
        UpdateUnsavedText();
    }

    private void UpdateUnsavedText()
    {
        UnsavedText.Text = _formDirty
            ? "有未保存的修改；检测和重连仍使用已保存配置。"
            : "设置已同步。三项开关独立生效。";
        if (UnattendedBox.IsChecked == true && StartWithWindowsBox.IsChecked != true)
            UnsavedText.Text += " 关闭自启后，重启需手动启动后台才能防睡眠。";
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_snapshot is null || _busy || _isDemo) return;
        var password = PasswordInput.Password;
        _rendering = true;
        PasswordInput.Clear();
        _rendering = false;
        var username = UsernameBox.Text.Trim();
        if (!PortalEndpoint.TryCreate(PortalBox.Text, out var portal))
        {
            Feedback("请输入校内 HTTP IPv4 门户首页地址，例如 http://10.62.164.38/。", true);
            return;
        }
        var carrier = CarrierBox.SelectedValue as string;
        if (!DrComProtocol.TryGetCarrier(carrier, out _))
        {
            Feedback("请选择受支持的认证选项。", true);
            return;
        }
        if (username.Length > 0 && !DrComProtocol.TryNormalizeUsername(username, carrier!, out _))
        {
            Feedback("账号后缀与所选认证选项不匹配，请填写基础账号。", true);
            return;
        }
        var unchangedAccount = username.Length == 0 && _snapshot.Settings.Username.Length == 0 ||
            DrComProtocol.TryNormalizeUsername(username, carrier!, out var baseAccount) && baseAccount == _snapshot.Settings.Username;
        if (password.Length == 0 && (!unchangedAccount || carrier != _snapshot.Settings.Carrier ||
            portal.Root.AbsoluteUri != _snapshot.Settings.PortalUrl ||
            (!_snapshot.HasPassword && username.Length > 0)))
        {
            Feedback("首次设置、更换账号、运营商或登录地址时，请重新输入密码。", true);
            return;
        }
        if (EnabledBox.IsChecked == true && username.Length == 0)
        {
            Feedback("启用自动重连前，请填写账号密码。", true);
            return;
        }
        var settings = _snapshot.Settings with
        {
            Username = username,
            PortalUrl = portal.Root.AbsoluteUri,
            Carrier = carrier!,
            StartWithWindows = StartWithWindowsBox.IsChecked == true,
            Enabled = EnabledBox.IsChecked == true,
            UnattendedMode = UnattendedBox.IsChecked == true
        };
        await SendCommandAsync(new ServiceRequest("save", settings, password), replaceForm: true);
    }

    private async void Check_Click(object sender, RoutedEventArgs e) => await SendCommandAsync(new ServiceRequest("check"));
    private async void Reconnect_Click(object sender, RoutedEventArgs e) => await SendCommandAsync(new ServiceRequest("reconnect"));
    private async void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (await SendCommandAsync(new ServiceRequest("pause")))
        {
            _rendering = true;
            EnabledBox.IsChecked = false;
            _rendering = false;
            UpdateUnsavedText();
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (WpfMessageBox.Show(this,
                "将删除本机保存的账号密码并暂停自动重连。开机自启和无人值守设置保留，当前校园网连接不会被注销。是否继续？",
                "清除已保存凭据", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        _rendering = true;
        PasswordInput.Clear();
        _rendering = false;
        await SendCommandAsync(new ServiceRequest("deleteCredentials"), replaceForm: true);
    }

    private async Task<bool> SendCommandAsync(ServiceRequest request, bool replaceForm = false)
    {
        if (_busy || _isDemo || !_connected) return false;
        _busy = true;
        UpdateControls();
        try
        {
            var reply = await _client.SendAsync(request, _lifetime.Token);
            if (reply.Snapshot is not null) ApplySnapshot(reply.Snapshot, replaceForm && reply.Success);
            Feedback(reply.Message, !reply.Success);
            return reply.Success;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return false; }
        catch (Exception exception)
        {
            Feedback(FriendlyError(exception), true);
            return false;
        }
        finally
        {
            _busy = false;
            UpdateControls();
        }
    }

    private async void StartService_Click(object sender, RoutedEventArgs e) => await ChangeServiceAsync(true);
    private async void StopService_Click(object sender, RoutedEventArgs e)
    {
        if (WpfMessageBox.Show(this,
                "停止后台后，自动重连和防睡眠请求都会结束；已保存设置及开机自启选项保留。是否停止？",
                "停止后台服务", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        await ChangeServiceAsync(false);
    }

    private async Task ChangeServiceAsync(bool start)
    {
        if (_busy || _isDemo) return;
        _busy = true;
        UpdateControls();
        Feedback(start ? "正在启动后台服务…" : "正在停止后台服务…");
        try
        {
            await ServiceControl.ChangeAsync(start, _lifetime.Token);
            Feedback(start ? "后台服务已启动，正在读取实际状态。" : "后台服务已停止，自动重连和防睡眠已结束。");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception) { Feedback(FriendlyError(exception), true); }
        finally
        {
            _busy = false;
            await RefreshAsync();
            UpdateControls();
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync(true);

    private void CopyEvents_Click(object sender, RoutedEventArgs e)
    {
        if (_snapshot is null || _snapshot.RecentEvents.Count == 0) return;
        try
        {
            System.Windows.Clipboard.SetText(DiagnosticText.Format(_snapshot.RecentEvents));
            Feedback("已复制脱敏事件记录。");
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            Feedback("剪贴板暂时不可用，请稍后重试。", true);
        }
    }

    private void UpdateControls()
    {
        var editable = _connected && !_busy && !_isDemo;
        SettingsForm.IsEnabled = editable;
        SaveButton.IsEnabled = editable;
        DeleteButton.IsEnabled = editable && _snapshot?.HasPassword == true;
        CheckButton.IsEnabled = editable;
        ReconnectButton.IsEnabled = editable;
        PauseButton.IsEnabled = editable && _snapshot?.Settings.Enabled == true;
        StartServiceButton.IsEnabled = !_isDemo && !_busy && _backgroundState == BackgroundState.Stopped;
        StopServiceButton.IsEnabled = !_isDemo && !_busy && _backgroundState == BackgroundState.Running;
        RefreshButton.IsEnabled = !_busy && !_polling;
        CopyEventsButton.IsEnabled = _snapshot?.RecentEvents.Count > 0;
    }

    private void Feedback(string message, bool error = false)
    {
        FeedbackText.Text = message;
        FeedbackBorder.Background = Brush(error ? "#FFF0DF" : "#E4F0EE");
        FeedbackBorder.Visibility = Visibility.Visible;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_exitRequested || _isDemo) return;
        e.Cancel = true;
        _rendering = true;
        PasswordInput.Clear();
        _rendering = false;
        Hide();
        if (!_closeExplained)
        {
            _tray?.ShowBalloonTip(3000, "CampusPulse 已收起", "后台继续按保存的设置运行。双击托盘图标打开窗口。", Forms.ToolTipIcon.Info);
            _closeExplained = true;
        }
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitInterface()
    {
        ShowFromTray();
        if (WpfMessageBox.Show(this,
                "退出的只是设置界面，后台服务将继续按已保存设置运行。未保存的修改不会生效。\n\n若要结束自动重连和防睡眠，请先点击“停止后台服务”。现在退出界面？",
                "退出 CampusPulse 界面", MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        _exitRequested = true;
        Close();
    }

    private void RenderDemo()
    {
        _backgroundState = BackgroundState.Running;
        ServiceStateText.Text = "运行中（演示）";
        var now = DateTimeOffset.Now;
        ApplySnapshot(new ServiceSnapshot
        {
            Settings = new CampusSettings { StartWithWindows = true, Enabled = true, UnattendedMode = true, Username = "demo-student" },
            HasPassword = true,
            State = ConnectionState.Online,
            Message = "演示状态：两个公网探测来源均已通过。此窗口未实际执行任何探测或认证。",
            LastCheck = now.AddSeconds(-28), LastSuccess = now.AddSeconds(-28), NextCheck = now.AddSeconds(92),
            KeepingAwake = true, ActualStartWithWindows = true,
            RecentEvents = new[]
            {
                new StatusEntry(now.AddSeconds(-28), "【演示】两个公网探测来源验证通过。"),
                new StatusEntry(now.AddMinutes(-3), "【演示】校园认证被接受，开始验证公网。"),
                new StatusEntry(now.AddMinutes(-4), "【演示】检测到校园网络可用，正在检查认证状态。")
            }
        }, replaceForm: true);
        UnsavedText.Text = "演示数据，不可保存，也不会操作系统服务。";
    }

    private static string FormatTime(DateTimeOffset? time, string empty = "—") => time?.ToLocalTime().ToString("MM-dd HH:mm:ss") ?? empty;
    private static SolidColorBrush Brush(string color) => new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
    private static string DescribeService(BackgroundState state) => state switch
    {
        BackgroundState.Running => "运行中", BackgroundState.Stopped => "已停止", BackgroundState.Starting => "正在启动",
        BackgroundState.Stopping => "正在停止", BackgroundState.NotInstalled => "尚未安装", BackgroundState.Paused => "已暂停", _ => "无法确认"
    };
    private static string DescribeConnection(ConnectionState state) => state switch
    {
        ConnectionState.Paused => "自动重连已暂停", ConnectionState.NeedsConfiguration => "请先配置账号密码",
        ConnectionState.Checking => "正在检查网络", ConnectionState.WaitingNetwork => "等待校园网恢复",
        ConnectionState.Authenticating => "正在认证", ConnectionState.Online => "公网验证通过",
        ConnectionState.IntranetOnline => "校内网已登录（无外网）",
        ConnectionState.AuthenticationRejected => "认证被拒绝", ConnectionState.PortalUnavailable => "校园门户暂不可用",
        ConnectionState.LimitedConnectivity => "互联网检测受限", _ => "状态未知"
    };
    private static string FriendlyError(Exception exception) => exception switch
    {
        TimeoutException => exception.Message,
        InvalidDataException => exception.Message,
        UnauthorizedAccessException => "没有后台访问权限，请使用管理员权限打开 CampusPulse。",
        Win32Exception { NativeErrorCode: 5 } => "没有服务管理权限，请使用管理员权限打开 CampusPulse。",
        Win32Exception { NativeErrorCode: 1060 } => "后台服务尚未安装，请使用正式安装包完成安装。",
        Win32Exception { NativeErrorCode: 1058 } => "后台服务被系统禁用，请在 Windows 服务管理器中恢复为手动启动。",
        Win32Exception error => $"Windows 服务操作失败（错误 {error.NativeErrorCode}），请稍后重试。",
        JsonException => "后台响应格式不兼容，请确认界面与服务为同一版本。",
        IOException => "无法与后台通信，请检查服务状态后重试。",
        _ => "操作未完成，请刷新后台状态后重试。"
    };

    private sealed record EventRow(string TimeText, string Message);
}
