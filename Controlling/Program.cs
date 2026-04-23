using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IEC104Simulator
{
    public class Iec104Simulator
    {
        private TcpClient? _client;
        private NetworkStream? _stream;
        private CancellationTokenSource? _cts;
        private bool _connected;
        private byte _sendSeq = 0;
        private byte _recvSeq = 0;
        private readonly object _lock = new();
        private Timer? _cyclicTimer;
        private bool _spontaneousEnabled = true;

        public bool IsConnected => _connected;
        public bool SpontaneousEnabled => _spontaneousEnabled;

        public async Task<bool> ConnectAsync(string ip, int port)
        {
            try
            {
                _client = new TcpClient();
                await _client.ConnectAsync(ip, port);
                _stream = _client.GetStream();
                _cts = new CancellationTokenSource();
                _connected = true;
                _sendSeq = 0;
                _recvSeq = 0;
                
                _ = Task.Run(() => ReceiveLoop(_cts.Token));
                
                await SendStartDtActAsync();
                
                Console.WriteLine($"[+] Connected to {ip}:{port}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[-] Connection failed: {ex.Message}");
                return false;
            }
        }

        public void Disconnect()
        {
            _cyclicTimer?.Dispose();
            _cyclicTimer = null;
            _cts?.Cancel();
            _stream?.Close();
            _client?.Close();
            _connected = false;
            Console.WriteLine("[*] Disconnected");
        }

        private async Task SendStartDtActAsync()
        {
            var frame = CreateUFrame(0x07);
            await _stream!.WriteAsync(frame, 0, frame.Length);
            await Task.Delay(200);
            var response = new byte[6];
            int bytesRead = 0;
            try { bytesRead = await _stream.ReadAsync(response, 0, response.Length); }
            catch { }
            
            if (bytesRead > 0)
            {
                if (response[5] == 0x0B)
                {
                    Console.WriteLine("[*] STARTDT-CON received");
                }
                else if (response[5] == 0x43)
                {
                    Console.WriteLine("[*] TESTFR-CON received");
                }
            }
        }

        private async Task SendStartDtConAsync()
        {
            var frame = CreateUFrame(0x0B);
            await _stream!.WriteAsync(frame, 0, frame.Length);
        }

        private byte[] CreateUFrame(byte controlField)
        {
            return new byte[] { 0x68, 0x04, 0x00, 0x00, 0x00, controlField };
        }

        private byte[] CreateIFrame(byte typeId, byte cot, byte[] data, int asduAddr = 1)
        {
            lock (_lock)
            {
                byte send = _sendSeq;
                _sendSeq = (byte)((_sendSeq + 2) & 0xFE);
                
                int asduLen = 6 + data.Length;
                int frameLen = 6 + asduLen;
                var frame = new byte[frameLen];
                
                frame[0] = 0x68;
                frame[1] = (byte)asduLen;
                frame[2] = send;
                frame[3] = (byte)(send << 1);
                frame[4] = _recvSeq;
                frame[5] = (byte)(_recvSeq << 1);
                
                frame[6] = typeId;
                frame[7] = 0x00;
                frame[8] = cot;
                frame[9] = 0x00;
                frame[10] = (byte)(asduAddr & 0xFF);
                frame[11] = (byte)((asduAddr >> 8) & 0xFF);
                frame[12] = 0x00;
                
                if (data.Length > 0)
                    Array.Copy(data, 0, frame, 13, Math.Min(data.Length, frameLen - 13));
                
                return frame;
            }
        }

        public async Task SendSingleCommandAsync(int ioa, bool value, bool select = false)
        {
            var data = new byte[5];
            data[0] = (byte)(ioa & 0xFF);
            data[1] = (byte)((ioa >> 8) & 0xFF);
            data[2] = (byte)((ioa >> 16) & 0xFF);
            data[3] = value ? (byte)0x01 : (byte)0x00;
            data[4] = select ? (byte)0x80 : (byte)0x00;
            
            var frame = CreateIFrame(0x2D, select ? (byte)0x06 : (byte)0x06, data);
            await _stream!.WriteAsync(frame, 0, frame.Length);
            Console.WriteLine($"[>] Single command: IOA={ioa}, Value={(value ? "ON" : "OFF")}" + (select ? " (Select)" : ""));
        }

        public async Task SendDoubleCommandAsync(int ioa, int state, bool select = false)
        {
            var data = new byte[5];
            data[0] = (byte)(ioa & 0xFF);
            data[1] = (byte)((ioa >> 8) & 0xFF);
            data[2] = (byte)((ioa >> 16) & 0xFF);
            data[3] = (byte)((state & 0x03) | (select ? 0x80 : 0x00));
            data[4] = 0x00;
            
            var frame = CreateIFrame(0x2E, 0x06, data);
            await _stream!.WriteAsync(frame, 0, frame.Length);
            string[] states = { "Intermediate", "OFF", "ON", "Indeterminate" };
            Console.WriteLine($"[>] Double command: IOA={ioa}, State={states[state & 3]}" + (select ? " (Select)" : ""));
        }

        public async Task SendSetpointNormalizedAsync(int ioa, short value, bool select = false)
        {
            var data = new byte[7];
            data[0] = (byte)(ioa & 0xFF);
            data[1] = (byte)((ioa >> 8) & 0xFF);
            data[2] = (byte)((ioa >> 16) & 0xFF);
            data[3] = (byte)(value & 0xFF);
            data[4] = (byte)((value >> 8) & 0xFF);
            data[5] = select ? (byte)0x80 : (byte)0x00;
            data[6] = 0x00;
            
            var frame = CreateIFrame(0x30, 0x06, data);
            await _stream!.WriteAsync(frame, 0, frame.Length);
            Console.WriteLine($"[>] Setpoint normalized: IOA={ioa}, Value={value}" + (select ? " (Select)" : ""));
        }

        public async Task SendSetpointFloatAsync(int ioa, float value, bool select = false)
        {
            var data = new byte[7];
            data[0] = (byte)(ioa & 0xFF);
            data[1] = (byte)((ioa >> 8) & 0xFF);
            data[2] = (byte)((ioa >> 16) & 0xFF);
            
            byte[] floatBytes = BitConverter.GetBytes(value);
            data[3] = floatBytes[0];
            data[4] = floatBytes[1];
            data[5] = floatBytes[2];
            data[6] = floatBytes[3];
            
            var frame = CreateIFrame(0x32, 0x06, data);
            await _stream!.WriteAsync(frame, 0, frame.Length);
            Console.WriteLine($"[>] Setpoint float: IOA={ioa}, Value={value:F4}");
        }

        public async Task SendSetpointScaledAsync(int ioa, short value)
        {
            var data = new byte[7];
            data[0] = (byte)(ioa & 0xFF);
            data[1] = (byte)((ioa >> 8) & 0xFF);
            data[2] = (byte)((ioa >> 16) & 0xFF);
            data[3] = (byte)(value & 0xFF);
            data[4] = (byte)((value >> 8) & 0xFF);
            data[5] = 0x00;
            data[6] = 0x00;
            
            var frame = CreateIFrame(0x31, 0x06, data);
            await _stream!.WriteAsync(frame, 0, frame.Length);
            Console.WriteLine($"[>] Setpoint scaled: IOA={ioa}, Value={value}");
        }

        public async Task SendSetpointInt32Async(int ioa, int value)
        {
            var data = new byte[7];
            data[0] = (byte)(ioa & 0xFF);
            data[1] = (byte)((ioa >> 8) & 0xFF);
            data[2] = (byte)((ioa >> 16) & 0xFF);
            data[3] = (byte)(value & 0xFF);
            data[4] = (byte)((value >> 8) & 0xFF);
            data[5] = (byte)((value >> 16) & 0xFF);
            data[6] = (byte)((value >> 24) & 0xFF);
            
            var frame = CreateIFrame(0x33, 0x06, data);
            await _stream!.WriteAsync(frame, 0, frame.Length);
            Console.WriteLine($"[>] Setpoint int32: IOA={ioa}, Value={value}");
        }

        public async Task SendBitstring32CommandAsync(int ioa, uint value)
        {
            var data = new byte[7];
            data[0] = (byte)(ioa & 0xFF);
            data[1] = (byte)((ioa >> 8) & 0xFF);
            data[2] = (byte)((ioa >> 16) & 0xFF);
            byte[] bytes = BitConverter.GetBytes(value);
            Array.Copy(bytes, 0, data, 3, 4);
            
            var frame = CreateIFrame(0x33, 0x06, data);
            await _stream!.WriteAsync(frame, 0, frame.Length);
            Console.WriteLine($"[>] Bitstring32: IOA={ioa}, Value=0x{value:X8} ({value})");
        }

        public async Task SendStepCommandAsync(int ioa, int state)
        {
            var data = new byte[5];
            data[0] = (byte)(ioa & 0xFF);
            data[1] = (byte)((ioa >> 8) & 0xFF);
            data[2] = (byte)((ioa >> 16) & 0xFF);
            data[3] = (byte)(state & 0x03);
            data[4] = 0x00;
            
            var frame = CreateIFrame(0x2F, 0x06, data);
            await _stream!.WriteAsync(frame, 0, frame.Length);
            Console.WriteLine($"[>] Step command: IOA={ioa}, State={state}");
        }

        public async Task SendBitstringCommandAsync(int ioa, byte[] bytes)
        {
            var data = new byte[3 + bytes.Length];
            data[0] = (byte)(ioa & 0xFF);
            data[1] = (byte)((ioa >> 8) & 0xFF);
            data[2] = (byte)((ioa >> 16) & 0xFF);
            Array.Copy(bytes, 0, data, 3, bytes.Length);
            
            var frame = CreateIFrame(0x2B, 0x06, data);
            await _stream!.WriteAsync(frame, 0, frame.Length);
            Console.WriteLine($"[>] Bitstring command: IOA={ioa}, Value={BitConverter.ToString(bytes)}");
        }

        public async Task SendParameterNormalizedAsync(int ioa, short value)
        {
            var data = new byte[7];
            data[0] = (byte)(ioa & 0xFF);
            data[1] = (byte)((ioa >> 8) & 0xFF);
            data[2] = (byte)((ioa >> 16) & 0xFF);
            data[3] = (byte)(value & 0xFF);
            data[4] = (byte)((value >> 8) & 0xFF);
            data[5] = 0x00;
            data[6] = 0x00;
            
            var frame = CreateIFrame(0x28, 0x06, data);
            await _stream!.WriteAsync(frame, 0, frame.Length);
            Console.WriteLine($"[>] Parameter normalized: IOA={ioa}, Value={value}");
        }

        public async Task SendParameterScaledAsync(int ioa, short value)
        {
            var data = new byte[7];
            data[0] = (byte)(ioa & 0xFF);
            data[1] = (byte)((ioa >> 8) & 0xFF);
            data[2] = (byte)((ioa >> 16) & 0xFF);
            data[3] = (byte)(value & 0xFF);
            data[4] = (byte)((value >> 8) & 0xFF);
            data[5] = 0x00;
            data[6] = 0x00;
            
            var frame = CreateIFrame(0x29, 0x06, data);
            await _stream!.WriteAsync(frame, 0, frame.Length);
            Console.WriteLine($"[>] Parameter scaled: IOA={ioa}, Value={value}");
        }

        public async Task SendParameterFloatAsync(int ioa, float value)
        {
            var data = new byte[7];
            data[0] = (byte)(ioa & 0xFF);
            data[1] = (byte)((ioa >> 8) & 0xFF);
            data[2] = (byte)((ioa >> 16) & 0xFF);
            
            byte[] floatBytes = BitConverter.GetBytes(value);
            data[3] = floatBytes[0];
            data[4] = floatBytes[1];
            data[5] = floatBytes[2];
            data[6] = floatBytes[3];
            
            var frame = CreateIFrame(0x2A, 0x06, data);
            await _stream!.WriteAsync(frame, 0, frame.Length);
            Console.WriteLine($"[>] Parameter float: IOA={ioa}, Value={value:F4}");
        }

        public async Task SendResetProcessCommandAsync()
        {
            var data = new byte[1] { 0x01 };
            var frame = CreateIFrame(0x69, 0x05, data);
            await _stream!.WriteAsync(frame, 0, frame.Length);
            Console.WriteLine("[>] Reset process command sent");
        }

        public async Task RequestInterrogationAsync(byte group = 0)
        {
            var data = new byte[1] { group };
            var frame = CreateIFrame(0x64, 0x05, data);
            await _stream!.WriteAsync(frame, 0, frame.Length);
            if (group == 0)
                Console.WriteLine("[>] General Interrogation (GI) request sent");
            else
                Console.WriteLine($"[>] Group Interrogation request sent: Group {group}");
        }

        public async Task RequestCounterInterrogationAsync(byte group = 0)
        {
            var data = new byte[1] { group };
            var frame = CreateIFrame(0x65, 0x05, data);
            await _stream!.WriteAsync(frame, 0, frame.Length);
            if (group == 0)
                Console.WriteLine("[>] Counter Interrogation request sent");
            else
                Console.WriteLine($"[>] Counter Group Interrogation: Group {group}");
        }

        public async Task RequestClockSyncAsync()
        {
            var now = DateTime.Now;
            var data = new byte[7];
            data[0] = (byte)now.Second;
            data[1] = (byte)now.Minute;
            data[2] = (byte)now.Hour;
            data[3] = (byte)now.Day;
            data[4] = (byte)now.Month;
            data[5] = (byte)(now.Year - 2000);
            data[6] = (byte)((int)now.DayOfWeek + 3);
            
            var frame = CreateIFrame(0x67, 0x06, data);
            await _stream!.WriteAsync(frame, 0, frame.Length);
            Console.WriteLine($"[>] Clock sync request sent: {now:yyyy-MM-dd HH:mm:ss}");
        }

        public async Task SendTestCommandAsync()
        {
            var now = DateTime.Now;
            var data = new byte[7];
            data[0] = (byte)now.Second;
            data[1] = (byte)now.Minute;
            data[2] = (byte)now.Hour;
            data[3] = (byte)now.Day;
            data[4] = (byte)now.Month;
            data[5] = (byte)(now.Year - 2000);
            data[6] = (byte)((int)now.DayOfWeek + 3);
            
            var frame = CreateIFrame(0x68, 0x06, data);
            await _stream!.WriteAsync(frame, 0, frame.Length);
            Console.WriteLine("[>] Test command sent (C_TS_TA_1)");
        }

        public async Task SendReadCommandAsync(int ioa)
        {
            var data = new byte[3];
            data[0] = (byte)(ioa & 0xFF);
            data[1] = (byte)((ioa >> 8) & 0xFF);
            data[2] = (byte)((ioa >> 16) & 0xFF);
            
            var frame = CreateIFrame(0x6D, 0x05, data);
            await _stream!.WriteAsync(frame, 0, frame.Length);
            Console.WriteLine($"[>] Read command sent: IOA={ioa}");
        }

        public void StartCyclicScan(int intervalMs)
        {
            _cyclicTimer?.Dispose();
            _cyclicTimer = new Timer(async _ => 
            {
                if (_connected)
                {
                    try { await RequestInterrogationAsync(); }
                    catch { }
                }
            }, null, intervalMs, intervalMs);
            Console.WriteLine($"[+] Cyclic scan started: every {intervalMs}ms");
        }

        public void StopCyclicScan()
        {
            _cyclicTimer?.Dispose();
            _cyclicTimer = null;
            Console.WriteLine("[*] Cyclic scan stopped");
        }

        public void SetSpontaneousEnabled(bool enabled)
        {
            _spontaneousEnabled = enabled;
            Console.WriteLine($"[+] Spontaneous messages: {(enabled ? "enabled" : "disabled")}");
        }

        private async Task ReceiveLoop(CancellationToken ct)
        {
            var buffer = new byte[512];
            while (!ct.IsCancellationRequested && _stream != null)
            {
                try
                {
                    int read = await _stream.ReadAsync(buffer, 0, buffer.Length, ct);
                    if (read > 0)
                    {
                        ProcessReceivedData(buffer, read);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Console.WriteLine($"[-] Receive error: {ex.Message}");
                    break;
                }
            }
        }

        private void ProcessReceivedData(byte[] buffer, int length)
        {
            int pos = 0;
            while (pos < length)
            {
                if (buffer[pos] != 0x68) { pos++; continue; }
                
                if (pos + 2 > length) break;
                int asduLen = buffer[pos + 1];
                if (asduLen < 4 || pos + 6 + asduLen > length) break;
                
                byte ctrl1 = buffer[pos + 2];
                byte ctrl2 = buffer[pos + 3];
                byte ctrl3 = buffer[pos + 4];
                byte ctrl4 = buffer[pos + 5];
                
                int frameType = (ctrl2 >> 1) & 0x03;
                
                if (frameType == 0)
                {
                    lock (_lock)
                    {
                        _recvSeq = (byte)((ctrl3 >> 1) & 0x7F);
                    }
                    
                    var asdu = new byte[asduLen];
                    Array.Copy(buffer, pos + 6, asdu, 0, asduLen);
                    ParseAsdu(asdu);
                }
                else if (frameType == 1)
                {
                    lock (_lock)
                    {
                        _recvSeq = (byte)((ctrl3 >> 1) & 0x7F);
                    }
                }
                else if (frameType == 3)
                {
                    if ((ctrl4 & 0x07) == 0x03)
                        Console.WriteLine("[*] U-Frame: STARTDT-CON (connection active)");
                    else if ((ctrl4 & 0x07) == 0x07)
                        Console.WriteLine("[*] U-Frame: STARTDT-ACT (waiting for confirmation)");
                    else if ((ctrl4 & 0x07) == 0x0B)
                        Console.WriteLine("[*] U-Frame: TESTFR-ACT");
                    else if ((ctrl4 & 0x07) == 0x23)
                        Console.WriteLine("[*] U-Frame: TESTFR-CON");
                }
                
                pos += 6 + asduLen;
            }
        }

        private void ParseAsdu(byte[] asdu)
        {
            byte typeId = asdu[0];
            byte sqNum = asdu[1];
            byte cot = asdu[2];
            byte origin = asdu[3];
            int asduAddr = asdu[4] | (asdu[5] << 8);
            
            string cotStr = cot switch
            {
                0x01 => "Periodic",
                0x02 => "Background",
                0x03 => "Spontaneous",
                0x04 => "Init",
                0x05 => "Request",
                0x06 => "Activation",
                0x07 => "Act-OK",
                0x08 => "Act-FAIL",
                0x09 => "Deactivation",
                0x0A => "Deact-OK",
                0x0B => "Deact-FAIL",
                0x0D => "Return",
                0x0E => "Return-Ctrl",
                _ => $"COT({cot:X2})"
            };
            
            string typeStr = GetTypeName(typeId);
            
            if (!_spontaneousEnabled && cot == 0x03) return;
            
            Console.WriteLine($"\n[<] ASDU: {typeStr}, COT={cotStr}, ASDU Addr={asduAddr}");
            
            int numObjects = (sqNum & 0x7F);
            bool isSequence = (sqNum & 0x80) != 0;
            
            int dataStart = 6;
            int ioaSize = 3;
            
            for (int i = 0; i < numObjects; i++)
            {
                int ioa;
                if (isSequence)
                {
                    ioa = asdu[dataStart] | (asdu[dataStart + 1] << 8) | (asdu[dataStart + 2] << 16);
                }
                else
                {
                    int objOffset = dataStart + i * (ioaSize + GetObjectSize(typeId) + (HasTimestamp(typeId) ? 7 : 0));
                    ioa = asdu[objOffset] | (asdu[objOffset + 1] << 8) | (asdu[objOffset + 2] << 16);
                }
                
                string valueStr = DecodeValue(asdu, typeId, dataStart, ioaSize, isSequence, numObjects, i);
                string timeStr = HasTimestamp(typeId) ? $" @ {DecodeTimestamp(asdu, GetTimestampOffset(typeId, dataStart, ioaSize, isSequence, numObjects, i))}" : "";
                
                Console.WriteLine($"    IOA={ioa}: {valueStr}{timeStr}");
            }
        }

        private string GetTypeName(byte typeId)
        {
            return typeId switch
            {
                0x01 => "M_SP_NA_1", 0x02 => "M_SP_TA_1", 0x03 => "M_DP_NA_1",
                0x04 => "M_DP_TA_1", 0x05 => "M_ST_NA_1", 0x06 => "M_ST_TA_1",
                0x07 => "M_BO_NA_1", 0x08 => "M_BO_TA_1", 0x09 => "M_ME_NA_1",
                0x0A => "M_ME_TA_1", 0x0B => "M_ME_NB_1", 0x0C => "M_ME_TB_1",
                0x0D => "M_ME_NC_1", 0x0E => "M_ME_TC_1", 0x0F => "M_IT_NA_1",
                0x10 => "M_IT_TA_1", 0x11 => "M_EP_NA_1", 0x12 => "M_EP_TA_1",
                0x13 => "M_EP_NB_1", 0x14 => "M_EP_TB_1", 0x15 => "M_EP_NC_1",
                0x16 => "M_EP_TC_1", 0x1E => "M_PS_NA_1", 0x1F => "M_ME_ND_1",
                0x28 => "P_ME_NA_1", 0x29 => "P_ME_NB_1", 0x2A => "P_ME_NC_1",
                0x2D => "C_SC_NA_1", 0x2E => "C_DC_NA_1", 0x2F => "C_RC_NA_1",
                0x30 => "C_SE_NA_1", 0x31 => "C_SE_NB_1", 0x32 => "C_SE_NC_1",
                0x33 => "C_BC_NA_1", 0x64 => "C_IC_NA_1", 0x65 => "C_CI_NA_1",
                0x66 => "C_RD_NA_1", 0x67 => "C_CS_NA_1", 0x68 => "C_TS_NA_1",
                0x69 => "C_RP_NA_1", 0x6A => "C_CD_NA_1", 0x6B => "C_TS_TA_1",
                _ => $"Type{typeId:X2}"
            };
        }

        private int GetObjectSize(byte typeId)
        {
            return typeId switch
            {
                0x01 => 1,   // M_SP_NA_1 Single
                0x02 => 1,   // M_SP_TA_1 Single with time
                0x03 => 1,   // M_DP_NA_1 Double
                0x04 => 1,   // M_DP_TA_1 Double with time
                0x05 => 2,   // M_ST_NA_1 Step position
                0x06 => 2,   // M_ST_TA_1 Step position with time
                0x07 => 4,   // M_BO_NA_1 Bitstring32
                0x08 => 4,   // M_BO_TA_1 Bitstring32 with time
                0x09 => 2,   // M_ME_NA_1 Normalized
                0x0A => 2,   // M_ME_TA_1 Normalized with time
                0x0B => 2,   // M_ME_NB_1 Scaled
                0x0C => 2,   // M_ME_TB_1 Scaled with time
                0x0D => 4,   // M_ME_NC_1 Float
                0x0E => 4,   // M_ME_TC_1 Float with time
                0x0F => 5,   // M_IT_NA_1 Integrated totals
                0x10 => 5,   // M_IT_TA_1 Integrated totals with time
                0x11 => 2,   // M_EP_NA_1 Status change
                0x12 => 2,   // M_EP_TA_1 Status change with time
                0x13 => 3,   // M_EP_NB_1 Protection
                0x14 => 3,   // M_EP_TB_1 Protection with time
                0x15 => 4,   // M_EP_NC_1 Protection with time
                0x16 => 4,   // M_EP_TC_1 Protection with time
                0x1E => 1,   // M_PS_NA_1 Packed single
                0x1F => 2,   // M_ME_ND_1 Normalized without quality
                0x28 => 7,   // P_ME_NA_1 Parameter Normalized
                0x29 => 7,   // P_ME_NB_1 Parameter Scaled
                0x2A => 7,   // P_ME_NC_1 Parameter Float
                0x2D => 5,   // C_SC_NA_1 Single command
                0x2E => 5,   // C_DC_NA_1 Double command
                0x2F => 5,   // C_RC_NA_1 Regulating step command
                0x30 => 7,   // C_SE_NA_1 Setpoint normalized
                0x31 => 7,   // C_SE_NB_1 Setpoint scaled
                0x32 => 7,   // C_SE_NC_1 Setpoint float
                0x33 => 7,   // C_BC_NA_1 Bitstring command
                0x64 => 1,   // C_IC_NA_1 Interrogation
                0x65 => 1,   // C_CI_NA_1 Counter interrogation
                0x66 => 3,   // C_RD_NA_1 Read
                0x67 => 7,   // C_CS_NA_1 Clock sync
                0x68 => 1,   // C_TS_NA_1 Test without time
                0x69 => 1,   // C_RP_NA_1 Reset process
                0x6A => 1,   // C_CD_NA_1 Delay acquisition
                0x6B => 7,   // C_TS_TA_1 Test with time
                _ => 1
            };
        }

        public string GetTypeDescription(byte typeId)
        {
            return typeId switch
            {
                0x01 => "M_SP_NA_1 - Single (boolean)",
                0x02 => "M_SP_TA_1 - Single with time",
                0x03 => "M_DP_NA_1 - Double (0-3)",
                0x04 => "M_DP_TA_1 - Double with time",
                0x05 => "M_ST_NA_1 - Step (0-3)",
                0x06 => "M_ST_TA_1 - Step with time",
                0x07 => "M_BO_NA_1 - Bitstring32 (DWORD)",
                0x08 => "M_BO_TA_1 - Bitstring32 with time",
                0x09 => "M_ME_NA_1 - Normalized (short, -32768 to 32767)",
                0x0A => "M_ME_TA_1 - Normalized with time",
                0x0B => "M_ME_NB_1 - Scaled (short, -32768 to 32767)",
                0x0C => "M_ME_TB_1 - Scaled with time",
                0x0D => "M_ME_NC_1 - Float (float)",
                0x0E => "M_ME_TC_1 - Float with time",
                0x0F => "M_IT_NA_1 - Counter (unsigned int32)",
                0x10 => "M_IT_TA_1 - Counter with time",
                0x11 => "M_EP_NA_1 - Status change",
                0x12 => "M_EP_TA_1 - Status change with time",
                0x13 => "M_EP_NB_1 - Protection (short)",
                0x14 => "M_EP_TB_1 - Protection with time",
                0x15 => "M_EP_NC_1 - Protection (float)",
                0x16 => "M_EP_TC_1 - Protection with time",
                0x1E => "M_PS_NA_1 - Packed single",
                0x1F => "M_ME_ND_1 - Normalized without quality",
                0x28 => "P_ME_NA_1 - Parameter Normalized",
                0x29 => "P_ME_NB_1 - Parameter Scaled",
                0x2A => "P_ME_NC_1 - Parameter Float",
                0x2D => "C_SC_NA_1 - Single Command",
                0x2E => "C_DC_NA_1 - Double Command",
                0x2F => "C_RC_NA_1 - Regulating Step Command",
                0x30 => "C_SE_NA_1 - Setpoint Normalized",
                0x31 => "C_SE_NB_1 - Setpoint Scaled",
                0x32 => "C_SE_NC_1 - Setpoint Float",
                0x33 => "C_BC_NA_1 - Bitstring Command",
                0x64 => "C_IC_NA_1 - Interrogation",
                0x65 => "C_CI_NA_1 - Counter Interrogation",
                0x66 => "C_RD_NA_1 - Read Command",
                0x67 => "C_CS_NA_1 - Clock Sync",
                0x68 => "C_TS_NA_1 - Test Command",
                0x69 => "C_RP_NA_1 - Reset Process Command",
                0x6A => "C_CD_NA_1 - Delay Acquisition",
                0x6B => "C_TS_TA_1 - Test Command with time",
                _ => $"Type{typeId:X2}"
            };
        }

        private bool HasTimestamp(byte typeId)
        {
            return typeId switch
            {
                0x02 or 0x04 or 0x06 or 0x08 or
                0x0A or 0x0C or 0x0E or 0x10 or
                0x12 or 0x14 or 0x16 or 0x6B => true,
                _ => false
            };
        }

        private string DecodeTimestamp(byte[] asdu, int offset)
        {
            if (offset + 7 > asdu.Length) return "";
            byte msb = asdu[offset];
            byte lsb = asdu[offset + 1];
            int milliseconds = (lsb << 8) | msb;
            int hour = asdu[offset + 2] & 0x1F;
            int minute = asdu[offset + 3] & 0x3F;
            int day = asdu[offset + 4] & 0x1F;
            int month = asdu[offset + 5] & 0x0F;
            int year = (asdu[offset + 6] & 0xFF) + 2000;
            return $"{year}-{month:D2}-{day:D2} {hour:D2}:{minute:D2}:{milliseconds / 1000:D2}.{milliseconds % 1000:D3}";
        }

        private int GetTimestampOffset(byte typeId, int dataStart, int ioaSize, bool isSequence, int numObjects, int index)
        {
            int objSize = GetObjectSize(typeId);
            if (isSequence)
            {
                return dataStart + ioaSize + index * (objSize + 7);
            }
            else
            {
                return dataStart + index * (ioaSize + objSize + 7) + ioaSize + objSize;
            }
        }

        private string DecodeValue(byte[] asdu, byte typeId, int dataStart, int ioaSize, bool isSequence, int numObjects, int index)
        {
            int objOffset;
            if (isSequence)
            {
                objOffset = dataStart + ioaSize + index * GetObjectSize(typeId);
            }
            else
            {
                objOffset = dataStart + index * (ioaSize + GetObjectSize(typeId) + (HasTimestamp(typeId) ? 7 : 0)) + ioaSize;
            }
            
            int objSize = GetObjectSize(typeId);
            if (objOffset + objSize > asdu.Length) return "N/A";
            
            return typeId switch
            {
                0x01 => (asdu[objOffset] & 0x01) != 0 ? "ON" : "OFF",  // M_SP_NA_1
                0x02 => (asdu[objOffset] & 0x01) != 0 ? "ON" : "OFF",  // M_SP_TA_1
                0x03 => (asdu[objOffset] & 0x03) switch { 0 => "Intermediate", 1 => "OFF", 2 => "ON", 3 => "Indeterminate", _ => "?" },  // M_DP_NA_1
                0x04 => (asdu[objOffset] & 0x03) switch { 0 => "Intermediate", 1 => "OFF", 2 => "ON", 3 => "Indeterminate", _ => "?" },  // M_DP_TA_1
                0x05 => (asdu[objOffset] & 0x7F) switch { 0 => "Lower", 1 => "Descend", 2 => "Ascend", 3 => "Higher", _ => "?" },  // M_ST_NA_1
                0x06 => (asdu[objOffset] & 0x7F) switch { 0 => "Lower", 1 => "Descend", 2 => "Ascend", 3 => "Higher", _ => "?" },  // M_ST_TA_1
                0x07 => DecodeBitstring32(asdu, objOffset),  // M_BO_NA_1
                0x08 => DecodeBitstring32(asdu, objOffset),  // M_BO_TA_1
                0x09 => DecodeNormalized(asdu, objOffset),   // M_ME_NA_1
                0x0A => DecodeNormalized(asdu, objOffset),  // M_ME_TA_1
                0x0B => BitConverter.ToInt16(asdu, objOffset).ToString(),  // M_ME_NB_1
                0x0C => BitConverter.ToInt16(asdu, objOffset).ToString(),  // M_ME_TB_1
                0x0D => BitConverter.ToSingle(asdu, objOffset).ToString("F4"),  // M_ME_NC_1
                0x0E => BitConverter.ToSingle(asdu, objOffset).ToString("F4"),  // M_ME_TC_1
                0x0F => DecodeCounter(asdu, objOffset),  // M_IT_NA_1
                0x10 => DecodeCounter(asdu, objOffset),  // M_IT_TA_1
                0x11 => $"SC:{asdu[objOffset]}, DQ:{asdu[objOffset+1]}",  // M_EP_NA_1
                0x12 => $"SC:{asdu[objOffset]}, DQ:{asdu[objOffset+1]}",  // M_EP_TA_1
                0x13 => $"Events:{asdu[objOffset]}, Status:{asdu[objOffset+1]}, Quality:{asdu[objOffset+2]}",  // M_EP_NB_1
                0x14 => $"Events:{asdu[objOffset]}, Status:{asdu[objOffset+1]}, Quality:{asdu[objOffset+2]}",  // M_EP_TB_1
                0x15 => $"Events:{asdu[objOffset]}, Status:{asdu[objOffset+1]}, Quality:{asdu[objOffset+2]}",  // M_EP_NC_1
                0x16 => $"Events:{asdu[objOffset]}, Status:{asdu[objOffset+1]}, Quality:{asdu[objOffset+2]}",  // M_EP_TC_1
                0x1E => DecodePackedSingle(asdu, objOffset),  // M_PS_NA_1
                0x1F => BitConverter.ToInt16(asdu, objOffset).ToString(),  // M_ME_ND_1
                0x28 => $"Param N:{BitConverter.ToInt16(asdu, objOffset)}, Q:{asdu[objOffset+6]}",  // P_ME_NA_1
                0x29 => $"Param S:{BitConverter.ToInt16(asdu, objOffset)}, Q:{asdu[objOffset+6]}",  // P_ME_NB_1
                0x2A => $"Param F:{BitConverter.ToSingle(asdu, objOffset):F4}, Q:{asdu[objOffset+6]}",  // P_ME_NC_1
                0x2D => (asdu[objOffset] & 0x01) != 0 ? "ON" : "OFF",  // C_SC_NA_1
                0x2E => (asdu[objOffset] & 0x03) switch { 0 => "Intermediate", 1 => "OFF", 2 => "ON", 3 => "Indeterminate", _ => "?" },  // C_DC_NA_1
                0x2F => (asdu[objOffset] & 0x03) switch { 0 => "Lower", 1 => "Descend", 2 => "Ascend", 3 => "Higher", _ => "?" },  // C_RC_NA_1
                0x30 => BitConverter.ToInt16(asdu, objOffset).ToString(),  // C_SE_NA_1
                0x31 => BitConverter.ToInt16(asdu, objOffset).ToString(),  // C_SE_NB_1
                0x32 => BitConverter.ToSingle(asdu, objOffset).ToString("F4"),  // C_SE_NC_1
                0x33 => DecodeBitstring32(asdu, objOffset),  // C_BC_NA_1
                0x64 => $"Group:{asdu[dataStart]}",  // C_IC_NA_1
                0x65 => $"Group:{asdu[dataStart]}",  // C_CI_NA_1
                0x66 => $"IOA:{asdu[dataStart] | (asdu[dataStart+1] << 8) | (asdu[dataStart+2] << 16)}",  // C_RD_NA_1
                0x67 => "Clock Sync",  // C_CS_NA_1
                0x68 => "Test",  // C_TS_NA_1
                0x69 => "Reset",  // C_RP_NA_1
                0x6B => "Test with time",  // C_TS_TA_1
                _ => BitConverter.ToString(asdu, objOffset, Math.Min(objSize, asdu.Length - objOffset))
            };
        }

        private string DecodeBitstring32(byte[] asdu, int offset)
        {
            if (offset + 4 > asdu.Length) return "N/A";
            uint val = BitConverter.ToUInt32(asdu, offset);
            return $"0x{val:X8} ({val})";
        }

        private string DecodeNormalized(byte[] asdu, int offset)
        {
            if (offset + 2 > asdu.Length) return "N/A";
            short val = BitConverter.ToInt16(asdu, offset);
            return val.ToString();
        }

        private string DecodeCounter(byte[] asdu, int offset)
        {
            if (offset + 5 > asdu.Length) return "N/A";
            uint val = BitConverter.ToUInt32(asdu, offset);
            byte sq = asdu[offset + 4];
            return $"{val} (SQ:{sq})";
        }

        private string DecodePackedSingle(byte[] asdu, int offset)
        {
            if (offset + 1 > asdu.Length) return "N/A";
            byte b = asdu[offset];
            var bits = new List<string>();
            for (int i = 0; i < 8; i++)
                if ((b & (1 << i)) != 0) bits.Add($"S{i+1}");
            return bits.Count > 0 ? string.Join(",", bits) : "None";
        }

        private string DecodeIntegratedTotals(byte[] asdu, int offset)
        {
            if (offset + 7 > asdu.Length) return "N/A";
            ulong val = BitConverter.ToUInt64(asdu, offset);
            return $"{val}";
        }
    }

    public class Iec104Redundancy
    {
        private Iec104Simulator _primary = new();
        private Iec104Simulator _backup = new();
        private bool _activePrimary = true;
        private Timer? _healthCheckTimer;
        private readonly object _lock = new();
        private string? _primaryIp;
        private string? _backupIp;
        private int _primaryPort;
        private int _backupPort;

        public bool IsPrimaryActive => _activePrimary;
        public bool IsBackupActive => !_activePrimary;
        public bool IsPrimaryConnected => _primary.IsConnected;
        public bool IsBackupConnected => _backup.IsConnected;

        public async Task ConnectBothAsync(string ip1, int port1, string ip2, int port2)
        {
            _primaryIp = ip1;
            _backupIp = ip2;
            _primaryPort = port1;
            _backupPort = port2;

            Console.WriteLine($"[*] Connecting to primary: {ip1}:{port1}");
            var primaryTask = _primary.ConnectAsync(ip1, port1);
            
            Console.WriteLine($"[*] Connecting to backup: {ip2}:{port2}");
            var backupTask = _backup.ConnectAsync(ip2, port2);

            await Task.WhenAll(primaryTask, backupTask);

            if (_primary.IsConnected)
            {
                _activePrimary = true;
                Console.WriteLine("[+] Primary connection active");
            }
            else if (_backup.IsConnected)
            {
                _activePrimary = false;
                Console.WriteLine("[+] Backup connection active (primary failed)");
            }
            else
            {
                Console.WriteLine("[-] Both connections failed");
                return;
            }

            StartHealthCheck();
        }

        public Iec104Simulator GetActiveConnection()
        {
            return _activePrimary ? _primary : _backup;
        }

        public void SwitchToBackup()
        {
            lock (_lock)
            {
                if (!_backup.IsConnected)
                {
                    Console.WriteLine("[-] Cannot switch: backup not connected");
                    return;
                }

                _activePrimary = false;
                Console.WriteLine("[*] Switched to backup connection");
            }
        }

        public void SwitchToPrimary()
        {
            lock (_lock)
            {
                if (!_primary.IsConnected)
                {
                    Console.WriteLine("[-] Cannot switch: primary not connected");
                    return;
                }

                _activePrimary = true;
                Console.WriteLine("[*] Switched to primary connection");
            }
        }

        public void DisconnectBoth()
        {
            _healthCheckTimer?.Dispose();
            _healthCheckTimer = null;
            _primary.Disconnect();
            _backup.Disconnect();
            Console.WriteLine("[*] Both connections disconnected");
        }

        private void StartHealthCheck()
        {
            _healthCheckTimer?.Dispose();
            _healthCheckTimer = new Timer(CheckConnections, null, 5000, 5000);
        }

        private async void CheckConnections(object? state)
        {
            try
            {
                lock (_lock)
                {
                    if (_activePrimary && !_primary.IsConnected)
                    {
                        Console.WriteLine("[-] Primary connection lost, switching to backup");
                        if (_backup.IsConnected)
                        {
                            _activePrimary = false;
                        }
                    }
                    else if (!_activePrimary && !_backup.IsConnected)
                    {
                        Console.WriteLine("[-] Backup connection lost, switching to primary");
                        if (_primary.IsConnected)
                        {
                            _activePrimary = true;
                        }
                    }
                }
                await Task.CompletedTask;
            }
            catch { }
        }

        public string GetStatus()
        {
            string active = _activePrimary ? "PRIMARY" : "BACKUP";
            string primaryStatus = _primary.IsConnected ? "Connected" : "Disconnected";
            string backupStatus = _backup.IsConnected ? "Connected" : "Disconnected";
            return $"Active: {active} | Primary: {primaryStatus} | Backup: {backupStatus}";
        }
    }

    public class Iec104MultiConnection
    {
        private Dictionary<string, Iec104Simulator> _connections = new();
        private readonly object _lock = new();

        public int Count
        {
            get { lock (_lock) return _connections.Count; }
        }

        public async Task<bool> ConnectAsync(string id, string ip, int port)
        {
            lock (_lock)
            {
                if (_connections.ContainsKey(id))
                {
                    Console.WriteLine($"[-] Connection ID '{id}' already exists");
                    return false;
                }
            }

            var simulator = new Iec104Simulator();
            var result = await simulator.ConnectAsync(ip, port);

            if (result)
            {
                lock (_lock)
                {
                    _connections[id] = simulator;
                }
                Console.WriteLine($"[+] Connection '{id}' added: {ip}:{port}");
                return true;
            }

            return false;
        }

        public async Task DisconnectAsync(string id)
        {
            Iec104Simulator? sim;
            lock (_lock)
            {
                if (!_connections.TryGetValue(id, out sim))
                {
                    Console.WriteLine($"[-] Connection '{id}' not found");
                    return;
                }
                _connections.Remove(id);
            }

            sim.Disconnect();
            Console.WriteLine($"[*] Connection '{id}' disconnected");
        }

        public void DisconnectAll()
        {
            lock (_lock)
            {
                foreach (var sim in _connections.Values)
                {
                    sim.Disconnect();
                }
                _connections.Clear();
            }
            Console.WriteLine("[*] All connections disconnected");
        }

        public async Task SendToAsync(string id, string command, params string[] args)
        {
            Iec104Simulator? sim;
            lock (_lock)
            {
                if (!_connections.TryGetValue(id, out sim))
                {
                    Console.WriteLine($"[-] Connection '{id}' not found");
                    return;
                }
            }

            try
            {
                switch (command.ToLower())
                {
                    case "sc":
                        if (args.Length >= 2 && int.TryParse(args[0], out int scIOA) && int.TryParse(args[1], out int scVal))
                            await sim.SendSingleCommandAsync(scIOA, scVal == 1);
                        else
                            Console.WriteLine($"Usage: msend {id} sc <ioa> <0|1>");
                        break;
                    case "dc":
                        if (args.Length >= 2 && int.TryParse(args[0], out int dcIOA) && int.TryParse(args[1], out int dcState))
                            await sim.SendDoubleCommandAsync(dcIOA, dcState);
                        else
                            Console.WriteLine($"Usage: msend {id} dc <ioa> <0-3>");
                        break;
                    case "rc":
                        if (args.Length >= 2 && int.TryParse(args[0], out int rcIOA) && int.TryParse(args[1], out int rcState))
                            await sim.SendStepCommandAsync(rcIOA, rcState);
                        else
                            Console.WriteLine($"Usage: msend {id} rc <ioa> <0-3>");
                        break;
                    case "sn":
                        if (args.Length >= 2 && int.TryParse(args[0], out int snIOA) && short.TryParse(args[1], out short snVal))
                            await sim.SendSetpointNormalizedAsync(snIOA, snVal);
                        else
                            Console.WriteLine($"Usage: msend {id} sn <ioa> <value>");
                        break;
                    case "sf":
                        if (args.Length >= 2 && int.TryParse(args[0], out int sfIOA) && float.TryParse(args[1], out float sfVal))
                            await sim.SendSetpointFloatAsync(sfIOA, sfVal);
                        else
                            Console.WriteLine($"Usage: msend {id} sf <ioa> <value>");
                        break;
                    case "sb":
                        if (args.Length >= 2 && int.TryParse(args[0], out int sbIOA) && short.TryParse(args[1], out short sbVal))
                            await sim.SendSetpointScaledAsync(sbIOA, sbVal);
                        else
                            Console.WriteLine($"Usage: msend {id} sb <ioa> <value>");
                        break;
                    case "si":
                        if (args.Length >= 2 && int.TryParse(args[0], out int siIOA) && int.TryParse(args[1], out int siVal))
                            await sim.SendSetpointInt32Async(siIOA, siVal);
                        else
                            Console.WriteLine($"Usage: msend {id} si <ioa> <value>");
                        break;
                    case "gi":
                        byte giGroup = 0;
                        if (args.Length >= 1 && byte.TryParse(args[0], out byte g))
                            giGroup = g;
                        await sim.RequestInterrogationAsync(giGroup);
                        break;
                    case "ci":
                        byte ciGroup = 0;
                        if (args.Length >= 1 && byte.TryParse(args[0], out byte c))
                            ciGroup = c;
                        await sim.RequestCounterInterrogationAsync(ciGroup);
                        break;
                    case "cs":
                        await sim.RequestClockSyncAsync();
                        break;
                    case "test":
                        await sim.SendTestCommandAsync();
                        break;
                    case "read":
                        if (args.Length >= 1 && int.TryParse(args[0], out int rdIOA))
                            await sim.SendReadCommandAsync(rdIOA);
                        else
                            Console.WriteLine($"Usage: msend {id} read <ioa>");
                        break;
                    default:
                        Console.WriteLine($"[-] Unknown command: {command}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[-] Send error: {ex.Message}");
            }
        }

        public Iec104Simulator? GetConnection(string id)
        {
            lock (_lock)
            {
                _connections.TryGetValue(id, out var sim);
                return sim;
            }
        }

        public Dictionary<string, Iec104Simulator> ListConnections()
        {
            lock (_lock)
            {
                return new Dictionary<string, Iec104Simulator>(_connections);
            }
        }

        public bool Contains(string id)
        {
            lock (_lock)
            {
                return _connections.ContainsKey(id);
            }
        }
    }

    class Program
    {
        static Iec104Redundancy? _redundancy = null;
        static Iec104MultiConnection? _multiConn = null;
        static Iec104Simulator? _simulator = null;

        static async Task Main(string[] args)
        {
            _simulator = new Iec104Simulator();
            
            Console.WriteLine("=========================================================");
            Console.WriteLine("      IEC 60870-5-104 Simulator (Master/Controller)      ");
            Console.WriteLine("=========================================================");
            
            while (true)
            {
                ShowMainMenu();
                var choice = ReadOption(1, 5);
                
                switch (choice)
                {
                    case 1:
                        await ShowSingleConnectionMenu();
                        break;
                    case 2:
                        await MultiConnectionMenuAsync();
                        break;
                    case 3:
                        await RedundancyMenuAsync();
                        break;
                    case 4:
                        ShowAllDataTypes();
                        break;
                    case 5:
                        CleanupAndExit();
                        return;
                }
            }
        }

        static void ShowMainMenu()
        {
            Console.WriteLine();
            Console.WriteLine("--- Main Menu ---");
            Console.WriteLine("1. Single Connection");
            Console.WriteLine("2. Multi-Connection (Multiple RTUs)");
            Console.WriteLine("3. Redundancy (Hot Standby)");
            Console.WriteLine("4. Show All Data Types");
            Console.WriteLine("5. Exit");
            Console.WriteLine();
        }

        static async Task ShowSingleConnectionMenu()
        {
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("--- Single Connection Menu ---");
                Console.WriteLine("1.  Connect to device");
                Console.WriteLine("2.  Disconnect");
                Console.WriteLine("3.  Single Command (SC)");
                Console.WriteLine("4.  Double Command (DC)");
                Console.WriteLine("5.  Step Command (RC)");
                Console.WriteLine("6.  Setpoint Normalized (SN)");
                Console.WriteLine("7.  Setpoint Scaled (SB)");
                Console.WriteLine("8.  Setpoint Float (SF)");
                Console.WriteLine("9.  Setpoint Int32 (SI)");
                Console.WriteLine("10. Bitstring (BS)");
                Console.WriteLine("11. General Interrogation");
                Console.WriteLine("12. Counter Interrogation");
                Console.WriteLine("13. Clock Sync");
                Console.WriteLine("14. Test Command");
                Console.WriteLine("15. Read IOA");
                Console.WriteLine("16. Cyclic Scan");
                Console.WriteLine("17. Stop Scan");
                Console.WriteLine("18. Spontaneous On/Off");
                Console.WriteLine("19. Back to Main Menu");
                Console.WriteLine();
                
                var choice = ReadOption(1, 19);
                
                if (choice == 19)
                    return;
                
                await HandleSingleConnectionChoice(choice);
            }
        }

        static async Task HandleSingleConnectionChoice(int choice)
        {
            if (choice >= 3 && choice <= 18 && !(_simulator?.IsConnected ?? false))
            {
                Console.WriteLine("[-] Not connected. Connect first.");
                return;
            }
            
            try
            {
                switch (choice)
                {
                    case 1:
                        await ConnectToDevice();
                        break;
                    case 2:
                        _simulator?.Disconnect();
                        break;
                    case 3:
                        await SendSingleCommand();
                        break;
                    case 4:
                        await SendDoubleCommand();
                        break;
                    case 5:
                        await SendStepCommand();
                        break;
                    case 6:
                        await SendSetpointNormalized();
                        break;
                    case 7:
                        await SendSetpointScaled();
                        break;
                    case 8:
                        await SendSetpointFloat();
                        break;
                    case 9:
                        await SendSetpointInt32();
                        break;
                    case 10:
                        await SendBitstring();
                        break;
                    case 11:
                        await _simulator!.RequestInterrogationAsync();
                        break;
                    case 12:
                        await _simulator!.RequestCounterInterrogationAsync();
                        break;
                    case 13:
                        await _simulator!.RequestClockSyncAsync();
                        break;
                    case 14:
                        await _simulator!.SendTestCommandAsync();
                        break;
                    case 15:
                        await SendReadIOA();
                        break;
                    case 16:
                        StartCyclicScan();
                        break;
                    case 17:
                        _simulator?.StopCyclicScan();
                        break;
                    case 18:
                        ToggleSpontaneous();
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[-] Error: {ex.Message}");
            }
        }

        static async Task ConnectToDevice()
        {
            Console.Write("Enter IP address: ");
            var ip = Console.ReadLine();
            Console.Write("Enter port: ");
            if (!int.TryParse(Console.ReadLine(), out int port))
            {
                Console.WriteLine("[-] Invalid port");
                return;
            }
            
            if (_redundancy != null)
            {
                Console.WriteLine("[-] Disconnect redundancy first");
                return;
            }
            
            await _simulator!.ConnectAsync(ip!, port);
        }

        static async Task SendSingleCommand()
        {
            Console.Write("Enter IOA: ");
            if (!int.TryParse(Console.ReadLine(), out int ioa))
            {
                Console.WriteLine("[-] Invalid IOA");
                return;
            }
            Console.Write("Enter value (0=OFF, 1=ON): ");
            if (!int.TryParse(Console.ReadLine(), out int val))
            {
                Console.WriteLine("[-] Invalid value");
                return;
            }
            await _simulator!.SendSingleCommandAsync(ioa, val == 1);
        }

        static async Task SendDoubleCommand()
        {
            Console.Write("Enter IOA: ");
            if (!int.TryParse(Console.ReadLine(), out int ioa))
            {
                Console.WriteLine("[-] Invalid IOA");
                return;
            }
            Console.Write("Enter state (0=Inter, 1=OFF, 2=ON, 3=Indet): ");
            if (!int.TryParse(Console.ReadLine(), out int state))
            {
                Console.WriteLine("[-] Invalid state");
                return;
            }
            await _simulator!.SendDoubleCommandAsync(ioa, state);
        }

        static async Task SendStepCommand()
        {
            Console.Write("Enter IOA: ");
            if (!int.TryParse(Console.ReadLine(), out int ioa))
            {
                Console.WriteLine("[-] Invalid IOA");
                return;
            }
            Console.Write("Enter state (0=Lower, 1=Desc, 2=Asc, 3=Higher): ");
            if (!int.TryParse(Console.ReadLine(), out int state))
            {
                Console.WriteLine("[-] Invalid state");
                return;
            }
            await _simulator!.SendStepCommandAsync(ioa, state);
        }

        static async Task SendSetpointNormalized()
        {
            Console.Write("Enter IOA: ");
            if (!int.TryParse(Console.ReadLine(), out int ioa))
            {
                Console.WriteLine("[-] Invalid IOA");
                return;
            }
            Console.Write("Enter value (short): ");
            if (!short.TryParse(Console.ReadLine(), out short val))
            {
                Console.WriteLine("[-] Invalid value");
                return;
            }
            await _simulator!.SendSetpointNormalizedAsync(ioa, val);
        }

        static async Task SendSetpointScaled()
        {
            Console.Write("Enter IOA: ");
            if (!int.TryParse(Console.ReadLine(), out int ioa))
            {
                Console.WriteLine("[-] Invalid IOA");
                return;
            }
            Console.Write("Enter value (short): ");
            if (!short.TryParse(Console.ReadLine(), out short val))
            {
                Console.WriteLine("[-] Invalid value");
                return;
            }
            await _simulator!.SendSetpointScaledAsync(ioa, val);
        }

        static async Task SendSetpointFloat()
        {
            Console.Write("Enter IOA: ");
            if (!int.TryParse(Console.ReadLine(), out int ioa))
            {
                Console.WriteLine("[-] Invalid IOA");
                return;
            }
            Console.Write("Enter value (float): ");
            if (!float.TryParse(Console.ReadLine(), out float val))
            {
                Console.WriteLine("[-] Invalid value");
                return;
            }
            await _simulator!.SendSetpointFloatAsync(ioa, val);
        }

        static async Task SendSetpointInt32()
        {
            Console.Write("Enter IOA: ");
            if (!int.TryParse(Console.ReadLine(), out int ioa))
            {
                Console.WriteLine("[-] Invalid IOA");
                return;
            }
            Console.Write("Enter value (int): ");
            if (!int.TryParse(Console.ReadLine(), out int val))
            {
                Console.WriteLine("[-] Invalid value");
                return;
            }
            await _simulator!.SendSetpointInt32Async(ioa, val);
        }

        static async Task SendBitstring()
        {
            Console.Write("Enter IOA: ");
            if (!int.TryParse(Console.ReadLine(), out int ioa))
            {
                Console.WriteLine("[-] Invalid IOA");
                return;
            }
            Console.Write("Enter value (hex, e.g. FF): ");
            var hexStr = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(hexStr) || !byte.TryParse(hexStr, System.Globalization.NumberStyles.HexNumber, null, out byte val))
            {
                Console.WriteLine("[-] Invalid hex value");
                return;
            }
            await _simulator!.SendBitstringCommandAsync(ioa, new byte[] { val });
        }

        static async Task SendReadIOA()
        {
            Console.Write("Enter IOA to read: ");
            if (!int.TryParse(Console.ReadLine(), out int ioa))
            {
                Console.WriteLine("[-] Invalid IOA");
                return;
            }
            await _simulator!.SendReadCommandAsync(ioa);
        }

        static void StartCyclicScan()
        {
            Console.Write("Enter scan interval (ms): ");
            if (!int.TryParse(Console.ReadLine(), out int interval))
            {
                Console.WriteLine("[-] Invalid interval");
                return;
            }
            _simulator!.StartCyclicScan(interval);
        }

        static void ToggleSpontaneous()
        {
            Console.Write("Enable spontaneous? (y/n): ");
            var ans = Console.ReadLine()?.ToLower();
            _simulator!.SetSpontaneousEnabled(ans == "y");
        }

        static async Task MultiConnectionMenuAsync()
        {
            _multiConn ??= new Iec104MultiConnection();
            
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("--- Multi-Connection Menu ---");
                Console.WriteLine("1. Connect to RTU");
                Console.WriteLine("2. Disconnect RTU");
                Console.WriteLine("3. List Connections");
                Console.WriteLine("4. Send to RTU");
                Console.WriteLine("5. Back to Main Menu");
                Console.WriteLine();
                
                var choice = ReadOption(1, 5);
                
                switch (choice)
                {
                    case 1:
                        await MultiConnect();
                        break;
                    case 2:
                        await MultiDisconnect();
                        break;
                    case 3:
                        MultiListConnections();
                        break;
                    case 4:
                        await MultiSend();
                        break;
                    case 5:
                        return;
                }
            }
        }

        static async Task MultiConnect()
        {
            Console.Write("Enter RTU ID: ");
            var id = Console.ReadLine();
            Console.Write("Enter IP address: ");
            var ip = Console.ReadLine();
            Console.Write("Enter port: ");
            if (!int.TryParse(Console.ReadLine(), out int port))
            {
                Console.WriteLine("[-] Invalid port");
                return;
            }
            
            await _multiConn!.ConnectAsync(id!, ip!, port);
        }

        static async Task MultiDisconnect()
        {
            Console.Write("Enter RTU ID to disconnect: ");
            var id = Console.ReadLine();
            await _multiConn!.DisconnectAsync(id!);
        }

        static void MultiListConnections()
        {
            var conns = _multiConn!.ListConnections();
            Console.WriteLine($"\n=== Active Connections ({conns.Count}) ===");
            foreach (var kvp in conns)
            {
                Console.WriteLine($"  {kvp.Key}: {(kvp.Value.IsConnected ? "Connected" : "Disconnected")}");
            }
        }

        static async Task MultiSend()
        {
            Console.Write("Enter RTU ID: ");
            var id = Console.ReadLine();
            
            var sim = _multiConn!.GetConnection(id!);
            if (sim == null)
            {
                Console.WriteLine($"[-] RTU '{id}' not found");
                return;
            }
            
            Console.WriteLine("\n--- Command Menu ---");
            Console.WriteLine("1. Single Command (SC)");
            Console.WriteLine("2. Double Command (DC)");
            Console.WriteLine("3. Step Command (RC)");
            Console.WriteLine("4. Setpoint Normalized (SN)");
            Console.WriteLine("5. Setpoint Scaled (SB)");
            Console.WriteLine("6. Setpoint Float (SF)");
            Console.WriteLine("7. Setpoint Int32 (SI)");
            Console.WriteLine("8. General Interrogation");
            Console.WriteLine("9. Counter Interrogation");
            Console.WriteLine("10. Clock Sync");
            Console.WriteLine("11. Test Command");
            Console.WriteLine("12. Read IOA");
            Console.Write("Select command: ");
            
            var cmdChoice = ReadOption(1, 12);
            
            Console.Write("Enter IOA: ");
            if (!int.TryParse(Console.ReadLine(), out int ioa))
            {
                Console.WriteLine("[-] Invalid IOA");
                return;
            }
            
            switch (cmdChoice)
            {
                case 1:
                    Console.Write("Value (0=OFF, 1=ON): ");
                    if (int.TryParse(Console.ReadLine(), out int scVal))
                        await sim.SendSingleCommandAsync(ioa, scVal == 1);
                    break;
                case 2:
                    Console.Write("State (0-3): ");
                    if (int.TryParse(Console.ReadLine(), out int dcState))
                        await sim.SendDoubleCommandAsync(ioa, dcState);
                    break;
                case 3:
                    Console.Write("State (0-3): ");
                    if (int.TryParse(Console.ReadLine(), out int rcState))
                        await sim.SendStepCommandAsync(ioa, rcState);
                    break;
                case 4:
                    if (short.TryParse(Console.ReadLine(), out short snVal))
                        await sim.SendSetpointNormalizedAsync(ioa, snVal);
                    break;
                case 5:
                    if (short.TryParse(Console.ReadLine(), out short sbVal))
                        await sim.SendSetpointScaledAsync(ioa, sbVal);
                    break;
                case 6:
                    if (float.TryParse(Console.ReadLine(), out float sfVal))
                        await sim.SendSetpointFloatAsync(ioa, sfVal);
                    break;
                case 7:
                    if (int.TryParse(Console.ReadLine(), out int siVal))
                        await sim.SendSetpointInt32Async(ioa, siVal);
                    break;
                case 8:
                    await sim.RequestInterrogationAsync();
                    break;
                case 9:
                    await sim.RequestCounterInterrogationAsync();
                    break;
                case 10:
                    await sim.RequestClockSyncAsync();
                    break;
                case 11:
                    await sim.SendTestCommandAsync();
                    break;
                case 12:
                    await sim.SendReadCommandAsync(ioa);
                    break;
            }
        }

        static async Task RedundancyMenuAsync()
        {
            _redundancy ??= new Iec104Redundancy();
            
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("--- Redundancy Menu ---");
                Console.WriteLine("1. Connect Both (Primary + Backup)");
                Console.WriteLine("2. Status");
                Console.WriteLine("3. Switch to Backup");
                Console.WriteLine("4. Disconnect Both");
                Console.WriteLine("5. Back to Main Menu");
                Console.WriteLine();
                
                var choice = ReadOption(1, 5);
                
                switch (choice)
                {
                    case 1:
                        await ConnectBoth();
                        break;
                    case 2:
                        ShowRedundancyStatus();
                        break;
                    case 3:
                        _redundancy!.SwitchToBackup();
                        break;
                    case 4:
                        _redundancy!.DisconnectBoth();
                        _redundancy = null;
                        break;
                    case 5:
                        return;
                }
            }
        }

        static async Task ConnectBoth()
        {
            Console.Write("Enter Primary IP: ");
            var ip1 = Console.ReadLine();
            Console.Write("Enter Primary port: ");
            if (!int.TryParse(Console.ReadLine(), out int port1))
            {
                Console.WriteLine("[-] Invalid port");
                return;
            }
            Console.Write("Enter Backup IP: ");
            var ip2 = Console.ReadLine();
            Console.Write("Enter Backup port: ");
            if (!int.TryParse(Console.ReadLine(), out int port2))
            {
                Console.WriteLine("[-] Invalid port");
                return;
            }
            
            await _redundancy!.ConnectBothAsync(ip1!, port1, ip2!, port2);
        }

        static void ShowRedundancyStatus()
        {
            if (_redundancy == null)
            {
                Console.WriteLine("Redundancy not initialized");
                return;
            }
            Console.WriteLine(_redundancy.GetStatus());
        }

        static void ShowAllDataTypes()
        {
            Console.WriteLine("\n=== IEC 60870-5-104 Data Types ===");
            Console.WriteLine("\n--- Monitor (Process Information) - M_xx_xA_1 ---");
            Console.WriteLine("  M_SP_NA_1  (0x01) - Single Point without time");
            Console.WriteLine("  M_SP_TA_1  (0x02) - Single Point with time");
            Console.WriteLine("  M_DP_NA_1  (0x03) - Double Point without time");
            Console.WriteLine("  M_DP_TA_1  (0x04) - Double Point with time");
            Console.WriteLine("  M_ST_NA_1  (0x05) - Step Position without time");
            Console.WriteLine("  M_ST_TA_1  (0x06) - Step Position with time");
            Console.WriteLine("  M_BO_NA_1  (0x07) - Bit String 32 without time");
            Console.WriteLine("  M_BO_TA_1  (0x08) - Bit String 32 with time");
            Console.WriteLine("  M_ME_NA_1  (0x09) - Measured Value Normalized without time");
            Console.WriteLine("  M_ME_TA_1  (0x0A) - Measured Value Normalized with time");
            Console.WriteLine("  M_ME_NB_1  (0x0B) - Measured Value Scaled without time");
            Console.WriteLine("  M_ME_TB_1  (0x0C) - Measured Value Scaled with time");
            Console.WriteLine("  M_ME_NC_1  (0x0D) - Measured Value Float without time");
            Console.WriteLine("  M_ME_TC_1  (0x0E) - Measured Value Float with time");
            Console.WriteLine("  M_IT_NA_1  (0x0F) - Integrated Totals without time");
            Console.WriteLine("  M_IT_TA_1  (0x10) - Integrated Totals with time");
            Console.WriteLine("\n--- Commands (Control) - C_xx_xA_1 ---");
            Console.WriteLine("  C_SC_NA_1  (0x2D) - Single Command");
            Console.WriteLine("  C_DC_NA_1  (0x2E) - Double Command");
            Console.WriteLine("  C_RC_NA_1  (0x2F) - Regulating Step Command");
            Console.WriteLine("  C_SE_NA_1  (0x30) - Set Point Command Normalized");
            Console.WriteLine("  C_SE_NB_1  (0x31) - Set Point Command Scaled");
            Console.WriteLine("  C_SE_NC_1  (0x32) - Set Point Command Float");
            Console.WriteLine("  C_BC_NA_1  (0x33) - Bit String Command");
            Console.WriteLine("\n--- System Commands ---");
            Console.WriteLine("  C_IC_NA_1  (0x64) - Interrogation");
            Console.WriteLine("  C_CI_NA_1  (0x65) - Counter Interrogation");
            Console.WriteLine("  C_RD_NA_1  (0x66) - Read Command");
            Console.WriteLine("  C_CS_NA_1  (0x67) - Clock Sync");
            Console.WriteLine("  C_TS_TA_1  (0x6B) - Test Command with time");
            Console.WriteLine("  C_RP_NA_1  (0x69) - Reset Process Command");
            Console.WriteLine("\n--- Parameter Commands - P_ME_xx_1 ---");
            Console.WriteLine("  P_ME_NA_1  (0x28) - Parameter Normalized");
            Console.WriteLine("  P_ME_NB_1  (0x29) - Parameter Scaled");
            Console.WriteLine("  P_ME_NC_1  (0x2A) - Parameter Float");
        }

        static int ReadOption(int min, int max)
        {
            while (true)
            {
                Console.Write($"Enter option ({min}-{max}): ");
                var input = Console.ReadLine();
                if (int.TryParse(input, out int choice) && choice >= min && choice <= max)
                    return choice;
                Console.WriteLine($"[-] Please enter a number between {min} and {max}");
            }
        }

        static void CleanupAndExit()
        {
            _redundancy?.DisconnectBoth();
            _multiConn?.DisconnectAll();
            _simulator?.Disconnect();
        }
    }
}