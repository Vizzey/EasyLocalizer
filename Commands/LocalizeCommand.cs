using System;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Project = EnvDTE.Project;

namespace LocalizeExtension
{
    internal sealed class LocalizeCommand
    {
        public const int CommandId = 0x0100;
        public static readonly Guid CommandSet = new Guid("A6F9D371-C156-467C-AB35-9A8F0C3CF528");
        private readonly AsyncPackage package;

        private LocalizeCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package;
            var cmd = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(Execute, cmd);
            commandService.AddCommand(menuItem);
        }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            var mcs = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            new LocalizeCommand(package, mcs);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // Получаем DTE и выделение
            var dte = (DTE2)package.GetServiceAsync(typeof(DTE)).Result;
            var sel = dte.ActiveDocument?.Selection as TextSelection;
            if (sel == null || string.IsNullOrWhiteSpace(sel.Text)) return;

            // Оригинал и очищенный для диалога
            string original = sel.Text.Trim();
            string cleanedForDialog = original;
            if (cleanedForDialog.Length > 1 && cleanedForDialog.StartsWith("\"") && cleanedForDialog.EndsWith("\""))
                cleanedForDialog = cleanedForDialog.Substring(1, cleanedForDialog.Length - 2);

            // Диалог
            var dialog = new LocalizeDialog(cleanedForDialog);
            if (dialog.ShowDialog() != true) return;
            string resName = dialog.ResourceName;
            string resValue = dialog.ResourceValue;

            // Добавляем в resx
            if (!AddResourceEntry(dte, resName, resValue))
            {
                VsShellUtilities.ShowMessageBox(
                    package,
                    "Не удалось обновить Localization.resx",
                    "Ошибка",
                    OLEMSGICON.OLEMSGICON_CRITICAL,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                return;
            }

            // EditPoints для диапазона
            var startPt = sel.TopPoint.CreateEditPoint();
            var endPt = sel.BottomPoint.CreateEditPoint();

            int start = startPt.AbsoluteCharOffset;
            int end = endPt.AbsoluteCharOffset;
            int docStart = startPt.Parent.StartPoint.AbsoluteCharOffset;
            int docEnd = endPt.Parent.EndPoint.AbsoluteCharOffset;

            // Проверяем кавычки вокруг
            string before = "";
            if (start > docStart)
            {
                startPt.MoveToAbsoluteOffset(start - 1);
                before = startPt.GetText(1);
            }
            string after = "";
            if (end < docEnd)
            {
                endPt.MoveToAbsoluteOffset(end);
                after = endPt.GetText(1);
            }

            bool expand = before == "\"" && after == "\"";
            if (expand)
            {
                start--; end++;
            }

            // Получаем полный текст диапазона
            startPt.MoveToAbsoluteOffset(start);
            endPt.MoveToAbsoluteOffset(end);
            string full = startPt.GetText(endPt);

            // Очищаем внешние кавычки, если расширяли
            string cleaned = full;
            if (expand && full.Length > 1 && full.StartsWith("\"") && full.EndsWith("\""))
                cleaned = full.Substring(1, full.Length - 2);

            // Формируем итоговую строку
            string newText = $"@Loc[\"{cleaned}\"]";

            // Заменяем диапазон
            startPt.ReplaceText(endPt, newText, (int)vsEPReplaceTextOptions.vsEPReplaceTextAutoformat);
        }

        private bool AddResourceEntry(DTE2 dte, string resName, string resValue)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            foreach (Project proj in dte.Solution.Projects)
            {
                string dir = Path.GetDirectoryName(proj.FullName);
                string resx = Path.Combine(dir, "Resources", "Languages", "Localization.resx");
                if (File.Exists(resx))
                {
                    var doc = XDocument.Load(resx);
                    var data = new XElement("data",
                        new XAttribute("name", resName),
                        new XAttribute(XNamespace.Xml + "space", "preserve"),
                        new XElement("value", resValue));
                    doc.Root.Add(data);
                    doc.Save(resx);
                    return true;
                }
            }
            return false;
        }
    }
}
