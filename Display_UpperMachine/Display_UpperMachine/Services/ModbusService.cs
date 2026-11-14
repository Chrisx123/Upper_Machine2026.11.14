using NModbus;
using NModbus.Serial;
using System;
using System.IO.Ports;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Display_UpperMachine.Services
{
    /// <summary>Modbus 32 位浮点的字节/字序（用于匹配不同设备的约定）。</summary>
    public enum FloatOrder { ABCD, CDAB, BADC, DCBA }

    /// <summary>
    /// Modbus 通信服务：封装 TCP / RTU 连接、保持寄存器读写。
    /// 约定：每个参数（float）占用 2 个寄存器，字节序为大端 ABCD（高字在前）。
    /// </summary>
    public class ModbusService : IDisposable
    {
        private IModbusMaster? _master;
        private TcpClient? _tcpClient;
        private SerialPort? _serialPort;

        // 读写锁：避免“定时读取”与“手动写入”同时操作同一个 master
        private readonly object _lock = new();

        /// <summary>当前是否已成功连接。</summary>
        public bool IsConnected { get; private set; }

        /// <summary>TCP：连接并读一次寄存器做连通测试。</summary>
        public async Task<(bool ok, string msg)> TestTcpAsync(string ip, int port, byte unitId)
        {
            try
            {
                Disconnect();
                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(ip, port);
                _master = new ModbusFactory().CreateMaster(_tcpClient);
                _master.Transport.ReadTimeout = 1000;
                _master.Transport.WriteTimeout = 1000;
                _master.Transport.Retries = 1;
                var regs = _master.ReadHoldingRegisters(unitId, 0, 1);
                IsConnected = true;
                return (true, $"连接成功，寄存器0 = {regs[0]}");
            }
            catch (Exception ex) { IsConnected = false; return (false, ex.Message); }
        }

        /// <summary>RTU：打开串口并读一次寄存器做连通测试。</summary>
        public (bool ok, string msg) TestRtu(string portName, int baud, Parity parity, byte unitId)
        {
            try
            {
                Disconnect();
                _serialPort = new SerialPort(portName, baud, parity, 8, StopBits.One);
                _serialPort.ReadTimeout = 1000;
                _serialPort.WriteTimeout = 1000;
                _serialPort.Open();
                _master = new ModbusFactory().CreateRtuMaster(_serialPort);
                _master.Transport.ReadTimeout = 1000;
                _master.Transport.WriteTimeout = 1000;
                _master.Transport.Retries = 1;
                var regs = _master.ReadHoldingRegisters(unitId, 0, 1);
                IsConnected = true;
                return (true, $"连接成功，寄存器0 = {regs[0]}");
            }
            catch (Exception ex) { IsConnected = false; return (false, ex.Message); }
        }

        /// <summary>读取一组保持寄存器（功能码 03）。</summary>
        public ushort[] ReadHoldingRegisters(byte unitId, ushort start, ushort count)
        {
            lock (_lock)
            {
                if (_master == null) throw new InvalidOperationException("请先连接设备");
                return _master.ReadHoldingRegisters(unitId, start, count);
            }
        }

        /// <summary>写入一组保持寄存器（功能码 16）。</summary>
        public void WriteMultipleRegisters(byte unitId, ushort start, ushort[] data)
        {
            lock (_lock)
            {
                if (_master == null) throw new InvalidOperationException("请先连接设备");
                _master.WriteMultipleRegisters(unitId, start, data);
            }
        }

        /// <summary>断开并释放当前连接。</summary>
        private void Disconnect()
        {
            _master?.Dispose(); _master = null;
            _tcpClient?.Close(); _tcpClient = null;
            if (_serialPort != null)
            {
                try { _serialPort.Close(); } catch { }
                _serialPort.Dispose(); _serialPort = null;
            }
            IsConnected = false;
        }

        public void Dispose() => Disconnect();

        // ---------------- 浮点 ↔ 寄存器（每个 float 占 2 个寄存器，字节序可选） ----------------

        /// <summary>把单个 float 转成 2 个寄存器，按指定字节序。</summary>
        public static ushort[] FloatToRegisters(float value, FloatOrder order)
        {
            byte[] abcd = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian) Array.Reverse(abcd);   // 先得到标准大端 ABCD

            byte[] ordered = order switch
            {
                FloatOrder.ABCD => abcd,
                FloatOrder.CDAB => new[] { abcd[2], abcd[3], abcd[0], abcd[1] },   // 字交换
                FloatOrder.BADC => new[] { abcd[1], abcd[0], abcd[3], abcd[2] },   // 字节交换
                FloatOrder.DCBA => new[] { abcd[3], abcd[2], abcd[1], abcd[0] },   // 全反（小端）
                _ => abcd,
            };

            return new ushort[]
            {
                (ushort)((ordered[0] << 8) | ordered[1]),
                (ushort)((ordered[2] << 8) | ordered[3]),
            };
        }

        /// <summary>把 2 个寄存器按指定字节序解析成 float。</summary>
        public static float RegistersToFloat(ushort r0, ushort r1, FloatOrder order)
        {
            // 设备发出的原始字节顺序：[r0高, r0低, r1高, r1低] = ABCD
            byte[] raw = { (byte)(r0 >> 8), (byte)(r0 & 0xFF), (byte)(r1 >> 8), (byte)(r1 & 0xFF) };

            byte[] abcd = order switch
            {
                FloatOrder.ABCD => raw,
                FloatOrder.CDAB => new[] { raw[2], raw[3], raw[0], raw[1] },
                FloatOrder.BADC => new[] { raw[1], raw[0], raw[3], raw[2] },
                FloatOrder.DCBA => new[] { raw[3], raw[2], raw[1], raw[0] },
                _ => raw,
            };

            byte[] tmp = (byte[])abcd.Clone();
            if (BitConverter.IsLittleEndian) Array.Reverse(tmp);
            return BitConverter.ToSingle(tmp, 0);
        }

        /// <summary>把一串寄存器按每 2 个解析成 float 数组。</summary>
        public static float[] RegistersToFloats(ushort[] registers, FloatOrder order)
        {
            var result = new float[registers.Length / 2];
            for (int i = 0; i < result.Length; i++)
                result[i] = RegistersToFloat(registers[i * 2], registers[i * 2 + 1], order);
            return result;
        }
    }
}
