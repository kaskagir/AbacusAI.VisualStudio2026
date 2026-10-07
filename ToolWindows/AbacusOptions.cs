using System;
using System.ComponentModel;
using Microsoft.VisualStudio.Shell;

namespace AbacusAI.VisualStudio2026.ToolWindows
{
    /// <summary>
    /// Optionsseite für Abacus AI Extension
    /// Erreichbar über: Tools > Options > Abacus AI
    /// </summary>
    public class AbacusOptions : DialogPage
    {
        private string cliExecutable = "abacusai.exe";
        private string defaultModel = "";
        private bool autoScroll = true;
        private int maxMessageHistory = 50;
        private bool showMetadata = false;
        private int processTimeout = 300; // Sekunden
        private long maxFileSize = 10 * 1024 * 1024; // 10 MB
        private int maxWildcardMatches = 50;
        private bool autoParseFileReferences = true;

        [Category("Allgemein")]
        [DisplayName("CLI-Ausführbare Datei")]
        [Description("Pfad zur abacusai.exe oder nur der Name, wenn sie im PATH ist")]
        public string CliExecutable
        {
            get => cliExecutable;
            set => cliExecutable = value;
        }

        [Category("Allgemein")]
        [DisplayName("Standard-KI-Modell")]
        [Description("Standard-KI-Modell für neue Conversations (leer = Standardmodell der CLI)")]
        public string DefaultModel
        {
            get => defaultModel;
            set => defaultModel = value;
        }

        [Category("Verhalten")]
        [DisplayName("Automatisches Scrollen")]
        [Description("Chat-Fenster automatisch zum neuesten Eintrag scrollen")]
        public bool AutoScroll
        {
            get => autoScroll;
            set => autoScroll = value;
        }

        [Category("Verhalten")]
        [DisplayName("Maximale Nachrichtenhistorie")]
        [Description("Maximale Anzahl von Nachrichten pro Conversation (0 = unbegrenzt)")]
        public int MaxMessageHistory
        {
            get => maxMessageHistory;
            set => maxMessageHistory = Math.Max(0, value);
        }

        [Category("Verhalten")]
        [DisplayName("Metadaten anzeigen")]
        [Description("Zeige Metadaten-Zeilen (Modell, Credits, etc.) im Chat an")]
        public bool ShowMetadata
        {
            get => showMetadata;
            set => showMetadata = value;
        }

        [Category("Verhalten")]
        [DisplayName("Prozess-Timeout (Sekunden)")]
        [Description("Maximale Zeit für eine CLI-Anfrage in Sekunden (0 = unbegrenzt)")]
        public int ProcessTimeout
        {
            get => processTimeout;
            set => processTimeout = Math.Max(0, value);
        }

        [Category("Datei-Anhänge")]
        [DisplayName("Maximale Dateigröße (Bytes)")]
        [Description("Maximale Größe für Datei-Anhänge in Bytes (Standard: 10 MB)")]
        public long MaxFileSize
        {
            get => maxFileSize;
            set => maxFileSize = Math.Max(1024, value);
        }

        [Category("Datei-Anhänge")]
        [DisplayName("Maximale Wildcard-Matches")]
        [Description("Maximale Anzahl von Dateien bei Wildcard-Patterns (z.B. @*.cs)")]
        public int MaxWildcardMatches
        {
            get => maxWildcardMatches;
            set => maxWildcardMatches = Math.Max(1, value);
        }

        [Category("Datei-Anhänge")]
        [DisplayName("@filepath-Syntax automatisch parsen")]
        [Description("Automatisch @filepath-Referenzen in der Eingabe erkennen und anhängen")]
        public bool AutoParseFileReferences
        {
            get => autoParseFileReferences;
            set => autoParseFileReferences = value;
        }

        /// <summary>
        /// Lädt die Optionen aus dem Visual Studio Registry
        /// </summary>
        public override void LoadSettingsFromStorage()
        {
            base.LoadSettingsFromStorage();
        }

        /// <summary>
        /// Speichert die Optionen in der Visual Studio Registry
        /// </summary>
        public override void SaveSettingsToStorage()
        {
            base.SaveSettingsToStorage();
        }
    }
}
