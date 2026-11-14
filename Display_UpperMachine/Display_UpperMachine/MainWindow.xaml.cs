using Display_UpperMachine.Models;
using Display_UpperMachine.Services;
using Display_UpperMachine.Storage;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Display_UpperMachine
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ModbusService _modbus = new();

        // 20 个参数：前 10 只读、后 10 可写，每个 float 占 2 个寄存器
        private const int ReadCount = 10;
        private const int WriteCount = 10;
        private const ushort ReadStart = 0;      // 读取区：寄存器 0..19
        private const ushort WriteStart = 20;    // 写入区：寄存器 20..39

        private readonly List<TagRow> _tags = new();

        // 实时读取定时器
        private readonly DispatcherTimer _pollTimer = new();
        private bool _polling;                   // 防止上一次读取未完成又触发
        private bool _lastPollOk = true;         // 上一次读取是否成功（用于只在状态变化时记日志）

        // ---- 存储相关 ----
        private readonly AppConfig _config = ConfigLoader.Load();
        private readonly IDataStore _store;
        private readonly Dictionary<string, double> _lastSaved = new();   // 各参数上次入库的值
        private bool _autoSave;                                           // 是否开启“值变化时自动保存”

        // 日志（最新在最上面）
        private readonly ObservableCollection<string> _logs = new();
        private const int MaxLogLines = 500;

        /// <summary>当前选择的浮点字节序。</summary>
        private FloatOrder SelectedFloatOrder =>
            Enum.TryParse(((ComboBoxItem)cmbFloatOrder.SelectedItem).Content.ToString(), out FloatOrder o)
                ? o : FloatOrder.ABCD;

        public MainWindow()
        {
            InitializeComponent();

            cmbMode.SelectedIndex = 0;

            // 枚举本机串口
            foreach (var port in SerialPort.GetPortNames())
                cmbPort.Items.Add(port);
            if (cmbPort.Items.Count > 0)
                cmbPort.SelectedIndex = 0;

            // 构建 20 个参数点位
            for (int i = 0; i < ReadCount; i++)
                _tags.Add(new TagRow { Name = $"读取参数{i + 1}", Address = ReadStart + i * 2, CanWrite = false });
            for (int i = 0; i < WriteCount; i++)
                _tags.Add(new TagRow { Name = $"写入参数{i + 1}", Address = WriteStart + i * 2, CanWrite = true });

            dgData.ItemsSource = _tags;

            // 日志列表
            lstLog.ItemsSource = _logs;

            // 按配置创建存储
            _store = StoreFactory.Create(_config);
            txtDbProvider.Text = _store.Name;
            Log($"存储类型：{_store.Name}");
            if (_store is SqliteStore sqlite)
                Log($"数据库文件：{sqlite.DbPath}");

            // 实时读取：每 500ms 读一次“读取区”
            _pollTimer.Interval = TimeSpan.FromMilliseconds(500);
            _pollTimer.Tick += PollTimer_Tick;
            _pollTimer.Start();
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            _pollTimer.Stop();
            _modbus.Dispose();
            _store.Dispose();
            Close();
        }

        private void cmbMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (pnlTcp == null || pnlRtu == null) return;
            bool isTcp = cmbMode.SelectedIndex == 0;
            pnlTcp.Visibility = isTcp ? Visibility.Visible : Visibility.Collapsed;
            pnlRtu.Visibility = isTcp ? Visibility.Collapsed : Visibility.Visible;
        }

        private async void btnTest_Click(object sender, RoutedEventArgs e)
        {
            (bool ok, string msg) r;

            if (cmbMode.SelectedIndex == 0)              // TCP
            {
                if (!int.TryParse(txtPort.Text, out int port)) { MessageBox.Show("端口号不对"); return; }
                if (!byte.TryParse(txtUnitedId.Text, out byte uid)) { MessageBox.Show("从站号不对"); return; }
                r = await _modbus.TestTcpAsync(txtIp.Text.Trim(), port, uid);
            }
            else                                         // RTU
            {
                if (cmbPort.SelectedItem == null) { MessageBox.Show("请选择串口"); return; }
                int baud = int.Parse(((ComboBoxItem)cmbBaud.SelectedItem).Content.ToString()!);
                var parity = (Parity)Enum.Parse(typeof(Parity),
                                ((ComboBoxItem)cmbParity.SelectedItem).Content.ToString()!);
                if (!byte.TryParse(txtUnitedId.Text, out byte uid)) { MessageBox.Show("从站号不对"); return; }
                r = _modbus.TestRtu(cmbPort.SelectedItem.ToString()!, baud, parity, uid);
            }

            SetStatus(r.ok, r.ok ? "已连接" : "未连接");
            Log(r.ok ? "连接成功" : $"连接失败：{r.msg}");
            MessageBox.Show(r.msg, r.ok ? "成功" : "失败");
        }

        /// <summary>实时读取“读取区”的 10 个浮点参数（在后台线程执行，避免卡界面）。</summary>
        private async void PollTimer_Tick(object? sender, EventArgs e)
        {
            if (_polling || !_modbus.IsConnected) return;
            _polling = true;
            try
            {
                if (!byte.TryParse(txtUnitedId.Text, out byte uid)) return;

                // 10 个 float = 20 个寄存器
                var regs = await Task.Run(() => _modbus.ReadHoldingRegisters(uid, ReadStart, (ushort)(ReadCount * 2)));
                var floats = ModbusService.RegistersToFloats(regs, SelectedFloatOrder);

                for (int i = 0; i < ReadCount && i < floats.Length; i++)
                {
                    _tags[i].Value = floats[i];
                    _tags[i].RawHex = $"{regs[i * 2]:X4} {regs[i * 2 + 1]:X4}";
                    SaveIfChanged(_tags[i], floats[i]);      // 值变化才入库
                }

                if (!_lastPollOk) { Log("读取恢复正常"); _lastPollOk = true; }
                SetStatus(true, "已连接");
            }
            catch (Exception ex)
            {
                // 读取失败（超时/断开）先忽略弹窗，只在状态变化时记一条日志
                SetStatus(false, "读取失败");
                if (_lastPollOk) { Log($"读取失败：{ex.Message}"); _lastPollOk = false; }
            }
            finally { _polling = false; }
        }

        /// <summary>写入单个可写参数（表格每行的“写入”按钮）。</summary>
        private void btnWriteRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TagRow row)
                WriteTag(row);
        }

        /// <summary>写入全部可写参数。</summary>
        private void btnWriteAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var row in _tags)
                if (row.CanWrite) WriteTag(row);
        }

        /// <summary>把某个参数的浮点值写入从站（占 2 个寄存器），成功后按需入库。</summary>
        private void WriteTag(TagRow row)
        {
            try
            {
                if (!byte.TryParse(txtUnitedId.Text, out byte uid)) { MessageBox.Show("从站号不对"); return; }
                var regs = ModbusService.FloatToRegisters(row.Value, SelectedFloatOrder);
                _modbus.WriteMultipleRegisters(uid, (ushort)row.Address, regs);

                Log($"写入 {row.Name} = {row.Value} @ {row.Address}");
                SaveIfChanged(row, row.Value, log: true);        // 写入后按需入库
            }
            catch (Exception ex)
            {
                Log($"写入失败 {row.Name}：{ex.Message}");
                MessageBox.Show($"{row.Name} 写入失败：{ex.Message}", "写入失败");
            }
        }

        // ---------------- 存储 ----------------

        /// <summary>“初始化/建表”按钮。</summary>
        private void btnDbInit_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _store.Init();
                txtDbStatus.Text = "已初始化";
                txtDbStatus.Foreground = (Brush)FindResource("GoodBrush");
                Log($"{_store.Name} 初始化/建表成功");
                MessageBox.Show("数据库初始化成功", "成功");
            }
            catch (Exception ex)
            {
                txtDbStatus.Text = "初始化失败";
                txtDbStatus.Foreground = (Brush)FindResource("BadBrush");
                Log($"初始化失败：{ex.Message}");
                MessageBox.Show(ex.Message, "初始化失败");
            }
        }

        private void chkAutoSave_Checked(object sender, RoutedEventArgs e)
        {
            _autoSave = true;
            Log("自动保存：开");
            try { _store.Init(); }        // 打开时确保已建表
            catch (Exception ex) { Log($"自动建表失败：{ex.Message}"); }
        }

        private void chkAutoSave_Unchecked(object sender, RoutedEventArgs e)
        {
            _autoSave = false;
            Log("自动保存：关");
        }

        /// <summary>值变化时才写库（开启自动保存时生效）。</summary>
        private void SaveIfChanged(TagRow row, double value, bool log = false)
        {
            if (!_autoSave) return;

            if (_lastSaved.TryGetValue(row.Name, out var last) && Math.Abs(last - value) < 1e-9)
                return;                                  // 值没变，跳过

            _lastSaved[row.Name] = value;
            try
            {
                _store.Save(DateTime.Now, row.Name, row.Address, value);
                if (log) Log($"已入库 {row.Name} = {value}");
            }
            catch (Exception ex)
            {
                Log($"入库失败 {row.Name}：{ex.Message}");
            }
        }

        // ---------------- 日志 ----------------

        /// <summary>写一条日志（线程安全，后台线程会自动切回界面线程）。</summary>
        private void Log(string message)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => AddLog(line));
                return;
            }
            AddLog(line);
        }

        private void AddLog(string line)
        {
            _logs.Insert(0, line);                       // 最新在最上面
            while (_logs.Count > MaxLogLines)
                _logs.RemoveAt(_logs.Count - 1);
        }

        /// <summary>更新顶部状态文字的颜色。</summary>
        private void SetStatus(bool connected, string text)
        {
            txtStatus.Text = text;
            txtStatus.Foreground = connected
                ? (Brush)FindResource("GoodBrush")
                : (Brush)FindResource("BadBrush");
        }
    }
}
