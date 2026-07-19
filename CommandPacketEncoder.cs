// ================================================================
//  CommandPacketEncoder.cs  –  GCS_240626
//
//  Encodes a CommandState into the 36-byte wire packet expected by
//  sgsim's ReceiveCMDPackets() / apio_decode.c.
//
//  WIRE FORMAT  (36 bytes)
//  ───────────────────────
//  [0-3]    sync  0xAA 0x55 0x55 0xAA
//  [4-31]   28-byte payload (= TempBuff[0..27] after sync stripped)
//  [32-35]  CRC-32 of bytes [4..31]   seed=0, poly=0xEDB88320
//
//  PAYLOAD BYTE MAP  (pkt offset = TempBuff index + 4)
//  ─────────────────────────────────────────────────────
//  pkt[4]  / TempBuff[0]  PacketNumber (increments each TX)
//  pkt[5-8]/ TempBuff[1-4] Timestamp (unused by sgsim in HILS)
//  pkt[9]  / TempBuff[5]  AP Byte 1
//  pkt[10] / TempBuff[6]  AP Byte 2
//  pkt[11] / TempBuff[7]  Landing Byte
//  pkt[12] / TempBuff[8]  Navigation Byte
//  pkt[13] / TempBuff[9]  Vehicle Control Byte
//  pkt[14] / TempBuff[10] Mixed Data Byte
//  pkt[15] / TempBuff[11] Auxiliary Byte
//  pkt[16] / TempBuff[12] TaxiControlCMD
//  pkt[17] / TempBuff[13] selectIncDecParam
//  pkt[18] / TempBuff[14] VehicleMode
//  pkt[19] / TempBuff[15] AP Byte 3
//  pkt[20-22]/TempBuff[16-18] SLT/SysID (NYI = 0)
//  pkt[23] / TempBuff[19] Mixed Byte 2
//  pkt[24] / TempBuff[20] Telemetry_MPC
//  pkt[25] / TempBuff[21] vehicle_id  (must = VEHICLE_ID = 2)
//  pkt[26] / TempBuff[22] RelayControlByte1
//  pkt[27] / TempBuff[23] RelayControlByte2
//  pkt[28] / TempBuff[24] RelayControlByte3
//  pkt[29] / TempBuff[25] Brake_Cmd
//  pkt[30-31]/TempBuff[26-27] Spare
//  pkt[32-35]             CRC-32
// ================================================================

namespace GCS_240626
{
    public static class CommandPacketEncoder
    {
        private static byte _pktNum;

        /// <summary>
        /// Encodes <paramref name="s"/> into a 36-byte command packet
        /// ready to be passed to <see cref="PacketReceiver.SendRaw"/>.
        /// </summary>
        public static byte[] Encode(CommandState s)
        {
            var pkt = new byte[36];

            // ── Sync ─────────────────────────────────────────────────
            pkt[0] = 0xAA; pkt[1] = 0x55; pkt[2] = 0x55; pkt[3] = 0xAA;

            // ── pkt[4] / TempBuff[0]  Packet number ─────────────────
            pkt[4] = _pktNum++;

            // pkt[5-8] / TempBuff[1-4]  Timestamp — zeros (unused)

            // ── pkt[9] / TempBuff[5]  AP Byte 1 ─────────────────────
            pkt[9] = (byte)(
                (s.IncParam             ? 0x01 : 0) |
                (s.DecParam             ? 0x02 : 0) |
                (s.SetDefaultLonGains   ? 0x04 : 0) |
                (s.SetDefaultLatGains   ? 0x08 : 0) |
                (s.HeightControlScheme  ? 0x10 : 0));
                // bit 5 (0x20) reserved for UseHeightErrLeadFilter — not yet wired

            // ── pkt[10] / TempBuff[6]  AP Byte 2 ────────────────────
            pkt[10] = (byte)(
                (s.EnableGainTuning              ? 0x10 : 0) |
                (s.EnableStabilityAugmentation   ? 0x20 : 0) |
                (s.UseNonLinearRollControl       ? 0x40 : 0) |
                (s.UseFlapsAsAilerons            ? 0x80 : 0));

            // ── pkt[11] / TempBuff[7]  Landing Byte ─────────────────
            pkt[11] = (byte)(
                (s.TODirection      ? 0x01 : 0) |
                (s.Landing          ? 0x02 : 0) |
                (s.LandingDirection ? 0x04 : 0) |
                (s.DummyLanding     ? 0x08 : 0) |
                (s.AbortLanding     ? 0x10 : 0) |
                (s.DenyHitCriteria  ? 0x20 : 0) |
                (s.EmergencyLanding ? 0x40 : 0) |
                (s.NLRetractionCmd  ? 0x80 : 0));

            // ── pkt[12] / TempBuff[8]  Navigation Byte ──────────────
            pkt[12] = (byte)(
                (s.GoToNextWP               ? 0x01 : 0) |
                (s.GoToPrevWP               ? 0x02 : 0) |
                (s.LeftXTrackCorrection     ? 0x04 : 0) |
                (s.RightXTrackCorrection    ? 0x08 : 0) |
                (s.UseCurveGuidance         ? 0x10 : 0) |
                ((s.LateralGuidanceScheme & 0x03) << 5) |
                (s.Loiter                   ? 0x80 : 0));

            // ── pkt[13] / TempBuff[9]  Vehicle Control Byte ─────────
            pkt[13] = (byte)(
                (s.UseGpsSpeed          ? 0x01 : 0) |
                (s.GcsAscend            ? 0x02 : 0) |
                (s.GcsDescend           ? 0x04 : 0) |
                (s.GcsIncrementSpeed    ? 0x08 : 0) |
                (s.GcsDecrementSpeed    ? 0x10 : 0) |
                (s.UseGpsAlt            ? 0x20 : 0) |
                (s.UsePresAlt           ? 0x40 : 0) |
                (s.UseRadarAlt          ? 0x80 : 0));

            // ── pkt[14] / TempBuff[10]  Mixed Data Byte ─────────────
            pkt[14] = (byte)(
                (s.DashR2Base           ? 0x01 : 0) |
                (s.GcsR2Base            ? 0x02 : 0) |
                (s.Search               ? 0x04 : 0) |
                (s.FullThrottle         ? 0x08 : 0) |
                (s.PilotAugmentationOff ? 0x10 : 0) |
                (s.Eco                  ? 0x40 : 0) |
                (s.EngineKill           ? 0x80 : 0));

            // ── pkt[15] / TempBuff[11]  Auxiliary Byte ──────────────
            pkt[15] = (byte)(
                (s.Switch2BupLink           ? 0x01 : 0) |
                (s.RetractNoseLandingGear   ? 0x02 : 0) |
                (s.GcsRunupBypass           ? 0x04 : 0) |
                (s.EnableFccOverride        ? 0x08 : 0) |
                (s.UseFcc                   ? 0x40 : 0) |
                (s.ElevFilterEnable         ? 0x80 : 0));

            pkt[16] = s.TaxiControlCmd;
            pkt[17] = s.SelectIncDecParam;
            pkt[18] = s.VehicleMode;

            // ── pkt[19] / TempBuff[15]  AP Byte 3 ───────────────────
            pkt[19] = (byte)(
                (s.FlapsDown            ? 0x01 : 0) |
                (s.AltitudeHold         ? 0x04 : 0) |
                (s.SpeedHold            ? 0x08 : 0) |
                (s.EnableLogging        ? 0x10 : 0) |
                (s.AirModesEnabledSwt   ? 0x20 : 0) |
                (s.GndCrewClearanceSwt  ? 0x40 : 0));

            // pkt[20-22] / TempBuff[16-18]  SLT / SysID — NYI = 0

            // ── pkt[23] / TempBuff[19]  Mixed Byte 2 ────────────────
            pkt[23] = (byte)(
                (s.DenyAbortTaxi            ? 0x01 : 0) |
                (s.DgpsCorrectionsEnabled   ? 0x02 : 0) |
                (s.EnableTurnCompensation   ? 0x04 : 0) |
                (s.EnableTelemetryLink      ? 0x08 : 0) |
                (s.EnablePfEstimation       ? 0x10 : 0) |
                (s.SetDefaultGndGains       ? 0x20 : 0) |
                (s.EnableFixedMission       ? 0x40 : 0) |
                (s.ReleaseBrakes            ? 0x80 : 0));

            pkt[24] = s.TelemetryMpc;
            pkt[25] = s.VehicleId;
            pkt[26] = s.RelayControlByte1;
            pkt[27] = s.RelayControlByte2;
            pkt[28] = s.RelayControlByte3;
            pkt[29] = s.BrakeCmd;
            // pkt[30-31] spare = 0

            // ── CRC-32 over pkt[4..31] → stored in pkt[32..35] ──────
            uint crc = CalcCrc32(pkt, 4, 32);
            pkt[32] = (byte)( crc        & 0xFF);
            pkt[33] = (byte)((crc >>  8) & 0xFF);
            pkt[34] = (byte)((crc >> 16) & 0xFF);
            pkt[35] = (byte)((crc >> 24) & 0xFF);

            return pkt;
        }

        /// <summary>
        /// Same CRC-32 as CalcCRC32() in apio.c:
        /// seed = 0x00000000, reflected polynomial 0xEDB88320, no final XOR.
        /// </summary>
        private static uint CalcCrc32(byte[] data, int start, int end)
        {
            uint crc = 0u;
            for (int i = start; i < end; i++)
            {
                crc ^= data[i];
                for (int k = 0; k < 8; k++)
                    crc = (crc & 1u) != 0u ? (0xEDB88320u ^ (crc >> 1)) : (crc >> 1);
            }
            return crc;
        }
    }
}
