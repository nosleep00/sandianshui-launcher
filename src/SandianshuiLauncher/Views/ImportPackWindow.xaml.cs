using SandianshuiLauncher.Core.Services;
using SandianshuiLauncher.Services;
using SandianshuiLauncher.ViewModels;
using Wpf.Ui.Controls;

namespace SandianshuiLauncher.Views;

/// <summary>统一的「导入整合包」窗口：压缩包内容导入 或 整合包实例导入。</summary>
public partial class ImportPackWindow : FluentWindow
{
    public ImportPackWindow(
        ConfigService config,
        DirectoryPackService directoryPack,
        PackImportService packImport,
        IDialogService dialogs)
    {
        InitializeComponent();
        DataContext = new ImportPackViewModel(config, directoryPack, packImport, dialogs);
    }
}
