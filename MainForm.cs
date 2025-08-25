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
        public const int UiRefreshIntervalMs = 5000;

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
        private ListBox threadsListBox = null!;
        private Button refreshButton = null!;
        private Button setProcessPriorityButton = null!;
        private Button setThreadPriorityButton = null!;
        private Label statusLabel = null!;
        private System.Windows.Forms.Timer refreshTimer = null!;

        private CheckBox applyToAllCheckBox = null!;
        private CheckBox applyToAllProcessesCheckBox = null!;
        private CheckBox lockPriorityCheckBox = null!;
        private NumericUpDown monitorIntervalUpDown = null!;

        private MonitorService? monitorService;

        private readonly Color bgColor = Color.FromArgb(24, 26, 32);
        private readonly Color panelColor = Color.FromArgb(30, 33, 40);
        private readonly Color controlBg = Color.FromArgb(40, 44, 52);
        private readonly Color accentColor = Color.FromArgb(0, 150, 220);
        private readonly Color textColor = Color.FromArgb(230, 230, 230);

        public MainForm()
        {
            InitializeComponent();

            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            UpdateStyles();

            CheckAdminRights();

            refreshTimer = new System.Windows.Forms.Timer();
            refreshTimer.Interval = AppConfig.UiRefreshIntervalMs;
            refreshTimer.Tick += async (s, e) => await LoadProcessesAsync();
            refreshTimer.Start();

            processComboBox.DropDown += (s, e) => refreshTimer?.Stop();
            processComboBox.DropDownClosed += (s, e) => refreshTimer?.Start();

            _ = LoadProcessesAsync();
        }

        private void InitializeComponent()
        {
            Text = "Thread Priority Manager";
            Size = new Size(1000, 720);
            MinimumSize = new Size(780, 480);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = bgColor;
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
            ForeColor = textColor;

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

            var leftPanel = new Panel { Dock = DockStyle.Fill, BackColor = panelColor, Padding = new Padding(12) };
            var leftStack = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 14, ColumnCount = 1 };
            for (int i = 0; i < 14; i++) leftStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            leftStack.Controls.Add(CreateLabel("Process:"));
            processComboBox = CreateComboBox();
            processComboBox.SelectedIndexChanged += ProcessComboBox_SelectedIndexChanged;
            leftStack.Controls.Add(processComboBox);

            instanceCountLabel = CreateLabel("Instances: 0");
            instanceCountLabel.Font = new Font(Font.FontFamily, 9F, FontStyle.Regular);
            leftStack.Controls.Add(instanceCountLabel);

            leftStack.Controls.Add(CreateLabel("Process Priority:"));
            processPriorityComboBox = CreateComboBox();
            LoadProcessPriorities();
            leftStack.Controls.Add(processPriorityComboBox);

            setProcessPriorityButton = CreateAccentButton("Set Process Priority");
            setProcessPriorityButton.Click += SetProcessPriorityButton_Click;
            leftStack.Controls.Add(setProcessPriorityButton);

            leftStack.Controls.Add(CreateDivider());

            leftStack.Controls.Add(CreateLabel("Threads (select one):"));
            threadsListBox = new ListBox { Dock = DockStyle.Fill, BackColor = controlBg, ForeColor = textColor, BorderStyle = BorderStyle.None, Height = 260 };
            leftStack.Controls.Add(threadsListBox);

            leftStack.Controls.Add(CreateLabel("Thread Priority:"));
            threadPriorityComboBox = CreateComboBox();
            LoadThreadPriorities();
            leftStack.Controls.Add(threadPriorityComboBox);

            var optionsFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            applyToAllCheckBox = CreateCheckBox("Apply to ALL threads (per process)");
            applyToAllProcessesCheckBox = CreateCheckBox("Apply to ALL processes with same name");
            optionsFlow.Controls.Add(applyToAllCheckBox);
            optionsFlow.Controls.Add(applyToAllProcessesCheckBox);
            leftStack.Controls.Add(optionsFlow);

            lockPriorityCheckBox = CreateCheckBox("Keep priority locked (monitor)");
            lockPriorityCheckBox.CheckedChanged += LockPriorityCheckBox_CheckedChanged;

            var monitorPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            monitorPanel.Controls.Add(lockPriorityCheckBox);
            monitorPanel.Controls.Add(new Label { Text = "Interval (s):", ForeColor = textColor, AutoSize = true, Margin = new Padding(8, 6, 0, 0) });
            monitorIntervalUpDown = new NumericUpDown { Minimum = 1, Maximum = 3600, Value = 2, Width = 70, BackColor = controlBg, ForeColor = textColor, BorderStyle = BorderStyle.None };
            monitorPanel.Controls.Add(monitorIntervalUpDown);
            leftStack.Controls.Add(monitorPanel);

            setThreadPriorityButton = CreateAccentButton("Apply Priority");
            setThreadPriorityButton.Click += SetThreadPriorityButton_Click;
            leftStack.Controls.Add(setThreadPriorityButton);

            leftPanel.Controls.Add(leftStack);
            mainLayout.Controls.Add(leftPanel, 0, 0);

            var rightPanel = new Panel { Dock = DockStyle.Fill, BackColor = panelColor, Padding = new Padding(12) };
            var rightStack = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6, ColumnCount = 1 };
            for (int i = 0; i < 6; i++) rightStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / 6F));

            var headerLabel = new Label { Text = "Status", Dock = DockStyle.Top, Font = new Font(Font.FontFamily, 14F, FontStyle.Bold), ForeColor = textColor, AutoSize = true };
            rightStack.Controls.Add(headerLabel);

            statusLabel = new Label { Text = "Ready", Dock = DockStyle.Top, ForeColor = textColor, AutoSize = true };
            rightStack.Controls.Add(statusLabel);

            var logLabel = new Label { Text = "Recent Actions", ForeColor = textColor, AutoSize = true };
            rightStack.Controls.Add(logLabel);

            var actionsListBox = new ListBox { Dock = DockStyle.Fill, BackColor = controlBg, ForeColor = textColor, BorderStyle = BorderStyle.None };
            rightStack.Controls.Add(actionsListBox);

            var quickPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            refreshButton = CreateSecondaryButton("Refresh");
            refreshButton.Click += async (s, e) => { await LoadProcessesAsync(); actionsListBox.Items.Insert(0, $"{DateTime.Now:T} - Refreshed"); };
            quickPanel.Controls.Add(refreshButton);

            quickPanel.Controls.Add(new Label { Text = "Run as Administrator to modify system processes", ForeColor = Color.FromArgb(180, 180, 180), AutoSize = true, Margin = new Padding(10, 8, 0, 0) });
            rightStack.Controls.Add(quickPanel);

            rightPanel.Controls.Add(rightStack);
            mainLayout.Controls.Add(rightPanel, 1, 0);

            Controls.Add(mainLayout);
        }

        private Label CreateLabel(string text) => new Label { Text = text, ForeColor = textColor, AutoSize = true, Margin = new Padding(3, 8, 3, 3) };
        private ComboBox CreateComboBox() => new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = controlBg, ForeColor = textColor, Height = 30 };
        private CheckBox CreateCheckBox(string text) => new CheckBox { Text = text, ForeColor = textColor, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(6, 6, 6, 6) };
        private Panel CreateDivider() => new Panel { Height = 8, Dock = DockStyle.Top, BackColor = Color.Transparent };
        private Button CreateAccentButton(string text)
        {
            var b = new Button { Text = text, Dock = DockStyle.Top, Height = 36, FlatStyle = FlatStyle.Flat, BackColor = accentColor, ForeColor = Color.White };
            b.FlatAppearance.BorderSize = 0;
            b.MouseEnter += (s, e) => b.BackColor = ControlPaint.Light(accentColor);
            b.MouseLeave += (s, e) => b.BackColor = accentColor;
            return b;
        }

        private Button CreateSecondaryButton(string text)
        {
            var b = new Button { Text = text, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(70, 70, 70), ForeColor = textColor, Margin = new Padding(4) };
            b.FlatAppearance.BorderSize = 0;
            b.MouseEnter += (s, e) => b.BackColor = ControlPaint.Light(Color.FromArgb(70, 70, 70));
            b.MouseLeave += (s, e) => b.BackColor = Color.FromArgb(70, 70, 70);
            return b;
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
                    var same = processComboBox.Items.Count == processes.Count;
                    if (same)
                    {
                        for (int i = 0; i < processes.Count; i++)
                        {
                            if (!(processComboBox.Items[i] is ProcessInfo pi && pi.Id == processes[i].Id)) { same = false; break; }
                        }
                    }

                    if (!same)
                    {
                        processComboBox.BeginUpdate();
                        processComboBox.Items.Clear();
                        foreach (var p in processes) processComboBox.Items.Add(p);
                        processComboBox.EndUpdate();
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

        private void LoadProcessPriorities()
        {
            processPriorityComboBox.Items.Clear();
            processPriorityComboBox.Items.Add(new PriorityInfo("Idle", AppConfig.IDLE_PRIORITY_CLASS));
            processPriorityComboBox.Items.Add(new PriorityInfo("Below Normal", AppConfig.BELOW_NORMAL_PRIORITY_CLASS));
            processPriorityComboBox.Items.Add(new PriorityInfo("Normal", AppConfig.NORMAL_PRIORITY_CLASS));
            processPriorityComboBox.Items.Add(new PriorityInfo("Above Normal", AppConfig.ABOVE_NORMAL_PRIORITY_CLASS));
            processPriorityComboBox.Items.Add(new PriorityInfo("High", AppConfig.HIGH_PRIORITY_CLASS));
            processPriorityComboBox.Items.Add(new PriorityInfo("Realtime", AppConfig.REALTIME_PRIORITY_CLASS));
            processPriorityComboBox.SelectedIndex = 2;
        }

        private void LoadThreadPriorities()
        {
            threadPriorityComboBox.Items.Clear();
            threadPriorityComboBox.Items.Add(new ThreadPriorityInfo("Idle", AppConfig.THREAD_PRIORITY_IDLE));
            threadPriorityComboBox.Items.Add(new ThreadPriorityInfo("Lowest", AppConfig.THREAD_PRIORITY_LOWEST));
            threadPriorityComboBox.Items.Add(new ThreadPriorityInfo("Below Normal", AppConfig.THREAD_PRIORITY_BELOW_NORMAL));
            threadPriorityComboBox.Items.Add(new ThreadPriorityInfo("Normal", AppConfig.THREAD_PRIORITY_NORMAL));
            threadPriorityComboBox.Items.Add(new ThreadPriorityInfo("Above Normal", AppConfig.THREAD_PRIORITY_ABOVE_NORMAL));
            threadPriorityComboBox.Items.Add(new ThreadPriorityInfo("Highest", AppConfig.THREAD_PRIORITY_HIGHEST));
            threadPriorityComboBox.Items.Add(new ThreadPriorityInfo("Time Critical", AppConfig.THREAD_PRIORITY_TIME_CRITICAL));
            threadPriorityComboBox.SelectedIndex = 3;
        }

        private async void ProcessComboBox_SelectedIndexChanged(object? sender, EventArgs? e)
        {
            await LoadThreadsForSelectedProcessAsync();

            if (processComboBox.SelectedItem is ProcessInfo pi)
            {
                var count = Process.GetProcessesByName(pi.Name).Length;
                instanceCountLabel.Text = $"Instances: {count}";
            }
            else
            {
                instanceCountLabel.Text = "Instances: 0";
            }

            if (lockPriorityCheckBox.Checked) StopMonitorService();
        }

        private async Task LoadThreadsForSelectedProcessAsync()
        {
            var wasRunning = refreshTimer?.Enabled ?? false;
            try
            {
                refreshTimer?.Stop();
                threadsListBox.InvokeIfRequired(() => { threadsListBox.BeginUpdate(); threadsListBox.Items.Clear(); });

                if (processComboBox.SelectedItem is ProcessInfo processInfo)
                {
                    SetUiEnabled(false, $"Loading threads for {processInfo.Name}...");
                    var threads = await Task.Run(() =>
                    {
                        try
                        {
                            var p = Process.GetProcessById(processInfo.Id);
                            return p.Threads.Cast<ProcessThread>().Select(t => new ThreadInfo(t.Id, GetThreadPriorityString(t))).ToList();
                        }
                        catch { return new List<ThreadInfo>(); }
                    });

                    threadsListBox.InvokeIfRequired(() =>
                    {
                        foreach (var t in threads) threadsListBox.Items.Add(t);
                        threadsListBox.EndUpdate();
                        statusLabel.Text = $"Loaded {threads.Count} threads for {processInfo.Name}";
                        SetUiEnabled(true);
                    });
                }
                else
                {
                    threadsListBox.InvokeIfRequired(() => { threadsListBox.EndUpdate(); statusLabel.Text = "No process selected"; SetUiEnabled(true); });
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

        private async void SetProcessPriorityButton_Click(object? sender, EventArgs? e)
        {
            if (processComboBox.SelectedItem is not ProcessInfo processInfo || processPriorityComboBox.SelectedItem is not PriorityInfo priorityInfo)
            {
                statusLabel.Text = "Select a process and a priority.";
                return;
            }

            SetUiEnabled(false, "Applying process priority...");

            await Task.Run(() =>
            {
                try
                {
                    using var handle = new SafeHandleWrap(NativeMethods.OpenProcess(AppConfig.PROCESS_SET_INFORMATION, false, processInfo.Id));
                    if (handle.Handle != IntPtr.Zero)
                    {
                        NativeMethods.SetPriorityClass(handle.Handle, priorityInfo.Value);
                        statusLabel.InvokeIfRequired(() => statusLabel.Text = $"Set process priority of {processInfo.Name} to {priorityInfo.Name}");
                    }
                    else
                    {
                        statusLabel.InvokeIfRequired(() => statusLabel.Text = "Cannot open process (access denied?)");
                    }
                }
                catch (Exception ex)
                {
                    statusLabel.InvokeIfRequired(() => statusLabel.Text = $"Error: {ex.Message}");
                }
                finally { SetUiEnabled(true); }
            });
        }

        private async void SetThreadPriorityButton_Click(object? sender, EventArgs? e)
        {
            if (threadPriorityComboBox.SelectedItem is not ThreadPriorityInfo threadPriorityInfo)
            {
                statusLabel.Text = "Please choose a thread priority to apply.";
                return;
            }

            if (processComboBox.SelectedItem is not ProcessInfo processInfo)
            {
                statusLabel.Text = "Please select a process first.";
                return;
            }

            SetUiEnabled(false, "Applying priority...");

            await Task.Run(() =>
            {
                try
                {
                    int totalChanged = 0;

                    if (applyToAllProcessesCheckBox.Checked)
                    {
                        var procs = Process.GetProcessesByName(processInfo.Name);
                        foreach (var p in procs)
                        {
                            foreach (ProcessThread t in p.Threads)
                            {
                                if (SetSingleThreadPriority((uint)t.Id, threadPriorityInfo.Value, disableBoost: true)) totalChanged++;
                            }
                        }

                        statusLabel.InvokeIfRequired(() => statusLabel.Text = $"Applied {threadPriorityInfo.Name} to {totalChanged} threads across {procs.Length} process(es) '{processInfo.Name}'");
                    }
                    else
                    {
                        var p = Process.GetProcessById(processInfo.Id);
                        if (applyToAllCheckBox.Checked)
                        {
                            foreach (ProcessThread t in p.Threads)
                            {
                                if (SetSingleThreadPriority((uint)t.Id, threadPriorityInfo.Value, disableBoost: true)) totalChanged++;
                            }

                            statusLabel.InvokeIfRequired(() => statusLabel.Text = $"Applied {threadPriorityInfo.Name} to {totalChanged} threads of {processInfo.Name}");
                        }
                        else
                        {
                            if (threadsListBox.SelectedItem is ThreadInfo ti)
                            {
                                var ok = SetSingleThreadPriority((uint)ti.Id, threadPriorityInfo.Value, disableBoost: true);
                                statusLabel.InvokeIfRequired(() => statusLabel.Text = ok ? $"Thread {ti.Id} priority set to {threadPriorityInfo.Name}" : $"Failed to set thread {ti.Id} priority");
                            }
                            else
                            {
                                statusLabel.InvokeIfRequired(() => statusLabel.Text = "Select a thread or enable 'Apply to ALL threads'.");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    statusLabel.InvokeIfRequired(() => statusLabel.Text = $"Error: {ex.Message}");
                }
                finally { SetUiEnabled(true); }
            });
        }

        private bool SetSingleThreadPriority(uint threadId, int priorityValue, bool disableBoost)
        {
            var hThread = NativeMethods.OpenThread(AppConfig.THREAD_SET_INFORMATION | AppConfig.THREAD_QUERY_INFORMATION, false, threadId);
            if (hThread == IntPtr.Zero) return false;

            try
            {
                var ok = NativeMethods.SetThreadPriority(hThread, priorityValue);
                var boostOk = NativeMethods.SetThreadPriorityBoost(hThread, disableBoost);
                return ok && boostOk;
            }
            finally { NativeMethods.CloseHandle(hThread); }
        }

        private void StartMonitorServiceForSelectedProcess()
        {
            if (processComboBox.SelectedItem is not ProcessInfo procInfo || threadPriorityComboBox.SelectedItem is not ThreadPriorityInfo prioInfo)
            {
                statusLabel.Text = "Select process and priority before starting monitor.";
                lockPriorityCheckBox.Checked = false;
                return;
            }

            StopMonitorService();

            var intervalMs = (int)monitorIntervalUpDown.Value * 1000;
            var applyToAllInstances = applyToAllProcessesCheckBox.Checked;

            Action monitorAction;
            if (applyToAllInstances)
            {
                monitorAction = () => PriorityHelpers.ApplyPriorityToAllProcessesByName(procInfo.Name, prioInfo.Value, disableBoost: true);
            }
            else
            {
                monitorAction = () =>
                {
                    try
                    {
                        var process = Process.GetProcessById(procInfo.Id);
                        PriorityHelpers.ApplyPriorityToAllThreads(process, prioInfo.Value, disableBoost: true);
                    }
                    catch (ArgumentException)
                    {
                        statusLabel.InvokeIfRequired(() => statusLabel.Text = $"Process {procInfo.Id} terminated. Monitor stopped.");
                        lockPriorityCheckBox.InvokeIfRequired(() => lockPriorityCheckBox.Checked = false);
                    }
                };
            }

            monitorService = new MonitorService(intervalMs, monitorAction);

            statusLabel.InvokeIfRequired(() => statusLabel.Text = $"Monitor ON (prio {prioInfo.Name}) - target: {(applyToAllInstances ? $"all '{procInfo.Name}' instances" : $"PID {procInfo.Id}")}, interval {intervalMs / 1000}s");
        }

        private void StopMonitorService()
        {
            monitorService?.Stop();
            monitorService = null;
            statusLabel.InvokeIfRequired(() => statusLabel.Text = "Monitor stopped.");
        }

        private void LockPriorityCheckBox_CheckedChanged(object? sender, EventArgs? e)
        {
            if (lockPriorityCheckBox.Checked) StartMonitorServiceForSelectedProcess(); else StopMonitorService();
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
                threadsListBox.Enabled = enabled;
                setProcessPriorityButton.Enabled = enabled;
                setThreadPriorityButton.Enabled = enabled;
                applyToAllCheckBox.Enabled = enabled;
                applyToAllProcessesCheckBox.Enabled = enabled;
                lockPriorityCheckBox.Enabled = enabled;
                monitorIntervalUpDown.Enabled = enabled;
                refreshButton.Enabled = enabled;
                if (!string.IsNullOrEmpty(tempStatus)) statusLabel.Text = tempStatus;
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
                    MessageBox.Show("Run the app as Administrator to modify other processes/threads' priorities.", "Insufficient privileges", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch { }
        }

    }

    internal sealed class MonitorService
    {
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        private readonly int intervalMs;
        private readonly Action action;

        public MonitorService(int intervalMs, Action action)
        {
            this.intervalMs = intervalMs;
            this.action = action ?? throw new ArgumentNullException(nameof(action));
            Task.Run(RunLoop, cts.Token);
        }

        private async Task RunLoop()
        {
            var token = cts.Token;
            while (!token.IsCancellationRequested)
            {
                try { action(); }
                catch { /* swallow: UI will report errors where appropriate */ }

                try { await Task.Delay(intervalMs, token); }
                catch (TaskCanceledException) { break; }
            }
        }

        public void Stop()
        {
            if (!cts.IsCancellationRequested) cts.Cancel();
            cts.Dispose();
        }
    }

    internal sealed class SafeHandleWrap : IDisposable
    {
        public IntPtr Handle { get; }
        public SafeHandleWrap(IntPtr handle) => Handle = handle;
        public void Dispose() { if (Handle != IntPtr.Zero) NativeMethods.CloseHandle(Handle); }
    }

    public class ProcessInfo
    {
        public int Id { get; }
        public string Name { get; }
        public ProcessInfo(Process process) { Id = process.Id; Name = process.ProcessName; }
        public override string ToString() => $"{Name} (PID: {Id})";
    }

    public class PriorityInfo
    {
        public string Name { get; }
        public uint Value { get; }
        public PriorityInfo(string name, uint value) { Name = name; Value = value; }
        public override string ToString() => Name;
    }

    public class ThreadPriorityInfo
    {
        public string Name { get; }
        public int Value { get; }
        public ThreadPriorityInfo(string name, int value) { Name = name; Value = value; }
        public override string ToString() => Name;
    }

    public class ThreadInfo
    {
        public int Id { get; }
        public string Priority { get; }
        public ThreadInfo(int id, string priority) { Id = id; Priority = priority; }
        public override string ToString() => $"Thread {Id} - Priority: {Priority}";
    }

    internal static class ControlExtensions
    {
        public static void InvokeIfRequired(this Control control, Action action)
        {
            if (control.IsHandleCreated && control.InvokeRequired) control.Invoke(action); else action();
        }
    }

    internal static partial class PriorityHelpers
    {
        public static void ApplyPriorityToAllThreads(Process process, int priorityValue, bool disableBoost)
        {
            try
            {
                int count = 0;
                foreach (ProcessThread thread in process.Threads)
                {
                    var h = NativeMethods.OpenThread(AppConfig.THREAD_SET_INFORMATION | AppConfig.THREAD_QUERY_INFORMATION, false, (uint)thread.Id);
                    if (h != IntPtr.Zero)
                    {
                        NativeMethods.SetThreadPriority(h, priorityValue);
                        NativeMethods.SetThreadPriorityBoost(h, disableBoost);
                        NativeMethods.CloseHandle(h);
                        count++;
                    }
                }
            }
            catch { /* intentionally swallow: callers update UI */ }
        }

        public static void ApplyPriorityToAllProcessesByName(string processName, int priorityValue, bool disableBoost)
        {
            try
            {
                var procs = Process.GetProcessesByName(processName);
                foreach (var p in procs)
                {
                    ApplyPriorityToAllThreads(p, priorityValue, disableBoost);
                }
            }
            catch { /* intentionally swallow: callers update UI */ }
        }
    }
}