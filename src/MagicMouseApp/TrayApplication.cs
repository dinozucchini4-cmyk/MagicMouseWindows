using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MagicMouseApp.Hardware;
using OwnMagicUtilities.Core;

namespace MagicMouseApp
{
    /// <summary>
    /// Phase 4 preview UI. This milestone intentionally does not start the old
    /// MagicMouseWindows raw-PDO reader: its fixed paths and report offsets are
    /// not validated for Magic Mouse 2015 and it requests write access.
    /// </summary>
    public sealed class TrayApplication : IDisposable
    {
        private readonly NotifyIcon _trayIcon;
        private readonly BatteryMonitorService _batteryMonitor = new();
        private readonly CancellationTokenSource _shutdown = new();

        private readonly ToolStripMenuItem _statusItem;
        private readonly ToolStripMenuItem _mouseBatteryItem;
        private readonly ToolStripMenuItem _trackpadBatteryItem;
        private readonly ToolStripMenuItem _keyboardBatteryItem;
        private readonly System.Windows.Forms.Timer _batteryRefreshTimer;
        private bool _batteryRefreshRunning;
        private bool _disposed;

        public TrayApplication()
        {
            var menu = new ContextMenuStrip
            {
                BackColor = Color.FromArgb(44, 44, 46),
                ForeColor = Color.White
            };

            var titleItem = new ToolStripMenuItem("Own Magic Utilities — Phase 4 Preview")
            {
                Enabled = false,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };

            _statusItem = new ToolStripMenuItem("Battery monitor starting…")
            {
                Enabled = false
            };

            _mouseBatteryItem = new ToolStripMenuItem("Magic Mouse battery: checking…")
            {
                Enabled = false
            };
            _trackpadBatteryItem = new ToolStripMenuItem("Magic Trackpad battery: checking…")
            {
                Enabled = false
            };
            _keyboardBatteryItem = new ToolStripMenuItem("Magic Keyboard battery: checking…")
            {
                Enabled = false
            };

            var refreshItem = new ToolStripMenuItem("Refresh battery status now");
            refreshItem.Click += async (_, _) => await RefreshBatteryStatusesAsync();

            var scopeItem = new ToolStripMenuItem("About this preview…");
            scopeItem.Click += (_, _) => MessageBox.Show(
                "Own Magic Utilities — Phase 4 Preview\n\n" +
                "Included: read-only HID battery queries and explicit device/report status.\n\n" +
                "Not yet included: Magic Mouse 2015 scrolling, Trackpad gesture translation, " +
                "driver installation or driver-stack modifications.\n\n" +
                "This preview does not use the proprietary Magic Utilities raw interface. " +
                "Battery readings depend on what the current Windows HID stack exposes.",
                "Own Magic Utilities",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            var exitItem = new ToolStripMenuItem("Quit");
            exitItem.Click += (_, _) => ExitApp();

            menu.Items.Add(titleItem);
            menu.Items.Add(_statusItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_mouseBatteryItem);
            menu.Items.Add(_trackpadBatteryItem);
            menu.Items.Add(_keyboardBatteryItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(refreshItem);
            menu.Items.Add(scopeItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            var bitmap = new Bitmap(16, 16);
            using (var graphics = Graphics.FromImage(bitmap))
            using (var brush = new SolidBrush(Color.FromArgb(10, 132, 255)))
            {
                graphics.Clear(Color.Transparent);
                graphics.FillEllipse(Brushes.White, 2, 2, 12, 12);
                graphics.FillEllipse(brush, 4, 4, 8, 8);
            }

            _trayIcon = new NotifyIcon
            {
                Icon = Icon.FromHandle(bitmap.GetHicon()),
                ContextMenuStrip = menu,
                Visible = true,
                Text = "Own Magic Utilities — Phase 4"
            };
            _trayIcon.DoubleClick += (_, _) => menu.Show(Cursor.Position);

            _batteryRefreshTimer = new System.Windows.Forms.Timer { Interval = 30_000 };
            _batteryRefreshTimer.Tick += async (_, _) => await RefreshBatteryStatusesAsync();
            _batteryRefreshTimer.Start();

            _statusItem.Text = "Battery monitor active; scroll/gestures not included";
            _ = RefreshBatteryStatusesAsync();
        }

        private async Task RefreshBatteryStatusesAsync()
        {
            if (_batteryRefreshRunning || _shutdown.IsCancellationRequested || _disposed)
            {
                return;
            }

            _batteryRefreshRunning = true;
            try
            {
                var readings = await _batteryMonitor.ReadAllAsync(_shutdown.Token);
                foreach (var item in readings)
                {
                    var label = FormatBatteryText(item.DeviceName, item.Reading);
                    switch (item.Kind)
                    {
                        case AppleDeviceKind.MagicMouse2015:
                            _mouseBatteryItem.Text = label;
                            break;
                        case AppleDeviceKind.MagicTrackpad2:
                            _trackpadBatteryItem.Text = label;
                            break;
                        case AppleDeviceKind.MagicKeyboard:
                            _keyboardBatteryItem.Text = label;
                            break;
                    }
                }
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
                // Normal shutdown.
            }
            catch (Exception ex)
            {
                _statusItem.Text = "Battery refresh failed; see details in next version";
                SetBatteryText(
                    "Magic Mouse battery: temporarily unavailable",
                    "Magic Trackpad battery: temporarily unavailable",
                    "Magic Keyboard battery: temporarily unavailable");
                System.Diagnostics.Debug.WriteLine(ex);
            }
            finally
            {
                _batteryRefreshRunning = false;
            }
        }

        private static string FormatBatteryText(string name, BatteryReading reading)
        {
            if (reading.Availability == BatteryAvailability.Available &&
                reading.Percentage is byte percentage)
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
            _mouseBatteryItem.Text = mouse;
            _trackpadBatteryItem.Text = trackpad;
            _keyboardBatteryItem.Text = keyboard;
        }

        private void ExitApp()
        {
            _shutdown.Cancel();
            _batteryRefreshTimer.Stop();
            _batteryRefreshTimer.Dispose();
            _trayIcon.Visible = false;
            Application.Exit();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _shutdown.Cancel();
            _batteryRefreshTimer.Stop();
            _batteryRefreshTimer.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _shutdown.Dispose();
        }
    }
}
