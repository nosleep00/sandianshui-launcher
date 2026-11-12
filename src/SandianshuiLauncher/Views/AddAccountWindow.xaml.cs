using System.Windows;
using System.Windows.Input;
using SandianshuiLauncher.Core.Models;
using SandianshuiLauncher.Core.Services;
using Wpf.Ui.Controls;

namespace SandianshuiLauncher.Views;

/// <summary>添加账号对话框：离线账号与外置登录（authlib-injector 皮肤站）。</summary>
public partial class AddAccountWindow : FluentWindow
{
    private readonly AccountService _accounts;
    private readonly AuthlibInjectorService _authlib;

    /// <summary>当前选中的"未设置用户名"的离线账号；非空时本窗口改为补填用户名。</summary>
    private GameAccount? _pendingAccount;

    public AddAccountWindow(AccountService accounts, AuthlibInjectorService authlib)
    {
        _accounts = accounts;
        _authlib = authlib;
        InitializeComponent();
        Loaded += (_, _) =>
        {
            PrepareUsernameEditing();
            UsernameBox.Focus();
        };
    }

    /// <summary>
    /// 仿 PCL2：离线账号允许不填用户名。选中这类账号时打开本窗口，
    /// 界面切换为"设置用户名"，填好后即可启动游戏。
    /// </summary>
    private void PrepareUsernameEditing()
    {
        if (_accounts.Current is not { IsUsernameMissing: true } account)
        {
            return;
        }

        _pendingAccount = account;
        Title = "设置用户名";
        WindowTitleBar.Title = "设置用户名";
        HeadingText.Text = "设置用户名";
        OkButton.Content = "保存用户名";
        UsernameHint.Text = "这个离线账号还没有用户名，填好名称后就能启动游戏（3–16 位字母、数字或下划线）。";
    }

    private void AuthlibSwitch_Changed(object sender, RoutedEventArgs e)
    {
        AuthlibPanel.Visibility = AuthlibSwitch.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (AuthlibSwitch.IsChecked == true)
        {
            EmailBox.Focus();
        }
        else
        {
            UsernameBox.Focus();
        }
    }

    private async void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (AuthlibSwitch.IsChecked == true)
        {
            await AddAuthlibAccountAsync();
        }
        else
        {
            AddOfflineAccount();
        }
    }

    private void AddOfflineAccount()
    {
        string username = UsernameBox.Text.Trim();

        // 补填用户名：现有账号还是空白名字，这里只做改名。
        if (_pendingAccount is not null)
        {
            if (string.IsNullOrEmpty(username))
            {
                ShowMessage("请填写用户名后再保存。", "用户名不能为空", MessageDialogKind.Warning);
                return;
            }

            try
            {
                _accounts.SetOfflineUsername(_pendingAccount.Id, username);
                DialogResult = true;
                Close();
            }
            catch (ArgumentException ex)
            {
                ShowMessage(ex.Message, "提示", MessageDialogKind.Warning);
            }
            return;
        }

        // 留空是允许的（仿 PCL2），只有填了内容才校验格式。
        if (!string.IsNullOrEmpty(username) && !AccountService.IsValidUsername(username))
        {
            ShowMessage("用户名需为 3–16 位字母、数字或下划线。", "用户名不合法", MessageDialogKind.Warning);
            return;
        }

        try
        {
            _accounts.AddOfflineAccount(username);
            DialogResult = true;
            Close();
        }
        catch (ArgumentException ex)
        {
            ShowMessage(ex.Message, "提示", MessageDialogKind.Warning);
        }
    }

    /// <summary>在账号窗口之上弹出与主界面同风格的消息弹窗。</summary>
    private void ShowMessage(string message, string title, MessageDialogKind kind)
    {
        new MessageDialogWindow(message, title, kind) { Owner = this }.ShowDialog();
    }

    private async Task AddAuthlibAccountAsync()
    {
        string server = ServerBox.Text.Trim();
        string email = EmailBox.Text.Trim();
        string password = PasswordBox.Password;
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            ShowMessage("请填写认证服务器地址、邮箱与密码。", "提示", MessageDialogKind.Warning);
            return;
        }

        OkButton.IsEnabled = false;
        StatusText.Text = "正在连接认证服务器…";
        try
        {
            var account = await _authlib.AuthenticateAsync(server, email, password);
            _accounts.AddAuthlibAccount(account.Username, account.Uuid, account.AccessToken!, account.AuthServerUrl!);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = string.Empty;
            ShowMessage(ex.Message, "外置登录失败", MessageDialogKind.Error);
        }
        finally
        {
            OkButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void UsernameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Ok_Click(sender, e);
        }
    }
}
