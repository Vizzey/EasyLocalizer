using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Project = EnvDTE.Project;

namespace LocalizeExtension
{
    internal sealed class LocalizeCommand
    {
        public const int CommandId = 0x0100;
        public static readonly Guid CommandSet = new Guid("A6F9D371-C156-467C-AB35-9A8F0C3CF528");
        private readonly Package package;

        private LocalizeCommand(Package package)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            if (this.ServiceProvider.GetService(typeof(IMenuCommandService)) is OleMenuCommandService commandService)
            {
                var menuCommandID = new CommandID(CommandSet, CommandId);
                var menuItem = new MenuCommand(this.Execute, menuCommandID);
                commandService.AddCommand(menuItem);
            }
        }

        public static LocalizeCommand Instance { get; private set; }

        private IServiceProvider ServiceProvider => this.package;

        public static void Initialize(Package package)
        {
            Instance = new LocalizeCommand(package);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // 1. Получаем активный документ и выделенный текст
            DTE2 dte = (DTE2)ServiceProvider.GetService(typeof(DTE));
            var activeDoc = dte?.ActiveDocument;
            if (activeDoc == null) return;

            TextSelection selection = activeDoc.Selection as TextSelection;
            if (selection == null || string.IsNullOrWhiteSpace(selection.Text)) return;

            // Сохраняем исходное выделение и очищаем от внешних кавычек
            string originalSelection = selection.Text.Trim();
            string cleanedForDialog = originalSelection;
            if (cleanedForDialog.Length > 1 &&
                cleanedForDialog.StartsWith("\"") &&
                cleanedForDialog.EndsWith("\""))
            {
                cleanedForDialog = cleanedForDialog.Substring(1, cleanedForDialog.Length - 2);
            }

            // 2. Вызов диалога с очищенным текстом
            var dialog = new LocalizeDialog(cleanedForDialog);
            if (dialog.ShowDialog() != true) return;

            string resourceName = dialog.ResourceName;
            string resourceValue = dialog.ResourceValue;

            // 3. Добавляем запись в Localization.resx
            if (!AddResourceEntry(dte, resourceName, resourceValue))
            {
                VsShellUtilities.ShowMessageBox(
                    this.ServiceProvider,
                    "Не удалось найти или обновить файл Localization.resx",
                    "Ошибка",
                    OLEMSGICON.OLEMSGICON_CRITICAL,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                return;
            }

            // 4. Работа с абсолютными смещениями
            EnvDTE.EditPoint epStart = selection.TopPoint.CreateEditPoint();
            EnvDTE.EditPoint epEnd = selection.BottomPoint.CreateEditPoint();

            int startOffset = epStart.AbsoluteCharOffset;
            int endOffset = epEnd.AbsoluteCharOffset;
            int docStart = epStart.Parent.StartPoint.AbsoluteCharOffset;
            int docEnd = epEnd.Parent.EndPoint.AbsoluteCharOffset;

            // Читаем символы вокруг
            string charBefore = "";
            if (startOffset > docStart)
            {
                epStart.MoveToAbsoluteOffset(startOffset - 1);
                charBefore = epStart.GetText(1);
                epStart.MoveToAbsoluteOffset(startOffset);
            }

            string charAfter = "";
            if (endOffset < docEnd)
            {
                epEnd.MoveToAbsoluteOffset(endOffset);
                charAfter = epEnd.GetText(1);
                epEnd.MoveToAbsoluteOffset(endOffset);
            }

            // 5. Расширение и замена
            if (charBefore == "\"" && charAfter == "\"")
            {
                // Расширяем
                epStart.MoveToAbsoluteOffset(startOffset - 1);
                epEnd.MoveToAbsoluteOffset(endOffset + 1);

                string fullText = epStart.GetText(epEnd);
                // Убираем внешние кавычки
                string cleaned = fullText;
                if (fullText.Length > 1 && fullText.StartsWith("\"") && fullText.EndsWith("\""))
                    cleaned = fullText.Substring(1, fullText.Length - 2);

                // Вставляем @Loc
                string newText = $"@Loc[\"{cleaned}\"]";
                epStart.Delete(epEnd);
                epStart.Insert(newText);
            }
            else
            {
                // 6. Простой вариант без кавычек
                string newText = $"@Loc[\"{cleanedForDialog}\"]";
                selection.Text = newText;
            }
        }









        /// <summary>
        /// Находит файл Localization.resx относительно корня проекта и добавляет новую запись.
        /// </summary>
        private bool AddResourceEntry(DTE2 dte, string resName, string resValue)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // Получаем активный проект
            var projects = dte.Solution.Projects.Cast<Project>();
            // Простейший поиск: проходим по всем проектам и ищем файл по пути "Resources/Languages/Localization.resx"
            foreach (Project project in projects)
            {
                string projectPath = Path.GetDirectoryName(project.FullName);
                string resxRelativePath = Path.Combine(projectPath, "Resources", "Languages", "Localization.resx");
                if (File.Exists(resxRelativePath))
                {
                    try
                    {
                        // Загружаем XML документа
                        XDocument doc = XDocument.Load(resxRelativePath);

                        // Создаем новый элемент <data>
                        XElement newData = new XElement("data",
                            new XAttribute("name", resName),
                            new XAttribute(XNamespace.Xml + "space", "preserve"),
                            new XElement("value", resValue)
                        );

                        // Добавляем новый элемент перед закрывающим тегом </root>
                        doc.Root.Add(newData);
                        doc.Save(resxRelativePath);

                        // Обновляем файл в решении (если требуется)
                        var resxFileName = project.ProjectItems?.Cast<ProjectItem>()
                            .FirstOrDefault(pi => pi.Name.Equals("Localization.resx", StringComparison.OrdinalIgnoreCase))?
                            .FileNames[1];

                        return true;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("Ошибка обновления resx: " + ex.Message);
                        return false;
                    }
                }
            }
            return false;
        }
    }
}
