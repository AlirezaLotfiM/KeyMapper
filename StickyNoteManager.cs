using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace KeyMapper
{
    public class StickyNoteManager
    {
        private static readonly Lazy<StickyNoteManager> _instance = new(() => new StickyNoteManager());
        public static StickyNoteManager Instance => _instance.Value;

        private readonly string _folderPath;
        private readonly string _filePath;
        public string MediaFolderPath { get; }

        private readonly Dictionary<string, StickyNoteWindow> _openWindows = new();
        private readonly object _syncRoot = new();

        public List<StickyNoteModel> Notes { get; private set; } = new();
        public event EventHandler? NotesUpdated;

        private StickyNoteManager()
        {
            _folderPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KeyMapper"
            );
            _filePath = Path.Combine(_folderPath, "sticky_notes.json");
            MediaFolderPath = Path.Combine(_folderPath, "NotesMedia");

            if (!Directory.Exists(_folderPath)) Directory.CreateDirectory(_folderPath);
            if (!Directory.Exists(MediaFolderPath)) Directory.CreateDirectory(MediaFolderPath);

            LoadNotes();
        }

        public void Initialize()
        {
            // Restore open notes on startup
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (Notes.Count == 0)
                {
                    // Create default welcome note if none exist
                    CreateNewNote("Welcome Note", "Welcome to KeyMapper Super Sticky Notes! 📌\n\nFeatures:\n• Always-On-Top Pinning or Stick to Desktop\n• Bi-directional RTL (Persian/Arabic) & LTR support\n• Rich formatting & Markdown tools\n• Images, Tables, & Voice Notes\n• Fold / Collapse mode", "Warm Yellow");
                }
                else
                {
                    foreach (var note in Notes.ToList())
                    {
                        if (!note.IsHidden)
                        {
                            OpenNoteWindow(note);
                        }
                    }
                }
            });
        }

        public void LoadNotes()
        {
            lock (_syncRoot)
            {
                try
                {
                    if (File.Exists(_filePath))
                    {
                        string json = File.ReadAllText(_filePath);
                        var loaded = JsonSerializer.Deserialize<List<StickyNoteModel>>(json);
                        if (loaded != null)
                        {
                            Notes = loaded;
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to load sticky notes: {ex.Message}");
                }

                Notes = new List<StickyNoteModel>();
            }
        }

        public void SaveNotes()
        {
            lock (_syncRoot)
            {
                try
                {
                    string json = JsonSerializer.Serialize(Notes, new JsonSerializerOptions { WriteIndented = true });
                    string tempPath = _filePath + ".tmp";
                    File.WriteAllText(tempPath, json);
                    File.Move(tempPath, _filePath, true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to save sticky notes: {ex.Message}");
                }
            }

            try
            {
                Application.Current.Dispatcher.Invoke(() => NotesUpdated?.Invoke(this, EventArgs.Empty));
            }
            catch { }
        }

        public StickyNoteModel CreateNewNote(string title = "Quick Note", string initialContent = "", string colorTheme = "Warm Yellow")
        {
            var note = new StickyNoteModel
            {
                Title = string.IsNullOrWhiteSpace(title) ? "Quick Note" : title,
                PlainTextContent = initialContent,
                ColorTheme = colorTheme,
                Left = 120 + (Notes.Count * 25) % 400,
                Top = 120 + (Notes.Count * 25) % 300,
                IsPinned = true
            };

            lock (_syncRoot)
            {
                Notes.Add(note);
                SaveNotes();
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                OpenNoteWindow(note);
            });

            return note;
        }

        public void OpenNoteWindow(StickyNoteModel note)
        {
            if (_openWindows.TryGetValue(note.Id, out var existing))
            {
                existing.Show();
                existing.Activate();
                return;
            }

            var win = new StickyNoteWindow(note);
            _openWindows[note.Id] = win;
            win.Closed += (s, e) => _openWindows.Remove(note.Id);
            win.Show();
        }

        public void HideNote(string id)
        {
            lock (_syncRoot)
            {
                var note = Notes.FirstOrDefault(n => n.Id == id);
                if (note != null)
                {
                    note.IsHidden = true;
                    SaveNotes();
                }
            }

            if (_openWindows.TryGetValue(id, out var win))
            {
                _openWindows.Remove(id);
                win.Close();
            }
        }

        public void UnhideNote(string id)
        {
            lock (_syncRoot)
            {
                var note = Notes.FirstOrDefault(n => n.Id == id);
                if (note != null)
                {
                    note.IsHidden = false;
                    SaveNotes();
                    Application.Current.Dispatcher.Invoke(() => OpenNoteWindow(note));
                }
            }
        }

        public void DeleteNote(string id)
        {
            lock (_syncRoot)
            {
                var note = Notes.FirstOrDefault(n => n.Id == id);
                if (note != null)
                {
                    Notes.Remove(note);
                    
                    // Clean up audio file if exists
                    if (!string.IsNullOrEmpty(note.AudioMemoPath) && File.Exists(note.AudioMemoPath))
                    {
                        try { File.Delete(note.AudioMemoPath); } catch { }
                    }

                    SaveNotes();
                }
            }

            if (_openWindows.TryGetValue(id, out var win))
            {
                _openWindows.Remove(id);
                win.Close();
            }
        }

        public void ShowAllNotes()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var note in Notes)
                {
                    OpenNoteWindow(note);
                }
            });
        }

        public void HideAllNotes()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var kvp in _openWindows.ToList())
                {
                    kvp.Value.Hide();
                }
            });
        }

        public void PinAllNotes(bool pin)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var kvp in _openWindows.Values)
                {
                    kvp.SetPinnedState(pin);
                }
            });
        }

        public void UpdateNoteModel(StickyNoteModel note)
        {
            note.UpdatedAt = DateTime.Now;
            SaveNotes();
        }
    }
}
