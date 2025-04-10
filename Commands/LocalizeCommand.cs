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

            // Получаем активный документ и выделенный текст через DTE
            DTE2 dte = (DTE2)ServiceProvider.GetService(typeof(DTE));
            var activeDoc = dte?.ActiveDocument;
            if (activeDoc == null)
            {
                return;
            }

            TextSelection selection = activeDoc.Selection as TextSelection;
            if (selection == null || string.IsNullOrWhiteSpace(selection.Text))
            {
                return;
            }

            // Сохраняем выделенный текст
            string selectedText = selection.Text.Trim();

            // Открываем диалоговое окно для ввода Name и Value
            var dialog = new LocalizeDialog(selectedText);
            if (dialog.ShowDialog() != true)
                return;

            string resourceName = dialog.ResourceName;
            string resourceValue = dialog.ResourceValue;

            // Добавляем новую запись в файл Localization.resx
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

            // Заменяем выделенный текст на <p>@Loc["ТЕКСТ"]</p>
            string replacedText = selection.Text.Replace(selectedText, $"@Loc[\"{selectedText}\"]");
            // Если выделение находится внутри тега <p>…</p> можно добавить обертку, если требуется.
            selection.Text = $"<p>{replacedText}</p>";
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
