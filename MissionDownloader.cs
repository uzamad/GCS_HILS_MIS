using System;
using System.Threading;
using System.Threading.Tasks;

// ================================================================
//  MissionDownloader.cs  –  GCS_240626
//
//  Downloads a mission from the FCC over the shared serial link
//  (COM9 / COM24 virtual pair) using the binary 36-byte response
//  protocol implemented in Tx_MissProgPacket() (apiotel.c).
//
//  PRECONDITION
//  ────────────
//  The FCC must have a mission in its MP_WPData[] buffer, i.e. a
//  GCS upload (HEADER_WRITE + WP_WRITE×N + ACTIVATE_MISSION) must
//  have succeeded first.  PrepareFixedMission() in sgsim writes to
//  WPData[] — a different array — and does NOT set Header_Written,
//  so READ commands will return WP_INVALID in that case.
//
//  RESPONSE PACKET FORMAT  (36 bytes from FCC)
//  ─────────────────────────────────────────────
//  [0-3]   sync  0xAA 0x55 0x55 0xBB
//  [4]     Vehicle ID
//  [5]     Command echo  (HEADER_READ=0x52, WP_READ=0x02, ...)
//  [6-7]   WP Number (uint16 LE)
//  [8-31]  24-byte data payload (same encoding as upload)
//  [32-35] CRC-32 of bytes [4..31]
//
//  After PacketReceiver strips the 4-byte sync, WaitForAck() returns
//  32 bytes where:
//    [0]     Vehicle ID
//    [1]     Command echo
//    [2-3]   WP Number
//    [4-27]  24-byte data payload
//    [28-31] CRC-32
//
//  DOWNLOAD SEQUENCE
//  ─────────────────
//    HEADER_READ  (0x52) → response contains TotalWP at data[18-19]
//    WP_READ      (0x02) × TotalWP
//      if bit1 of data[23] (POIFlag)    → POI_READ    (0xB1)
//      if bit2 of data[23] (SearchFlag) → SEARCH_READ (0xC1)
// ================================================================

namespace GCS_240626
{
    public static class MissionDownloader
    {
        private const int ACK_TIMEOUT_MS = 2000;
        private const int MAX_RETRIES    = 3;
        private const int INTER_PKT_MS  = 20;

        // Offsets within the stripped 32-byte response (after sync removed)
        private const int OFF_CMD   = 1;
        private const int OFF_DATA  = 4;   // data payload starts here

        /// <summary>
        /// Download the mission currently stored in the FCC's MP_WPData[] buffer.
        /// Returns a populated MissionData on success, null on failure.
        /// </summary>
        public static async Task<MissionData> DownloadAsync(
            byte               vehicleId,
            PacketReceiver     receiver,
            IProgress<MissionUploadProgress> progress = null,
            CancellationToken  ct = default)
        {
            if (receiver == null) throw new ArgumentNullException(nameof(receiver));

            receiver.BeginMissionOp();
            try
            {
                // ── Step 1: Read header ───────────────────────────────────────
                Report(progress, 0, 0, "Requesting header…");

                byte[] hdrResp = await SendAndWait(
                    receiver, vehicleId, MissionPacketEncoder.CMD_HEADER_READ, 0, ct);

                if (hdrResp == null)
                { Report(progress, 0, 0, "HEADER_READ timeout", true); return null; }

                if (hdrResp[OFF_CMD] != MissionPacketEncoder.CMD_HEADER_READ)
                { Report(progress, 0, 0, $"HEADER_READ: unexpected CMD 0x{hdrResp[OFF_CMD]:X2}", true); return null; }

                // Decode header data  (data payload = hdrResp[4..27])
                var md = DecodeHeader(hdrResp, OFF_DATA);
                int totalWP = md.WayPoints.Capacity;   // set by DecodeHeader

                // Fetch actual count from the decoded header
                // (WayPoints.Capacity is set as a temp holder — read it back)
                int wpCount = totalWP;
                Report(progress, 0, wpCount, $"Header OK — {wpCount} WPs");

                await Task.Delay(INTER_PKT_MS, ct).ConfigureAwait(false);

                // ── Step 2: Read each WP ──────────────────────────────────────
                for (int i = 0; i < wpCount; i++)
                {
                    if (ct.IsCancellationRequested) return null;

                    // WP_READ
                    byte[] wpResp = await SendAndWait(
                        receiver, vehicleId, MissionPacketEncoder.CMD_WP_READ, (ushort)i, ct);

                    if (wpResp == null)
                    { Report(progress, i, wpCount, $"WP#{i} timeout", true); return null; }

                    if (wpResp[OFF_CMD] == MissionPacketEncoder.ACK_WP_INVALID ||
                        wpResp[OFF_DATA] == MissionPacketEncoder.ACK_WP_INVALID)
                    { Report(progress, i, wpCount, $"WP#{i} WP_INVALID", true); return null; }

                    MissionWP wp = DecodeWP(wpResp, OFF_DATA, i);
                    md.WayPoints.Add(wp);

                    Report(progress, i + 1, wpCount, $"WP#{i} OK");
                    await Task.Delay(INTER_PKT_MS, ct).ConfigureAwait(false);

                    // POI_READ if flagged
                    if (wp.POIFlag)
                    {
                        byte[] poiResp = await SendAndWait(
                            receiver, vehicleId, MissionPacketEncoder.CMD_POI_READ, (ushort)i, ct);

                        if (poiResp != null && poiResp[OFF_CMD] == MissionPacketEncoder.CMD_POI_READ)
                            DecodePOI(poiResp, OFF_DATA, wp);
                        else
                            Report(progress, i + 1, wpCount, $"WP#{i} POI read failed — skipped", true);

                        await Task.Delay(INTER_PKT_MS, ct).ConfigureAwait(false);
                    }

                    // SEARCH_READ if flagged
                    if (wp.SearchFlag)
                    {
                        byte[] srchResp = await SendAndWait(
                            receiver, vehicleId, MissionPacketEncoder.CMD_SEARCH_READ, (ushort)i, ct);

                        if (srchResp != null && srchResp[OFF_CMD] == MissionPacketEncoder.CMD_SEARCH_READ)
                            DecodeSearch(srchResp, OFF_DATA, wp);
                        else
                            Report(progress, i + 1, wpCount, $"WP#{i} SEARCH read failed — skipped", true);

                        await Task.Delay(INTER_PKT_MS, ct).ConfigureAwait(false);
                    }
                }

                Report(progress, wpCount, wpCount, $"Download complete — {wpCount} WPs ✓");
                return md;
            }
            finally
            {
                receiver.EndMissionOp();
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  Send a READ request and wait for the FCC response
        // ════════════════════════════════════════════════════════════════
        private static async Task<byte[]> SendAndWait(
            PacketReceiver receiver, byte vehicleId, byte cmd, ushort wpNum,
            CancellationToken ct)
        {
            // Build a 32-byte READ request (same format as WRITE)
            byte[] pkt = new byte[MissionPacketEncoder.PACKET_SIZE];
            pkt[0] = vehicleId;
            pkt[1] = cmd;
            pkt[2] = (byte)(wpNum & 0xFF);
            pkt[3] = (byte)((wpNum >> 8) & 0xFF);
            // bytes [4-27] = zeros for READ requests
            AppendCRC(pkt);

            for (int retry = 0; retry < MAX_RETRIES; retry++)
            {
                if (ct.IsCancellationRequested) return null;
                receiver.SendMissionPacket(pkt);
                byte[] resp = receiver.WaitForAck(ACK_TIMEOUT_MS);
                if (resp != null) return resp;
            }
            return null;
        }

        private static void AppendCRC(byte[] pkt)
        {
            uint crc = CalcCRC32(pkt, 0, MissionPacketEncoder.CRC_OFFSET);
            pkt[28] = (byte)(crc        & 0xFF);
            pkt[29] = (byte)((crc >> 8) & 0xFF);
            pkt[30] = (byte)((crc >>16) & 0xFF);
            pkt[31] = (byte)((crc >>24) & 0xFF);
        }

        /// <summary>
        /// Mirrors CalcCRC32() from apio.c:
        ///   seed = 0x00000000, no final XOR, reflected polynomial 0xEDB88320.
        /// NOTE: This is NOT standard ISO CRC-32 (which uses seed 0xFFFFFFFF).
        /// </summary>
        private static uint CalcCRC32(byte[] data, int start, int end)
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

        // ════════════════════════════════════════════════════════════════
        //  Decoders  (inverse of MissionPacketEncoder scale factors)
        // ════════════════════════════════════════════════════════════════

        // ── Header ────────────────────────────────────────────────────────
        // data[0-7]  = timestamp (8 bytes double)
        // data[8]    = UAV ID
        // data[9-17] = name (9 bytes ASCII)
        // data[18-19]= TotalWP (uint16 LE)
        private static MissionData DecodeHeader(byte[] pkt, int d)
        {
            var md = new MissionData();

            // UAV ID
            md.UAV_ID = pkt[d + 8];

            // Mission name (9 bytes, null-terminated)
            var chars = new System.Text.StringBuilder();
            for (int i = 0; i < 9; i++)
            {
                byte b = pkt[d + 9 + i];
                if (b == 0) break;
                chars.Append((char)b);
            }
            md.Name = chars.ToString();

            // TotalWP — stored temporarily in Capacity
            int totalWP = pkt[d + 18] | (pkt[d + 19] << 8);
            md.WayPoints.Capacity = totalWP;

            return md;
        }

        // ── WP ────────────────────────────────────────────────────────────
        // data[0-3]  Lat   uint32
        // data[4-7]  Lon   uint32
        // data[8-10] Alt   uint24
        // data[11-12] CSpeed uint16
        // data[13-14] LtrAlt uint16
        // data[15]   LtrRadius byte
        // data[16-17] LtrSpeed uint16
        // data[18]   LtrTime  byte
        // data[19-22] zeros
        // data[23]   flags  bit0=Ltr bit1=POI bit2=Search
        private static MissionWP DecodeWP(byte[] pkt, int d, int idx)
        {
            var wp = new MissionWP
            {
                DataValid = true,
                Category  = idx == 0 ? WPCategory.BStation : WPCategory.Waypoint,
                Number    = (ushort)idx,
                Lat       = DecLat(GetU32(pkt, d + 0)),
                Lon       = DecLon(GetU32(pkt, d + 4)),
                Alt       = DecAlt(GetU24(pkt, d + 8)),
                CSpeed    = DecSpeed(GetU16(pkt, d + 11)),
                LtrAlt    = DecAltSmall(GetU16(pkt, d + 13)),
                LtrRadius = DecLtrRadius(pkt[d + 15]),
                LtrSpeed  = DecSpeed(GetU16(pkt, d + 16)),
                LtrTime   = pkt[d + 18],
            };
            byte flags  = pkt[d + 23];
            wp.LtrFlag    = (flags & 0x01) != 0;
            wp.POIFlag    = (flags & 0x02) != 0;
            wp.SearchFlag = (flags & 0x04) != 0;
            return wp;
        }

        // ── POI ───────────────────────────────────────────────────────────
        // data[0-3]  POILat  uint32
        // data[4-7]  POILon  uint32
        // data[8-10] POIAlt  uint24
        private static void DecodePOI(byte[] pkt, int d, MissionWP wp)
        {
            wp.POILat = DecLat(GetU32(pkt, d + 0));
            wp.POILon = DecLon(GetU32(pkt, d + 4));
            wp.POIAlt = DecAlt(GetU24(pkt, d + 8));
        }

        // ── SEARCH ────────────────────────────────────────────────────────
        // data[0-1]  SearchBearing uint16
        // data[2-3]  SearchWidth   uint16
        // data[4-5]  SearchLength  uint16
        // data[6]    SearchTime    byte
        // data[7-8]  SearchSpeed   uint16
        // data[9-10] SearchAlt     uint16
        private static void DecodeSearch(byte[] pkt, int d, MissionWP wp)
        {
            wp.SearchBearing = DecBearing(GetU16(pkt, d + 0));
            wp.SearchWidth   = DecSearchWidth(GetU16(pkt, d + 2));
            wp.SearchLength  = DecAltSmall(GetU16(pkt, d + 4));
            wp.SearchTime    = pkt[d + 6];
            wp.SearchSpeed   = DecSpeed(GetU16(pkt, d + 7));
            wp.SearchAlt     = DecAltSmall(GetU16(pkt, d + 9));
        }

        // ════════════════════════════════════════════════════════════════
        //  Byte readers
        // ════════════════════════════════════════════════════════════════
        private static uint GetU32(byte[] b, int o)
            => (uint)(b[o] | (b[o+1]<<8) | (b[o+2]<<16) | (b[o+3]<<24));
        private static uint GetU24(byte[] b, int o)
            => (uint)(b[o] | (b[o+1]<<8) | (b[o+2]<<16));
        private static ushort GetU16(byte[] b, int o)
            => (ushort)(b[o] | (b[o+1]<<8));

        // ════════════════════════════════════════════════════════════════
        //  Inverse scale factors  (mirror of MissionPacketEncoder)
        // ════════════════════════════════════════════════════════════════
        private static double DecLat(uint v)        => (v / 23860929.4166667)  - 90.0;
        private static double DecLon(uint v)        => (v / 11930464.7083333)  - 180.0;
        private static double DecAlt(uint v)        => v / 838.860750;
        private static double DecSpeed(ushort v)    => v / 655.350;
        private static double DecAltSmall(ushort v) => v / 3.276750;
        private static double DecLtrRadius(byte v)  => v / 0.0510;
        private static double DecBearing(ushort v)  => v / 182.041667;
        private static double DecSearchWidth(ushort v) => v / 13.1070;

        // ════════════════════════════════════════════════════════════════
        //  Progress helper
        // ════════════════════════════════════════════════════════════════
        private static void Report(
            IProgress<MissionUploadProgress> p,
            int sent, int total, string status, bool isError = false)
        {
            p?.Report(new MissionUploadProgress
            {
                PacketsSent  = sent,
                PacketsTotal = total,
                Status       = status,
                IsError      = isError,
            });
        }
    }
}
