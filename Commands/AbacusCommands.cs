using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TextManager.Interop;
using AbacusAI.VisualStudio2026.ToolWindows;

namespace AbacusAI.VisualStudio2026.Commands
{
    internal sealed class AbacusCommands
    {
        public const int Open = 0x0100;
        public const int Ask = 0x0101;
        public const int Explain = 0x0102;
        public const int Fix = 0x0103;

        static readonly Guid CommandSet = new Guid("4A7EAA5A-0A65-4A2D-9A25-6C9B6E4E7F02");

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync();
            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commandService == null) return;

            Add(commandService, Open, async (_, __) => await AbacusToolWindow.ShowAsync(package));
            Add(commandService, Ask, async (_, __) => await SendSelectionAsync(package, "Work on the current Visual Studio context. Explain what you would change before changing anything."));
            Add(commandService, Explain, async (_, __) => await SendSelectionAsync(package, "Explain the selected code in detail, including dependencies, risks and possible improvements. Do not modify files."));
            Add(commandService, Fix, async (_, __) => await SendSelectionAsync(package, "Analyze the selected code for bugs. Propose and implement a safe fix, then run the most relevant tests."));
        }

        static void Add(OleMenuCommandService service, int id, EventHandler handler)
        {
            service.AddCommand(new MenuCommand(handler, new CommandID(CommandSet, id)));
        }

        static async Task SendSelectionAsync(AsyncPackage package, string instruction)
        {
            await AbacusToolWindow.SendContextAsync(package, instruction);
        }
    }
}
