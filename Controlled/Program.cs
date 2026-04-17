using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace IEC104Controlled
{
    public enum IecValueType { SingleBit, DoubleBit, StepPosition, BitString32, Normalized, Scaled, Counter, Binary };

    public class DataPoint
    {
        public int IOA { get; set; }
        public IecValueType Type { get; set; }
        public object Value { get; set; }
        public byte Quality { get; set; } = 0x00;
        public DateTime Timestamp { get; set; } = DateTime.Now;

        public DataPoint(int ioa, IecValueType type, object value)
        {
            IOA = ioa;
            Type = type;
            Value = value;
            Timestamp = DateTime.Now;
        }

        public byte[] ToASDU()
        {
            List<byte> data = new List<byte>();
            data.AddRange(BitConverter.GetBytes(IOA).Reverse());
            byte[] valueBytes = Type switch
            {
                IecValueType.SingleBit => new[] { (byte)((bool)Value ? 1 : 0), Quality },
                IecValueType.DoubleBit => new[] { (byte)((int)Value & 0x03), Quality },
                IecValueType.StepPosition => new[] { (byte)((sbyte)Value), (byte)((sbyte)Value >> 8), Quality },
                IecValueType.BitString32 => BitConverter.GetBytes((uint)Value).Reverse().Append(Quality).ToArray(),
                IecValueType.Normalized => BitConverter.GetBytes((short)Value).Reverse().Append(Quality).ToArray(),
                IecValueType.Scaled => BitConverter.GetBytes((short)Value).Reverse().Append(Quality).ToArray(),
                IecValueType.Counter => BitConverter.GetBytes((uint)Value).Reverse().Append(Quality).ToArray(),
                IecValueType.Binary => new[] { (byte)((bool)Value ? 1 : 0), Quality },
                _ => new[] { (byte)0, Quality }
            };
            data.AddRange(valueBytes);
            return data.ToArray();
        }
    }

    public class DataStore
    {
        private readonly Dictionary<int, DataPoint> _points = new();

        public void AddPoint(int ioa, IecValueType type, object value)
        {
            _points[ioa] = new DataPoint(ioa, type, value);
        }

        public DataPoint? GetPoint(int ioa)
        {
            return _points.TryGetValue(ioa, out var point) ? point : null;
        }

        public bool UpdatePoint(int ioa, object value)
        {
            if (_points.TryGetValue(ioa, out var point))
            {
                point.Value = value;
                point.Timestamp = DateTime.Now;
                return true;
            }
            return false;
        }

        public Dictionary<int, DataPoint> GetAllPoints() => new(_points);

        public IEnumerable<DataPoint> GetPointsByType(IecValueType type)
        {
            foreach (var p in _points.Values)
                if (p.Type == type) yield return p;
        }
    }

    public enum AsduType
    {
        M_SP_NA_1 = 1, M_SP_TA_1 = 2, M_DP_NA_1 = 3, M_DP_TA_1 = 4,
        M_ST_NA_1 = 5, M_ST_TA_1 = 6, M_BO_NA_1 = 7, M_BO_TA_1 = 8,
        M_ME_NA_1 = 9, M_ME_TA_1 = 10, M_ME_NB_1 = 11, M_ME_TB_1 = 12,
        M_ME_NC_1 = 13, M_ME_TC_1 = 14, M_IT_NA_1 = 15, M_IT_TA_1 = 16,
        M_EP_NA_1 = 17, M_EP_TA_1 = 18, M_EP_NB_1 = 19, M_EP_TB_1 = 20,
        M_EP_NC_1 = 21, M_EP_TC_1 = 22, M_PS_NA_1 = 23, M_ME_ND_1 = 24,
        M_SP_TB_1 = 30, M_DP_TB_1 = 31, M_ST_TB_1 = 32, M_BO_TB_1 = 33,
        M_ME_TD_1 = 34, M_ME_TE_1 = 35, M_ME_TF_1 = 36, M_IT_TB_1 = 37,
        M_EP_TD_1 = 38, M_EP_TE_1 = 39, M_EP_TF_1 = 40,
        C_SC_NA_1 = 45, C_DC_NA_1 = 46, C_RC_NA_1 = 47, C_SE_NA_1 = 48,
        C_SE_NB_1 = 49, C_SE_NC_1 = 50, C_BO_NA_1 = 51, C_SC_TA_1 = 58,
        C_DC_TA_1 = 59, C_RC_TA_1 = 60, C_SE_TA_1 = 61, C_SE_TB_1 = 62,
        C_SE_TC_1 = 63, C_BO_TA_1 = 64,
        P_ME_NA_1 = 100, P_ME_NB_1 = 101, P_ME_NC_1 = 102, P_AC_NA_1 = 103,
        F_FR_NA_1 = 120, F_SR_NA_1 = 121, F_SC_NA_1 = 122, F_LS_NA_1 = 123,
        F_AF_NA_1 = 124, F_SG_NA_1 = 125, F_DR_TA_1 = 126, F_SC_NB_1 = 127
    }

    public enum CauseOfTransmission
    {
        Periodic = 1, BackgroundScan = 2, Spontaneous = 3, Init = 4,
        Request = 5, Activation = 6, ActivationCon = 7, Deactivation = 8,
        DeactivationCon = 9, ActivationTerm = 10, ReturnInfoRemote = 11,
        ReturnInfoLocal = 12, FileTransfer = 13
    }

    public class Iec104Slave
    {
        private TcpListener? _listener;
        private TcpClient? _client;
        private NetworkStream? _stream;
        private CancellationTokenSource? _cts;
        private readonly DataStore _dataStore;
        private ushort _vs = 0, _vr = 0;
        private bool _connected = false;
        private bool _dataTransfer = false;
        private int _asduAddress = 1;
        private int _port = 2404;

        public event Action<string>? OnStatusChanged;
        public event Action<DataPoint>? OnSpontaneousSent;
        public bool IsRunning => _listener != null;
        public bool IsConnected => _connected;
        public int Port => _port;

        public Iec104Slave(DataStore dataStore)
        {
            _dataStore = dataStore;
        }

        public void SetPort(int port) => _port = port;
        public void SetAsduAddress(int addr) => _asduAddress = addr;

        public async Task StartAsync()
        {
            if (_listener != null) return;
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            OnStatusChanged?.Invoke($"Server started on port {_port}");
            _ = Task.Run(async () => await AcceptClientAsync(_cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
            _client?.Close();
            _listener?.Stop();
            _listener = null;
            _connected = false;
            _dataTransfer = false;
            OnStatusChanged?.Invoke("Server stopped");
        }

        private async Task AcceptClientAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener != null)
            {
                try
                {
                    _client = await _listener.AcceptTcpClientAsync(ct);
                    _stream = _client.GetStream();
                    _connected = true;
                    _vs = 0; _vr = 0;
                    OnStatusChanged?.Invoke("Master connected");
                    _ = Task.Run(async () => await HandleClientAsync(ct));
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    OnStatusChanged?.Invoke($"Accept error: {ex.Message}");
                }
            }
        }

        private async Task HandleClientAsync(CancellationToken ct)
        {
            byte[] buffer = new byte[260];
            while (!ct.IsCancellationRequested && _client?.Connected == true)
            {
                try
                {
                    if (_stream == null) break;
                    int read = await _stream.ReadAsync(buffer, 0, buffer.Length, ct);
                    if (read == 0) break;
                    ProcessAPDU(buffer, read);
                }
                catch (Exception ex)
                {
                    OnStatusChanged?.Invoke($"Read error: {ex.Message}");
                    break;
                }
            }
            _connected = false;
            _dataTransfer = false;
            OnStatusChanged?.Invoke("Master disconnected");
        }

        private void ProcessAPDU(byte[] buffer, int length)
        {
            if (length < 6) return;
            byte startByte = buffer[0];
            if (startByte == 0x68) // I-format
            {
                int apduLen = buffer[1];
                if (length < 2 + apduLen) return;
                ushort recvNs = (ushort)((buffer[2] << 8) | buffer[3]);
                ushort recvNr = (ushort)((buffer[4] << 8) | buffer[5]);
                if (recvNs != _vs) { SendSFrame(); return; }
                _vr = (ushort)((_vr + 1) % 32768);
                _dataTransfer = true;
                SendSFrame();
                ProcessASDU(buffer, 6, apduLen - 4);
            }
            else if (startByte == 0xE5) // S-frame
            {
                if (length >= 4)
                    _vs = (ushort)((buffer[2] << 8) | buffer[3]);
            }
            else if (startByte == 0x68 && length == 6 && buffer[1] == 4)
            {
                if (buffer[2] == 0x07 && buffer[3] == 0x00) // STARTDT_ACT
                    SendStartDT_CON();
                else if (buffer[2] == 0x0A && buffer[3] == 0x00) // STOPDT_ACT
                    _dataTransfer = false;
                else if (buffer[2] == 0x01 && buffer[3] == 0x00) // TESTFR_ACT
                    SendTESTFR_CON();
            }
        }

        private void ProcessASDU(byte[] data, int offset, int length)
        {
            if (length < 10) return;
            int typeId = data[offset];
            byte sqNum = data[offset + 1];
            byte vsq = data[offset + 1];
            int numObjects = vsq & 0x7F;
            bool isSequence = (vsq & 0x80) != 0;
            ushort ca = (ushort)((data[offset + 2] << 8) | data[offset + 3]);
            byte cotByte = data[offset + 4];
            byte oa = data[offset + 5];
            int cot = cotByte & 0x3F;
            bool isTest = (cotByte & 0x80) != 0;
            ushort firstIOA = 0;
            int infoOffset = offset + 10;

            for (int i = 0; i < numObjects; i++)
            {
                int ioa = isSequence ? firstIOA + i : ReadInt24(data, infoOffset + i * 7);
                int dataStart = isSequence ? offset + 10 : infoOffset + i * 7;
                if (!isSequence) ioa = ReadInt24(data, dataStart);
                if (i == 0 && isSequence) firstIOA = (ushort)ioa;
                int dataPos = isSequence ? offset + 10 + i * (GetDataSize((AsduType)typeId)) : dataStart + 3;
                HandleCommand((AsduType)typeId, cot, ioa, data, dataPos);
            }

            if (cot == 6) SendConfirmation((AsduType)typeId, ca);
            else if (cot == 7 && _dataTransfer) SendGIResponse(ca);
        }

        private int GetDataSize(AsduType type) => type switch
        {
            AsduType.M_SP_NA_1 or AsduType.C_SC_NA_1 => 5,
            AsduType.M_DP_NA_1 or AsduType.C_DC_NA_1 => 5,
            AsduType.M_ST_NA_1 or AsduType.C_SE_NA_1 => 6,
            AsduType.M_BO_NA_1 or AsduType.C_BO_NA_1 => 7,
            AsduType.M_ME_NA_1 => 5,
            AsduType.M_ME_NB_1 => 5,
            AsduType.M_ME_NC_1 => 7,
            AsduType.M_IT_NA_1 => 7,
            _ => 5
        };

        private int GetDataSizeByValueType(IecValueType type) => type switch
        {
            IecValueType.SingleBit => 5,
            IecValueType.DoubleBit => 5,
            IecValueType.StepPosition => 6,
            IecValueType.BitString32 => 7,
            IecValueType.Normalized => 5,
            IecValueType.Scaled => 5,
            IecValueType.Counter => 7,
            IecValueType.Binary => 5,
            _ => 5
        };

        private int ReadInt24(byte[] data, int offset) => (data[offset] << 16) | (data[offset + 1] << 8) | data[offset + 2];

        private void HandleCommand(AsduType type, int cot, int ioa, byte[] data, int pos)
        {
            string cmd = type switch
            {
                AsduType.C_SC_NA_1 => "Single Command",
                AsduType.C_DC_NA_1 => "Double Command",
                AsduType.C_RC_NA_1 => "Regulating Step",
                AsduType.C_SE_NA_1 or AsduType.C_SE_NB_1 or AsduType.C_SE_NC_1 => "Setpoint",
                AsduType.C_BO_NA_1 => "Bitstring Setpoint",
                _ => "Unknown"
            };
            object? value = null;
            switch (type)
            {
                case AsduType.C_SC_NA_1: value = (data[pos] & 0x01) != 0; break;
                case AsduType.C_DC_NA_1: value = data[pos] & 0x03; break;
                case AsduType.C_RC_NA_1: value = (sbyte)data[pos]; break;
                case AsduType.C_SE_NA_1: value = (short)((data[pos] << 8) | data[pos + 1]); break;
                case AsduType.C_SE_NB_1: value = (short)((data[pos] << 8) | data[pos + 1]); break;
                case AsduType.C_SE_NC_1: value = BitConverter.ToSingle(data, pos); break;
                case AsduType.C_BO_NA_1: value = (uint)((data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3]); break;
                default: return;
            }
            if (_dataStore.GetPoint(ioa) != null)
                _dataStore.UpdatePoint(ioa, value!);
            OnStatusChanged?.Invoke($"Command: {cmd} IOA={ioa} Value={value}");
        }

        private void SendSFrame()
        {
            if (_stream == null || !_connected) return;
            byte[] frame = { 0x68, 0x04, 0x01, 0, (byte)(_vr >> 8), (byte)(_vr & 0xFF) };
            _ = _stream.WriteAsync(frame);
        }

        private void SendStartDT_CON()
        {
            if (_stream == null || !_connected) return;
            byte[] frame = { 0x68, 0x04, 0x0B, 0x00, 0, 0 };
            _ = _stream.WriteAsync(frame);
            _dataTransfer = true;
            OnStatusChanged?.Invoke("STARTDT_CON sent");
        }

        private void SendTESTFR_CON()
        {
            if (_stream == null || !_connected) return;
            byte[] frame = { 0x68, 0x04, 0x0B, 0x00, 0, 0 };
            _ = _stream.WriteAsync(frame);
        }

        private void SendConfirmation(AsduType type, ushort ca)
        {
            if (_stream == null || !_connected) return;
            int asduType = (int)type;
            byte cot = (byte)((int)CauseOfTransmission.ActivationCon | 0x40);
            byte[] asdu = new byte[10];
            asdu[0] = (byte)asduType;
            asdu[1] = 1;
            asdu[2] = (byte)(ca >> 8);
            asdu[3] = (byte)(ca & 0xFF);
            asdu[4] = cot;
            asdu[5] = 0;
            _vs = (ushort)((_vs + 1) % 32768);
            byte[] frame = new byte[6 + 10];
            frame[0] = 0x68;
            frame[1] = 10;
            frame[2] = (byte)(_vs >> 8);
            frame[3] = (byte)(_vs & 0xFF);
            frame[4] = (byte)(_vr >> 8);
            frame[5] = (byte)(_vr & 0xFF);
            Buffer.BlockCopy(asdu, 0, frame, 6, 10);
            _ = _stream.WriteAsync(frame);
            OnStatusChanged?.Invoke("Activation confirmation sent");
        }

        private void SendGIResponse(ushort ca)
        {
            if (_stream == null || !_connected) return;
            var points = _dataStore.GetAllPoints();
            if (points.Count == 0) return;
            List<byte> data = new();
            foreach (var p in points.Values)
            {
                data.AddRange(p.ToASDU());
            }
            int totalLen = 10 + data.Count;
            byte[] frame = new byte[6 + totalLen];
            frame[0] = 0x68;
            frame[1] = (byte)totalLen;
            _vs = (ushort)((_vs + 1) % 32768);
            frame[2] = (byte)(_vs >> 8);
            frame[3] = (byte)(_vs & 0xFF);
            frame[4] = (byte)(_vr >> 8);
            frame[5] = (byte)(_vr & 0xFF);
            frame[6] = 20; // M_ME_NC_1 or similar
            frame[7] = (byte)points.Count;
            frame[8] = (byte)(ca >> 8);
            frame[9] = (byte)(ca & 0xFF);
            frame[10] = (byte)((int)CauseOfTransmission.Request | 0x40);
            frame[11] = 0;
            Buffer.BlockCopy(data.ToArray(), 0, frame, 12, data.Count);
            _ = _stream.WriteAsync(frame);
            OnStatusChanged?.Invoke($"GI response sent: {points.Count} points");
        }

        public async Task SendSpontaneousAsync(int ioa)
        {
            var point = _dataStore.GetPoint(ioa);
            if (point == null || _stream == null || !_connected || !_dataTransfer) return;
            _vs = (ushort)((_vs + 1) % 32768);
            byte[] asdu = new byte[10 + GetDataSizeByValueType(point.Type)];
            asdu[0] = 13; // M_SP_TB_1
            asdu[1] = 1;
            asdu[2] = (byte)(_asduAddress >> 8);
            asdu[3] = (byte)(_asduAddress & 0xFF);
            asdu[4] = (byte)((int)CauseOfTransmission.Spontaneous | 0x40);
            asdu[5] = 0;
            byte[] val = point.ToASDU();
            Buffer.BlockCopy(val, 0, asdu, 10, val.Length);
            byte[] frame = new byte[6 + asdu.Length];
            frame[0] = 0x68;
            frame[1] = (byte)asdu.Length;
            frame[2] = (byte)(_vs >> 8);
            frame[3] = (byte)(_vs & 0xFF);
            frame[4] = (byte)(_vr >> 8);
            frame[5] = (byte)(_vr & 0xFF);
            Buffer.BlockCopy(asdu, 0, frame, 6, asdu.Length);
            await _stream.WriteAsync(frame);
            OnSpontaneousSent?.Invoke(point);
        }
    }

    public class Program
    {
        private static DataStore _dataStore = new();
        private static Iec104Slave? _slave;
        private static int _asduAddress = 1;

        static async Task Main(string[] args)
        {
            Console.WriteLine("=== IEC 60870-5-104 Slave Simulator ===");
            while (true)
            {
                ShowMenu();
                var choice = Console.ReadLine();
                await HandleMenuAsync(choice);
            }
        }

        static void ShowMenu()
        {
            Console.WriteLine("\n--- Main Menu ---");
            Console.WriteLine("1. Start Server");
            Console.WriteLine("2. Stop Server");
            Console.WriteLine("3. Add Data Points");
            Console.WriteLine("4. Modify Data Point");
            Console.WriteLine("5. List Data Points");
            Console.WriteLine("6. Send Spontaneous Data");
            Console.WriteLine("7. Configure ASDU Address");
            Console.WriteLine("8. Configure Port");
            Console.WriteLine("9. Connection Status");
            Console.WriteLine("10. Show All Data Types");
            Console.WriteLine("11. Exit");
            Console.Write("Select: ");
        }

        static async Task HandleMenuAsync(string? choice)
        {
            switch (choice)
            {
                case "1": await StartServer(); break;
                case "2": StopServer(); break;
                case "3": AddDataPoints(); break;
                case "4": ModifyDataPoint(); break;
                case "5": ListDataPoints(); break;
                case "6": await SendSpontaneous(); break;
                case "7": ConfigureAsduAddress(); break;
                case "8": ConfigurePort(); break;
                case "9": ShowConnectionStatus(); break;
                case "10": ShowAllDataTypes(); break;
                case "11": Exit(); break;
                default: Console.WriteLine("Invalid choice"); break;
            }
        }

        static async Task StartServer()
        {
            if (_slave != null && _slave.IsRunning) { Console.WriteLine("Server already running"); return; }
            _slave = new Iec104Slave(_dataStore);
            _slave.SetAsduAddress(_asduAddress);
            _slave.OnStatusChanged += s => Console.WriteLine($"[Status] {s}");
            _slave.OnSpontaneousSent += p => Console.WriteLine($"[Sent] IOA={p.IOA} Value={p.Value}");
            await _slave.StartAsync();
        }

        static void StopServer()
        {
            _slave?.Stop();
            Console.WriteLine("Server stopped");
        }

        static void AddDataPoints()
        {
            Console.Write("IOA: "); if (!int.TryParse(Console.ReadLine(), out int ioa)) return;
            Console.WriteLine("Type (0=SingleBit,1=DoubleBit,2=StepPos,3=BitString,4=Normalized,5=Scaled,6=Counter,7=Binary): ");
            if (!int.TryParse(Console.ReadLine(), out int t)) return;
            Console.Write("Value: "); var valStr = Console.ReadLine();
            object? value = null;
            try
            {
                value = (IecValueType)t switch
                {
                    IecValueType.SingleBit or IecValueType.Binary => bool.Parse(valStr!),
                    IecValueType.DoubleBit or IecValueType.StepPosition => sbyte.Parse(valStr!),
                    IecValueType.BitString32 or IecValueType.Counter => uint.Parse(valStr!),
                    IecValueType.Normalized or IecValueType.Scaled => short.Parse(valStr!),
                    _ => valStr
                };
            }
            catch { Console.WriteLine("Invalid value"); return; }
            _dataStore.AddPoint(ioa, (IecValueType)t, value!);
            Console.WriteLine($"Added IOA {ioa}");
        }

        static void ModifyDataPoint()
        {
            Console.Write("IOA: "); if (!int.TryParse(Console.ReadLine(), out int ioa)) return;
            Console.Write("New Value: "); var valStr = Console.ReadLine();
            var point = _dataStore.GetPoint(ioa);
            if (point == null) { Console.WriteLine("Point not found"); return; }
            object? value = point.Type switch
            {
                IecValueType.SingleBit or IecValueType.Binary => bool.Parse(valStr!),
                IecValueType.DoubleBit or IecValueType.StepPosition => sbyte.Parse(valStr!),
                IecValueType.BitString32 or IecValueType.Counter => uint.Parse(valStr!),
                IecValueType.Normalized or IecValueType.Scaled => short.Parse(valStr!),
                _ => valStr
            };
            _dataStore.UpdatePoint(ioa, value!);
            Console.WriteLine($"Updated IOA {ioa}");
        }

        static void ListDataPoints()
        {
            var points = _dataStore.GetAllPoints();
            if (points.Count == 0) { Console.WriteLine("No points"); return; }
            foreach (var p in points.Values)
                Console.WriteLine($"IOA={p.IOA} Type={p.Type} Value={p.Value}");
        }

        static async Task SendSpontaneous()
        {
            Console.Write("IOA: "); if (!int.TryParse(Console.ReadLine(), out int ioa)) return;
            if (_slave != null) await _slave.SendSpontaneousAsync(ioa);
            else Console.WriteLine("Server not running");
        }

        static void ConfigureAsduAddress()
        {
            Console.Write("ASDU Address: "); if (int.TryParse(Console.ReadLine(), out int addr)) { _asduAddress = addr; _slave?.SetAsduAddress(addr); Console.WriteLine($"Set to {addr}"); }
        }

        static void ConfigurePort()
        {
            Console.Write("Port: "); if (int.TryParse(Console.ReadLine(), out int port)) { if (_slave != null) _slave.SetPort(port); Console.WriteLine($"Port set to {port} (use after restart)"); }
        }

        static void ShowConnectionStatus()
        {
            if (_slave == null) { Console.WriteLine("Server not created"); return; }
            Console.WriteLine($"Running: {_slave.IsRunning}");
            Console.WriteLine($"Connected: {_slave.IsConnected}");
            Console.WriteLine($"Port: {_slave.Port}");
            Console.WriteLine($"ASDU Address: {_asduAddress}");
        }

        static void ShowAllDataTypes()
        {
            Console.WriteLine("Supported ASDU Types:");
            Console.WriteLine("Single Point (M_SP_NA_1) - Single Bit");
            Console.WriteLine("Double Point (M_DP_NA_1) - Double Bit");
            Console.WriteLine("Step Position (M_ST_NA_1) - Step Position");
            Console.WriteLine("Bitstring32 (M_BO_NA_1) - 32-bit");
            Console.WriteLine("Normalized (M_ME_NA_1) - Normalized Value");
            Console.WriteLine("Scaled (M_ME_NB_1) - Scaled Value");
            Console.WriteLine("Counter (M_IT_NA_1) - Counter");
            Console.WriteLine("Binary (M_BO_NA_1) - Binary");
            Console.WriteLine("\nCommands supported:");
            Console.WriteLine("C_SC_NA_1 - Single Command");
            Console.WriteLine("C_DC_NA_1 - Double Command");
            Console.WriteLine("C_RC_NA_1 - Regulating Step");
            Console.WriteLine("C_SE_NA_1/NB_1/NC_1 - Setpoint Commands");
            Console.WriteLine("C_BO_NA_1 - Bitstring Command");
        }

        static void Exit()
        {
            _slave?.Stop();
            Environment.Exit(0);
        }
    }
}