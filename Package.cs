using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;

namespace AbacusAI.VisualStudio2026
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("Abacus AI for Visual Studio 2026", "Abacus AI coding agent integration", "1.0")]
    [ProvideMenuResource("Menus.ctmenu", 2)]
    [ProvideToolWindow(typeof(ToolWindows.AbacusToolWindow))]
    [Guid(PackageGuidString)]
    public sealed class Package : AsyncPackage
    {
        public const string PackageGuidString = "8D3C0C9E-8B3E-4E0D-9B53-9A4D6D7D1C01";

        protected override async Task InitializeAsync(
            CancellationToken cancellationToken,
            IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            await Commands.AbacusCommands.InitializeAsync(this);
        }
    }
}
