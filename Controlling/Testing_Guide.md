# IEC 60870-5-104 Controlling (Master) Testing Guide

## Overview
The Controlling application simulates an IEC 60870-5-104 master device (client). It connects to Slave devices, requests data, and sends commands.

---

## Starting the Application

```
C:\Apps\IEC61850\104\Controlling\publish\IEC104Simulator.exe
```

---

## Main Menu

```
--- Main Menu ---
1. Single Connection
2. Multi-Connection (Multiple RTUs)
3. Redundancy (Hot Standby)
4. Show All Data Types
5. Exit

Enter option (1-5):
```

---

## Option 1: Single Connection Testing

### 1. Connect to Device

```
Enter option: 1

--- Single Connection ---
...
Enter option (1-19): 1
Enter IP address: 127.0.0.1
Enter Port: 2404
[+] Connected to 127.0.0.1:2404
```

### 2. General Interrogation (GI) - Option 11

Request all data points from slave:
```
Enter option: 11
[>] General Interrogation (GI) request sent

-- Wait for response --
[<] ASDU: M_SP_NA_1, COT=Act-OK, ASDU Addr=1
    IOA=1000: ON
    IOA=1001: OFF
[<] ASDU: M_ME_NA_1, COT=Act-OK, ASDU Addr=1
    IOA=2000: 1234
[<] ASDU: M_ME_NC_1, COT=Act-OK, ASDU Addr=1
    IOA=3000: 45.6700
```

### 3. Send Single Command (SC) - Option 3

Turn a switch ON/OFF:
```
Enter option: 3
Enter IOA: 1000
Enter Value (0=OFF, 1=ON): 1
[>] Single command: IOA=1000, Value=ON
```

### 4. Send Double Command (DC) - Option 4

Set double-point state:
```
Enter option: 4
Enter IOA: 1001
Enter State (0=inter,1=off,2=on,3=indet): 2
[>] Double command: IOA=1001, State=ON
```

### 5. Send Step Command (RC) - Option 5

Control step position:
```
Enter option: 5
Enter IOA: 1002
Enter State (0=low,1=desc,2=asc,3=high): 2
[>] Step command: IOA=1002, State=2
```

### 6. Setpoint Normalized (SN) - Option 6

Send normalized value (-32768 to 32767):
```
Enter option: 6
Enter IOA: 2000
Enter Value: 5000
[>] Setpoint normalized: IOA=2000, Value=5000
```

### 7. Setpoint Scaled (SB) - Option 7

Send scaled value:
```
Enter option: 7
Enter IOA: 2001
Enter Value: -1000
[>] Setpoint scaled: IOA=2001, Value=-1000
```

### 8. Setpoint Float (SF) - Option 8

Send float value:
```
Enter option: 8
Enter IOA: 3000
Enter Value: 99.99
[>] Setpoint float: IOA=3000, Value=99.9900
```

### 9. Setpoint Int32 (SI) - Option 9

Send 32-bit integer:
```
Enter option: 9
Enter IOA: 4000
Enter Value: 12345678
[>] Setpoint int32: IOA=4000, Value=12345678
```

### 10. Bitstring Command (BS) - Option 10

Send 32-bit bitstring:
```
Enter option: 10
Enter IOA: 5000
Enter Value (hex): FF
[>] Bitstring command: IOA=5000, Value=FF-00-00-00
```

### 11. Counter Interrogation (CI) - Option 12

Request counter values:
```
Enter option: 12
[>] Counter Interrogation request sent
```

### 12. Clock Sync (CS) - Option 13

Synchronize slave clock:
```
Enter option: 13
[>] Clock sync request sent: 2026-04-13 14:30:00
```

### 13. Test Command - Option 14

Test link integrity:
```
Enter option: 14
[>] Test command sent (C_TS_TA_1)
```

### 14. Read Specific IOA - Option 15

Read single point:
```
Enter option: 15
Enter IOA: 1000
[>] Read command sent: IOA=1000
```

### 15. Cyclic Scan - Option 16

Auto-poll every X milliseconds:
```
Enter option: 16
Enter interval (ms): 5000
[+] Cyclic scan started: every 5000ms
```

### 16. Stop Scan - Option 17

Stop cyclic polling:
```
Enter option: 17
[*] Cyclic scan stopped
```

### 17. Spontaneous On/Off - Option 18

Enable/disable event messages:
```
Enter option: 18
1. Enable Spontaneous
2. Disable Spontaneous
Enter option (1-2): 1
[+] Spontaneous messages: enabled
```

### 18. Disconnect - Option 2

```
Enter option: 2
[*] Disconnected
```

---

## Option 2: Multi-Connection Testing

Connect to multiple RTUs simultaneously:

### 1. Connect to First RTU

```
Enter option: 2

--- Multi-Connection ---
1. Connect to RTU
2. Disconnect RTU
3. List Connections
4. Send to RTU
5. Back to Main Menu

Enter option (1-5): 1
Enter RTU ID: rtu1
Enter IP address: 192.168.1.100
Enter Port: 2404
[+] Connected to rtu1 (192.168.1.100:2404)
```

### 2. Connect to Second RTU

```
Enter option: 1
Enter RTU ID: rtu2
Enter IP address: 192.168.1.101
Enter Port: 2404
[+] Connected to rtu2 (192.168.1.101:2404)
```

### 3. List All Connections

```
Enter option: 3

--- Active Connections ---
rtu1: 192.168.1.100:2404 [ACTIVE]
rtu2: 192.168.1.101:2404 [ACTIVE]
```

### 4. Send Command to Specific RTU

```
Enter option: 4
Enter RTU ID: rtu1
Enter command (sc/dc/rc/sn/sb/sf/si/bs/gi/ci/cs): sc
Enter IOA: 1000
Enter Value (0=OFF, 1=ON): 1
[>] Single command: IOA=1000, Value=ON (rtu1)
```

### 5. Disconnect Specific RTU

```
Enter option: 2
Enter RTU ID: rtu2
[*] Disconnected from rtu2
```

---

## Option 3: Redundancy Testing

Hot standby redundancy with primary and backup:

### 1. Connect Both (Primary + Backup)

```
Enter option: 3

--- Redundancy ---
1. Connect Both (Primary + Backup)
2. Status
3. Switch to Backup
4. Disconnect Both
5. Back to Main Menu

Enter option (1-5): 1
Enter Primary IP: 192.168.1.100
Enter Primary Port: 2404
Enter Backup IP: 192.168.1.101
Enter Backup Port: 2404
[+] Primary connected: 192.168.1.100:2404
[+] Backup connected: 192.168.1.101:2404
```

### 2. Check Status

```
Enter option: 2

--- Redundancy Status ---
Primary: 192.168.1.100:2404 [ACTIVE]
Backup: 192.168.1.101:2404 [STANDBY]
```

### 3. Manual Switch to Backup

```
Enter option: 3
[*] Switched to backup connection
```

### 4. Check Status After Switch

```
Enter option: 2

--- Redundancy Status ---
Primary: 192.168.1.100:2404 [DISCONNECTED]
Backup: 192.168.1.101:2404 [ACTIVE]
```

### 5. Disconnect Both

```
Enter option: 4
[*] Primary disconnected
[*] Backup disconnected
```

---

## Expected Response Format

When Slave sends data, you should see:

**Single Point:**
```
[<] ASDU: M_SP_NA_1, COT=Spontaneous, ASDU Addr=1
    IOA=1000: ON
```

**Double Point:**
```
[<] ASDU: M_DP_NA_1, COT=Spontaneous, ASDU Addr=1
    IOA=1001: ON
```

**Normalized:**
```
[<] ASDU: M_ME_NA_1, COT=Periodic, ASDU Addr=1
    IOA=2000: 1234
```

**Float:**
```
[<] ASDU: M_ME_NC_1, COT=Spontaneous, ASDU Addr=1
    IOA=3000: 45.6700
```

**With Timestamp:**
```
[<] ASDU: M_SP_TB_1, COT=Spontaneous, ASDU Addr=1
    IOA=1000: ON @ 2026-04-13 14:30:25.123
```

---

## Test Scenarios

### Scenario 1: Basic Communication
1. Start Controlled (slave)
2. Start Controlling (master)
3. Connect → Should show "[+] Connected"
4. GI → Should receive all points
5. Send SC → Should work
6. Disconnect → Should show "[*] Disconnected"

### Scenario 2: Multi-RTU
1. Connect to RTU1
2. Connect to RTU2
3. Send to RTU1 → Only RTU1 receives
4. Send to RTU2 → Only RTU2 receives

### Scenario 3: Redundancy
1. Connect both (primary + backup)
2. Send commands → Should go to primary
3. Switch to backup → Should work
4. Send commands → Should go to backup

---

## Test Checklist

- [ ] Can connect to device
- [ ] GI returns all points
- [ ] SC command works
- [ ] SF command works
- [ ] SN command works
- [ ] Cyclic scan works
- [ ] Multi-connection works
- [ ] Redundancy switch works
- [ ] Can disconnect properly

---

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Connection refused | Ensure Controlled is running |
| No data on GI | Ensure points added in Controlled |
| Commands not working | Check connection established |
| Multi-RTU issues | Check each IP is correct |