using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using Microsoft.Win32;
using Paratext.PluginInterfaces;

namespace StyleGuide
{
    /// <summary>
    /// Paratext plugin that adds Tools > Style Guide...: the Biblica style-guide modules
    /// (Punctuation so far), filled in from what the project's text shows. It reads the project
    /// and writes only the answers, in &lt;project&gt;\local\StyleGuide\, and the exported .docx.
    /// </summary>
    public class StyleGuidePlugin : IParatextStandalonePlugin
    {
        public const string PluginName = "Style Guide";
        public const string PublisherName = "Phil Leckrone (SIL)";

        private const string DefaultProjectsDir = @"C:\My Paratext 9 Projects";

        /// <summary>Each project's survey (by project ID).</summary>
        private static readonly Dictionary<string, StyleGuideSession> Sessions = new Dictionary<string, StyleGuideSession>();

        /// <summary>The open window of each project (by project ID): a second click brings it to the front.</summary>
        private static readonly Dictionary<string, StyleGuideForm> OpenWindows = new Dictionary<string, StyleGuideForm>();

        private IPluginHost host;

        public StyleGuidePlugin()
        {
        }

        public StyleGuidePlugin(IPluginHost host)
        {
            this.host = host;
        }

        public string Name
        {
            get { return PluginName; }
        }

        public Version Version
        {
            get { return new Version(Strings.ToolVersion); }
        }

        public string VersionString
        {
            get { return Strings.ToolVersion; }
        }

        public string Publisher
        {
            get { return PublisherName; }
        }

        public IEnumerable<PluginMenuEntry> PluginMenuEntries
        {
            get { yield return new PluginMenuEntry(Strings.English().MenuText, OnMenuClicked, PluginMenuLocation.ScrTextTools); }
        }

        public string GetDescription(string locale)
        {
            return Strings.For(locale).Description;
        }

        public IDataFileMerger GetMerger(IPluginHost pluginHost, string dataIdentifier)
        {
            return null;   // the answers live in local\, which Send/Receive does not share
        }

        // ------------------------------------------------------------ sessions

        private StyleGuideSession SessionFor(IProject project)
        {
            string id = project.ID ?? project.ShortName;
            StyleGuideSession session;
            if (Sessions.TryGetValue(id, out session)) return session;
            session = new StyleGuideSession(new ParatextBookSource(host, project));
            Sessions[id] = session;
            // Saving a chapter in Paratext: the survey no longer matches the text.
            project.ScriptureDataChanged += delegate(IProject p, int bookNum, int chapter) { session.OutOfDate = true; };
            return session;
        }

        // ------------------------------------------------------------ the window

        private void OnMenuClicked(IPluginHost pluginHost, IParatextChildState state)
        {
            if (host == null) host = pluginHost;
            Strings strings = Strings.For(UiLocale(host));
            try
            {
                // Paratext's Terms\Lists (Biblical Terms) sits beside the Paratext program that loaded this plugin.
                string paratextDir = System.IO.Path.GetDirectoryName(typeof(IPluginHost).Assembly.Location);
                if (paratextDir != null) GeneralSurvey.TermsFolder = System.IO.Path.Combine(System.IO.Path.Combine(paratextDir, "Terms"), "Lists");
            }
            catch (Exception) { }   // GeneralSurvey probes Program Files instead
            try
            {
                IProject project = state != null ? state.Project : null;
                if (project == null || project.IsResource)
                {
                    Report(strings, strings.OpenProjectFirst, null);
                    return;
                }
                OpenWindow(project, strings);
            }
            catch (Exception ex)
            {
                Report(strings, string.Format(strings.Error, ex.Message), ex);
            }
        }

        private void OpenWindow(IProject project, Strings strings)
        {
            string id = project.ID ?? project.ShortName;
            StyleGuideForm open;
            if (OpenWindows.TryGetValue(id, out open) && !open.IsDisposed)
            {
                if (open.WindowState == FormWindowState.Minimized) open.WindowState = FormWindowState.Normal;
                open.Activate();
                return;
            }
            StyleGuideSession session = SessionFor(project);
            if (session.ProjectDirectory == null)
            {
                Report(strings, strings.NoProjectFolder, null);
                return;
            }
            // Not modal, so the user can look at the text in Paratext beside it.
            var form = new StyleGuideForm(session, strings);
            OpenWindows[id] = form;
            form.FormClosed += delegate
            {
                OpenWindows.Remove(id);
                form.Dispose();
            };
            form.Show();
        }

        private void Report(Strings strings, string message, Exception ex)
        {
            try
            {
                if (host != null)
                    host.Log(this, message + (ex != null ? "\n" + ex : ""));
            }
            catch (Exception) { }
            MessageBox.Show(message, strings.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private static string UiLocale(IPluginHost pluginHost)
        {
            try
            {
                if (pluginHost != null && pluginHost.UserSettings != null)
                    return pluginHost.UserSettings.UiLocale;
            }
            catch (Exception) { }
            return null;
        }

        // ------------------------------------------------------------ project folder

        /// <summary>
        /// The Paratext projects folder: Settings_Directory under SOFTWARE\Paratext\8 (Paratext 9
        /// still uses that key), checked in both registry views, then for the current user.
        /// Same as the AutoCorrect plugin.
        /// </summary>
        internal static string ProjectsDirectory()
        {
            foreach (RegistryView view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                string dir = ReadSettingsDirectory(RegistryHive.LocalMachine, view);
                if (dir != null) return dir;
            }
            string user = ReadSettingsDirectory(RegistryHive.CurrentUser, RegistryView.Default);
            return user ?? DefaultProjectsDir;
        }

        private static string ReadSettingsDirectory(RegistryHive hive, RegistryView view)
        {
            try
            {
                using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view))
                using (RegistryKey key = root.OpenSubKey(@"SOFTWARE\Paratext\8"))
                {
                    if (key == null) return null;
                    string dir = key.GetValue("Settings_Directory") as string;
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        return dir.TrimEnd('\\');
                }
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>
        /// The project's folder: projectsDir\shortName when its Settings.xml Guid matches the
        /// project ID (or has no readable Guid); otherwise the subfolder whose Guid matches.
        /// Same as the AutoCorrect plugin.
        /// </summary>
        internal static string ResolveProjectDir(string projectsDir, string shortName, string projectId)
        {
            if (string.IsNullOrEmpty(projectsDir) || !Directory.Exists(projectsDir)) return null;

            string byName = null;
            if (!string.IsNullOrEmpty(shortName))
            {
                string candidate = Path.Combine(projectsDir, shortName);
                string settings = Path.Combine(candidate, "Settings.xml");
                if (File.Exists(settings))
                {
                    byName = candidate;
                    string guid = ReadProjectGuid(settings);
                    if (guid == null || string.IsNullOrEmpty(projectId) || SameId(guid, projectId))
                        return candidate;
                }
            }

            if (string.IsNullOrEmpty(projectId)) return byName;
            try
            {
                foreach (string dir in Directory.GetDirectories(projectsDir))
                {
                    string settings = Path.Combine(dir, "Settings.xml");
                    if (File.Exists(settings) && SameId(ReadProjectGuid(settings), projectId))
                        return dir;
                }
            }
            catch (Exception) { }
            return byName;
        }

        private static bool SameId(string a, string b)
        {
            return a != null && b != null && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        internal static string ReadProjectGuid(string settingsPath)
        {
            try
            {
                var xmlSettings = new XmlReaderSettings();
                xmlSettings.DtdProcessing = DtdProcessing.Ignore;
                using (XmlReader reader = XmlReader.Create(settingsPath, xmlSettings))
                {
                    if (reader.ReadToFollowing("Guid"))
                    {
                        string guid = reader.ReadElementContentAsString();
                        return string.IsNullOrEmpty(guid) ? null : guid.Trim();
                    }
                }
            }
            catch (Exception) { }
            return null;
        }
    }

    /// <summary>
    /// The plugin API's view of a project, for reading only. Text comes through Paratext
    /// (GetUSFM), so it is what Paratext's own editor shows.
    /// </summary>
    internal sealed class ParatextBookSource : IBookSource
    {
        private readonly IPluginHost host;
        private readonly IProject project;
        private readonly List<BookId> books;
        private string projectDirectory;
        private bool projectDirectoryResolved;

        public ParatextBookSource(IPluginHost host, IProject project)
        {
            this.host = host;
            this.project = project;
            books = project.AvailableBooks.OrderBy(b => b.Number).Select(b => new BookId(b.Number, b.Code)).ToList();
        }

        public string ProjectName
        {
            get { return project.ShortName; }
        }

        public IList<BookId> Books
        {
            get { return books; }
        }

        public string GetBook(int bookNum)
        {
            return project.GetUSFM(bookNum, 0);
        }

        public string ProjectDirectory
        {
            get
            {
                if (!projectDirectoryResolved)
                {
                    projectDirectory = StyleGuidePlugin.ResolveProjectDir(
                        StyleGuidePlugin.ProjectsDirectory(), project.ShortName, project.ID);
                    projectDirectoryResolved = true;
                }
                return projectDirectory;
            }
        }

        /// <summary>Moves the project's open text window (or a new one) to the verse.</summary>
        public void GoTo(int bookNum, int chapter, int verse)
        {
            IVerseRef reference = project.Versification.CreateReference(bookNum, Math.Max(1, chapter), verse);
            host.OpenTextWindowFor(project, OpenWindowBehavior.UseExisting, reference);
        }
    }
}
