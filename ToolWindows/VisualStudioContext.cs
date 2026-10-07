using System;
using System.IO;
using System.Runtime.InteropServices;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TextManager.Interop;

namespace AbacusAI.VisualStudio2026.ToolWindows
{
    internal static class VisualStudioContext
    {
        internal sealed class Context
        {
            public string WorkingDirectory;
            public string FilePath;
            public string Selection;
        }

        public static Context GetCurrent()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var dte = Package.GetGlobalService(typeof(DTE)) as DTE2;
            var solution = dte?.Solution?.FullName;
            var working = !string.IsNullOrWhiteSpace(solution)
                ? Path.GetDirectoryName(solution)
                : Environment.CurrentDirectory;

            string file = null;
            string selection = null;

            var doc = dte?.ActiveDocument;
            if (doc != null)
            {
                file = doc.FullName;
                var text = doc.Selection as TextSelection;
                if (text != null)
                {
                    selection = text.Text;
                }
            }

            return new Context
            {
                WorkingDirectory = working ?? Environment.CurrentDirectory,
                FilePath = file,
                Selection = selection
            };
        }
    }
}
