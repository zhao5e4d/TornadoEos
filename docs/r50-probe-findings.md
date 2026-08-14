# EOS R50 — USB/PTP probe findings (read-only)

Captured with `tools/WpdProbe` against a real Canon EOS R50 over USB (WPD/MTP),
read-only. No data was written to the camera.

## Device

| Field | Value |
| --- | --- |
| Model | Canon EOS R50 |
| Manufacturer | Canon.Inc |
| Firmware | 3-1.5.0 (firmware 1.5.0) |
| Protocol | MTP 1.00 |
| USB IDs | VID 0x04A9 (Canon), PID 0x330D (EOS R50) |

## WPD/MTP device-property space

No language / region field is exposed as a device property. Only standard WPD
device properties plus two standard MTP device properties (0xD406
SessionInitiatorVersionInfo, 0xD407 PerceivedDeviceType). The language lock is
**not** in the property space.

## Canon vendor operations reachable over WPD (KEY FINDING)

`WPD_COMMAND_MTP_EXT_GET_SUPPORTED_VENDOR_OPCODES` returned **136 Canon vendor
operation codes** in the 0x9xxx range. This is the Canon EOS vendor PTP protocol —
the same transport used by EOS Utility, the EDSDK, and service tools. It means
Canon vendor operations *can* be issued to this camera through the standard,
safe Windows USB channel via `IPortableDevice.SendCommand`.

Confidently-identified, relevant opcodes present in the list:

| Opcode | Canon EOS operation | Access |
| --- | --- | --- |
| 0x9101 | EOS_GetStorageIDs | read |
| 0x9102 | EOS_GetStorageInfo | read |
| 0x9104 | EOS_GetObject | read |
| 0x9107 | EOS_GetPartialObject | read |
| 0x9108 | EOS_GetDeviceInfoEx (lists supported EOS props/events) | read |
| 0x9110 | EOS_SetDevicePropValueEx (writes a property) | **write** |
| 0x9113 | EOS_GetRemoteMode | read |
| 0x9114 | EOS_SetRemoteMode (enter PC-remote mode) | state |
| 0x9115 | EOS_SetEventMode | state |
| 0x9116 | EOS_GetEvent (reads property values / events) | read |
| 0x9153 | EOS_GetViewFinderData (live view) | read |
| 0x9154 | EOS_DoAf | action |

(Full 136-code list is in the probe output.)

## What this does and does NOT mean

- ✅ The transport works and the Canon vendor protocol is reachable over a safe,
  Windows-native channel. A DIY path is therefore *technically on this channel*.
- ⚠️ The normal EOS protocol (above) exposes shooting/settings properties. The
  **regional language lock is a service-mode (factory) property**, not a normal
  EOS device property. Reaching it requires entering Canon **service mode** via an
  undocumented vendor sequence and writing a model-specific firmware property
  address — none of which is published for the R50 (DIGIC X).
- ❗ `0x9110 SetDevicePropValueEx` and service-mode writes are how a real unlock
  would happen, but sending fabricated property IDs/values risks corrupting
  firmware / bricking the camera. This project will not send blind writes.

## EOS session enumeration (DONE — read-only)

We entered read-only PC-remote mode and enumerated the full EOS property set:

1. `0x9114 SetRemoteMode(1)` → MTP response 0x2001 (OK)
2. `0x9115 SetEventMode(1)` → OK
3. `0x9108 GetDeviceInfoEx` → 716 bytes → **20 events, 150 device properties**
4. `0x9116 GetEvent` → 15 KB of current property values

The values read back are real and self-consistent, confirming the whole transport
is correct, e.g.:

| Code | Meaning | Value |
| --- | --- | --- |
| 0xD109 | WhiteBalance | 23 |
| 0xD10A | ColorTemperature | 5200 K |
| 0xD1D8 | (lens name string) | "RF-S18-45mm F4.5-6.3 IS …" |
| 0xD105 | AutoExposureMode | 3 |

## Definitive conclusion

The **entire normal EOS device-property space (all 150 codes) was enumerated and
contains no menu-language or region field.** This is expected: language/region is a
factory/service-mode firmware property, not a normal EOS PTP property.

So, over the safe Windows (WPD) channel we have proven:

- ✅ The Canon EOS vendor protocol is fully reachable (read 150 live properties).
- ❌ The language lock is **not** exposed as a normal property — it lives behind
  Canon **service mode**.

Reaching the lock would require:
- an undocumented service-mode entry sequence, **and**
- the model-specific firmware property ID + value for the R50 (DIGIC X),

neither of which is public. Writing fabricated service-mode properties risks
bricking the camera, so this project stops at the safe boundary. The probe and
EOS session reader remain as a clean, read-only diagnostic foundation.
