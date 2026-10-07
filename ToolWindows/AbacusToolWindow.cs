using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

        readonly System.Collections.ObjectModel.ObservableCollection<ChatMessage> messages
            = new System.Collections.ObjectModel.ObservableCollection<ChatMessage>();

        readonly ItemsControl chatList;
        readonly ScrollViewer chatScroll;
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

        readonly TextBox executable = new TextBox
        {
            Text = "abacusai.exe",
            Width = 140,
            Background = ControlBg,
            Foreground = TextPrimary,
            CaretBrush = TextPrimary,
            BorderBrush = BorderCol
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
        readonly Button send = new Button { Content = "Senden  ➤", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(8, 0, 0, 0), Background = AccentBg, Foreground = Brushes.White, BorderBrush = AccentBg, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Bottom };
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

        ChatMessage pendingAssistantMessage;
        readonly StringBuilder pendingAssistantText = new StringBuilder();

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

        bool authFailureDetected;
        bool keystoreLockDetected;
        readonly StringBuilder authFailureDetails = new StringBuilder();
        bool loginWindowOpen;

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

        public AbacusControl(AbacusToolWindow owner)
        {
            var root = new Grid { Margin = new Thickness(6), Background = PanelBg };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var settingsBar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            settingsBar.Children.Add(new TextBlock { Text = "✦ Abacus AI", FontWeight = FontWeights.SemiBold, FontSize = 15, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 12, 0) });
            settingsBar.Children.Add(new TextBlock { Text = "CLI:", Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            settingsBar.Children.Add(executable);
            settingsBar.Children.Add(new TextBlock { Text = "Modell:", Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) });
            LoadModels();
            settingsBar.Children.Add(modelBox);
            var clear = new Button { Content = "＋ Neuer Chat", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3), Background = ControlBg, Foreground = TextPrimary, BorderBrush = BorderCol };
            clear.Click += (_, __) => { messages.Clear(); status.Text = ""; };
            settingsBar.Children.Add(clear);
            settingsBar.Children.Add(loginButton);
            settingsBar.Children.Add(authStatus);

            loginButton.Click += async (_, __) => await LoginAsync();
            _ = RefreshAuthStatusAsync();

            chatList = new ItemsControl { ItemsSource = messages, ItemTemplate = BuildMessageTemplate(), Background = PanelBg };
            chatScroll = new ScrollViewer { Content = chatList, Background = PanelBg, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

            var roundStyle = BuildRoundButtonStyle();
            send.Style = roundStyle;
            loginButton.Style = roundStyle;
            clear.Style = roundStyle;
            FontFamily = UiFont;
            executable.Padding = new Thickness(4, 2, 4, 2);
            modelBox.Padding = new Thickness(4, 2, 4, 2);

            var inputBorder = new Border
            {
                Background = ControlBg,
                BorderBrush = BorderCol,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 6, 8, 6),
                Child = input
            };

            var inputBar = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            inputBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inputBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(inputBorder, 0);
            Grid.SetColumn(send, 1);
            inputBar.Children.Add(inputBorder);
            inputBar.Children.Add(send);

            var bottom = new StackPanel();
            bottom.Children.Add(inputBar);
            bottom.Children.Add(status);

            Grid.SetRow(settingsBar, 0);
            Grid.SetRow(chatScroll, 1);
            Grid.SetRow(bottom, 2);

            root.Children.Add(settingsBar);
            root.Children.Add(chatScroll);
            root.Children.Add(bottom);
            Content = root;

            send.Click += async (_, __) => await SendInputAsync();
            input.PreviewKeyDown += async (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter &&
                    (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) == 0)
                {
                    e.Handled = true;
                    await SendInputAsync();
                }
            };
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

        static DataTemplate BuildMessageTemplate()
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
            input.Text = text;
            await SendInputAsync();
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

            var logMessage = new ChatMessage { IsUser = false, RoleLabel = isLoggingOut ? "Logout" : "Login", Text = "" };
            var logText = new StringBuilder();
            messages.Add(logMessage);
            ScrollToEnd();

            try
            {
                var exe = executable.Text.Trim();
                var subcommand = isLoggingOut ? "auth logout" : "auth login";
                var workingDirectory = VisualStudioContext.GetCurrent().WorkingDirectory;

                if (isLoggingOut)
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = exe,
                        Arguments = subcommand,
                        WorkingDirectory = workingDirectory,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    };

                    using (var process = new Process { StartInfo = psi, EnableRaisingEvents = true })
                    {
                        process.OutputDataReceived += (_, e) =>
                        {
                            if (e.Data == null) return;
                            Dispatcher.BeginInvoke(new Action(() => AppendLine(logText, logMessage, e.Data)));
                        };
                        process.ErrorDataReceived += (_, e) =>
                        {
                            if (e.Data == null) return;
                            Dispatcher.BeginInvoke(new Action(() => AppendLine(logText, logMessage, e.Data)));
                        };

                        process.Start();
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();
                        await Task.Run(() => process.WaitForExit());
                    }

                    if (logText.Length == 0)
                        logMessage.Text = "Abgemeldet.";
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

                    if (alreadyLoggedIn)
                    {
                        logMessage.Text = "Bereits angemeldet - kein neues Login-Fenster nötig. Status wird aktualisiert...";
                    }
                    else
                    {
                        // "auth login" needs a real, interactive console (it refuses to run with
                        // redirected/no-window stdio). Open a visible console window for it instead
                        // of capturing its output.
                        logMessage.Text = "Es öffnet sich ein Konsolenfenster für den Login. Bitte dort anmelden und danach das Fenster schließen.";

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

                        logMessage.Text = "Login-Fenster geschlossen. Status wird aktualisiert...";
                    }
                }
            }
            catch (Exception ex)
            {
                logMessage.Text = "Fehler: " + ex.Message;
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

        async Task SendInputAsync()
        {
            var text = input.Text?.Trim();
            if (string.IsNullOrEmpty(text)) return;

            if (loginWindowOpen)
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

            input.Text = string.Empty;
            send.IsEnabled = false;
            status.Text = "Abacus arbeitet...";

            messages.Add(new ChatMessage { IsUser = true, RoleLabel = "Du", Text = text });
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
                var exe = executable.Text.Trim();
                var prompt = BuildConversationPrompt(text);

                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = BuildModelArgument() + "-p " + Quote(prompt),
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (var process = new Process { StartInfo = psi, EnableRaisingEvents = true })
                {
                    process.OutputDataReceived += (_, e) =>
                    {
                        if (e.Data == null) return;
                        Dispatcher.BeginInvoke(new Action(() => AppendAssistantLine(e.Data)));
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
                    authStatus.Foreground = WarningCol;
                    authStatus.Text = "● Nicht angemeldet";
                    authStatus.ToolTip = authFailureDetails.ToString().Trim();
                    loginButton.Content = "Login";
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
            }
            catch (Exception ex)
            {
                AppendAssistantLine("Fehler beim Ausführen der Abacus CLI: " + ex.Message);
            }
            finally
            {
                send.IsEnabled = true;
                status.Text = "";
                pendingAssistantMessage = null;
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

        string BuildModelArgument()
        {
            var model = (modelBox.SelectedValue as string ?? "").Trim();
            if (model.Length == 0) return "";
            return "--model " + Quote(model) + " ";
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

        string BuildConversationPrompt(string latestUserText)
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
            return sb.ToString();
        }

        static string Quote(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", "\\n") + "\"";
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
