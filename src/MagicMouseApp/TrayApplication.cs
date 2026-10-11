using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MagicMouseApp.Hardware;
using OwnMagicUtilities.Core;

namespace MagicMouseApp
{
    public class TrayApplication : IDisposable
    {
        private NotifyIcon   _trayIcon;
        private DeviceReader _reader;
        private ScrollEngine _scrollEngine;
        private AppSettings  _settings;
        private SettingsForm _settingsForm;
        private string       _status = "Starting...";
        private System.Windows.Forms.Timer _reconnectTimer;
        private System.Windows.Forms.Timer? _batteryRefreshTimer;
        private readonly BatteryMonitorService _batteryMonitor = new();
        private readonly CancellationTokenSource _shutdown = new();
        private bool _batteryRefreshRunning;
        private ToolStripMenuItem? _mouseBatteryItem;
        private ToolStripMenuItem? _trackpadBatteryItem;
        private ToolStripMenuItem? _keyboardBatteryItem;

        public TrayApplication()
        {
            _settings     = AppSettings.Load();
            _scrollEngine = new ScrollEngine(_settings);

            InitTrayIcon();
            InitDevice();
            StartBatteryMonitoring();
        }

        private void InitTrayIcon()
        {
            var menu = new ContextMenuStrip();
            menu.BackColor = Color.FromArgb(44, 44, 46);
            menu.ForeColor = Color.White;

            var titleItem = new ToolStripMenuItem("Own Magic Utilities") { Enabled = false };
            titleItem.Font = new Font("Segoe UI", 10f, FontStyle.Bold);

            var statusItem = new ToolStripMenuItem("● Connecting...") { Enabled = false };
            statusItem.Font = new Font("Segoe UI", 9f);

            _mouseBatteryItem = new ToolStripMenuItem("Magic Mouse battery: checking...") { Enabled = false };
            _trackpadBatteryItem = new ToolStripMenuItem("Magic Trackpad battery: checking...") { Enabled = false };
            _keyboardBatteryItem = new ToolStripMenuItem("Magic Keyboard battery: checking...") { Enabled = false };

            var settingsItem = new ToolStripMenuItem("Settings...");
            settingsItem.Click += (s, e) => ShowSettings();

            var exitItem = new ToolStripMenuItem("Quit");
            exitItem.Click += (s, e) => ExitApp();

            menu.Items.Add(titleItem);
            menu.Items.Add(statusItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_mouseBatteryItem);
            menu.Items.Add(_trackpadBatteryItem);
            menu.Items.Add(_keyboardBatteryItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(settingsItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.FillEllipse(Brushes.White, 2, 2, 12, 12);
                using var brush = new SolidBrush(Color.FromArgb(10, 132, 255));
                g.FillEllipse(brush, 4, 4, 8, 8);
            }

            _trayIcon = new NotifyIcon
            {
                Icon             = Icon.FromHandle(bmp.GetHicon()),
                ContextMenuStrip = menu,
                Visible          = true,
                Text             = "Own Magic Utilities"
            };

            _trayIcon.DoubleClick += (s, e) => ShowSettings();

            var updateTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            updateTimer.Tick += (s, e) =>
            {
                statusItem.Text = $"● {_status}";
                statusItem.ForeColor = _status.StartsWith("Connected", StringComparison.OrdinalIgnoreCase)
                    ? Color.FromArgb(48, 209, 88)
                    : Color.FromArgb(255, 159, 10);
            };
            updateTimer.Start();
        }

        private void StartBatteryMonitoring()
        {
            _batteryRefreshTimer = new System.Windows.Forms.Timer { Interval = 30_000 };
            _batteryRefreshTimer.Tick += async (_, _) => await RefreshBatteryStatusesAsync();
            _batteryRefreshTimer.Start();
            _ = RefreshBatteryStatusesAsync();
        }

        private async Task RefreshBatteryStatusesAsync()
        {
            if (_batteryRefreshRunning || _shutdown.IsCancellationRequested)
            {
                return;
            }

            _batteryRefreshRunning = true;
            try
            {
                var readings = await _batteryMonitor.ReadAllAsync(_shutdown.Token);
                foreach (var item in readings)
                {
                    var text = FormatBatteryText(item.DeviceName, item.Reading);
                    switch (item.Kind)
                    {
                        case AppleDeviceKind.MagicMouse2015:
                            if (_mouseBatteryItem is not null) _mouseBatteryItem.Text = text;
                            break;
                        case AppleDeviceKind.MagicTrackpad2:
                            if (_trackpadBatteryItem is not null) _trackpadBatteryItem.Text = text;
                            break;
                        case AppleDeviceKind.MagicKeyboard:
                            if (_keyboardBatteryItem is not null) _keyboardBatteryItem.Text = text;
                            break;
                    }
                }
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
                // Normal shutdown.
            }
            catch
            {
                SetBatteryText("Magic Mouse battery: unavailable", "Magic Trackpad battery: unavailable", "Magic Keyboard battery: unavailable");
            }
            finally
            {
                _batteryRefreshRunning = false;
            }
        }

        private static string FormatBatteryText(string name, BatteryReading reading)
        {
            if (reading.Availability == BatteryAvailability.Available && reading.Percentage is byte percentage)
            {
                var suffix = reading.IsFullyCharged == true
                    ? " (fully charged)"
                    : reading.IsCharging == true
                        ? " (charging)"
                        : string.Empty;
                return $"{name} battery: {percentage}%{suffix}";
            }

            return reading.Availability switch
            {
                BatteryAvailability.NotDetected => $"{name} battery: not detected",
                BatteryAvailability.Unsupported => $"{name} battery: report not exposed",
                BatteryAvailability.Error => $"{name} battery: read error",
                _ => $"{name} battery: temporarily unavailable"
            };
        }

        private void SetBatteryText(string mouse, string trackpad, string keyboard)
        {
            if (_mouseBatteryItem is not null) _mouseBatteryItem.Text = mouse;
            if (_trackpadBatteryItem is not null) _trackpadBatteryItem.Text = trackpad;
            if (_keyboardBatteryItem is not null) _keyboardBatteryItem.Text = keyboard;
        }

        private void InitDevice()
        {
            _reader = new DeviceReader();

            _reader.StatusChanged += status =>
            {
                _status = status;
                _settingsForm?.UpdateStatus(status);
            };

            _reader.ReportReceived += report =>
            {
                _scrollEngine.ProcessReport(report);
            };

            _reader.DeviceDisconnected += () =>
            {
                _status = "Disconnected - reconnecting...";
                ScheduleReconnect();
            };

            Connect();
        }

        private void Connect()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                if (_reader.Open())
                {
                    _status = "Connected - Magic Mouse active";
                    _reader.StartReading();
                }
                else
                {
                    _status = "Not connected";
                    ScheduleReconnect();
                }
            });
        }

        private void ScheduleReconnect()
        {
            if (_reconnectTimer != null) return;

            _reconnectTimer = new System.Windows.Forms.Timer { Interval = 3000 };
            _reconnectTimer.Tick += (s, e) =>
            {
                _reconnectTimer.Stop();
                _reconnectTimer.Dispose();
                _reconnectTimer = null;

                _reader = new DeviceReader();
                _reader.StatusChanged      += status => { _status = status; _settingsForm?.UpdateStatus(status); };
                _reader.ReportReceived     += report => _scrollEngine.ProcessReport(report);
                _reader.DeviceDisconnected += () => { _status = "Disconnected"; ScheduleReconnect(); };
                Connect();
            };
            _reconnectTimer.Start();
        }

        private void ShowSettings()
        {
            if (_settingsForm != null && !_settingsForm.IsDisposed)
            {
                _settingsForm.BringToFront();
                return;
            }

            _settingsForm = new SettingsForm(_settings, _status);
            _settingsForm.SettingsChanged += newSettings =>
            {
                _settings = newSettings;
                _scrollEngine.UpdateSettings(_settings);
            };
            _settingsForm.Show();
        }

        private void ExitApp()
        {
            _shutdown.Cancel();
            _batteryRefreshTimer?.Stop();
            _batteryRefreshTimer?.Dispose();
            _reader?.Stop();
            _trayIcon.Visible = false;
            Application.Exit();
        }

        public void Dispose()
        {
            _shutdown.Cancel();
            _batteryRefreshTimer?.Stop();
            _batteryRefreshTimer?.Dispose();
            _reader?.Stop();
            _trayIcon?.Dispose();
            _shutdown.Dispose();
        }
    }
}
