using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace KeyMapper
{
    public class ReminderItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Note { get; set; } = string.Empty;
        public DateTime TargetTime { get; set; }
        public bool IsTriggered { get; set; }
    }

    public sealed class ReminderService
    {
        private static readonly Lazy<ReminderService> LazyInstance = new(() => new ReminderService());
        public static ReminderService Instance => LazyInstance.Value;

        private readonly List<ReminderItem> _reminders = new();
        private readonly DispatcherTimer _checkTimer;
        private readonly string _storagePath;

        public event Action<ReminderItem>? OnReminderTriggered;

        private ReminderService()
        {
            _storagePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KeyMapper",
                "reminders.json");

            LoadReminders();

            _checkTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _checkTimer.Tick += CheckTimer_Tick;
            _checkTimer.Start();
        }

        public IReadOnlyList<ReminderItem> GetActiveReminders()
        {
            lock (_reminders)
            {
                return _reminders.Where(r => !r.IsTriggered).OrderBy(r => r.TargetTime).ToList();
            }
        }

        public ReminderItem AddReminder(string note, TimeSpan delay)
        {
            DateTime target = DateTime.Now.Add(delay);
            return AddReminderAt(note, target);
        }

        public ReminderItem AddReminderAt(string note, DateTime targetTime)
        {
            var item = new ReminderItem
            {
                Note = note,
                TargetTime = targetTime,
                IsTriggered = false
            };

            lock (_reminders)
            {
                _reminders.Add(item);
                SaveReminders();
            }

            return item;
        }

        public bool RemoveReminder(string id)
        {
            lock (_reminders)
            {
                int removed = _reminders.RemoveAll(r => r.Id == id);
                if (removed > 0)
                {
                    SaveReminders();
                    return true;
                }
            }
            return false;
        }

        private void CheckTimer_Tick(object? sender, EventArgs e)
        {
            List<ReminderItem> dueItems = new();
            DateTime now = DateTime.Now;

            lock (_reminders)
            {
                foreach (var item in _reminders)
                {
                    if (!item.IsTriggered && item.TargetTime <= now)
                    {
                        item.IsTriggered = true;
                        dueItems.Add(item);
                    }
                }

                if (dueItems.Count > 0)
                {
                    SaveReminders();
                }
            }

            foreach (var item in dueItems)
            {
                OnReminderTriggered?.Invoke(item);
            }
        }

        private void LoadReminders()
        {
            try
            {
                if (File.Exists(_storagePath))
                {
                    string json = File.ReadAllText(_storagePath);
                    var list = JsonSerializer.Deserialize<List<ReminderItem>>(json);
                    if (list != null)
                    {
                        lock (_reminders)
                        {
                            _reminders.Clear();
                            _reminders.AddRange(list);
                        }
                    }
                }
            }
            catch { }
        }

        private void SaveReminders()
        {
            try
            {
                string dir = Path.GetDirectoryName(_storagePath)!;
                Directory.CreateDirectory(dir);
                lock (_reminders)
                {
                    string json = JsonSerializer.Serialize(_reminders, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_storagePath, json);
                }
            }
            catch { }
        }
    }
}
