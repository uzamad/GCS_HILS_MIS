using System;
using System.Collections.Generic;
using System.IO;

// ================================================================
//  MissionPacketEncoder.cs  –  GCS_240626
//
//  Encodes a MissionData object into the binary 32-byte packet
//  stream used by the FCC mission-programming protocol.
//
//  Packet layout (always 32 bytes, little-endian):
//    [0]     Vehicle ID
//    [1]     Command byte
//    [2-3]   WP Number (uint16 LE)
//    [4-27]  Data payload (24 bytes)
//    [28-31] CRC-32 of bytes 0-27
//
//  Upload sequence per WP:
//    HEADER_WRITE  (0x51) — once, before any WPs
//    WP_WRITE      (0x01) — once per WP
//    POI_WRITE     (0xB0) — once per WP if POIFlag = true
//    SEARCH_WRITE  (0xC0) — once per WP if SearchFlag = true
//    ACTIVATE_MISSION (0xA1) — once at the end
//
//  FCC ACK response (32 bytes):
//    [0]  Vehicle ID
//    [1]  Echoed command
//    [2-3] WP Number
//    [4]  ACK code  (0x01=OK, 0x02=HEADER_INVALID, 0x03=WP_INVALID,
//                    0x05=ALL_WP_NOT_RECEIVED, 0x10=MISSION_CRC_FAILED)
//
//  Encoding scale factors (from MissionProgramming.c):
//    Lat:          (lat  + 90.0)  × 23 860 929.416 667  → uint32
//    Lon:          (lon  + 180.0) × 11 930 464.708 333  → uint32
//    Alt:          alt            × 838.860 750          → uint24 (3 bytes)
//    Speed/CSpeed: spd            × 655.350              → uint16 (knots)
//    LtrAlt/SrchAlt: alt          × 3.276 750            → uint16 (m)
//    LtrRadius:    radius         × 0.051 0              → byte   (m)
//    LtrTime:      minutes                               → byte
//    SearchBearing:deg            × 182.041 667          → uint16 (deg)
//    SearchWidth:  m              × 13.107 0             → uint16 (m)
//    SearchLength: m              × 3.276 750            → uint16 (m)
//    SearchTime:   minutes                               → byte
// ================================================================

namespace GCS_240626
{
    public static class MissionPacketEncoder
    {
        // ── Command codes ─────────────────────────────────────────────────
        public const byte CMD_HEADER_WRITE     = 0x51;
        public const byte CMD_HEADER_READ      = 0x52;
        public const byte CMD_WP_WRITE         = 0x01;
        public const byte CMD_WP_READ          = 0x02;
        public const byte CMD_POI_WRITE        = 0xB0;
        public const byte CMD_POI_READ         = 0xB1;
        public const byte CMD_SEARCH_WRITE     = 0xC0;
        public const byte CMD_SEARCH_READ      = 0xC1;
        public const byte CMD_CRC_READ         = 0xA0;
        public const byte CMD_ACTIVATE_MISSION = 0xA1;
        public const byte CMD_FILE_DELETE      = 0xA2;

        // ── ACK codes ─────────────────────────────────────────────────────
        public const byte ACK_OK                  = 0x01;
        public const byte ACK_HEADER_INVALID      = 0x02;
        public const byte ACK_WP_INVALID          = 0x03;
        public const byte ACK_WP_LIM_EXCEEDED     = 0x04;
        public const byte ACK_ALL_WP_NOT_RECEIVED = 0x05;
        public const byte ACK_FILE_NOT_EXIST      = 0x06;
        public const byte ACK_HEADER_NOT_RECEIVED = 0x07;
        public const byte ACK_MISSION_CRC_FAILED  = 0x10;
        public const byte ACK_MISSION_PROG_DENIED = 0x11;

        public const int PACKET_SIZE = 32;
        public const int DATA_OFFSET = 4;   // data starts at byte 4
        public const int DATA_LEN    = 24;  // 24 data bytes (4..27)
        public const int CRC_OFFSET  = 28;  // CRC32 at bytes 28-31

        // ════════════════════════════════════════════════════════════════
        //  Public API
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Encode the full mission upload sequence into a byte array.
        /// Sequence: HEADER_WRITE → WP_WRITE[0..N] (+ POI/SEARCH) → ACTIVATE_MISSION
        /// Only BStation (WP0) and Waypoint category entries are encoded;
        /// LP/TK runway markers are map-display only and not sent to FCC.
        /// </summary>
        public static byte[] Encode(MissionData md, byte vehicleId)
        {
            var mwps = GetMissionWPs(md);
            int totalWP = mwps.Count;

            // 1. Build header (data[20..23] = zeros for now)
            byte[] headerPkt = BuildHeaderPacket(vehicleId, md, totalWP);

            // 2. Build WP/POI/SEARCH packets (same order Calc_MP_CRC processes them)
            var wpPackets = new List<byte[]>();
            for (int i = 0; i < mwps.Count; i++)
            {
                var wp = mwps[i];
                wpPackets.Add(BuildWPPacket(vehicleId, wp, i));
                if (wp.POIFlag)
                    wpPackets.Add(BuildPOIPacket(vehicleId, wp, i));
                if (wp.SearchFlag)
                    wpPackets.Add(BuildSearchPacket(vehicleId, wp, i));
            }

            // 3. Compute mission CRC — mirrors Calc_MP_CRC / Calculate_CRC32 in MissionProgramming.c:
            //      MP_CRC seed=0, accumulate over header[4..23] then each WP/POI/SEARCH[4..27]
            //      Result stored in header pkt[24..27] (= data[20..23]).
            uint missionCRC = 0u;
            missionCRC = CalcCRC32(headerPkt,  4, 24, missionCRC); // header bytes [4..23] = 20 bytes
            foreach (var pkt in wpPackets)
                missionCRC = CalcCRC32(pkt, 4, 28, missionCRC);    // WP bytes [4..27] = 24 bytes

            headerPkt[24] = (byte)(missionCRC         & 0xFF);
            headerPkt[25] = (byte)((missionCRC >>  8) & 0xFF);
            headerPkt[26] = (byte)((missionCRC >> 16) & 0xFF);
            headerPkt[27] = (byte)((missionCRC >> 24) & 0xFF);
            AppendCRC(headerPkt);   // re-compute per-packet CRC now that [24..27] changed

            // 4. Activate
            byte[] activatePkt = BuildActivatePacket(vehicleId);

            // Concatenate: header + WPs + activate
            var allPackets = new List<byte[]> { headerPkt };
            allPackets.AddRange(wpPackets);
            allPackets.Add(activatePkt);

            using (var ms = new MemoryStream())
            {
                foreach (var pkt in allPackets)
                    ms.Write(pkt, 0, pkt.Length);
                return ms.ToArray();
            }
        }

        /// <summary>Encode and save the packet stream to a .bin file.</summary>
        public static void WriteToFile(string path, MissionData md, byte vehicleId)
        {
            byte[] stream = Encode(md, vehicleId);
            File.WriteAllBytes(path, stream);
        }

        /// <summary>How many 32-byte packets the full upload will produce.</summary>
        public static int PacketCount(MissionData md)
        {
            int n = 2; // header + activate
            foreach (var wp in GetMissionWPs(md))
            {
                n++;
                if (wp.POIFlag)    n++;
                if (wp.SearchFlag) n++;
            }
            return n;
        }

        /// <summary>Decode an ACK packet from the FCC.  Returns the ACK code byte.</summary>
        public static byte DecodeAck(byte[] pkt, out byte cmd, out ushort wpNum)
        {
            cmd   = pkt.Length > 1 ? pkt[1] : (byte)0;
            wpNum = pkt.Length > 3 ? (ushort)(pkt[2] | (pkt[3] << 8)) : (ushort)0;
            return pkt.Length > 4 ? pkt[4] : (byte)0;
        }

        // ════════════════════════════════════════════════════════════════
        //  Packet builders
        // ════════════════════════════════════════════════════════════════

        // ── HEADER_WRITE ─────────────────────────────────────────────────
        //  [4-11]  TimeStamp  (8 bytes, treated as double by FCC)
        //  [12]    UAV ID
        //  [13-21] Mission name (9 bytes ASCII, null-padded)
        //  [22-23] TotalWP (uint16 LE)
        //  [24-27] Reserved (zeros)
        private static byte[] BuildHeaderPacket(byte vehicleId, MissionData md, int totalWP)
        {
            byte[] pkt  = NewPacket(vehicleId, CMD_HEADER_WRITE, 0);
            byte[] data = new byte[DATA_LEN];

            // TimeStamp — current time as Unix double (8 bytes)
            double ts = (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
            byte[] tsb = BitConverter.GetBytes(ts);
            Array.Copy(tsb, 0, data, 0, 8);

            // UAV ID
            data[8] = vehicleId;

            // Name (9 bytes, null-padded)
            string name = md.Name ?? "";
            for (int i = 0; i < 9 && i < name.Length; i++)
                data[9 + i] = (byte)name[i];

            // TotalWP
            data[18] = (byte)(totalWP & 0xFF);
            data[19] = (byte)((totalWP >> 8) & 0xFF);

            WriteData(pkt, data);
            AppendCRC(pkt);
            return pkt;
        }

        // ── WP_WRITE ──────────────────────────────────────────────────────
        //  [4-7]   Lat      → uint32
        //  [8-11]  Lon      → uint32
        //  [12-14] Alt      → uint24
        //  [15-16] CSpeed   → uint16
        //  [17-18] LtrAlt   → uint16
        //  [19]    LtrRadius→ byte
        //  [20-21] LtrSpeed → uint16
        //  [22]    LtrTime  → byte
        //  [23-26] zeros
        //  [27]    Flags    → bit0=Ltr, bit1=POI, bit2=Search
        private static byte[] BuildWPPacket(byte vehicleId, MissionWP wp, int idx)
        {
            byte[] pkt  = NewPacket(vehicleId, CMD_WP_WRITE, (ushort)idx);
            byte[] data = new byte[DATA_LEN];

            PutU32(data,  0, EncLat(wp.Lat));
            PutU32(data,  4, EncLon(wp.Lon));
            PutU24(data,  8, EncAlt(wp.Alt));
            PutU16(data, 11, EncSpeed(wp.CSpeed));
            PutU16(data, 13, EncAltSmall(wp.LtrAlt));
            data[15] = EncLtrRadius(wp.LtrRadius);
            PutU16(data, 16, EncSpeed(wp.LtrSpeed));
            data[18] = (byte)Math.Round(wp.LtrTime);
            // [19-22] zeros
            byte flags = 0;
            if (wp.LtrFlag)    flags |= 0x01;
            if (wp.POIFlag)    flags |= 0x02;
            if (wp.SearchFlag) flags |= 0x04;
            data[23] = flags;

            WriteData(pkt, data);
            AppendCRC(pkt);
            return pkt;
        }

        // ── POI_WRITE ─────────────────────────────────────────────────────
        //  [4-7]   POILat  → uint32
        //  [8-11]  POILon  → uint32
        //  [12-14] POIAlt  → uint24
        //  [15-27] zeros
        private static byte[] BuildPOIPacket(byte vehicleId, MissionWP wp, int idx)
        {
            byte[] pkt  = NewPacket(vehicleId, CMD_POI_WRITE, (ushort)idx);
            byte[] data = new byte[DATA_LEN];

            PutU32(data, 0, EncLat(wp.POILat));
            PutU32(data, 4, EncLon(wp.POILon));
            PutU24(data, 8, EncAlt(wp.POIAlt));

            WriteData(pkt, data);
            AppendCRC(pkt);
            return pkt;
        }

        // ── SEARCH_WRITE ──────────────────────────────────────────────────
        //  [4-5]   SearchBearing → uint16  (deg)
        //  [6-7]   SearchWidth   → uint16  (m)
        //  [8-9]   SearchLength  → uint16  (m)
        //  [10]    SearchTime    → byte    (min)
        //  [11-12] SearchSpeed   → uint16  (kts)
        //  [13-14] SearchAlt     → uint16  (m)
        //  [15-27] zeros
        private static byte[] BuildSearchPacket(byte vehicleId, MissionWP wp, int idx)
        {
            byte[] pkt  = NewPacket(vehicleId, CMD_SEARCH_WRITE, (ushort)idx);
            byte[] data = new byte[DATA_LEN];

            PutU16(data, 0, EncBearing(wp.SearchBearing));
            PutU16(data, 2, EncSearchWidth(wp.SearchWidth));
            PutU16(data, 4, EncAltSmall(wp.SearchLength));
            data[6] = (byte)Math.Round(wp.SearchTime);
            PutU16(data, 7, EncSpeed(wp.SearchSpeed));
            PutU16(data, 9, EncAltSmall(wp.SearchAlt));

            WriteData(pkt, data);
            AppendCRC(pkt);
            return pkt;
        }

        // ── ACTIVATE_MISSION ──────────────────────────────────────────────
        private static byte[] BuildActivatePacket(byte vehicleId)
        {
            byte[] pkt = NewPacket(vehicleId, CMD_ACTIVATE_MISSION, 0);
            // data bytes 4-27 are zeros
            AppendCRC(pkt);
            return pkt;
        }

        // ════════════════════════════════════════════════════════════════
        //  Packet helpers
        // ════════════════════════════════════════════════════════════════
        private static byte[] NewPacket(byte vehicleId, byte cmd, ushort wpNum)
        {
            byte[] pkt = new byte[PACKET_SIZE];
            pkt[0] = vehicleId;
            pkt[1] = cmd;
            pkt[2] = (byte)(wpNum & 0xFF);
            pkt[3] = (byte)((wpNum >> 8) & 0xFF);
            return pkt;
        }

        private static void WriteData(byte[] pkt, byte[] data)
        {
            for (int i = 0; i < DATA_LEN && i < data.Length; i++)
                pkt[DATA_OFFSET + i] = data[i];
        }

        private static void AppendCRC(byte[] pkt)
        {
            uint crc = CalcCRC32(pkt, 0, CRC_OFFSET);
            pkt[28] = (byte)(crc & 0xFF);
            pkt[29] = (byte)((crc >> 8) & 0xFF);
            pkt[30] = (byte)((crc >> 16) & 0xFF);
            pkt[31] = (byte)((crc >> 24) & 0xFF);
        }

        /// <summary>
        /// Mirrors CalcCRC32() / Calculate_CRC32() from apio.c / MissionProgramming.c:
        ///   seed = 0x00000000, no final XOR, reflected polynomial 0xEDB88320.
        /// NOTE: This is NOT standard ISO CRC-32 (which uses seed 0xFFFFFFFF).
        /// Pass <paramref name="init"/> to accumulate a running CRC across multiple
        /// byte ranges (mirrors the global MP_CRC accumulator in Calc_MP_CRC).
        /// </summary>
        private static uint CalcCRC32(byte[] data, int start, int end, uint init = 0u)
        {
            uint crc = init;
            for (int i = start; i < end; i++)
            {
                crc ^= data[i];
                for (int k = 0; k < 8; k++)
                    crc = (crc & 1u) != 0u ? (0xEDB88320u ^ (crc >> 1)) : (crc >> 1);
            }
            return crc;
        }

        // ── Little-endian writers ─────────────────────────────────────────
        private static void PutU32(byte[] buf, int off, uint v)
        {
            buf[off]   = (byte)(v & 0xFF);
            buf[off+1] = (byte)((v >> 8)  & 0xFF);
            buf[off+2] = (byte)((v >> 16) & 0xFF);
            buf[off+3] = (byte)((v >> 24) & 0xFF);
        }
        private static void PutU24(byte[] buf, int off, uint v)
        {
            buf[off]   = (byte)(v & 0xFF);
            buf[off+1] = (byte)((v >> 8)  & 0xFF);
            buf[off+2] = (byte)((v >> 16) & 0xFF);
        }
        private static void PutU16(byte[] buf, int off, ushort v)
        {
            buf[off]   = (byte)(v & 0xFF);
            buf[off+1] = (byte)((v >> 8) & 0xFF);
        }

        // ════════════════════════════════════════════════════════════════
        //  Encoding scale factors (from MissionProgramming.c)
        // ════════════════════════════════════════════════════════════════
        private static uint   EncLat(double deg)
            => (uint)Math.Round((deg + 90.0)  * 23860929.4166667);
        private static uint   EncLon(double deg)
            => (uint)Math.Round((deg + 180.0) * 11930464.7083333);
        private static uint   EncAlt(double m)
            => (uint)Math.Round(m * 838.860750);
        private static ushort EncSpeed(double kts)
            => (ushort)Math.Round(kts * 655.350);
        private static ushort EncAltSmall(double m)
            => (ushort)Math.Round(m * 3.276750);
        private static byte   EncLtrRadius(double m)
            => (byte)Math.Round(m * 0.0510);
        private static ushort EncBearing(double deg)
            => (ushort)Math.Round(deg * 182.041667);
        private static ushort EncSearchWidth(double m)
            => (ushort)Math.Round(m * 13.1070);

        // ════════════════════════════════════════════════════════════════
        //  Mission WP filter (BStation + Waypoint only — no LP/TK)
        // ════════════════════════════════════════════════════════════════
        private static List<MissionWP> GetMissionWPs(MissionData md)
        {
            var list = new List<MissionWP>();
            foreach (var wp in md.WayPoints)
                if (wp.Category == WPCategory.BStation ||
                    wp.Category == WPCategory.Waypoint)
                    list.Add(wp);
            // Ensure WP0 (BStation) is first, then sorted by number
            list.Sort((a, b) =>
            {
                if (a.Category == WPCategory.BStation) return -1;
                if (b.Category == WPCategory.BStation) return  1;
                return a.Number.CompareTo(b.Number);
            });
            return list;
        }
    }
}
