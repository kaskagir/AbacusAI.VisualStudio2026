using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.VisualStudio.Shell;

namespace AbacusAI.VisualStudio2026.ToolWindows
{
    [Guid("D9C0E7F5-0E6C-4B2D-9C0B-2C0B9E8F1A03")]
    public sealed class AbacusToolWindow : ToolWindowPane
    {
        internal static AbacusControl Control;

        public AbacusToolWindow() : base(null)
        {
            Caption = "Abacus AI";
            Control = new AbacusControl(this);
            Content = Control;
        }

        public static async Task ShowAsync(AsyncPackage package)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync();
            var window = await package.FindToolWindowAsync(typeof(AbacusToolWindow), 0, true, package.DisposalToken);
            if (window?.Frame == null) throw new NotSupportedException();
            ((Microsoft.VisualStudio.Shell.Interop.IVsWindowFrame)window.Frame).Show();
        }

        public static async Task SendContextAsync(AsyncPackage package, string instruction)
        {
            await ShowAsync(package);
            await package.JoinableTaskFactory.SwitchToMainThreadAsync();

            var context = VisualStudioContext.GetCurrent();
            await Control.SendAsync(BuildPrompt(context, instruction));
        }

        static string BuildPrompt(VisualStudioContext.Context context, string instruction)
        {
            var sb = new StringBuilder();
            sb.AppendLine(instruction);
            sb.AppendLine();
            sb.AppendLine("Visual Studio context:");
            sb.AppendLine("Solution/working directory: " + context.WorkingDirectory);
            if (!string.IsNullOrWhiteSpace(context.FilePath))
                sb.AppendLine("Current file: " + context.FilePath);
            if (!string.IsNullOrWhiteSpace(context.Selection))
            {
                sb.AppendLine("Selected code:");
                sb.AppendLine("```");
                sb.AppendLine(context.Selection);
                sb.AppendLine("```");
            }
            sb.AppendLine();
            sb.AppendLine("Use the project files directly. Do not assume missing context.");
            return sb.ToString();
        }
    }

    internal sealed class ChatMessage : System.ComponentModel.INotifyPropertyChanged
    {
        public bool IsUser { get; set; }
        public string RoleLabel { get; set; }

        string text;
        public string Text
        {
            get => text;
            set
            {
                text = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Text)));
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    }

    internal sealed class FileAttachment : System.ComponentModel.INotifyPropertyChanged
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public long FileSize { get; set; }

        string displayName;
        public string DisplayName
        {
            get => displayName;
            set
            {
                displayName = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(DisplayName)));
            }
        }

        public FileAttachment(string filePath)
        {
            FilePath = filePath;
            FileName = Path.GetFileName(filePath);
            try
            {
                FileSize = new FileInfo(filePath).Length;
                DisplayName = $"{FileName} ({FormatFileSize(FileSize)})";
            }
            catch
            {
                DisplayName = FileName;
            }
        }

        internal static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    }

    // Titel eines Tabs/einer Conversation. Separates Objekt, damit das TabItem-Header
    // per Binding automatisch aktualisiert wird, sobald die erste Nachricht gesendet wurde.
    internal sealed class ConversationInfo : System.ComponentModel.INotifyPropertyChanged
    {
        string title = "Neuer Chat";
        public string Title
        {
            get => title;
            set
            {
                title = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Title)));
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    }

    internal sealed class AbacusControl : UserControl
    {
        static readonly SolidColorBrush PanelBg = Freeze(Color.FromRgb(0x1B, 0x1B, 0x1F));
        static readonly SolidColorBrush ControlBg = Freeze(Color.FromRgb(0x2A, 0x2A, 0x30));
        static readonly SolidColorBrush AccentBg = Freeze(Color.FromRgb(0x4F, 0x8C, 0xFF));
        static readonly FontFamily UiFont = new FontFamily("Segoe UI Variable Text, Segoe UI");
        static readonly SolidColorBrush BorderCol = Freeze(Color.FromRgb(0x38, 0x38, 0x42));
        static readonly SolidColorBrush TextPrimary = Freeze(Color.FromRgb(0xF1, 0xF1, 0xF1));
        static readonly SolidColorBrush TextSecondary = Freeze(Color.FromRgb(0xB8, 0xB8, 0xB8));
        static readonly SolidColorBrush SuccessCol = Freeze(Color.FromRgb(0x6A, 0xC2, 0x6A));
        static readonly SolidColorBrush WarningCol = Freeze(Color.FromRgb(0xE0, 0xA8, 0x3A));
        static readonly SolidColorBrush ErrorCol = Freeze(Color.FromRgb(0xE0, 0x6C, 0x6C));

        static SolidColorBrush Freeze(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        readonly TabControl tabControl = new TabControl
        {
            Background = PanelBg,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0)
        };

        readonly TextBox executable = new TextBox
        {
            Text = "abacusai.exe",
            Width = 140,
            Background = ControlBg,
            Foreground = TextPrimary,
            CaretBrush = TextPrimary,
            BorderBrush = BorderCol,
            ToolTip = "CLI-Ausführbare Datei (wird aus Einstellungen geladen)"
        };
        readonly ComboBox modelBox = new ComboBox
        {
            Width = 190,
            Margin = new Thickness(6, 0, 0, 0),
            ToolTip = "KI-Modell (Standard = Standardmodell der CLI)",
            DisplayMemberPath = "Key",
            SelectedValuePath = "Value",
            Foreground = Brushes.Black
        };
        readonly ComboBox modeBox = new ComboBox
        {
            Width = 150,
            Margin = new Thickness(6, 0, 0, 0),
            DisplayMemberPath = "Key",
            SelectedValuePath = "Value",
            Foreground = Brushes.Black
        };
        readonly TextBlock status = new TextBlock { Foreground = TextSecondary, Margin = new Thickness(0, 2, 0, 0) };

        readonly TextBlock authStatus = new TextBlock
        {
            Foreground = TextSecondary,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 320
        };
        readonly Button loginButton = new Button { Content = "Login", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2), Background = ControlBg, Foreground = TextPrimary, BorderBrush = BorderCol };

        bool loginWindowOpen;

        static readonly string[] AuthFailureMarkers =
        {
            "needs an interactive terminal",
            "credentials did not finish",
            "startup_timeout",
            "keystore.enc.unreadable"
        };

        // Nur ein vorübergehender Konflikt (z.B. weil parallel ein "auth login"-Fenster
        // offen ist und den Keystore hält) - kein echter Auth-Fehler, Nutzer soll es
        // einfach erneut versuchen statt sich neu einzuloggen.
        const string KeystoreLockMarker = "could not acquire the keystore lock";

        static bool LooksLikeAuthFailure(string line)
        {
            if (string.IsNullOrEmpty(line)) return false;
            foreach (var marker in AuthFailureMarkers)
            {
                if (line.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        AbacusOptions GetAbacusOptions()
        {
            try
            {
                var package = Package.GetGlobalService(typeof(Package)) as Package;
                if (package != null)
                {
                    return (AbacusOptions)package.GetDialogPage(typeof(AbacusOptions));
                }
            }
            catch { }
            return null;
        }

        void ApplyOptionsToUI(AbacusOptions options)
        {
            if (options == null) return;
            
            if (!string.IsNullOrWhiteSpace(options.CliExecutable))
                executable.Text = options.CliExecutable;
            
            if (!string.IsNullOrWhiteSpace(options.DefaultModel))
                modelBox.SelectedValue = options.DefaultModel;

            if (!string.IsNullOrWhiteSpace(options.DefaultPermissionMode))
                modeBox.SelectedValue = options.DefaultPermissionMode;
        }

        void SaveOptionsFromUI(AbacusOptions options)
        {
            if (options == null) return;
            
            options.CliExecutable = executable.Text.Trim();
            if (modelBox.SelectedValue != null)
                options.DefaultModel = modelBox.SelectedValue.ToString();
            if (modeBox.SelectedValue != null)
                options.DefaultPermissionMode = modeBox.SelectedValue.ToString();
            
            options.SaveSettingsToStorage();
        }

        public AbacusControl(AbacusToolWindow owner)
        {
            // Lade Optionen aus Visual Studio Einstellungen
            var options = GetAbacusOptions();

            var root = new Grid { Margin = new Thickness(6), Background = PanelBg };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var settingsBar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            settingsBar.Children.Add(new TextBlock { Text = "✦ Abacus AI", FontWeight = FontWeights.SemiBold, FontSize = 15, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 12, 0) });
            settingsBar.Children.Add(new TextBlock { Text = "CLI:", Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            settingsBar.Children.Add(executable);
            settingsBar.Children.Add(new TextBlock { Text = "Modell:", Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) });
            LoadModels();
            settingsBar.Children.Add(modelBox);
            settingsBar.Children.Add(new TextBlock { Text = "Modus:", Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) });
            LoadModes();
            settingsBar.Children.Add(modeBox);
            
            // Wende gespeicherte Optionen an
            ApplyOptionsToUI(options);
            modelBox.SelectionChanged += (_, __) => SaveOptionsFromUI(options);
            modeBox.SelectionChanged += (_, __) => SaveOptionsFromUI(options);
            var newChat = new Button { Content = "＋ Neuer Chat", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3), Background = ControlBg, Foreground = TextPrimary, BorderBrush = BorderCol };
            newChat.Click += (_, __) => AddTab(select: true);
            settingsBar.Children.Add(newChat);
            
            var settingsBtn = new Button { Content = "⚙ Einstellungen", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3), Background = ControlBg, Foreground = TextPrimary, BorderBrush = BorderCol };
            settingsBtn.Click += (_, __) => OpenSettings();
            settingsBar.Children.Add(settingsBtn);
            
            settingsBar.Children.Add(loginButton);
            settingsBar.Children.Add(authStatus);

            loginButton.Click += async (_, __) => await LoginAsync();
            _ = RefreshAuthStatusAsync();

            var roundStyle = BuildRoundButtonStyle();
            loginButton.Style = roundStyle;
            newChat.Style = roundStyle;
            settingsBtn.Style = roundStyle;
            FontFamily = UiFont;
            executable.Padding = new Thickness(4, 2, 4, 2);
            modelBox.Padding = new Thickness(4, 2, 4, 2);
            modeBox.Padding = new Thickness(4, 2, 4, 2);

            tabControl.ItemContainerStyle = BuildTabItemStyle();
            tabControl.Template = BuildTabControlTemplate();

            Grid.SetRow(settingsBar, 0);
            Grid.SetRow(tabControl, 1);

            root.Children.Add(settingsBar);
            root.Children.Add(tabControl);
            Content = root;

            AddTab(select: true);
        }

        void OpenSettings()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var dte = Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
                if (dte != null)
                {
                    dte.ExecuteCommand("Tools.Options");
                }
            }
            catch { }
        }

        void AddTab(bool select)
        {
            var tab = new ChatTab(this);
            var item = new TabItem { Content = tab };
            item.Header = BuildTabHeader(tab.Info, item);
            tab.Info.PropertyChanged += (_, __) => UpdateHeaderText(item, tab.Info);
            tabControl.Items.Add(item);
            if (select) tabControl.SelectedItem = item;
        }

        void CloseTab(TabItem item)
        {
            tabControl.Items.Remove(item);
            if (tabControl.Items.Count == 0)
                AddTab(select: true);
        }

        FrameworkElement BuildTabHeader(ConversationInfo info, TabItem owningItem)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            var title = new TextBlock
            {
                Text = info.Title,
                Foreground = TextPrimary,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 140,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 6, 0),
                Tag = info
            };
            var close = new Button
            {
                Content = "✕",
                Width = 18,
                Height = 18,
                Padding = new Thickness(0),
                FontSize = 10,
                Background = Brushes.Transparent,
                Foreground = TextSecondary,
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            close.Click += (_, __) => CloseTab(owningItem);
            panel.Children.Add(title);
            panel.Children.Add(close);
            return panel;
        }

        static void UpdateHeaderText(TabItem item, ConversationInfo info)
        {
            if (item.Header is StackPanel panel && panel.Children.Count > 0 && panel.Children[0] is TextBlock title)
                title.Text = info.Title;
        }

        static Style BuildTabItemStyle()
        {
            const string xaml = @"<Style xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" TargetType=""TabItem"">
  <Setter Property=""Padding"" Value=""10,5,6,5""/>
  <Setter Property=""Margin"" Value=""0,0,4,0""/>
  <Setter Property=""Template"">
    <Setter.Value>
      <ControlTemplate TargetType=""TabItem"">
        <Border x:Name=""bd"" CornerRadius=""8,8,0,0"" Padding=""{TemplateBinding Padding}"" Margin=""{TemplateBinding Margin}"" Background=""#2A2A30"" BorderBrush=""#38383A"" BorderThickness=""1,1,1,0"">
          <ContentPresenter ContentSource=""Header"" VerticalAlignment=""Center""/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsSelected"" Value=""True"">
            <Setter TargetName=""bd"" Property=""Background"" Value=""#1B1B1F""/>
            <Setter TargetName=""bd"" Property=""BorderBrush"" Value=""#4F8CFF""/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>";
            return (Style)System.Windows.Markup.XamlReader.Parse(xaml);
        }

        static ControlTemplate BuildTabControlTemplate()
        {
            const string xaml = @"<ControlTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" TargetType=""TabControl"">
  <Grid Background=""#1B1B1F"">
    <Grid.RowDefinitions>
      <RowDefinition Height=""Auto""/>
      <RowDefinition Height=""*""/>
    </Grid.RowDefinitions>
    <TabPanel Grid.Row=""0"" IsItemsHost=""True"" Background=""Transparent""/>
    <Border Grid.Row=""1"" BorderBrush=""#38383A"" BorderThickness=""1"" Background=""#1B1B1F"">
      <ContentPresenter ContentSource=""SelectedContent""/>
    </Border>
  </Grid>
</ControlTemplate>";
            return (ControlTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
        }

        static Style BuildRoundButtonStyle()
        {
            const string xaml = @"<Style xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" TargetType=""Button"">
  <Setter Property=""Cursor"" Value=""Hand""/>
  <Setter Property=""Template"">
    <Setter.Value>
      <ControlTemplate TargetType=""Button"">
        <Border x:Name=""bd"" Background=""{TemplateBinding Background}"" BorderBrush=""{TemplateBinding BorderBrush}"" BorderThickness=""1"" CornerRadius=""8"" Padding=""{TemplateBinding Padding}"">
          <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsMouseOver"" Value=""True""><Setter TargetName=""bd"" Property=""Opacity"" Value=""0.85""/></Trigger>
          <Trigger Property=""IsPressed"" Value=""True""><Setter TargetName=""bd"" Property=""Opacity"" Value=""0.7""/></Trigger>
          <Trigger Property=""IsEnabled"" Value=""False""><Setter TargetName=""bd"" Property=""Opacity"" Value=""0.4""/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>";
            return (Style)System.Windows.Markup.XamlReader.Parse(xaml);
        }

        internal static DataTemplate BuildMessageTemplate()
        {
            var bubble = new FrameworkElementFactory(typeof(Border));
            bubble.SetValue(Border.PaddingProperty, new Thickness(12, 8, 12, 10));
            bubble.SetValue(Border.MarginProperty, new Thickness(4, 5, 4, 5));
            bubble.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            bubble.SetBinding(Border.CornerRadiusProperty, new System.Windows.Data.Binding("IsUser")
            {
                Converter = new UserToRadiusConverter()
            });
            bubble.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("IsUser")
            {
                Converter = new UserToBrushConverter()
            });
            bubble.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("IsUser")
            {
                Converter = new UserToBrushConverter(),
                ConverterParameter = "border"
            });
            bubble.SetBinding(FrameworkElement.HorizontalAlignmentProperty, new System.Windows.Data.Binding("IsUser")
            {
                Converter = new UserToAlignmentConverter()
            });
            bubble.SetBinding(FrameworkElement.MaxWidthProperty, new System.Windows.Data.Binding("ActualWidth")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(ScrollViewer), 1),
                Converter = new WidthFractionConverter()
            });

            var stack = new FrameworkElementFactory(typeof(StackPanel));
            var role = new FrameworkElementFactory(typeof(TextBlock));
            role.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("RoleLabel"));
            role.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            role.SetValue(TextBlock.FontSizeProperty, 11.0);
            role.SetValue(TextBlock.OpacityProperty, 0.65);
            role.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 0, 3));
            role.SetValue(TextBlock.ForegroundProperty, Brushes.White);

            // Read-only TextBox statt TextBlock: Text lässt sich markieren und kopieren.
            var text = new FrameworkElementFactory(typeof(TextBox));
            text.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding("Text") { Mode = System.Windows.Data.BindingMode.OneWay });
            text.SetValue(TextBox.IsReadOnlyProperty, true);
            text.SetValue(TextBox.BorderThicknessProperty, new Thickness(0));
            text.SetValue(TextBox.BackgroundProperty, Brushes.Transparent);
            text.SetValue(TextBox.PaddingProperty, new Thickness(0));
            text.SetValue(TextBox.TextWrappingProperty, TextWrapping.Wrap);
            text.SetValue(TextBox.FontSizeProperty, 13.0);
            text.SetValue(TextBox.ForegroundProperty, Brushes.White);
            text.SetValue(TextBox.SelectionBrushProperty, new SolidColorBrush(Color.FromRgb(0x26, 0x4F, 0x78)));

            text.SetBinding(UIElement.VisibilityProperty, new System.Windows.Data.Binding("IsUser")
            {
                Converter = new UserToVisibilityConverter(),
                ConverterParameter = "user"
            });

            // Antworten von Abacus werden als Markdown gerendert.
            var md = new FrameworkElementFactory(typeof(MarkdownBox));
            md.SetBinding(MarkdownBox.MarkdownProperty, new System.Windows.Data.Binding("Text"));
            md.SetBinding(UIElement.VisibilityProperty, new System.Windows.Data.Binding("IsUser")
            {
                Converter = new UserToVisibilityConverter(),
                ConverterParameter = "bot"
            });

            stack.AppendChild(role);
            stack.AppendChild(text);
            stack.AppendChild(md);
            bubble.AppendChild(stack);

            return new DataTemplate { VisualTree = bubble };
        }

        public async Task SendAsync(string text)
        {
            if (!(tabControl.SelectedItem is TabItem item) || !(item.Content is ChatTab tab))
            {
                AddTab(select: true);
                item = (TabItem)tabControl.SelectedItem;
                tab = (ChatTab)item.Content;
            }
            await tab.SendAsync(text);
        }

        async Task RefreshAuthStatusAsync()
        {
            authStatus.Foreground = TextSecondary;
            authStatus.Text = "Prüfe Login...";
            try
            {
                var exe = executable.Text.Trim();
                var output = await RunCaptureAsync(exe, "auth status");
                ApplyAuthStatus(output);
            }
            catch (Exception ex)
            {
                authStatus.Foreground = ErrorCol;
                authStatus.Text = "Status nicht verfügbar";
                authStatus.ToolTip = ex.Message;
                loginButton.Content = "Login";
            }
        }

        void ApplyAuthStatus(string output)
        {
            output = output ?? string.Empty;
            var notLoggedIn = output.IndexOf("Not logged in", StringComparison.OrdinalIgnoreCase) >= 0
                || output.IndexOf("Not authenticated", StringComparison.OrdinalIgnoreCase) >= 0;

            if (notLoggedIn || string.IsNullOrWhiteSpace(output))
            {
                authStatus.Foreground = WarningCol;
                authStatus.Text = "● Nicht angemeldet";
                authStatus.ToolTip = string.IsNullOrWhiteSpace(output) ? null : output.Trim();
                loginButton.Content = "Login";
                return;
            }

            var email = ExtractField(output, "Email:");
            var org = ExtractField(output, "Org:");
            var name = ExtractField(output, "Name:");
            var who = !string.IsNullOrEmpty(email) ? email : (name ?? "Angemeldet");

            authStatus.Foreground = SuccessCol;
            authStatus.Text = string.IsNullOrEmpty(org)
                ? "● Angemeldet als " + who
                : "● Angemeldet als " + who + " (" + org + ")";
            authStatus.ToolTip = output.Trim();
            loginButton.Content = "Logout";
        }

        void SetLoggedOutStatus(string tooltip)
        {
            authStatus.Foreground = WarningCol;
            authStatus.Text = "● Nicht angemeldet";
            authStatus.ToolTip = tooltip;
            loginButton.Content = "Login";
        }

        static string ExtractField(string output, string prefix)
        {
            foreach (var rawLine in output.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return line.Substring(prefix.Length).Trim();
            }
            return null;
        }

        async Task LoginAsync()
        {
            var isLoggingOut = string.Equals((string)loginButton.Content, "Logout", StringComparison.Ordinal);
            loginButton.IsEnabled = false;
            authStatus.Foreground = TextSecondary;
            authStatus.Text = isLoggingOut ? "Melde ab..." : "Login läuft...";

            try
            {
                var exe = executable.Text.Trim();
                var subcommand = isLoggingOut ? "auth logout" : "auth login";
                var workingDirectory = VisualStudioContext.GetCurrent().WorkingDirectory;

                if (isLoggingOut)
                {
                    await RunCaptureAsync(exe, subcommand);
                }
                else
                {
                    // Vorher den echten Status prüfen: Ist bereits eine gültige Session
                    // vorhanden, würde "auth login" nur auf eine Yes/No-Nachfrage warten
                    // und dabei den Keystore blockieren (lässt parallele Chat-Aufrufe mit
                    // "could not acquire the keystore lock" fehlschlagen). Also in dem Fall
                    // gar nicht erst öffnen.
                    var currentStatus = await RunCaptureAsync(exe, "auth status");
                    var alreadyLoggedIn = !string.IsNullOrWhiteSpace(currentStatus)
                        && currentStatus.IndexOf("Not logged in", StringComparison.OrdinalIgnoreCase) < 0
                        && currentStatus.IndexOf("Not authenticated", StringComparison.OrdinalIgnoreCase) < 0;

                    if (!alreadyLoggedIn)
                    {
                        // "auth login" needs a real, interactive console (it refuses to run with
                        // redirected/no-window stdio). Open a visible console window for it instead
                        // of capturing its output.
                        var psi = new ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = "/c " + Quote(exe) + " " + subcommand + " & pause",
                            WorkingDirectory = workingDirectory,
                            UseShellExecute = true,
                            CreateNoWindow = false
                        };

                        loginWindowOpen = true;
                        try
                        {
                            using (var process = new Process { StartInfo = psi })
                            {
                                process.Start();
                                await Task.Run(() => process.WaitForExit());
                            }
                        }
                        finally
                        {
                            loginWindowOpen = false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                authStatus.Foreground = ErrorCol;
                authStatus.Text = "Fehler: " + ex.Message;
            }
            finally
            {
                loginButton.IsEnabled = true;
                await RefreshAuthStatusAsync();
            }
        }

        static async Task<string> RunCaptureAsync(string exe, string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using (var process = new Process { StartInfo = psi })
            {
                process.Start();
                var stdout = await process.StandardOutput.ReadToEndAsync();
                var stderr = await process.StandardError.ReadToEndAsync();
                await Task.Run(() => process.WaitForExit());
                return string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
            }
        }

        void LoadModels()
        {
            var list = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>>
            {
                new System.Collections.Generic.KeyValuePair<string, string>("Standard", "")
            };
            try
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".abacusai", "cache", "startup.json");
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path, Encoding.UTF8);
                    var seen = new System.Collections.Generic.HashSet<string>();
                    foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                        json, "\"llmName\"\\s*:\\s*\"([^\"]+)\"\\s*,\\s*\"name\"\\s*:\\s*\"([^\"]+)\""))
                    {
                        if (seen.Add(m.Groups[1].Value))
                            list.Add(new System.Collections.Generic.KeyValuePair<string, string>(m.Groups[2].Value, m.Groups[1].Value));
                    }
                }
            }
            catch
            {
            }
            modelBox.ItemsSource = list;
            modelBox.SelectedIndex = 0;
        }

        void LoadModes()
        {
            var kv = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>>
            {
                new System.Collections.Generic.KeyValuePair<string, string>("Standard (CLI)", ""),
                new System.Collections.Generic.KeyValuePair<string, string>("Auto", "auto"),
                new System.Collections.Generic.KeyValuePair<string, string>("Supervise", "supervise"),
                new System.Collections.Generic.KeyValuePair<string, string>("Accept edits", "accept-edits"),
                new System.Collections.Generic.KeyValuePair<string, string>("Plan", "plan"),
                new System.Collections.Generic.KeyValuePair<string, string>("Unsupervised", "unsupervised")
            };
            modeBox.ItemsSource = kv;
            modeBox.SelectedIndex = 0;
            modeBox.ToolTip =
                "Auto: arbeitet selbstständig, prüft jeden Befehl/Edit, lehnt Gefährliches ab\n" +
                "Supervise: fragt vor Edits und anderen verändernden Tools\n" +
                "Accept edits: wendet Datei-Edits ohne Nachfrage an, fragt bei Befehlen\n" +
                "Plan: nur lesen und planen, Edits erst nach Freigabe\n" +
                "Unsupervised: führt jeden Befehl ohne Nachfrage aus";
        }

        string BuildModelArgument()
        {
            var arg = "";
            var model = (modelBox.SelectedValue as string ?? "").Trim();
            if (model.Length > 0) arg += "--model " + Quote(model) + " ";
            var mode = (modeBox.SelectedValue as string ?? "").Trim();
            if (mode.Length > 0) arg += "--permission-mode " + mode + " ";
            return arg;
        }

        static string Quote(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", "\\n") + "\"";
        }

        // Eine einzelne Conversation/Tab: eigene Nachrichtenliste, eigenes Eingabefeld,
        // eigener CLI-Prozessaufruf. CLI-Pfad, Modell und Login bleiben oben geteilt.
        sealed class ChatTab : UserControl
        {
            readonly AbacusControl owner;
            public readonly ConversationInfo Info = new ConversationInfo();

            readonly System.Collections.ObjectModel.ObservableCollection<ChatMessage> messages
                = new System.Collections.ObjectModel.ObservableCollection<ChatMessage>();

            readonly System.Collections.ObjectModel.ObservableCollection<FileAttachment> attachments
                = new System.Collections.ObjectModel.ObservableCollection<FileAttachment>();

            readonly ItemsControl chatList;
            readonly ScrollViewer chatScroll;
            readonly ItemsControl attachmentList;
            readonly TextBox input = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 140,
                MinHeight = 24,
                Padding = new Thickness(4),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = TextPrimary,
                CaretBrush = TextPrimary,
                FontSize = 13,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            readonly Button send = new Button { Content = "Senden  ➤", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(8, 0, 0, 0), Background = AccentBg, Foreground = Brushes.White, BorderBrush = AccentBg, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Bottom };
            readonly Button cancel = new Button { Content = "⊘ Abbrechen", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(8, 0, 0, 0), Background = ErrorCol, Foreground = Brushes.White, BorderBrush = ErrorCol, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Bottom, Visibility = Visibility.Collapsed };
            readonly TextBlock status = new TextBlock { Foreground = TextSecondary, Margin = new Thickness(0, 2, 0, 0) };

            readonly CheckBox includeActiveFile = new CheckBox
            {
                IsChecked = true,
                Foreground = TextSecondary,
                Margin = new Thickness(8, 6, 6, 0),
                Visibility = Visibility.Collapsed
            };
            readonly TextBlock activeFileLabel = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
            string activeFilePath;
            System.Windows.Threading.DispatcherTimer activeFileTimer;

            ChatMessage pendingAssistantMessage;
            readonly StringBuilder pendingAssistantText = new StringBuilder();
            bool authFailureDetected;
            bool keystoreLockDetected;
            readonly StringBuilder authFailureDetails = new StringBuilder();
            Process currentProcess;

            // Conversation-ID, die die Abacus CLI in ihrer Ausgabe zurückmeldet. Sobald bekannt,
            // wird sie per --resume an jede weitere Nachricht dieses Tabs angehängt, damit der
            // Server den Gesprächsverlauf führt statt ihn clientseitig erneut mitzuschicken.
            string conversationId;
            bool lastLineWasMeta;

            // Erkennt die Metadaten-Zeilen, die die CLI zu jeder Antwort ausgibt
            // (z.B. "model: ... | cwd: ... | conversation: <id>", "title: ...",
            // "Credits used: ...", "To resume Session:" / "abacusai -p --resume <id>")
            // und blendet sie aus dem Chatverlauf aus, statt sie als Antworttext anzuzeigen.
            static readonly System.Text.RegularExpressions.Regex ConversationLineRegex =
                new System.Text.RegularExpressions.Regex(@"^model:.*\|\s*conversation:\s*(\S+)\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            static readonly System.Text.RegularExpressions.Regex TitleLineRegex =
                new System.Text.RegularExpressions.Regex(@"^title:\s*(.+?)\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            static readonly System.Text.RegularExpressions.Regex ResumeCommandRegex =
                new System.Text.RegularExpressions.Regex(@"--resume\s+(\S+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            static readonly System.Text.RegularExpressions.Regex CreditsLineRegex =
                new System.Text.RegularExpressions.Regex(@"^Credits used:", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            static readonly System.Text.RegularExpressions.Regex ResumeHintLineRegex =
                new System.Text.RegularExpressions.Regex(@"^To resume Session:\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            bool TryHandleMetaLine(string line)
            {
                var convMatch = ConversationLineRegex.Match(line);
                if (convMatch.Success)
                {
                    conversationId = convMatch.Groups[1].Value;
                    lastLineWasMeta = true;
                    return true;
                }
                var titleMatch = TitleLineRegex.Match(line);
                if (titleMatch.Success)
                {
                    Info.Title = titleMatch.Groups[1].Value;
                    lastLineWasMeta = true;
                    return true;
                }
                if (CreditsLineRegex.IsMatch(line) || ResumeHintLineRegex.IsMatch(line))
                {
                    lastLineWasMeta = true;
                    return true;
                }
                var resumeCmdMatch = ResumeCommandRegex.Match(line);
                if (resumeCmdMatch.Success)
                {
                    if (string.IsNullOrEmpty(conversationId))
                        conversationId = resumeCmdMatch.Groups[1].Value;
                    lastLineWasMeta = true;
                    return true;
                }
                if (line.Length == 0 && lastLineWasMeta)
                {
                    // Leerzeile direkt nach einem Metadaten-Block gehört noch dazu, nicht zur Antwort.
                    return true;
                }
                lastLineWasMeta = false;
                return false;
            }

            public ChatTab(AbacusControl owner)
            {
                this.owner = owner;

                chatList = new ItemsControl { ItemsSource = messages, ItemTemplate = BuildMessageTemplate(), Background = PanelBg };
                chatScroll = new ScrollViewer { Content = chatList, Background = PanelBg, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                send.Style = BuildRoundButtonStyle();
                cancel.Style = BuildRoundButtonStyle();

                var inputBorder = new Border
                {
                    Background = ControlBg,
                    BorderBrush = BorderCol,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(8, 6, 8, 6),
                    Child = input
                };

                var inputBar = new Grid { Margin = new Thickness(6, 8, 6, 0) };
                inputBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                inputBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                inputBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(inputBorder, 0);
                Grid.SetColumn(send, 1);
                Grid.SetColumn(cancel, 2);
                inputBar.Children.Add(inputBorder);
                inputBar.Children.Add(send);
                inputBar.Children.Add(cancel);

                // Datei-Anhänge UI
                attachmentList = new ItemsControl 
                { 
                    ItemsSource = attachments, 
                    ItemTemplate = BuildAttachmentTemplate(), 
                    Background = PanelBg,
                    Margin = new Thickness(6, 4, 6, 4)
                };
                var attachmentScroll = new ScrollViewer 
                { 
                    Content = attachmentList, 
                    Background = PanelBg, 
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    MaxHeight = 80
                };

                var bottom = new StackPanel();
                bottom.Children.Add(attachmentScroll);
                bottom.Children.Add(inputBar);
                includeActiveFile.Content = activeFileLabel;
                bottom.Children.Add(includeActiveFile);
                bottom.Children.Add(new Border { Padding = new Thickness(6, 0, 6, 4), Child = status });

                var grid = new Grid { Background = PanelBg };
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(chatScroll, 0);
                Grid.SetRow(bottom, 1);
                grid.Children.Add(chatScroll);
                grid.Children.Add(bottom);
                Content = grid;

                Loaded += (_, __) =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    StartActiveFileTracking();
                };
                Unloaded += (_, __) => activeFileTimer?.Stop();

                send.Click += async (_, __) => await SendInputAsync();
                cancel.Click += (_, __) => CancelCurrentProcess();
                input.PreviewKeyDown += async (_, e) =>
                {
                    if (e.Key == System.Windows.Input.Key.Enter &&
                        (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) == 0)
                    {
                        e.Handled = true;
                        await SendInputAsync();
                    }
                    // Ctrl+V für Dateien/Bilder aus Zwischenablage
                    else if (e.Key == System.Windows.Input.Key.V &&
                        (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0)
                    {
                        // Versuche zuerst, Dateien/Bilder zu verarbeiten
                        if (TryHandlePasteFilesOrImages())
                        {
                            e.Handled = true;
                        }
                        // Sonst: Standard-Paste-Verhalten (Text)
                    }
                };

                // Drag&Drop aktivieren
                inputBorder.AllowDrop = true;
                inputBorder.PreviewDragOver += (_, e) =>
                {
                    e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                        ? DragDropEffects.Copy
                        : DragDropEffects.None;
                    e.Handled = true;
                };
                inputBorder.PreviewDrop += (_, e) =>
                {
                    e.Handled = true;
                    HandleDroppedFiles(e);
                };
            }

            public async Task SendAsync(string text)
            {
                input.Text = text;
                await SendInputAsync();
            }

            void CancelCurrentProcess()
            {
                if (currentProcess != null && !currentProcess.HasExited)
                {
                    try
                    {
                        currentProcess.Kill();
                        AppendAssistantLine("\n⊘ Conversation unterbrochen.");
                        status.Text = "Unterbrochen";
                    }
                    catch (Exception ex)
                    {
                        AppendAssistantLine("\n⚠ Fehler beim Abbrechen: " + ex.Message);
                    }
                }
            }



            void AppendAssistantLine(string line)
            {
                AppendLine(pendingAssistantText, pendingAssistantMessage, line);
            }

            void AppendLine(StringBuilder buffer, ChatMessage message, string line)
            {
                if (buffer.Length > 0) buffer.AppendLine();
                buffer.Append(line);
                if (message != null) message.Text = buffer.ToString();
                ScrollToEnd();
            }

            void ScrollToEnd()
            {
                chatScroll.ScrollToEnd();
            }

            void StartActiveFileTracking()
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                RefreshActiveFile();
                if (activeFileTimer == null)
                {
                    activeFileTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                    activeFileTimer.Tick += (_, __) =>
                    {
                        ThreadHelper.ThrowIfNotOnUIThread();
                        RefreshActiveFile();
                    };
                }
                activeFileTimer.Start();
            }

            void RefreshActiveFile()
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                try
                {
                    var file = VisualStudioContext.GetCurrent().FilePath;
                    activeFilePath = !string.IsNullOrWhiteSpace(file) && Path.IsPathRooted(file) && File.Exists(file) ? file : null;
                }
                catch
                {
                    activeFilePath = null;
                }

                if (activeFilePath == null)
                {
                    includeActiveFile.Visibility = Visibility.Collapsed;
                    return;
                }
                activeFileLabel.Text = "@" + Path.GetFileName(activeFilePath) + " als Referenz mitsenden";
                activeFileLabel.ToolTip = activeFilePath;
                includeActiveFile.Visibility = Visibility.Visible;
            }

            bool TryHandlePasteFilesOrImages()
            {
                try
                {
                    if (Clipboard.ContainsFileDropList())
                    {
                        foreach (var file in Clipboard.GetFileDropList())
                            AddFileAttachment(file);
                        return true;
                    }

                    if (Clipboard.ContainsImage())
                    {
                        var image = Clipboard.GetImage();
                        if (image == null) return false;
                        var tempDir = Path.Combine(Path.GetTempPath(), "AbacusAI");
                        Directory.CreateDirectory(tempDir);
                        var tempFile = Path.Combine(tempDir, "screenshot-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".png");
                        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                        using (var fs = File.Create(tempFile)) encoder.Save(fs);
                        AddFileAttachment(tempFile);
                        return true;
                    }

                    return false;
                }
                catch (Exception ex)
                {
                    status.Text = "Fehler beim Einfügen: " + ex.Message;
                    return false;
                }
            }

            void HandleDroppedFiles(DragEventArgs e)
            {
                try
                {
                    // Versuche, Dateien zu holen
                    if (e.Data.GetDataPresent(DataFormats.FileDrop))
                    {
                        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                        if (files != null && files.Length > 0)
                        {
                            foreach (var file in files)
                            {
                                AddFileAttachment(file);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    status.Text = "Fehler beim Verarbeiten der Datei: " + ex.Message;
                }
            }

            bool IsImageFile(string filePath)
            {
                var ext = Path.GetExtension(filePath).ToLowerInvariant();
                return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif" || ext == ".bmp" || ext == ".webp";
            }

            void AddFileAttachment(string filePath)
            {
                try
                {
                    if (!File.Exists(filePath) && !Directory.Exists(filePath))
                    {
                        status.Text = "Datei nicht gefunden: " + filePath;
                        return;
                    }

                    // Prüfe, ob die Datei bereits hinzugefügt wurde
                    foreach (var att in attachments)
                    {
                        if (att.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase))
                        {
                            status.Text = "Datei bereits hinzugefügt: " + Path.GetFileName(filePath);
                            return;
                        }
                    }

                    var attachment = new FileAttachment(filePath);
                    attachments.Add(attachment);
                    status.Text = "Datei hinzugefügt: " + attachment.DisplayName;
                }
                catch (Exception ex)
                {
                    status.Text = "Fehler beim Hinzufügen der Datei: " + ex.Message;
                }
            }

            void RemoveFileAttachment(FileAttachment attachment)
            {
                attachments.Remove(attachment);
                status.Text = "Datei entfernt: " + attachment.FileName;
            }

            (string cleanedText, int successCount, int errorCount) ParseAndAttachFiles(string inputText)
            {
                var successCount = 0;
                var errorCount = 0;
                var errors = new System.Collections.Generic.List<string>();
                
                var pattern = @"@([^\s\n]+)";
                var matches = System.Text.RegularExpressions.Regex.Matches(inputText, pattern);
                
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    var fileRef = match.Groups[1].Value;
                    if (!TryAddFileReference(fileRef, out var errorMsg))
                    {
                        errorCount++;
                        errors.Add(errorMsg);
                    }
                    else
                    {
                        successCount++;
                    }
                }
                
                var cleanedText = System.Text.RegularExpressions.Regex.Replace(inputText, pattern, "").Trim();
                
                if (errorCount > 0)
                {
                    var errorSummary = string.Join("; ", errors.Take(3).ToList());
                    status.Text = $"⚠ {successCount} Datei(en) erkannt, {errorCount} Fehler: {errorSummary}";
                }
                else if (successCount > 0)
                {
                    status.Text = $"✓ {successCount} Datei(en) erkannt";
                }
                
                return (cleanedText, successCount, errorCount);
            }

            bool TryAddFileReference(string fileRef, out string errorMessage)
            {
                errorMessage = null;
                
                try
                {
                    if (fileRef.Contains("*") || fileRef.Contains("?"))
                    {
                        return TryAddWildcardFiles(fileRef, out errorMessage);
                    }
                    
                    return TryAddSingleFile(fileRef, out errorMessage);
                }
                catch (Exception ex)
                {
                    errorMessage = $"Fehler bei '{fileRef}': {ex.Message}";
                    return false;
                }
            }

            bool TryAddSingleFile(string filePath, out string errorMessage)
            {
                errorMessage = null;
                
                if (!IsValidFilePath(filePath))
                {
                    errorMessage = $"Ungültiger Dateipfad: {filePath}";
                    return false;
                }
                
                if (!File.Exists(filePath))
                {
                    errorMessage = $"Datei nicht gefunden: {filePath}";
                    return false;
                }
                
                var fileInfo = new FileInfo(filePath);
                const long maxSize = 10 * 1024 * 1024;
                if (fileInfo.Length > maxSize)
                {
                    errorMessage = $"Datei zu groß ({FileAttachment.FormatFileSize(fileInfo.Length)}): {filePath}";
                    return false;
                }
                
                foreach (var att in attachments)
                {
                    if (att.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase))
                    {
                        errorMessage = $"Datei bereits hinzugefügt: {Path.GetFileName(filePath)}";
                        return false;
                    }
                }
                
                var attachment = new FileAttachment(filePath);
                attachments.Add(attachment);
                return true;
            }

            bool TryAddWildcardFiles(string pattern, out string errorMessage)
            {
                errorMessage = null;
                var addedCount = 0;
                
                try
                {
                    var directory = Path.GetDirectoryName(pattern);
                    if (string.IsNullOrEmpty(directory))
                        directory = ".";
                    
                    if (!Directory.Exists(directory))
                    {
                        errorMessage = $"Verzeichnis nicht gefunden: {directory}";
                        return false;
                    }
                    
                    var filePattern = Path.GetFileName(pattern);
                    var files = Directory.GetFiles(directory, filePattern, SearchOption.TopDirectoryOnly);
                    
                    if (files.Length == 0)
                    {
                        errorMessage = $"Keine Dateien gefunden für: {pattern}";
                        return false;
                    }
                    
                    if (files.Length > 50)
                    {
                        errorMessage = $"Zu viele Dateien ({files.Length}), max. 50 erlaubt";
                        return false;
                    }
                    
                    foreach (var file in files)
                    {
                        if (TryAddSingleFile(file, out _))
                            addedCount++;
                    }
                    
                    if (addedCount == 0)
                    {
                        errorMessage = $"Keine Dateien konnten hinzugefügt werden: {pattern}";
                        return false;
                    }
                    
                    return true;
                }
                catch (Exception ex)
                {
                    errorMessage = $"Fehler beim Wildcard-Matching: {ex.Message}";
                    return false;
                }
            }

            bool IsValidFilePath(string path)
            {
                if (string.IsNullOrWhiteSpace(path))
                    return false;
                
                try
                {
                    var invalidChars = Path.GetInvalidPathChars();
                    if (path.IndexOfAny(invalidChars) >= 0)
                        return false;
                    
                    if (path.Contains("*") || path.Contains("?"))
                        return true;
                    
                    return File.Exists(path);
                }
                catch
                {
                    return false;
                }
            }

            DataTemplate BuildAttachmentTemplate()
            {
                var template = new DataTemplate();
                var border = new FrameworkElementFactory(typeof(Border));
                border.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x30)));
                border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x3A)));
                border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
                border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
                border.SetValue(Border.PaddingProperty, new Thickness(8, 4, 4, 4));
                border.SetValue(Border.MarginProperty, new Thickness(0, 2, 4, 2));

                var grid = new FrameworkElementFactory(typeof(Grid));
                
                // Spalten zum Grid hinzufügen
                var col1Factory = new FrameworkElementFactory(typeof(ColumnDefinition));
                col1Factory.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
                grid.AppendChild(col1Factory);
                
                var col2Factory = new FrameworkElementFactory(typeof(ColumnDefinition));
                col2Factory.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
                grid.AppendChild(col2Factory);
                
                var textBlock = new FrameworkElementFactory(typeof(TextBlock));
                textBlock.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("DisplayName"));
                textBlock.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0xF1, 0xF1, 0xF1)));
                textBlock.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
                textBlock.SetValue(TextBlock.FontSizeProperty, 12.0);
                textBlock.SetValue(Grid.ColumnProperty, 0);

                var button = new FrameworkElementFactory(typeof(Button));
                button.SetValue(Button.ContentProperty, "✕");
                button.SetValue(Button.WidthProperty, 20.0);
                button.SetValue(Button.HeightProperty, 20.0);
                button.SetValue(Button.PaddingProperty, new Thickness(0));
                button.SetValue(Button.MarginProperty, new Thickness(4, 0, 0, 0));
                button.SetValue(Button.FontSizeProperty, 10.0);
                button.SetValue(Button.BackgroundProperty, Brushes.Transparent);
                button.SetValue(Button.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0xB8, 0xB8, 0xB8)));
                button.SetValue(Button.BorderThicknessProperty, new Thickness(0));
                button.SetValue(Button.CursorProperty, System.Windows.Input.Cursors.Hand);
                button.SetValue(Button.VerticalAlignmentProperty, VerticalAlignment.Center);
                button.SetValue(Grid.ColumnProperty, 1);
                button.AddHandler(Button.ClickEvent, new RoutedEventHandler((s, e) =>
                {
                    if (s is Button btn && btn.DataContext is FileAttachment att)
                    {
                        RemoveFileAttachment(att);
                    }
                }));

                grid.AppendChild(textBlock);
                grid.AppendChild(button);
                border.AppendChild(grid);
                template.VisualTree = border;
                return template;
            }

            async Task SendInputAsync()
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var text = input.Text?.Trim();
                if (string.IsNullOrEmpty(text) && attachments.Count == 0) return;

                var (cleanedText, fileSuccessCount, fileErrorCount) = ParseAndAttachFiles(text ?? "");
                
                if (fileErrorCount > 0 && fileSuccessCount == 0)
                {
                    return;
                }
                
                text = cleanedText;

                if (owner.loginWindowOpen)
                {
                    messages.Add(new ChatMessage
                    {
                        IsUser = false,
                        RoleLabel = "Abacus AI",
                        Text = "Es ist noch ein Login-Fenster offen. Bitte dort zuerst die Frage beantworten und das Fenster schließen, bevor der Chat weiter benutzt wird."
                    });
                    ScrollToEnd();
                    return;
                }

                if (messages.Count == 0)
                    Info.Title = (text?.Length ?? 0) > 28 ? text.Substring(0, 28) + "..." : (text ?? "Datei-Upload");

                input.Text = string.Empty;
                send.IsEnabled = false;
                cancel.Visibility = Visibility.Visible;
                status.Text = "Abacus arbeitet...";

                RefreshActiveFile();
                var refFiles = new System.Collections.Generic.List<FileAttachment>(attachments);
                if (includeActiveFile.IsChecked == true && activeFilePath != null
                    && !refFiles.Exists(a => string.Equals(a.FilePath, activeFilePath, StringComparison.OrdinalIgnoreCase)))
                    refFiles.Insert(0, new FileAttachment(activeFilePath));

                var userMessage = text ?? "";
                if (refFiles.Count > 0)
                {
                    userMessage += "\n\nAngehängte Dateien:\n";
                    foreach (var att in refFiles)
                    {
                        userMessage += $"- {att.FileName}\n";
                    }
                }

                messages.Add(new ChatMessage { IsUser = true, RoleLabel = "Du", Text = userMessage });
                ScrollToEnd();

                pendingAssistantMessage = new ChatMessage { IsUser = false, RoleLabel = "Abacus AI", Text = "" };
                pendingAssistantText.Clear();
                authFailureDetected = false;
                keystoreLockDetected = false;
                authFailureDetails.Clear();
                messages.Add(pendingAssistantMessage);

                try
                {
                    var workingDirectory = VisualStudioContext.GetCurrent().WorkingDirectory;
                    var exe = owner.executable.Text.Trim();
                    
                    // Baue den Prompt mit Datei-Inhalten
                    var prompt = string.IsNullOrEmpty(conversationId) 
                        ? BuildConversationPrompt(text, refFiles, workingDirectory) 
                        : BuildPromptWithAttachments(text, refFiles, workingDirectory);
                    
                    var resumeArgument = string.IsNullOrEmpty(conversationId) ? "" : "--resume " + Quote(conversationId) + " ";

                    var psi = new ProcessStartInfo
                    {
                        FileName = exe,
                        Arguments = owner.BuildModelArgument() + "-p " + resumeArgument + Quote(prompt),
                        WorkingDirectory = workingDirectory,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    };

                    currentProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
                    using (var process = currentProcess)
                    {
                        process.OutputDataReceived += (_, e) =>
                        {
                            if (e.Data == null) return;
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                if (TryHandleMetaLine(e.Data)) return;
                                AppendAssistantLine(e.Data);
                            }));
                        };
                        process.ErrorDataReceived += (_, e) =>
                        {
                            if (e.Data == null) return;
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                if (LooksLikeAuthFailure(e.Data))
                                {
                                    authFailureDetected = true;
                                    authFailureDetails.AppendLine(e.Data);
                                    return;
                                }
                                if (e.Data.IndexOf(KeystoreLockMarker, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    keystoreLockDetected = true;
                                    authFailureDetails.AppendLine(e.Data);
                                    return;
                                }
                                if (TryHandleMetaLine(e.Data)) return;
                                AppendAssistantLine(e.Data);
                            }));
                        };

                        process.Start();
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();
                        await Task.Run(() => process.WaitForExit());
                    }

                    if (authFailureDetected)
                    {
                        pendingAssistantText.Clear();
                        AppendAssistantLine("⚠ Anmeldung ungültig oder abgelaufen. Bitte oben auf \"Login\" klicken und den Login in einem normalen Terminal abschließen.");
                        owner.SetLoggedOutStatus(authFailureDetails.ToString().Trim());
                    }
                    else if (keystoreLockDetected)
                    {
                        pendingAssistantText.Clear();
                        AppendAssistantLine("⚠ Die Abacus CLI war kurzzeitig blockiert (z.B. durch ein offenes Login-Fenster). Bitte Login-Fenster schließen/beantworten und die Nachricht erneut senden.");
                    }
                    else if (pendingAssistantText.Length == 0)
                    {
                        AppendAssistantLine("(keine Ausgabe)");
                    }

                    // Leere die Anhänge nach erfolgreichem Senden
                    attachments.Clear();
                }
                catch (Exception ex)
                {
                    AppendAssistantLine("Fehler beim Ausführen der Abacus CLI: " + ex.Message);
                }
                finally
                {
                    send.IsEnabled = true;
                    cancel.Visibility = Visibility.Collapsed;
                    status.Text = "";
                    pendingAssistantMessage = null;
                    currentProcess = null;
                }
            }

            static string AtReference(string workingDirectory, string path)
            {
                var rel = path.TrimEnd('\\', '/');
                try
                {
                    if (!string.IsNullOrEmpty(workingDirectory))
                    {
                        var baseDir = workingDirectory.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                        if (rel.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                            rel = rel.Substring(baseDir.Length);
                    }
                }
                catch { }
                rel = rel.Replace('\\', '/');
                return rel.IndexOf(' ') >= 0 ? "@\"" + rel + "\"" : "@" + rel;
            }

            string BuildAttachmentSection(System.Collections.Generic.IList<FileAttachment> files, string workingDirectory)
            {
                if (files.Count == 0) return "";
                var sb = new StringBuilder();
                var hasImage = false;
                sb.AppendLine();
                sb.AppendLine();
                sb.Append("Referenzierte Dateien:");
                foreach (var att in files)
                {
                    sb.Append(' ').Append(AtReference(workingDirectory, att.FilePath));
                    if (IsImageFile(att.FilePath)) hasImage = true;
                }
                if (hasImage)
                {
                    sb.AppendLine();
                    sb.Append("(Bilddateien darin sind Screenshots/Bilder des Nutzers: bitte ansehen und berücksichtigen.)");
                }
                return sb.ToString();
            }

            string BuildPromptWithAttachments(string latestUserText, System.Collections.Generic.IList<FileAttachment> files, string workingDirectory)
            {
                return (latestUserText ?? "") + BuildAttachmentSection(files, workingDirectory);
            }

            string BuildConversationPrompt(string latestUserText, System.Collections.Generic.IList<FileAttachment> files, string workingDirectory)
            {
                var sb = new StringBuilder();
                var relevant = new System.Collections.Generic.List<ChatMessage>();
                foreach (var m in messages)
                {
                    if (ReferenceEquals(m, pendingAssistantMessage)) continue;
                    relevant.Add(m);
                }
                if (relevant.Count > 1)
                {
                    sb.AppendLine("Conversation so far:");
                    for (int i = 0; i < relevant.Count - 1; i++)
                    {
                        sb.AppendLine((relevant[i].IsUser ? "User: " : "Assistant: ") + relevant[i].Text);
                    }
                    sb.AppendLine();
                    sb.AppendLine("New message:");
                }
                sb.Append(latestUserText);
                sb.Append(BuildAttachmentSection(files, workingDirectory));
                return sb.ToString();
            }
        }
    }

    internal sealed class UserToBrushConverter : System.Windows.Data.IValueConverter
    {
        static readonly SolidColorBrush UserBg = Make(0x3A, 0x6E, 0xD8);
        static readonly SolidColorBrush UserBorder = Make(0x4F, 0x8C, 0xFF);
        static readonly SolidColorBrush BotBg = Make(0x2A, 0x2A, 0x32);
        static readonly SolidColorBrush BotBorder = Make(0x3C, 0x3C, 0x48);

        static SolidColorBrush Make(byte r, byte g, byte b)
        {
            var br = new SolidColorBrush(Color.FromRgb(r, g, b));
            br.Freeze();
            return br;
        }

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var isUser = value is bool b && b;
            var border = parameter as string == "border";
            if (isUser) return border ? UserBorder : UserBg;
            return border ? BotBorder : BotBg;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }

    internal sealed class UserToVisibilityConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var isUser = value is bool b && b;
            var showForUser = parameter as string == "user";
            return isUser == showForUser ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }

    internal sealed class UserToRadiusConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var isUser = value is bool b && b;
            return isUser ? new CornerRadius(14, 14, 3, 14) : new CornerRadius(14, 14, 14, 3);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }

    internal sealed class WidthFractionConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var w = value is double d ? d : 0;
            return Math.Max(200, w * 0.88 - 24);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }

    internal sealed class UserToAlignmentConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var isUser = value is bool b && b;
            return isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }
}
