using System;
using System.Threading;
using System.Threading.Tasks;

// ================================================================
//  MissionUploader.cs  –  GCS_240626
//
//  Sends a MissionData object to the FCC over the shared serial link
//  (COM9 / COM24 virtual pair) using the binary 32-byte packet protocol
//  reverse-engineered from MissionProgramming.c.
//
//  Flow
//  ────
//    BeginMissionUpload() ─ puts PacketReceiver into ACK-routing mode
//    For every packet in the encoded stream:
//        SendRaw(packet)
//        WaitForAck(timeout)  ─ blocks until 32-byte reply or timeout
//        Inspect ACK code     ─ retry up to MAX_RETRIES on NACK/timeout
//    EndMissionUpload()   ─ restores normal telemetry-only mode
//
//  Progress
//  ────────
//    IProgress<MissionUploadProgress> reports after every packet.
//    Bind to a ToolStripProgressBar / Label in Form1.
//
//  ACK codes (from MissionProgramming.c)
//    0x01  OK
//    0x02  HEADER_INVALID       header rejected by FCC
//    0x03  WP_INVALID           WP data out of range
//    0x04  WP_LIM_EXCEEDED      too many WPs
//    0x05  ALL_WP_NOT_RECEIVED  ACTIVATE sent before all WPs arrived
//    0x06  FILE_NOT_EXIST
//    0x07  HEADER_NOT_RECEIVED  WP sent before header
//    0x10  MISSION_CRC_FAILED   FCC CRC check failed on ACTIVATE
//    0x11  MISSION_PROG_DENIED  FCC busy / not in prog mode
// ================================================================

namespace GCS_240626
{
    public struct MissionUploadProgress
    {
        public int    PacketsSent;
        public int    PacketsTotal;
        public string Status;       // short one-line description
        public bool   IsError;
    }

    public static class MissionUploader
    {
        private const int MAX_RETRIES   = 3;
        private const int ACK_TIMEOUT_MS = 2000;   // ms to wait for each ACK
        private const int INTER_PKT_MS  =   20;    // gap between packets (ms)

        /// <summary>
        /// Encode and upload a full mission to the FCC asynchronously.
        /// Must be called from a non-UI thread (or awaited from UI via Task.Run).
        /// Returns true if all packets ACK'd and ACTIVATE succeeded.
        /// </summary>
        public static async Task<bool> UploadAsync(
            MissionData        md,
            byte               vehicleId,
            PacketReceiver     receiver,
            IProgress<MissionUploadProgress> progress = null,
            CancellationToken  ct = default)
        {
            if (md == null)
                throw new ArgumentNullException(nameof(md));
            if (receiver == null)
                throw new ArgumentNullException(nameof(receiver));

            // Build the full binary stream
            byte[] stream = MissionPacketEncoder.Encode(md, vehicleId);
            int total = stream.Length / MissionPacketEncoder.PACKET_SIZE;

            receiver.BeginMissionUpload();
            try
            {
                for (int i = 0; i < total; i++)
                {
                    if (ct.IsCancellationRequested)
                    {
                        Report(progress, i, total, "Upload cancelled.", true);
                        return false;
                    }

                    // Extract this 32-byte packet from the stream
                    byte[] pkt = new byte[MissionPacketEncoder.PACKET_SIZE];
                    Array.Copy(stream, i * MissionPacketEncoder.PACKET_SIZE,
                               pkt, 0, MissionPacketEncoder.PACKET_SIZE);

                    byte cmd   = pkt[1];
                    ushort wpN = (ushort)(pkt[2] | (pkt[3] << 8));

                    bool packetOk = false;

                    for (int retry = 0; retry < MAX_RETRIES; retry++)
                    {
                        if (ct.IsCancellationRequested) return false;

                        try { receiver.SendMissionPacket(pkt); }
                        catch (Exception ex)
                        {
                            Report(progress, i, total,
                                   $"TX error: {ex.Message}", true);
                            return false;
                        }

                        // Hex dump of actual wire frame (sync header + payload)
                        byte[] wire = new byte[4 + pkt.Length];
                        wire[0] = 0xAA; wire[1] = 0x55; wire[2] = 0x55; wire[3] = 0xBB;
                        Array.Copy(pkt, 0, wire, 4, pkt.Length);
                        Report(progress, i, total,
                               $"  TX[{wire.Length}]: {HexDump(wire)}");

                        string cmdName = CmdName(cmd);
                        Report(progress, i, total,
                               $"Sent {cmdName} WP#{wpN}" +
                               (retry > 0 ? $" (retry {retry})" : ""));

                        byte[] ack = receiver.WaitForAck(ACK_TIMEOUT_MS);

                        if (ack == null)
                        {
                            Report(progress, i, total,
                                   $"{cmdName} WP#{wpN}: timeout (no ACK)", true);
                            continue;   // retry
                        }

                        // Hex dump of received ACK
                        Report(progress, i, total,
                               $"  RX[{ack.Length}]: {HexDump(ack)}");

                        byte ackCode = ack[4];

                        if (ackCode == MissionPacketEncoder.ACK_OK)
                        {
                            Report(progress, i + 1, total,
                                   $"{cmdName} WP#{wpN}: OK");
                            packetOk = true;
                            break;
                        }

                        // Known fatal codes — no point retrying
                        if (ackCode == MissionPacketEncoder.ACK_WP_LIM_EXCEEDED ||
                            ackCode == MissionPacketEncoder.ACK_MISSION_PROG_DENIED)
                        {
                            Report(progress, i, total,
                                   $"{cmdName} WP#{wpN}: NACK 0x{ackCode:X2} ({AckName(ackCode)}) — aborted",
                                   true);
                            return false;
                        }

                        // Retryable NACK
                        Report(progress, i, total,
                               $"{cmdName} WP#{wpN}: NACK 0x{ackCode:X2} ({AckName(ackCode)})", true);
                    }

                    if (!packetOk)
                    {
                        Report(progress, i, total,
                               $"Packet {i} failed after {MAX_RETRIES} retries — aborted", true);
                        return false;
                    }

                    // Small inter-packet gap — gives FCC time to process
                    if (i < total - 1)
                        await Task.Delay(INTER_PKT_MS, ct).ConfigureAwait(false);
                }

                Report(progress, total, total, "Upload complete ✓");
                return true;
            }
            finally
            {
                receiver.EndMissionUpload();
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────
        private static void Report(
            IProgress<MissionUploadProgress> p,
            int sent, int total, string status, bool isError = false)
        {
            p?.Report(new MissionUploadProgress
            {
                PacketsSent  = sent,
                PacketsTotal = total,
                Status       = status,
                IsError      = isError
            });
        }

        private static string CmdName(byte cmd)
        {
            switch (cmd)
            {
                case MissionPacketEncoder.CMD_HEADER_WRITE:     return "HEADER";
                case MissionPacketEncoder.CMD_WP_WRITE:         return "WP";
                case MissionPacketEncoder.CMD_POI_WRITE:        return "POI";
                case MissionPacketEncoder.CMD_SEARCH_WRITE:     return "SRCH";
                case MissionPacketEncoder.CMD_ACTIVATE_MISSION: return "ACTIVATE";
                default:                                        return "CMD_0x" + cmd.ToString("X2");
            }
        }

        private static string HexDump(byte[] b)
        {
            if (b == null || b.Length == 0) return "(empty)";
            var sb = new System.Text.StringBuilder(b.Length * 3);
            for (int k = 0; k < b.Length; k++)
            {
                if (k > 0 && k % 16 == 0)
                    sb.Append("\r\n         ");   // 9-space indent on continuation lines
                sb.AppendFormat("{0:X2} ", b[k]);
            }
            return sb.ToString().TrimEnd();
        }

        private static string AckName(byte code)
        {
            switch (code)
            {
                case 0x01: return "OK";
                case 0x02: return "HEADER_INVALID";
                case 0x03: return "WP_INVALID";
                case 0x04: return "WP_LIM_EXCEEDED";
                case 0x05: return "ALL_WP_NOT_RECEIVED";
                case 0x06: return "FILE_NOT_EXIST";
                case 0x07: return "HEADER_NOT_RECEIVED";
                case 0x10: return "MISSION_CRC_FAILED";
                case 0x11: return "MISSION_PROG_DENIED";
                default:   return "UNKNOWN";
            }
        }
    }
}
