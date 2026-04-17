# IEC 60870-5-104 Simulator - Technical Documentation

## Table of Contents
1. [Overview](#overview)
2. [Architecture](#architecture)
3. [Project Structure](#project-structure)
4. [Data Flow](#data-flow)
5. [Key Components](#key-components)
6. [IEC 104 Protocol](#iec-104-protocol)
7. [Code Classes](#code-classes)
8. [Building the Project](#building-the-project)
9. [Distribution](#distribution)

---

## 1. Overview

This project implements **IEC 60870-5-104** simulator with two applications:

| Application | Role | Description |
|-------------|------|-------------|
| **Controlling** | Master/Client | Connects to devices, sends commands, requests data |
| **Controlled** | Slave/Server | Listens for connections, responds to commands, sends data |

**Technology Stack:**
- Language: C# (.NET 10)
- Framework: .NET Console Application
- Protocol: IEC 60870-5-104 (TCP/IP)
- No external DLLs required - uses only .NET libraries

---

## 2. Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                    IEC 104 Protocol                            │
│              (IEC 60870-5-104 over TCP/IP)                     │
└─────────────────────────────────────────────────────────────────┘
                              │
          ┌───────────────────┴───────────────────┐
          ↓                                         ↓
┌──────────────────────┐                 ┌──────────────────────┐
│   CONTROLLING        │                 │   CONTROLLED       │
│   (Master/Client)   │   ◄──────────► │   (Slave/Server)   │
│                    │    TCP 2404     │                   │
│ - Sends commands    │                 │ - Listens         │
│ - Requests data    │                 │ - Processes cmds   │
│ - Displays data   │                 │ - Sends data       │
└──────────────────────┘                 └──────────────────────┘

Multiple Modes:
- Single Connection   (1 RTU)
- Multi-Connection   (Multiple RTUs)
- Redundancy        (Hot Standby)
```

---

## 3. Project Structure

### Controlling (Master)
```
C:\Apps\IEC61850\104\Controlling\
├── IEC104Simulator.sln          # Solution file
├── IEC104Simulator.csproj     # Project file
├── Program.cs               # All code (single file)
├── Testing_Guide.md          # User testing guide
└── publish\
    └── IEC104Simulator.exe   # Self-contained executable
```

### Controlled (Slave)
```
C:\Apps\IEC61850\104\Controlled\
├── IEC104Controlled.sln
├── IEC104Controlled.csproj
├── Program.cs              # All code
├── Testing_Guide.md
└── publish\
    └── IEC104Controlled.exe
```

---

## 4. Data Flow

### Controlling (Master) Data Flow
```
User Input (Menu)
       ↓
Parse Command
       ↓
Create I-Frame (APCI + ASDU)
       ↓
TCP Send → Network
       ↓
Receive Response (TCP)
       ↓
Parse APCI + ASDU
       ↓
Decode Values
       ↓
Display to Console
```

### Controlled (Slave) Data Flow
```
TCP Listen
       ↓
Accept Connection
       ↓
Receive I-Frame
       ↓
Parse APCI + ASDU
       ↓
Process Command
       ↓
Create Response (ASDU)
       ↓
TCP Send → Network
       ↓
Display to Console
```

---

## 5. Key Components

### A. Iec104Simulator (Controlling)

| Class | Responsibility |
|-------|----------------|
| `Iec104Simulator` | Core IEC 104 client, TCP connection, send/receive |
| `Iec104Redundancy` | Hot standby redundancy (primary + backup) |
| `Iec104MultiConnection` | Multiple simultaneous RTUs |
| `Program` | Menu system, user interaction |

**Key Methods:**
```csharp
ConnectAsync(ip, port)        # Connect to device
SendSingleCommandAsync()      # Send SC command
SendDoubleCommandAsync()     # Send DC command
SendSetpointFloatAsync()    # Send SF command
RequestInterrogationAsync() # GI request
ParseAsdu()                # Decode received data
```

### B. Iec104Controlled (Slave)

| Class | Responsibility |
|-------|----------------|
| `DataPoint` | Individual data point (IOA, type, value) |
| `DataStore` | Collection of data points |
| `Iec104Slave` | TCP server, handle connections |
| `Program` | Menu system, user interaction |

**Key Methods:**
```csharp
StartServer()              # Start TCP listener
StopServer()              # Stop listener
AddPoint()               # Add data point
SendSpontaneous()         # Send to master
ProcessCommand()         # Handle master commands
```

---

## 6. IEC 104 Protocol

### A. Frame Types

| Type | Hex | Description |
|------|-----|-------------|
| **I-Frame** | - | Information transfer (has sequence numbers) |
| **S-Frame** | 0x01 | Supervisory (acknowledge only) |
| **U-Frame** | 0x03/0x07/0x0B | Unnumbered control |

### B. ASDU Types (Process Information)

| Type ID | Name | Description |
|---------|------|-------------|
| 0x01 | M_SP_NA_1 | Single (Boolean) |
| 0x03 | M_DP_NA_1 | Double (0-3) |
| 0x09 | M_ME_NA_1 | Normalized (Int16) |
| 0x0D | M_ME_NC_1 | Float |
| 0x0F | M_IT_NA_1 | Counter (UInt32) |

### C. ASDU Types (Commands)

| Type ID | Name | Description |
|--------|------|-------------|
| 0x2D | C_SC_NA_1 | Single Command |
| 0x2E | C_DC_NA_1 | Double Command |
| 0x30 | C_SE_NA_1 | Setpoint Normalized |
| 0x32 | C_SE_NC_1 | Setpoint Float |
| 0x64 | C_IC_NA_1 | Interrogation |
| 0x67 | C_CS_NA_1 | Clock Sync |

### D. Cause of Transmission (COT)

| Value | Name | Description |
|-------|------|-------------|
| 0x01 | Periodic | Cyclic data |
| 0x02 | Background | Background scan |
| 0x03 | Spontaneous | Event/alarm |
| 0x05 | Request | General request |
| 0x06 | Activation | CommandExecution |
| 0x07 | Act-OK | Command successful |
| 0x0D | Return | Information response |

### E. APDU Structure

```
I-Frame:
┌────┬────┬────┬────┬────┬────┬────┬────┬────┬────┐
│ 68 │ LL │ A1 │ A2 │ A3 │ A4 │ T  │..data..│
└────┴────┴────┴────┴────┴────┴────┴────┴────┴────┘
 68  = Start byte
  LL = Length (bytes 2-6)
  A1-A4 = APCI (send/receive sequence)
  T    = Type identification
  ..   = ASDU (10+ bytes)
```

---

## 7. Code Classes

### A. Controlling - Iec104Simulator Class

```csharp
// Location: Program.cs (lines 10-650)

public class Iec104Simulator
{
    private TcpClient _client;        // TCP connection
    private NetworkStream _stream;   // Data stream
    private byte _sendSeq = 0;       // Send sequence
    private byte _recvSeq = 0;       // Receive sequence
    
    // Connection methods
    public async Task<bool> ConnectAsync(string ip, int port)
    public void Disconnect()
    
    // Command methods
    public async Task SendSingleCommandAsync(int ioa, bool value)
    public async Task SendDoubleCommandAsync(int ioa, int state)
    public async Task SendSetpointNormalizedAsync(int ioa, short value)
    public async Task SendSetpointFloatAsync(int ioa, float value)
    public async Task RequestInterrogationAsync(byte group = 0)
    public async Task RequestClockSyncAsync()
    
    // Internal methods
    private byte[] CreateIFrame(byte typeId, byte cot, byte[] data)
    private byte[] CreateUFrame(byte controlField)
    private void ParseAsdu(byte[] asdu)
    private string DecodeValue(byte[] asdu, ...)
}
```

### B. Controlled - Iec104Slave Class

```csharp
// Location: Program.cs (lines 100-250)

public class Iec104Slave
{
    private TcpListener _listener;     // TCP server
    private NetworkStream _stream;     // Data stream
    
    // Server methods
    public async Task StartAsync(int port)
    public void Stop()
    
    // Processing
    public async Task ProcessReceivedFrame(byte[] frame)
    public async Task SendAsdu(byte[] asdu)
    public void HandleCommand(byte typeId, byte[] data)
}
```

### C. Controlled - DataStore Class

```csharp
// Location: Program.cs (lines 49-90)

public class DataStore
{
    private Dictionary<int, DataPoint> _points;
    
    public void AddPoint(int ioa, IecValueType type, object value)
    public DataPoint? GetPoint(int ioa)
    public void UpdatePoint(int ioa, object value)
    public List<DataPoint> GetAllPoints()
    public byte[] CreateGIResponse()
}
```

---

## 8. Building the Project

### Requirements
- .NET 10 SDK (for building)
- No external packages needed

### Build Commands

**Development Build:**
```bash
cd C:\Apps\IEC61850\104\Controlling
dotnet build

cd C:\Apps\IEC61850\104\Controlled
dotnet build
```

**Release Build (Debug]:**
```bash
dotnet build -c Release
```

**Create Solution:**
```bash
cd C:\Apps\IEC61850\104\Controlling
dotnet new sln -n IEC104Simulator
dotnet sln add IEC104Simulator.csproj
```

---

## 9. Distribution

### Self-Contained Executable

Create single-file executable (no .NET needed on target):

```bash
# Windows x64
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish

# Linux x64
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
```

### Published Output
```
C:\Apps\IEC61850\104\Controlling\publish\
└── IEC104Simulator.exe    (~70MB, self-contained)

C:\Apps\IEC61850\104\Controlled\publish\
└── IEC104Controlled.exe  (~70MB, self-contained)
```

### What the .exe Contains
- .NET runtime
- Application code
- All dependencies
- No separate installation needed

---

## 10. Feature Summary

### Controlling Features
- ✓ Single connection to RTU
- ✓ Multiple simultaneous connections
- ✓ Hot standby redundancy
- ✓ All standard ASDU types
- ✓ With/without timestamps
- ✓ Menu-driven interface
- ✓ Command history display

### Controlled Features
- ✓ TCP server on configurable port
- ✓ Multiple data point types
- ✓ General Interrogation response
- ✓ Command processing
- ✓ Spontaneous data sending
- ✓ Menu-driven interface
- ✓ Connection status

---

## 11. Testing Checklist

- [ ] Controlled starts and listens on port 2404
- [ ] Controlling connects to Controlled
- [ ] GI returns all data points
- [ ] SC command sends and acknowledges
- [ ] SF (float) command works
- [ ] SN (normalized) command works
- [ ] Spontaneous data received
- [ ] Multi-connection works
- [ ] Redundancy failover works
- [ ] Disconnect/connect works properly

---

## 12. Demo Script

### Part 1: Show Structure
1. Show project files in Explorer
2. Open solution in IDE
3. Point out Program.cs (single file architecture)

### Part 2: Build
1. Open terminal
2. Run `dotnet build`
3. Show success

### Part 3: Run Controlled
1. Run IEC104Controlled.exe
2. Show menu
3. Start server
4. Add data points

### Part 4: Run Controlling
1. Run IEC104Simulator.exe
2. Connect to 127.0.0.1:2404
3. Send GI - show data
4. Send command - show response

### Part 5: Advanced
1. Show multi-connection
2. Show redundancy

---

## Contact & Support

For issues or questions, refer to:
- Testing_Guide.md (user guide)
- Program.cs (source code comments)