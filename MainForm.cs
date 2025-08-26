using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ThreadPriorityManager
{
    internal static class AppConfig
    {
        public const int UI_REFRESH_INTERVAL_MS = 10000;
        public const int MINIMUM_WINDOW_WIDTH = 1024;
        public const int MINIMUM_WINDOW_HEIGHT = 950; // Increased for better visibility
        public const int THREAD_LIST_HEIGHT = 350; // Increased height
        public const int MAX_ACTION_LOG_ENTRIES = 50;

        public const uint PROCESS_SET_INFORMATION = 0x0200;
        public const uint PROCESS_QUERY_INFORMATION = 0x0400;
        public const uint THREAD_SET_INFORMATION = 0x0020;
        public const uint THREAD_QUERY_INFORMATION = 0x0040;

        public const uint IDLE_PRIORITY_CLASS = 0x40;
        public const uint BELOW_NORMAL_PRIORITY_CLASS = 0x4000;
        public const uint NORMAL_PRIORITY_CLASS = 0x20;
        public const uint ABOVE_NORMAL_PRIORITY_CLASS = 0x8000;
        public const uint HIGH_PRIORITY_CLASS = 0x80;
        public const uint REALTIME_PRIORITY_CLASS = 0x100;

        public const int THREAD_PRIORITY_IDLE = -15;
        public const int THREAD_PRIORITY_LOWEST = -2;
        public const int THREAD_PRIORITY_BELOW_NORMAL = -1;
        public const int THREAD_PRIORITY_NORMAL = 0;
        public const int THREAD_PRIORITY_ABOVE_NORMAL = 1;
        public const int THREAD_PRIORITY_HIGHEST = 2;
        public const int THREAD_PRIORITY_TIME_CRITICAL = 15;
    }

    internal static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetPriorityClass(IntPtr hProcess, uint dwPriorityClass);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint GetPriorityClass(IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenThread(uint dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetThreadPriority(IntPtr hThread, int nPriority);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern int GetThreadPriority(IntPtr hThread);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetThreadPriorityBoost(IntPtr hThread, bool DisablePriorityBoost);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetProcessPriorityBoost(IntPtr hProcess, bool DisablePriorityBoost);
    }

    public partial class MainForm : Form
    {
        private ComboBox processComboBox = null!;
        private Label instanceCountLabel = null!;
        private ComboBox processPriorityComboBox = null!;
        private ComboBox threadPriorityComboBox = null!;
        private ListView threadsListView = null!;
        private Button refreshButton = null!;
        private Button setProcessPriorityButton = null!;
        private Button setThreadPriorityButton = null!;
        private Label statusLabel = null!;
        private ListBox actionsListBox = null!;
        private System.Windows.Forms.Timer refreshTimer = null!;

        private CheckBox applyToAllCheckBox = null!;
        private CheckBox applyToAllProcessesCheckBox = null!;
        private CheckBox lockPriorityCheckBox = null!;
        private NumericUpDown monitorIntervalUpDown = null!;

        private MonitorService? monitorService;
        private readonly ColumnSortManager columnSortManager = new ColumnSortManager();
        private int? selectedProcessId; // Store selected process ID to preserve selection
        private readonly ColorScheme colorScheme = new ColorScheme();

        public MainForm()
        {
            InitializeComponent();
            ConfigureWindow();
            CheckAdminRights();
            InitializeTimer();
            LoadInitialData();
        }

        private void ConfigureWindow()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            UpdateStyles();
        }

        private void InitializeTimer()
        {
            refreshTimer = new System.Windows.Forms.Timer();
            refreshTimer.Interval = AppConfig.UI_REFRESH_INTERVAL_MS;
            refreshTimer.Tick += async (s, e) => await LoadProcessesAsync();
            refreshTimer.Start();

            processComboBox.DropDown += (s, e) => refreshTimer?.Stop();
            processComboBox.DropDownClosed += (s, e) => refreshTimer?.Start();
        }

        private async void LoadInitialData()
        {
            await LoadProcessesAsync();
        }

        private void InitializeComponent()
        {
            ConfigureMainWindow();
            var mainLayout = CreateMainLayout();
            var leftPanel = CreateLeftPanel();
            var rightPanel = CreateRightPanel();

            mainLayout.Controls.Add(leftPanel, 0, 0);
            mainLayout.Controls.Add(rightPanel, 1, 0);
            Controls.Add(mainLayout);
        }

        private void ConfigureMainWindow()
        {
            Text = "Thread Priority Manager";
            Size = new Size(AppConfig.MINIMUM_WINDOW_WIDTH, AppConfig.MINIMUM_WINDOW_HEIGHT);
            MinimumSize = new Size(AppConfig.MINIMUM_WINDOW_WIDTH, AppConfig.MINIMUM_WINDOW_HEIGHT);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = colorScheme.BackgroundColor;
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
            ForeColor = colorScheme.TextColor;
        }

        private TableLayoutPanel CreateMainLayout()
        {
            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(12),
                BackColor = Color.Transparent
            };
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62F));
            return mainLayout;
        }

        private Panel CreateLeftPanel()
        {
            var leftPanel = new Panel { Dock = DockStyle.Fill, BackColor = colorScheme.PanelColor, Padding = new Padding(12) };
            var leftStack = CreateLeftStackLayout();

            AddProcessControls(leftStack);
            AddThreadControls(leftStack);
            AddOptionsControls(leftStack);
            AddActionButtons(leftStack);

            leftPanel.Controls.Add(leftStack);
            return leftPanel;
        }

        private TableLayoutPanel CreateLeftStackLayout()
        {
            var leftStack = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 16, ColumnCount = 1 };
            for (int i = 0; i < 16; i++)
                leftStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            return leftStack;
        }

        private void AddProcessControls(TableLayoutPanel leftStack)
        {
            leftStack.Controls.Add(CreateLabel("Process:"));
            processComboBox = CreateComboBox();
            processComboBox.SelectedIndexChanged += ProcessComboBox_SelectedIndexChanged;
            leftStack.Controls.Add(processComboBox);

            instanceCountLabel = CreateInstanceCountLabel();
            leftStack.Controls.Add(instanceCountLabel);

            leftStack.Controls.Add(CreateLabel("Process Priority:"));
            processPriorityComboBox = CreateComboBox();
            LoadProcessPriorities();
            leftStack.Controls.Add(processPriorityComboBox);

            setProcessPriorityButton = CreateAccentButton("Set Process Priority");
            setProcessPriorityButton.Click += SetProcessPriorityButton_Click;
            leftStack.Controls.Add(setProcessPriorityButton);

            leftStack.Controls.Add(CreateDivider());
        }

        private void AddThreadControls(TableLayoutPanel leftStack)
        {
            var threadsLabel = CreateLabel("Threads (CTRL+Click for multiple, click headers to sort):");
            leftStack.Controls.Add(threadsLabel);

            threadsListView = CreateThreadsListView();
            leftStack.Controls.Add(threadsListView);

            leftStack.Controls.Add(CreateLabel("Thread Priority:"));
            threadPriorityComboBox = CreateComboBox();
            LoadThreadPriorities();
            leftStack.Controls.Add(threadPriorityComboBox);
        }

        private ListView CreateThreadsListView()
        {
            var listView = new ListView
            {
                Dock = DockStyle.Fill,
                BackColor = colorScheme.ControlBackgroundColor,
                ForeColor = colorScheme.TextColor,
                BorderStyle = BorderStyle.None,
                Height = AppConfig.THREAD_LIST_HEIGHT,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = true,
                GridLines = true
            };

            listView.Columns.Add("Thread ID", 100);
            listView.Columns.Add("Priority", 120);
            listView.Columns.Add("Priority Value", 100);

            // Enable column click sorting
            listView.ColumnClick += ThreadsListView_ColumnClick;

            return listView;
        }

        private void ThreadsListView_ColumnClick(object? sender, ColumnClickEventArgs e)
        {
            SortThreadsByColumn(e.Column);
        }

        private void SortThreadsByColumn(int columnIndex)
        {
            var sortOrder = columnSortManager.GetNextSortOrder(columnIndex);
            var items = threadsListView.Items.Cast<ListViewItem>()
                .Where(item => item.Tag is ThreadInfo)
                .Select(item => new { Item = item, Thread = item.Tag as ThreadInfo })
                .Where(x => x.Thread != null)
                .ToList();

            var sortedItems = columnIndex switch
            {
                0 => sortOrder == SortOrder.Ascending
                    ? items.OrderBy(x => x.Thread!.Id)
                    : items.OrderByDescending(x => x.Thread!.Id),
                1 => sortOrder == SortOrder.Ascending
                    ? items.OrderBy(x => x.Thread!.Priority)
                    : items.OrderByDescending(x => x.Thread!.Priority),
                2 => sortOrder == SortOrder.Ascending
                    ? items.OrderBy(x => x.Thread!.PriorityValue)
                    : items.OrderByDescending(x => x.Thread!.PriorityValue),
                _ => items.OrderBy(x => x.Thread!.Id)
            };

            RefreshThreadsListView(sortedItems.Select(x => x.Item).ToList());
        }

        private void RefreshThreadsListView(List<ListViewItem> sortedItems)
        {
            threadsListView.BeginUpdate();
            threadsListView.Items.Clear();

            foreach (var item in sortedItems)
            {
                threadsListView.Items.Add(item);
            }

            threadsListView.EndUpdate();
        }

        private void AddOptionsControls(TableLayoutPanel leftStack)
        {
            var optionsFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown };

            applyToAllCheckBox = CreateCheckBox("Apply to ALL threads (per process)");
            applyToAllProcessesCheckBox = CreateCheckBox("Apply to ALL processes with same name");

            optionsFlow.Controls.Add(applyToAllCheckBox);
            optionsFlow.Controls.Add(applyToAllProcessesCheckBox);
            leftStack.Controls.Add(optionsFlow);

            var monitorPanel = CreateMonitorPanel();
            leftStack.Controls.Add(monitorPanel);
        }

        private Panel CreateMonitorPanel()
        {
            lockPriorityCheckBox = CreateCheckBox("Keep priority locked (monitor)");
            lockPriorityCheckBox.CheckedChanged += LockPriorityCheckBox_CheckedChanged;

            var monitorPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            monitorPanel.Controls.Add(lockPriorityCheckBox);
            monitorPanel.Controls.Add(new Label { Text = "Interval (s):", ForeColor = colorScheme.TextColor, AutoSize = true, Margin = new Padding(8, 6, 0, 0) });

            monitorIntervalUpDown = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 3600,
                Value = 2,
                Width = 70,
                BackColor = colorScheme.ControlBackgroundColor,
                ForeColor = colorScheme.TextColor,
                BorderStyle = BorderStyle.None
            };
            monitorPanel.Controls.Add(monitorIntervalUpDown);
            return monitorPanel;
        }

        private void AddActionButtons(TableLayoutPanel leftStack)
        {
            setThreadPriorityButton = CreateAccentButton("Apply Priority");
            setThreadPriorityButton.Click += SetThreadPriorityButton_Click;
            setThreadPriorityButton.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            leftStack.Controls.Add(setThreadPriorityButton);
        }

        private Panel CreateRightPanel()
        {
            var rightPanel = new Panel { Dock = DockStyle.Fill, BackColor = colorScheme.PanelColor, Padding = new Padding(12) };
            var rightStack = CreateRightStackLayout();

            AddStatusControls(rightStack);
            AddActionsLog(rightStack);
            AddQuickActions(rightStack);

            rightPanel.Controls.Add(rightStack);
            return rightPanel;
        }

        private TableLayoutPanel CreateRightStackLayout()
        {
            var rightStack = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6, ColumnCount = 1 };
            rightStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rightStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rightStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rightStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            rightStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rightStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            return rightStack;
        }

        private void AddStatusControls(TableLayoutPanel rightStack)
        {
            var headerLabel = new Label
            {
                Text = "Status",
                Dock = DockStyle.Top,
                Font = new Font(Font.FontFamily, 14F, FontStyle.Bold),
                ForeColor = colorScheme.TextColor,
                AutoSize = true
            };
            rightStack.Controls.Add(headerLabel);

            statusLabel = new Label
            {
                Text = "Ready",
                Dock = DockStyle.Top,
                ForeColor = colorScheme.TextColor,
                AutoSize = true
            };
            rightStack.Controls.Add(statusLabel);
        }

        private void AddActionsLog(TableLayoutPanel rightStack)
        {
            var logLabel = new Label { Text = "Recent Actions", ForeColor = colorScheme.TextColor, AutoSize = true };
            rightStack.Controls.Add(logLabel);

            actionsListBox = new ListBox
            {
                Dock = DockStyle.Fill,
                BackColor = colorScheme.ControlBackgroundColor,
                ForeColor = colorScheme.TextColor,
                BorderStyle = BorderStyle.None
            };
            rightStack.Controls.Add(actionsListBox);
        }

        private void AddQuickActions(TableLayoutPanel rightStack)
        {
            var quickPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };

            refreshButton = CreateSecondaryButton("Refresh");
            refreshButton.Click += RefreshButton_Click;
            quickPanel.Controls.Add(refreshButton);

            var adminLabel = new Label
            {
                Text = "Run as Administrator to modify system processes",
                ForeColor = Color.FromArgb(180, 180, 180),
                AutoSize = true,
                Margin = new Padding(10, 8, 0, 0)
            };
            quickPanel.Controls.Add(adminLabel);

            rightStack.Controls.Add(quickPanel);
        }

        private Label CreateLabel(string text) => new Label
        {
            Text = text,
            ForeColor = colorScheme.TextColor,
            AutoSize = true,
            Margin = new Padding(3, 8, 3, 3)
        };

        private Label CreateInstanceCountLabel() => new Label
        {
            Text = "Instances: 0",
            ForeColor = colorScheme.TextColor,
            Font = new Font(Font.FontFamily, 9F, FontStyle.Regular),
            AutoSize = true
        };

        private ComboBox CreateComboBox() => new ComboBox
        {
            Dock = DockStyle.Top,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = colorScheme.ControlBackgroundColor,
            ForeColor = colorScheme.TextColor,
            Height = 30
        };

        private CheckBox CreateCheckBox(string text) => new CheckBox
        {
            Text = text,
            ForeColor = colorScheme.TextColor,
            AutoSize = true,
            BackColor = Color.Transparent,
            Margin = new Padding(6, 6, 6, 6)
        };

        private Panel CreateDivider() => new Panel
        {
            Height = 8,
            Dock = DockStyle.Top,
            BackColor = Color.Transparent
        };

        private Button CreateAccentButton(string text)
        {
            var button = new Button
            {
                Text = text,
                Dock = DockStyle.Top,
                Height = 36,
                FlatStyle = FlatStyle.Flat,
                BackColor = colorScheme.AccentColor,
                ForeColor = Color.White
            };
            button.FlatAppearance.BorderSize = 0;
            button.MouseEnter += (s, e) => button.BackColor = ControlPaint.Light(colorScheme.AccentColor);
            button.MouseLeave += (s, e) => button.BackColor = colorScheme.AccentColor;
            return button;
        }

        private Button CreateSecondaryButton(string text)
        {
            var button = new Button
            {
                Text = text,
                Height = 32,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(70, 70, 70),
                ForeColor = colorScheme.TextColor,
                Margin = new Padding(4)
            };
            button.FlatAppearance.BorderSize = 0;
            button.MouseEnter += (s, e) => button.BackColor = ControlPaint.Light(Color.FromArgb(70, 70, 70));
            button.MouseLeave += (s, e) => button.BackColor = Color.FromArgb(70, 70, 70);
            return button;
        }

        private async Task LoadProcessesAsync()
        {
            var wasRunning = refreshTimer?.Enabled ?? false;
            try
            {
                refreshTimer?.Stop();
                SetUiEnabled(false, "Loading processes...");

                var processes = await Task.Run(() => Process.GetProcesses()
                    .Where(p => !string.IsNullOrEmpty(p.ProcessName))
                    .OrderBy(p => p.ProcessName)
                    .Select(p => new ProcessInfo(p))
                    .ToList());

                processComboBox.InvokeIfRequired(() =>
                {
                    var hasChanges = HasProcessListChanged(processes);
                    if (hasChanges)
                    {
                        UpdateProcessComboBoxWithSelection(processes);
                    }

                    statusLabel.Text = $"Loaded {processes.Count} processes";
                    SetUiEnabled(true);
                });
            }
            catch (Exception ex)
            {
                statusLabel.InvokeIfRequired(() => statusLabel.Text = $"Error loading processes: {ex.Message}");
                SetUiEnabled(true);
            }
            finally
            {
                if (wasRunning) refreshTimer?.Start();
            }
        }

        private bool HasProcessListChanged(List<ProcessInfo> newProcesses)
        {
            if (processComboBox.Items.Count != newProcesses.Count)
                return true;

            for (int i = 0; i < newProcesses.Count; i++)
            {
                if (!(processComboBox.Items[i] is ProcessInfo existingProcess && existingProcess.Id == newProcesses[i].Id))
                    return true;
            }

            return false;
        }

        private void UpdateProcessComboBoxWithSelection(List<ProcessInfo> processes)
        {
            // Store current selection
            if (processComboBox.SelectedItem is ProcessInfo currentSelection)
            {
                selectedProcessId = currentSelection.Id;
            }

            processComboBox.BeginUpdate();
            processComboBox.Items.Clear();

            ProcessInfo? itemToSelect = null;
            foreach (var process in processes)
            {
                processComboBox.Items.Add(process);

                // Find the previously selected process
                if (selectedProcessId.HasValue && process.Id == selectedProcessId.Value)
                {
                    itemToSelect = process;
                }
            }

            // Restore selection if the process still exists
            if (itemToSelect != null)
            {
                processComboBox.SelectedItem = itemToSelect;
            }

            processComboBox.EndUpdate();
        }

        private void LoadProcessPriorities()
        {
            var priorities = new[]
            {
                new PriorityInfo("Idle", AppConfig.IDLE_PRIORITY_CLASS),
                new PriorityInfo("Below Normal", AppConfig.BELOW_NORMAL_PRIORITY_CLASS),
                new PriorityInfo("Normal", AppConfig.NORMAL_PRIORITY_CLASS),
                new PriorityInfo("Above Normal", AppConfig.ABOVE_NORMAL_PRIORITY_CLASS),
                new PriorityInfo("High", AppConfig.HIGH_PRIORITY_CLASS),
                new PriorityInfo("Realtime", AppConfig.REALTIME_PRIORITY_CLASS)
            };

            processPriorityComboBox.Items.Clear();
            foreach (var priority in priorities)
                processPriorityComboBox.Items.Add(priority);

            processPriorityComboBox.SelectedIndex = 2; // Normal
        }

        private void LoadThreadPriorities()
        {
            var priorities = new[]
            {
                new ThreadPriorityInfo("Idle", AppConfig.THREAD_PRIORITY_IDLE),
                new ThreadPriorityInfo("Lowest", AppConfig.THREAD_PRIORITY_LOWEST),
                new ThreadPriorityInfo("Below Normal", AppConfig.THREAD_PRIORITY_BELOW_NORMAL),
                new ThreadPriorityInfo("Normal", AppConfig.THREAD_PRIORITY_NORMAL),
                new ThreadPriorityInfo("Above Normal", AppConfig.THREAD_PRIORITY_ABOVE_NORMAL),
                new ThreadPriorityInfo("Highest", AppConfig.THREAD_PRIORITY_HIGHEST),
                new ThreadPriorityInfo("Time Critical", AppConfig.THREAD_PRIORITY_TIME_CRITICAL)
            };

            threadPriorityComboBox.Items.Clear();
            foreach (var priority in priorities)
                threadPriorityComboBox.Items.Add(priority);

            threadPriorityComboBox.SelectedIndex = 3; // Normal
        }

        private async void ProcessComboBox_SelectedIndexChanged(object? sender, EventArgs? e)
        {
            await LoadThreadsForSelectedProcessAsync();
            UpdateInstanceCount();
            StopMonitorIfActive();
        }

        private void UpdateInstanceCount()
        {
            if (processComboBox.SelectedItem is ProcessInfo processInfo)
            {
                var count = Process.GetProcessesByName(processInfo.Name).Length;
                instanceCountLabel.Text = $"Instances: {count}";
            }
            else
            {
                instanceCountLabel.Text = "Instances: 0";
            }
        }

        private void StopMonitorIfActive()
        {
            if (lockPriorityCheckBox.Checked)
                StopMonitorService();
        }

        private async Task LoadThreadsForSelectedProcessAsync()
        {
            var wasRunning = refreshTimer?.Enabled ?? false;
            try
            {
                refreshTimer?.Stop();
                threadsListView.InvokeIfRequired(() =>
                {
                    threadsListView.BeginUpdate();
                    threadsListView.Items.Clear();
                });

                if (processComboBox.SelectedItem is ProcessInfo processInfo)
                {
                    SetUiEnabled(false, $"Loading threads for {processInfo.Name}...");
                    var threads = await LoadThreadsForProcess(processInfo);

                    threadsListView.InvokeIfRequired(() =>
                    {
                        PopulateThreadsListView(threads);
                        threadsListView.EndUpdate();
                        statusLabel.Text = $"Loaded {threads.Count} threads for {processInfo.Name}";
                        SetUiEnabled(true);
                    });
                }
                else
                {
                    threadsListView.InvokeIfRequired(() =>
                    {
                        threadsListView.EndUpdate();
                        statusLabel.Text = "No process selected";
                        SetUiEnabled(true);
                    });
                }
            }
            catch (Exception ex)
            {
                statusLabel.InvokeIfRequired(() => statusLabel.Text = $"Error loading threads: {ex.Message}");
                SetUiEnabled(true);
            }
            finally
            {
                if (wasRunning) refreshTimer?.Start();
            }
        }

        private async Task<List<ThreadInfo>> LoadThreadsForProcess(ProcessInfo processInfo)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var process = Process.GetProcessById(processInfo.Id);
                    var threadInfos = new List<ThreadInfo>();

                    foreach (ProcessThread thread in process.Threads)
                    {
                        var threadInfo = CreateThreadInfo(thread);
                        if (threadInfo != null)
                        {
                            threadInfos.Add(threadInfo);
                        }
                    }

                    return threadInfos;
                }
                catch
                {
                    return new List<ThreadInfo>();
                }
            });
        }

        private ThreadInfo? CreateThreadInfo(ProcessThread? thread)
        {
            if (thread == null) return null;

            try
            {
                var priorityString = GetThreadPriorityString(thread);
                var priorityValue = GetThreadPriorityValue(thread);
                return new ThreadInfo(thread.Id, priorityString, priorityValue);
            }
            catch
            {
                return null;
            }
        }

        private void PopulateThreadsListView(List<ThreadInfo> threads)
        {
            foreach (var thread in threads)
            {
                var item = new ListViewItem(thread.Id.ToString());
                item.SubItems.Add(thread.Priority);
                item.SubItems.Add(thread.PriorityValue.ToString());
                item.Tag = thread;
                threadsListView.Items.Add(item);
            }
        }

        private string GetThreadPriorityString(ProcessThread thread)
        {
            try
            {
                var handle = NativeMethods.OpenThread(AppConfig.THREAD_QUERY_INFORMATION, false, (uint)thread.Id);
                if (handle != IntPtr.Zero)
                {
                    var priority = NativeMethods.GetThreadPriority(handle);
                    NativeMethods.CloseHandle(handle);

                    return priority switch
                    {
                        AppConfig.THREAD_PRIORITY_IDLE => "Idle",
                        AppConfig.THREAD_PRIORITY_LOWEST => "Lowest",
                        AppConfig.THREAD_PRIORITY_BELOW_NORMAL => "Below Normal",
                        AppConfig.THREAD_PRIORITY_NORMAL => "Normal",
                        AppConfig.THREAD_PRIORITY_ABOVE_NORMAL => "Above Normal",
                        AppConfig.THREAD_PRIORITY_HIGHEST => "Highest",
                        AppConfig.THREAD_PRIORITY_TIME_CRITICAL => "Time Critical",
                        _ => $"Custom ({priority})"
                    };
                }
            }
            catch { }

            return "Unknown";
        }

        private int GetThreadPriorityValue(ProcessThread thread)
        {
            try
            {
                var handle = NativeMethods.OpenThread(AppConfig.THREAD_QUERY_INFORMATION, false, (uint)thread.Id);
                if (handle != IntPtr.Zero)
                {
                    var priority = NativeMethods.GetThreadPriority(handle);
                    NativeMethods.CloseHandle(handle);
                    return priority;
                }
            }
            catch { }

            return 0;
        }

        private async void RefreshButton_Click(object? sender, EventArgs? e)
        {
            await LoadProcessesAsync();
            await LoadThreadsForSelectedProcessAsync();
            LogAction("Manual refresh completed");
        }

        private void LogAction(string message)
        {
            actionsListBox.InvokeIfRequired(() =>
            {
                actionsListBox.Items.Insert(0, $"{DateTime.Now:T} - {message}");
                if (actionsListBox.Items.Count > AppConfig.MAX_ACTION_LOG_ENTRIES)
                    actionsListBox.Items.RemoveAt(actionsListBox.Items.Count - 1);
            });
        }

        private async void SetProcessPriorityButton_Click(object? sender, EventArgs? e)
        {
            if (!ValidateProcessPrioritySelection(out var processInfo, out var priorityInfo))
                return;

            SetUiEnabled(false, "Applying process priority...");

            await Task.Run(() =>
            {
                try
                {
                    ApplyProcessPriority(processInfo!, priorityInfo!);
                }
                catch (Exception ex)
                {
                    statusLabel.InvokeIfRequired(() => statusLabel.Text = $"Error: {ex.Message}");
                }
                finally
                {
                    SetUiEnabled(true);
                }
            });
        }

        private bool ValidateProcessPrioritySelection(out ProcessInfo? processInfo, out PriorityInfo? priorityInfo)
        {
            processInfo = processComboBox.SelectedItem as ProcessInfo;
            priorityInfo = processPriorityComboBox.SelectedItem as PriorityInfo;

            if (processInfo == null || priorityInfo == null)
            {
                statusLabel.Text = "Select a process and a priority.";
                return false;
            }

            return true;
        }

        private void ApplyProcessPriority(ProcessInfo processInfo, PriorityInfo priorityInfo)
        {
            using var handle = new SafeHandleWrap(NativeMethods.OpenProcess(AppConfig.PROCESS_SET_INFORMATION, false, processInfo.Id));
            if (handle.Handle != IntPtr.Zero)
            {
                NativeMethods.SetPriorityClass(handle.Handle, priorityInfo.Value);
                statusLabel.InvokeIfRequired(() => statusLabel.Text = $"Set process priority of {processInfo.Name} to {priorityInfo.Name}");
                LogAction($"Process priority set: {processInfo.Name} -> {priorityInfo.Name}");
            }
            else
            {
                statusLabel.InvokeIfRequired(() => statusLabel.Text = "Cannot open process (access denied?)");
            }
        }

        private async void SetThreadPriorityButton_Click(object? sender, EventArgs? e)
        {
            if (!ValidateThreadPrioritySelection(out var threadPriorityInfo, out var processInfo))
                return;

            SetUiEnabled(false, "Applying priority...");

            await Task.Run(() =>
            {
                try
                {
                    ApplyThreadPriority(processInfo!, threadPriorityInfo!);
                }
                catch (Exception ex)
                {
                    statusLabel.InvokeIfRequired(() => statusLabel.Text = $"Error: {ex.Message}");
                }
                finally
                {
                    SetUiEnabled(true);
                }
            });

            await LoadThreadsForSelectedProcessAsync();
        }

        private bool ValidateThreadPrioritySelection(out ThreadPriorityInfo? threadPriorityInfo, out ProcessInfo? processInfo)
        {
            threadPriorityInfo = threadPriorityComboBox.SelectedItem as ThreadPriorityInfo;
            processInfo = processComboBox.SelectedItem as ProcessInfo;

            if (threadPriorityInfo == null)
            {
                statusLabel.Text = "Please choose a thread priority to apply.";
                return false;
            }

            if (processInfo == null)
            {
                statusLabel.Text = "Please select a process first.";
                return false;
            }

            return true;
        }

        private void ApplyThreadPriority(ProcessInfo processInfo, ThreadPriorityInfo threadPriorityInfo)
        {
            int totalChanged = 0;

            if (applyToAllProcessesCheckBox.Checked)
            {
                totalChanged = ApplyPriorityToAllProcessInstances(processInfo.Name, threadPriorityInfo);
                statusLabel.InvokeIfRequired(() => statusLabel.Text = $"Applied {threadPriorityInfo.Name} to {totalChanged} threads across all '{processInfo.Name}' processes");
                LogAction($"Priority applied to all processes: {processInfo.Name} -> {threadPriorityInfo.Name} ({totalChanged} threads)");
            }
            else
            {
                totalChanged = ApplyPriorityToSingleProcess(processInfo, threadPriorityInfo);
            }
        }

        private int ApplyPriorityToAllProcessInstances(string processName, ThreadPriorityInfo threadPriorityInfo)
        {
            var processes = Process.GetProcessesByName(processName);
            int totalChanged = 0;

            foreach (var process in processes)
            {
                foreach (ProcessThread thread in process.Threads)
                {
                    if (SetSingleThreadPriority((uint)thread.Id, threadPriorityInfo.Value, disableBoost: true))
                        totalChanged++;
                }
            }

            return totalChanged;
        }

        private int ApplyPriorityToSingleProcess(ProcessInfo processInfo, ThreadPriorityInfo threadPriorityInfo)
        {
            var process = Process.GetProcessById(processInfo.Id);
            int totalChanged = 0;

            if (applyToAllCheckBox.Checked)
            {
                totalChanged = ApplyPriorityToAllThreadsInProcess(process, threadPriorityInfo);
                statusLabel.InvokeIfRequired(() => statusLabel.Text = $"Applied {threadPriorityInfo.Name} to {totalChanged} threads of {processInfo.Name}");
                LogAction($"Priority applied to all threads: {processInfo.Name} -> {threadPriorityInfo.Name} ({totalChanged} threads)");
            }
            else
            {
                totalChanged = ApplyPriorityToSelectedThreads(threadPriorityInfo);
            }

            return totalChanged;
        }

        private int ApplyPriorityToAllThreadsInProcess(Process process, ThreadPriorityInfo threadPriorityInfo)
        {
            int totalChanged = 0;
            foreach (ProcessThread thread in process.Threads)
            {
                if (SetSingleThreadPriority((uint)thread.Id, threadPriorityInfo.Value, disableBoost: true))
                    totalChanged++;
            }
            return totalChanged;
        }

        private int ApplyPriorityToSelectedThreads(ThreadPriorityInfo threadPriorityInfo)
        {
            var selectedThreads = GetSelectedThreads();
            if (selectedThreads.Count == 0)
            {
                statusLabel.InvokeIfRequired(() => statusLabel.Text = "Select threads or enable 'Apply to ALL threads'.");
                return 0;
            }

            int successCount = 0;
            foreach (var threadInfo in selectedThreads)
            {
                if (SetSingleThreadPriority((uint)threadInfo.Id, threadPriorityInfo.Value, disableBoost: true))
                    successCount++;
            }

            statusLabel.InvokeIfRequired(() => statusLabel.Text = $"Applied {threadPriorityInfo.Name} to {successCount}/{selectedThreads.Count} selected threads");
            LogAction($"Priority applied to selected threads: {threadPriorityInfo.Name} ({successCount}/{selectedThreads.Count} successful)");

            return successCount;
        }

        private List<ThreadInfo> GetSelectedThreads()
        {
            return threadsListView.SelectedItems.Cast<ListViewItem>()
                .Where(item => item.Tag is ThreadInfo)
                .Select(item => item.Tag as ThreadInfo)
                .Where(thread => thread != null)
                .Cast<ThreadInfo>()
                .ToList();
        }

        private bool SetSingleThreadPriority(uint threadId, int priorityValue, bool disableBoost)
        {
            var threadHandle = NativeMethods.OpenThread(AppConfig.THREAD_SET_INFORMATION | AppConfig.THREAD_QUERY_INFORMATION, false, threadId);
            if (threadHandle == IntPtr.Zero)
                return false;

            try
            {
                var prioritySet = NativeMethods.SetThreadPriority(threadHandle, priorityValue);
                var boostSet = NativeMethods.SetThreadPriorityBoost(threadHandle, disableBoost);
                return prioritySet && boostSet;
            }
            finally
            {
                NativeMethods.CloseHandle(threadHandle);
            }
        }

        private void StartMonitorServiceForSelectedProcess()
        {
            if (!ValidateMonitorServiceRequirements(out var processInfo, out var priorityInfo))
                return;

            StopMonitorService();

            var intervalMs = (int)monitorIntervalUpDown.Value * 1000;
            var applyToAllInstances = applyToAllProcessesCheckBox.Checked;

            var monitorAction = CreateMonitorAction(processInfo!, priorityInfo!, applyToAllInstances);
            monitorService = new MonitorService(intervalMs, monitorAction);

            var targetDescription = applyToAllInstances ? $"all '{processInfo!.Name}' instances" : $"PID {processInfo!.Id}";
            statusLabel.InvokeIfRequired(() => statusLabel.Text = $"Monitor ON (prio {priorityInfo!.Name}) - target: {targetDescription}, interval {intervalMs / 1000}s");
            LogAction($"Monitor started: {targetDescription} -> {priorityInfo!.Name}");
        }

        private bool ValidateMonitorServiceRequirements(out ProcessInfo? processInfo, out ThreadPriorityInfo? priorityInfo)
        {
            processInfo = processComboBox.SelectedItem as ProcessInfo;
            priorityInfo = threadPriorityComboBox.SelectedItem as ThreadPriorityInfo;

            if (processInfo == null || priorityInfo == null)
            {
                statusLabel.Text = "Select process and priority before starting monitor.";
                lockPriorityCheckBox.Checked = false;
                return false;
            }

            return true;
        }

        private Action CreateMonitorAction(ProcessInfo processInfo, ThreadPriorityInfo priorityInfo, bool applyToAllInstances)
        {
            if (applyToAllInstances)
            {
                return () => PriorityHelpers.ApplyPriorityToAllProcessesByName(processInfo.Name, priorityInfo.Value, disableBoost: true);
            }
            else
            {
                return () =>
                {
                    try
                    {
                        var process = Process.GetProcessById(processInfo.Id);
                        PriorityHelpers.ApplyPriorityToAllThreads(process, priorityInfo.Value, disableBoost: true);
                    }
                    catch (ArgumentException)
                    {
                        statusLabel.InvokeIfRequired(() => statusLabel.Text = $"Process {processInfo.Id} terminated. Monitor stopped.");
                        lockPriorityCheckBox.InvokeIfRequired(() => lockPriorityCheckBox.Checked = false);
                    }
                };
            }
        }

        private void StopMonitorService()
        {
            monitorService?.Stop();
            monitorService = null;
            statusLabel.InvokeIfRequired(() => statusLabel.Text = "Monitor stopped.");
            LogAction("Monitor stopped");
        }

        private void LockPriorityCheckBox_CheckedChanged(object? sender, EventArgs? e)
        {
            if (lockPriorityCheckBox.Checked)
                StartMonitorServiceForSelectedProcess();
            else
                StopMonitorService();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            StopMonitorService();
            refreshTimer?.Stop();
            refreshTimer?.Dispose();
            base.OnFormClosed(e);
        }

        private void SetUiEnabled(bool enabled, string tempStatus = "")
        {
            this.InvokeIfRequired(() =>
            {
                processComboBox.Enabled = enabled;
                processPriorityComboBox.Enabled = enabled;
                threadPriorityComboBox.Enabled = enabled;
                threadsListView.Enabled = enabled;
                setProcessPriorityButton.Enabled = enabled;
                setThreadPriorityButton.Enabled = enabled;
                applyToAllCheckBox.Enabled = enabled;
                applyToAllProcessesCheckBox.Enabled = enabled;
                lockPriorityCheckBox.Enabled = enabled;
                monitorIntervalUpDown.Enabled = enabled;
                refreshButton.Enabled = enabled;

                if (!string.IsNullOrEmpty(tempStatus))
                    statusLabel.Text = tempStatus;
            });
        }

        private void CheckAdminRights()
        {
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
                {
                    MessageBox.Show("Run the app as Administrator to modify other processes/threads' priorities.",
                        "Insufficient privileges", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch { }
        }
    }

    internal sealed class ColumnSortManager
    {
        private int lastSortedColumn = -1;
        private SortOrder currentSortOrder = SortOrder.None;

        public SortOrder GetNextSortOrder(int columnIndex)
        {
            if (lastSortedColumn != columnIndex)
            {
                lastSortedColumn = columnIndex;
                currentSortOrder = SortOrder.Descending;
            }
            else
            {
                currentSortOrder = currentSortOrder == SortOrder.Descending
                    ? SortOrder.Ascending
                    : SortOrder.Descending;
            }

            return currentSortOrder;
        }
    }

    internal sealed class ColorScheme
    {
        public Color BackgroundColor { get; } = Color.FromArgb(24, 26, 32);
        public Color PanelColor { get; } = Color.FromArgb(30, 33, 40);
        public Color ControlBackgroundColor { get; } = Color.FromArgb(40, 44, 52);
        public Color AccentColor { get; } = Color.FromArgb(0, 150, 220);
        public Color TextColor { get; } = Color.FromArgb(230, 230, 230);
    }

    internal sealed class MonitorService
    {
        private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private readonly int intervalMs;
        private readonly Action monitorAction;

        public MonitorService(int intervalMs, Action monitorAction)
        {
            this.intervalMs = intervalMs;
            this.monitorAction = monitorAction ?? throw new ArgumentNullException(nameof(monitorAction));
            Task.Run(RunMonitorLoop, cancellationTokenSource.Token);
        }

        private async Task RunMonitorLoop()
        {
            var token = cancellationTokenSource.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    monitorAction();
                }
                catch
                {
                
                }

                try
                {
                    await Task.Delay(intervalMs, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        public void Stop()
        {
            if (!cancellationTokenSource.IsCancellationRequested)
                cancellationTokenSource.Cancel();
            cancellationTokenSource.Dispose();
        }
    }

    internal sealed class SafeHandleWrap : IDisposable
    {
        public IntPtr Handle { get; }

        public SafeHandleWrap(IntPtr handle) => Handle = handle;

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
                NativeMethods.CloseHandle(Handle);
        }
    }

    public class ProcessInfo
    {
        public int Id { get; }
        public string Name { get; }

        public ProcessInfo(Process process)
        {
            Id = process.Id;
            Name = process.ProcessName;
        }

        public override string ToString() => $"{Name} (PID: {Id})";
    }

    public class PriorityInfo
    {
        public string Name { get; }
        public uint Value { get; }

        public PriorityInfo(string name, uint value)
        {
            Name = name;
            Value = value;
        }

        public override string ToString() => Name;
    }

    public class ThreadPriorityInfo
    {
        public string Name { get; }
        public int Value { get; }

        public ThreadPriorityInfo(string name, int value)
        {
            Name = name;
            Value = value;
        }

        public override string ToString() => Name;
    }

    public class ThreadInfo
    {
        public int Id { get; }
        public string Priority { get; }
        public int PriorityValue { get; }

        public ThreadInfo(int id, string priority, int priorityValue)
        {
            Id = id;
            Priority = priority;
            PriorityValue = priorityValue;
        }

        public override string ToString() => $"Thread {Id} - Priority: {Priority}";
    }

    internal static class ControlExtensions
    {
        public static void InvokeIfRequired(this Control control, Action action)
        {
            if (control.IsHandleCreated && control.InvokeRequired)
                control.Invoke(action);
            else
                action();
        }
    }

    internal static class PriorityHelpers
    {
        public static void ApplyPriorityToAllThreads(Process process, int priorityValue, bool disableBoost)
        {
            try
            {
                foreach (ProcessThread thread in process.Threads)
                {
                    ApplyPriorityToSingleThread((uint)thread.Id, priorityValue, disableBoost);
                }
            }
            catch
            {
            
            }
        }

        public static void ApplyPriorityToAllProcessesByName(string processName, int priorityValue, bool disableBoost)
        {
            try
            {
                var processes = Process.GetProcessesByName(processName);
                foreach (var process in processes)
                {
                    ApplyPriorityToAllThreads(process, priorityValue, disableBoost);
                }
            }
            catch
            {
            
            }
        }

        private static void ApplyPriorityToSingleThread(uint threadId, int priorityValue, bool disableBoost)
        {
            var threadHandle = NativeMethods.OpenThread(AppConfig.THREAD_SET_INFORMATION | AppConfig.THREAD_QUERY_INFORMATION, false, threadId);
            if (threadHandle != IntPtr.Zero)
            {
                NativeMethods.SetThreadPriority(threadHandle, priorityValue);
                NativeMethods.SetThreadPriorityBoost(threadHandle, disableBoost);
                NativeMethods.CloseHandle(threadHandle);
            }
        }
    }
}
