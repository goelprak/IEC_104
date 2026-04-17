# IEC 60870-5-104 Controlled (Slave) Testing Guide

## Overview
The Controlled application simulates an IEC 60870-5-104 slave device (RTU). It listens for connections from Master devices and responds to commands.

---

## Starting the Application

```
C:\Apps\IEC61850\104\Controlled\publish\IEC104Controlled.exe
```

---

## Menu Options

```
--- Main Menu ---
1. Start Server
2. Stop Server
3. Add Data Points
4. Modify Data Point
5. List Data Points
6. Send Spontaneous Data
7. Configure ASDU Address
8. Configure Port
9. Connection Status
10. Show All Data Types
11. Exit
```

---

## Step-by-Step Testing

### 1. Configure Port (Option 8)
Default port is 2404. Change if needed:
```
Enter option: 8
Enter Port (default 2404): 2404
Port set to 2404
```

### 2. Configure ASDU Address (Option 7)
Default is 1:
```
Enter option: 7
Enter ASDU Address (default 1): 1
ASDU Address set to 1
```

### 3. Start Server (Option 1)
```
Enter option: 1
Server started on port 2404
Waiting for master connection...
```

### 4. Add Data Points (Option 3)
Create simulated data points that the Master can read:

**Add Single (Boolean):**
```
Enter option: 3
Enter option (1-9): 1
Enter IOA: 1000
Value (0=OFF, 1=ON): 1
Point added: IOA=1000, Type=SingleBit, Value=True
```

**Add Double:**
```
Enter option: 3
Enter option (1-9): 2
Enter IOA: 1001
Value (0-3): 2
Point added: IOA=1001, Type=DoubleBit, Value=2
```

**Add Normalized (Int16):**
```
Enter option: 3
Enter option (1-9): 5
Enter IOA: 2000
Value (-32768 to 32767): 1234
Point added: IOA=2000, Type=Normalized, Value=1234
```

**Add Float:**
```
Enter option: 3
Enter option (1-9): 8
Enter IOA: 3000
Value: 45.67
Point added: IOA=3000, Type=Float, Value=45.67
```

**Add Counter (UInt32):**
```
Enter option: 3
Enter option (1-9): 7
Enter IOA: 4000
Value: 12345678
Point added: IOA=4000, Type=Counter, Value=12345678
```

**Add Bitstring32:**
```
Enter option: 3
Enter option (1-9): 4
Enter IOA: 5000
Value (hex): FFFF
Point added: IOA=5000, Type=BitString32, Value=65535
```

### 5. List Data Points (Option 5)
View all configured points:
```
Enter option: 5

--- Data Points ---
IOA=1000 Type=SingleBit Value=True
IOA=1001 Type=DoubleBit Value=2
IOA=2000 Type=Normalized Value=1234
IOA=3000 Type=Float Value=45.67
IOA=4000 Type=Counter Value=12345678
IOA=5000 Type=BitString32 Value=65535
```

### 6. Modify Data Point (Option 4)
Change an existing point value:
```
Enter option: 4
Enter IOA to modify: 1000
Current Value: True
Enter new value (0=OFF, 1=ON): 0
Point modified: IOA=1000, New Value=False
```

### 7. Check Connection Status (Option 9)
When Master connects:
```
Enter option: 9

--- Connection Status ---
Server Status: Running
Port: 2404
ASDU Address: 1
Connected Masters: 1
  - 127.0.0.1:54321 (Active)
```

### 8. Send Spontaneous Data (Option 6)
Send data to Master without being polled:
```
Enter option: 6
Enter IOA to send: 1000
Data sent to master
```

### 9. Stop Server (Option 2)
```
Enter option: 2
Server stopped
```

---

## What Happens When Master Connects

When a Master connects, it will typically:
1. Send STARTDT_ACT (activate data transfer)
2. Send General Interrogation (GI) → You respond with all points
3. Send commands → You process and respond
4. Send Clock Sync → You update your time

**Watch for these messages:**
```
[*] Master connected from 127.0.0.1:54321
[<] Received: C_IC_NA_1 (GI request) - Sending all points
[<] Received: C_SC_NA_1 (Single Command) - IOA=1000, Value=ON
[<] Received: C_SE_NC_1 (Setpoint Float) - IOA=3000, Value=99.99
```

---

## Data Types Available

| Option | Type | Range | Description |
|--------|------|-------|-------------|
| 1 | Single | 0/1 | Boolean on/off |
| 2 | Double | 0-3 | Intermediate/Off/On/Indeterminate |
| 3 | Step | 0-3 | Lower/Descend/Ascend/Higher |
| 4 | Bitstring32 | 0-4294967295 | 32-bit value |
| 5 | Normalized | -32768 to 32767 | Scaled -1 to 1 |
| 6 | Scaled | -32768 to 32767 | Direct integer |
| 7 | Counter | 0-4294967295 | Unsigned 32-bit |
| 8 | Float | ±3.4e38 | Single precision float |

---

## Test Checklist

- [ ] Server starts on port 2404
- [ ] Can add multiple data points
- [ ] Can list all points
- [ ] Can modify point values
- [ ] Can send spontaneous data
- [ ] Master can connect
- [ ] Master can send GI and receive all points
- [ ] Master can send commands and get responses
- [ ] Connection status shows master connected

---

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Port already in use | Change port in Option 8 |
| No points showing | Add points in Option 3 |
| Master can't connect | Ensure server started (Option 1) |
| Connection drops | Check network/firewall |