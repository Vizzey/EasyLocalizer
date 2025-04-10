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

namespace LocalizeExtension
{
    internal sealed class LocalizeCommand
    {
        public const int CommandId = 0x0100;
        public static readonly Guid CommandSet =
            new Guid("A6F9D371-C156-467C-AB35-9A8F0C3CF528");

        private readonly AsyncPackage package;

        private LocalizeCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package;
            var cmdId = new CommandID(CommandSet, CommandId);
            var menu = new MenuCommand(this.Execute, cmdId);
            commandService.AddCommand(menu);
        }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory
                              .SwitchToMainThreadAsync(package.DisposalToken);
            var mcs = await package.GetServiceAsync(
                        typeof(IMenuCommandService))
                      as OleMenuCommandService;
            new LocalizeCommand(package, mcs);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var dte = (DTE2)package.GetServiceAsync(typeof(DTE)).Result;
            var sel = dte.ActiveDocument?.Selection as TextSelection;
            if (sel == null) return;

            if (string.IsNullOrWhiteSpace(sel.Text))
            {
                var tp = sel.ActivePoint;
                var epStart = tp.CreateEditPoint();
                var epEnd = tp.CreateEditPoint();

                while (!epStart.AtStartOfDocument)
                {
                    epStart.MoveToAbsoluteOffset(epStart.AbsoluteCharOffset - 1);
                    char c = epStart.GetText(1)[0];
                    if (char.IsWhiteSpace(c) || c == '"' || c == '<' || c == '>')
                    {
                        epStart.MoveToAbsoluteOffset(epStart.AbsoluteCharOffset + 1);
                        break;
                    }
                }
                while (!epEnd.AtEndOfDocument)
                {
                    char c = epEnd.GetText(1)[0];
                    if (char.IsWhiteSpace(c) || c == '"' || c == '<' || c == '>')
                        break;
                    epEnd.MoveToAbsoluteOffset(epEnd.AbsoluteCharOffset + 1);
                }

                sel.MoveToAbsoluteOffset(epStart.AbsoluteCharOffset);
                sel.MoveToAbsoluteOffset(epEnd.AbsoluteCharOffset, true);
            }

            string original = sel.Text?.Trim();
            if (string.IsNullOrEmpty(original)) return;

            string cleaned = original;
            if (cleaned.Length > 1 &&
                cleaned.StartsWith("\"") &&
                cleaned.EndsWith("\""))
            {
                cleaned = cleaned.Substring(1, cleaned.Length - 2);
            }

            var dialog = new LocalizeDialog(cleaned);
            if (dialog.ShowDialog() != true) return;

            string prefix = dialog.KeyPrefix;
            string resxRelPath = dialog.ResxPath;
            string resName = dialog.ResourceName;
            string resValue = dialog.ResourceValue;

            if (!AddResourceEntry(dte, resName, resValue, resxRelPath))
            {
                VsShellUtilities.ShowMessageBox(
                    package,
                    $"Не удалось обновить файл ресурсов:\n{resxRelPath}",
                    "Ошибка",
                    OLEMSGICON.OLEMSGICON_CRITICAL,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                return;
            }

            foreach (var kv in dialog.LocaleValues)
            {
                var culture = kv.Key;
                var val = kv.Value;
                var dir = Path.GetDirectoryName(resxRelPath);
                var baseName = Path.GetFileNameWithoutExtension(resxRelPath);
                var localizedRel =
                    Path.Combine(dir, $"{baseName}.{culture}.resx");
                AddResourceEntry(dte, resName, val, localizedRel);
            }

            {
                string wrapStart = "@" + prefix + "[\"";
                string wrapEnd = "\"]";

                var startPtCtx = sel.TopPoint.CreateEditPoint();
                var endPtCtx = sel.BottomPoint.CreateEditPoint();
                int startOffset = startPtCtx.AbsoluteCharOffset;
                int endOffset = endPtCtx.AbsoluteCharOffset;

                string left = "", right = "";
                if (startOffset > wrapStart.Length)
                {
                    startPtCtx.MoveToAbsoluteOffset(startOffset - wrapStart.Length);
                    left = startPtCtx.GetText(wrapStart.Length);
                }
                endPtCtx.MoveToAbsoluteOffset(endOffset);
                right = endPtCtx.GetText(wrapEnd.Length);

                if (left == wrapStart && right == wrapEnd)
                    return;
            }

            bool hasQuotes = original.Length > 1 &&
                             original.StartsWith("\"") &&
                             original.EndsWith("\"");
            string inner = hasQuotes
                ? original.Substring(1, original.Length - 2)
                : original;
            string newText = $"@{prefix}[\"{inner}\"]";

            var startPt = sel.TopPoint.CreateEditPoint();
            var endPt = sel.BottomPoint.CreateEditPoint();
            int start = startPt.AbsoluteCharOffset;
            int end = endPt.AbsoluteCharOffset;
            int ds = startPt.Parent.StartPoint.AbsoluteCharOffset;
            int de = endPt.Parent.EndPoint.AbsoluteCharOffset;

            string before = "", after = "";
            if (start > ds)
            {
                startPt.MoveToAbsoluteOffset(start - 1);
                before = startPt.GetText(1);
            }
            if (end < de)
            {
                endPt.MoveToAbsoluteOffset(end);
                after = endPt.GetText(1);
            }
            if (before == "\"" && after == "\"")
            {
                start--; end++;
            }

            startPt.MoveToAbsoluteOffset(start);
            endPt.MoveToAbsoluteOffset(end);
            startPt.ReplaceText(endPt, newText,
                (int)vsEPReplaceTextOptions.vsEPReplaceTextAutoformat);
        }

        private bool AddResourceEntry(DTE2 dte,
                                      string name,
                                      string value,
                                      string resxRelPath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var proj = dte.ActiveDocument.ProjectItem.ContainingProject;
            var projDir = Path.GetDirectoryName(proj.FullName);
            var full = Path.Combine(projDir, resxRelPath);
            if (!File.Exists(full)) return false;

            var doc = XDocument.Load(full);
            var root = doc.Root;
            var data = root.Elements("data")
                           .FirstOrDefault(x =>
                               (string)x.Attribute("name") == name);

            if (data == null)
            {
                data = new XElement("data",
                    new XAttribute("name", name),
                    new XAttribute(XNamespace.Xml + "space", "preserve"),
                    new XElement("value", value));
                root.Add(data);
            }
            else
            {
                data.Element("value").Value = value;
            }

            doc.Save(full);
            return true;
        }
    }
}
