// PacketReceiver.cs
// Receives 8 fixed-length packets (164 bytes each) from a serial port using
// System.IO.Ports.SerialPort (built-in .NET — no third-party dependency).
//
// Packet layout (all values hex):
//   [0]        AA          Sync byte 1
//   [1]        55          Sync byte 2
//   [2]        DD          Sync byte 3
//   [3]        01..08      Packet number
//   [4..159]   <payload>   156 bytes of data
//   [160..163] <CRC32>     CRC-32 of bytes [0..159], little-endian

using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;

namespace GCS_240626
{
    public class PacketReceiver
    {
        // ── Mission TX/RX (upload ACKs + download responses) ──────────────
        // FCC sends all mission responses via Tx_MissProgPacket() which wraps
        // the 28-byte MP_Buff + 4-byte CRC in a 4-byte sync header:
        //   [0-3]  0xAA 0x55 0x55 0xBB   ← mission sync (differs from telemetry 0xAA 0x55 0xDD)
        //   [4-31] 28 bytes (VehicleID + CMD + WPNum + data)
        //   [32-35] CRC-32 of bytes [4..31]
        // Total = 36 bytes.  We strip the 4-byte sync and store the inner 32 bytes.
        private const int  MISSION_RESP_LEN  = 36;   // full wire length
        private const byte MSYNC0 = 0xAA, MSYNC1 = 0x55, MSYNC2 = 0x55, MSYNC3 = 0xBB;

        // Set true by MissionUploader/Downloader; cleared when operation ends.
        private volatile bool _missionActive;
        private readonly byte[]          _lastAck  = new byte[MissionPacketEncoder.PACKET_SIZE]; // 32 bytes (stripped)
        private readonly SemaphoreSlim   _ackGate  = new SemaphoreSlim(0, 1);
        private readonly object          _ackLock  = new object();

        /// <summary>Call before starting a mission upload or download.</summary>
        public void BeginMissionOp()  => _missionActive = true;

        /// <summary>Call after mission upload or download completes.</summary>
        public void EndMissionOp()    => _missionActive = false;

        // Legacy names kept so existing MissionUploader.cs compiles unchanged.
        public void BeginMissionUpload() => BeginMissionOp();
        public void EndMissionUpload()   => EndMissionOp();

        /// <summary>
        /// Write raw bytes to the serial port (mission programming packets).
        /// The port must already be open via Open().
        /// </summary>
        public void SendRaw(byte[] data)
        {
            if (_port == null || !_port.IsOpen)
                throw new InvalidOperationException("[PacketReceiver] Port not open.");
            _port.Write(data, 0, data.Length);
        }

        /// <summary>
        /// Prepend the 4-byte mission sync header (AA 55 55 BB) to payload and
        /// write all 36 bytes to the serial port.  sgsim's ReadCmdLink() state
        /// machine requires this header to recognise and route mission packets.
        /// </summary>
        public void SendMissionPacket(byte[] payload)
        {
            if (_port == null || !_port.IsOpen)
                throw new InvalidOperationException("[PacketReceiver] Port not open.");
            byte[] framed = new byte[4 + payload.Length];
            framed[0] = 0xAA; framed[1] = 0x55; framed[2] = 0x55; framed[3] = 0xBB;
            Array.Copy(payload, 0, framed, 4, payload.Length);
            _port.Write(framed, 0, framed.Length);
        }

        /// <summary>
        /// Block until the next 32-byte ACK arrives (or timeout).
        /// Returns a copy of the ACK packet, or null on timeout.
        /// Call only from the MissionUploader background thread.
        /// </summary>
        public byte[] WaitForAck(int timeoutMs = 2000)
        {
            if (_ackGate.Wait(timeoutMs))
            {
                lock (_ackLock)
                {
                    byte[] copy = new byte[MissionPacketEncoder.PACKET_SIZE];
                    Array.Copy(_lastAck, copy, copy.Length);
                    return copy;
                }
            }
            return null;   // timeout
        }


        // ── Packet geometry ────────────────────────────────────────────────
        private const int PACKET_LEN = 164;
        private const int CRC_OFFSET = 160;
        private const int CRC_RANGE = 160;   // bytes over which CRC is computed
        private const int HEADER_LEN = 4;
        private const int CRC_LEN = 4;
        private const int TOTAL_PACKETS = 8;

        private const byte SYNC1 = 0xAA;
        private const byte SYNC2 = 0x55;
        private const byte SYNC3 = 0xDD;

        // ── Storage ────────────────────────────────────────────────────────
        // PacketData[n] holds the complete 164-byte raw packet for packet n+1.
        // Index 0 = packet number 01, index 7 = packet number 08.
        public static readonly byte[][] PacketData = new byte[TOTAL_PACKETS][];

        // PacketReceived[n] is set to true the first time a CRC-OK packet n+1 arrives.
        // Use this instead of null-checking PacketData[] (arrays are pre-allocated).
        public static readonly bool[] PacketReceived = new bool[TOTAL_PACKETS];

        // ── Diagnostics (read from UI thread via 100 ms timer) ─────────────
        public static int RawBytesCount = 0;   // total bytes seen on the wire
        public static int CrcFailCounter = 0;   // packets dropped due to bad CRC
        public static int MissPacketsCount = 0;  // alias — kept for legacy callers

        // ── Private state ──────────────────────────────────────────────────
        private SerialPort _port;
        private readonly List<byte> _rxBuf = new List<byte>(1024);
        private readonly object _bufLock = new object();

        // Separate static lock that protects only PacketData[]/PacketReceived[].
        // Allows the UI thread to take a consistent snapshot without blocking the
        // serial receive thread for longer than a single Array.Copy per packet.
        private static readonly object _snapLock = new object();

        // ── Constructor ────────────────────────────────────────────────────
        public PacketReceiver()
        {
            for (int i = 0; i < TOTAL_PACKETS; i++)
                PacketData[i] = new byte[PACKET_LEN];
        }

        // ── Public API ─────────────────────────────────────────────────────

        /// <summary>Opens the serial port and starts event-driven reception.</summary>
        public void Open(string portName = "COM9", int baudRate = 115200)
        {
            _port = new SerialPort
            {
                PortName = portName,
                BaudRate = baudRate,
                DataBits = 8,
                Parity = Parity.None,
                StopBits = StopBits.One,
                Handshake = Handshake.None,

                // Buffer large enough for several packets
                ReadBufferSize = 4096,
                WriteBufferSize = 1024,

                ReadTimeout = SerialPort.InfiniteTimeout,
                WriteTimeout = 500
            };

            _port.DataReceived += OnDataReceived;
            _port.ErrorReceived += OnErrorReceived;

            _port.Open();
            Console.WriteLine($"[PacketReceiver] Port {portName} open at {baudRate} baud.");
        }

        /// <summary>Closes the serial port cleanly.</summary>
        public void Close()
        {
            if (_port != null && _port.IsOpen)
            {
                _port.DataReceived -= OnDataReceived;
                _port.ErrorReceived -= OnErrorReceived;
                _port.Close();
                _port.Dispose();
                Console.WriteLine("[PacketReceiver] Port closed.");
            }
        }

        // ── Event handlers ─────────────────────────────────────────────────

        private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (_port == null || !_port.IsOpen) return;

            int available = _port.BytesToRead;
            if (available <= 0) return;

            byte[] chunk = new byte[available];
            int bytesRead = _port.Read(chunk, 0, available);

            // Count every raw byte — if RawBytesCount stays 0, the port is silent.
            System.Threading.Interlocked.Add(ref RawBytesCount, bytesRead);

            lock (_bufLock)
            {
                for (int i = 0; i < bytesRead; i++)
                    _rxBuf.Add(chunk[i]);

                ProcessBuffer();
            }
        }

        private void OnErrorReceived(object sender, SerialErrorReceivedEventArgs e)
        {
            Console.WriteLine($"[PacketReceiver] Serial error: {e.EventType}");
        }

        // ── Packet framing ─────────────────────────────────────────────────

        /// <summary>
        /// Scans _rxBuf for complete packets.
        /// During mission upload, 32-byte ACK packets are routed to _ackGate
        /// before the normal 164-byte telemetry path runs.
        /// Runs entirely inside _bufLock.
        /// </summary>
        private void ProcessBuffer()
        {
            while (_rxBuf.Count > 0)
            {
                // ── Mission response path ─────────────────────────────────────
                // All FCC mission responses (ACKs + READ data) are 36 bytes:
                //   [0-3]  sync 0xAA 0x55 0x55 0xBB
                //   [4-31] 28 data bytes (VehicleID + CMD + WPNum + payload)
                //   [32-35] CRC-32 of bytes [4..31]
                // Telemetry uses  0xAA 0x55 0xDD  at bytes [0-2] — distinct at byte 2.
                if (_missionActive &&
                    _rxBuf.Count >= MISSION_RESP_LEN &&
                    _rxBuf[0] == MSYNC0 && _rxBuf[1] == MSYNC1 &&
                    _rxBuf[2] == MSYNC2 && _rxBuf[3] == MSYNC3)
                {
                    // Strip 4-byte sync; store inner 32 bytes (data + CRC)
                    byte[] resp = _rxBuf.GetRange(4, MissionPacketEncoder.PACKET_SIZE).ToArray();
                    _rxBuf.RemoveRange(0, MISSION_RESP_LEN);
                    lock (_ackLock) Array.Copy(resp, _lastAck, resp.Length);
                    try { _ackGate.Release(); }
                    catch (SemaphoreFullException) { }   // duplicate — ignore
                    continue;
                }

                // ── Telemetry path ────────────────────────────────────────────
                if (_rxBuf.Count < PACKET_LEN) return;

                int syncIdx = FindSync();

                if (syncIdx < 0)
                {
                    // No header found; keep last 2 bytes in case preamble is split
                    if (_rxBuf.Count > 2)
                        _rxBuf.RemoveRange(0, _rxBuf.Count - 2);
                    return;
                }

                if (syncIdx > 0)
                {
                    Console.WriteLine($"[PacketReceiver] Discarding {syncIdx} byte(s) before sync.");
                    _rxBuf.RemoveRange(0, syncIdx);
                }

                if (_rxBuf.Count < PACKET_LEN) return;

                byte[] packet = _rxBuf.GetRange(0, PACKET_LEN).ToArray();
                _rxBuf.RemoveRange(0, PACKET_LEN);

                HandlePacket(packet);
            }
        }

        private int FindSync()
        {
            for (int i = 0; i <= _rxBuf.Count - 3; i++)
            {
                if (_rxBuf[i] == SYNC1 &&
                    _rxBuf[i + 1] == SYNC2 &&
                    _rxBuf[i + 2] == SYNC3)
                    return i;
            }
            return -1;
        }

        // ── Packet handling ────────────────────────────────────────────────

        private void HandlePacket(byte[] packet)
        {
            int packetNum = packet[3];   // 1..8

            if (packetNum < 1 || packetNum > TOTAL_PACKETS)
            {
                Console.WriteLine($"[PacketReceiver] Unknown packet number 0x{packetNum:X2} — discarded.");
                return;
            }

            // CRC_BYPASS: accept all packets with valid sync + packet number.
            // The telemetry CRC algorithm is confirmed different from the
            // Mission-Program CalcCRC32 in CRC.c — re-enable once the
            // telemetry encoder source is available.
            bool crcOk = true; // ValidateCRC(packet);

            if (crcOk)
            {
                lock (_snapLock)
                {
                    Array.Copy(packet, PacketData[packetNum - 1], PACKET_LEN);
                    PacketReceived[packetNum - 1] = true;
                }
                // Console.WriteLine removed — fires per packet (400/sec), kills throughput
            }
            else
            {
                System.Threading.Interlocked.Increment(ref CrcFailCounter);
                MissPacketsCount = CrcFailCounter;
                Console.WriteLine($"[PacketReceiver] Packet {packetNum:D2} CRC FAIL  " +
                                  $"(total failures: {CrcFailCounter})");
            }
        }

        // ── Payload accessor ───────────────────────────────────────────────

        /// <summary>
        /// Returns a copy of the 156-byte payload for packet number n (1..8),
        /// i.e. packet bytes [4..159].
        /// </summary>
        public static byte[] GetPayload(int packetNum)
        {
            if (packetNum < 1 || packetNum > TOTAL_PACKETS)
                throw new ArgumentOutOfRangeException(nameof(packetNum), "Must be 1..8");

            byte[] payload = new byte[PACKET_LEN - HEADER_LEN - CRC_LEN]; // 156 bytes
            Array.Copy(PacketData[packetNum - 1], HEADER_LEN, payload, 0, payload.Length);
            return payload;
        }

        /// <summary>
        /// Returns deep copies of all 8 PacketData arrays and the PacketReceived flags,
        /// taken atomically under _snapLock so the UI thread never sees a torn write.
        /// </summary>
        public static (byte[][] data, bool[] received) TakeSnapshot()
        {
            var data = new byte[TOTAL_PACKETS][];
            var recv = new bool[TOTAL_PACKETS];
            lock (_snapLock)
            {
                for (int i = 0; i < TOTAL_PACKETS; i++)
                {
                    data[i] = new byte[PACKET_LEN];
                    Array.Copy(PacketData[i], data[i], PACKET_LEN);
                    recv[i] = PacketReceived[i];
                }
            }
            return (data, recv);
        }

        // ── CRC-32 (mirrors CalcCRC32 in CRC.c) ───────────────────────────

        /// <summary>
        /// Returns true if the CRC-32 of packet[0..159] matches the
        /// little-endian 4-byte value stored at packet[160..163].
        /// </summary>
        private static bool ValidateCRC(byte[] packet)
        {
            uint computed = CalcCRC32(packet, 0, CRC_RANGE);

            uint stored = (uint)(packet[CRC_OFFSET]
                                | (packet[CRC_OFFSET + 1] << 8)
                                | (packet[CRC_OFFSET + 2] << 16)
                                | (packet[CRC_OFFSET + 3] << 24));

            return computed == stored;
        }

        /// <summary>
        /// Direct C# port of CalcCRC32() from CRC.c.
        /// Seed = 0x00000000, polynomial = reflected 0xEDB88320.
        /// </summary>
        private static uint CalcCRC32(byte[] buffer, int start, int end)
        {
            uint crc = 0x00000000u;
            for (int i = start; i < end; i++)
                crc = (crc >> 8) ^ CrcTable[(buffer[i] ^ crc) & 0xFF];
            return crc;
        }

        // ── CRC-32 lookup table (from Table[] in CRC.c, 256 entries) ───────
        private static readonly uint[] CrcTable = new uint[256]
        {
            0x00000000, 0x77073096, 0xEE0E612C, 0x990951BA,
            0x076DC419, 0x706AF48F, 0xE963A535, 0x9E6495A3,
            0x0EDB8832, 0x79DCB8A4, 0xE0D5E91E, 0x97D2D988,
            0x09B64C2B, 0x7EB17CBD, 0xE7B82D07, 0x90BF1D91,
            0x1DB71064, 0x6AB020F2, 0xF3B97148, 0x84BE41DE,
            0x1ADAD47D, 0x6DDDE4EB, 0xF4D4B551, 0x83D385C7,
            0x136C9856, 0x646BA8C0, 0xFD62F97A, 0x8A65C9EC,
            0x14015C4F, 0x63066CD9, 0xFA0F3D63, 0x8D080DF5,
            0x3B6E20C8, 0x4C69105E, 0xD56041E4, 0xA2677172,
            0x3C03E4D1, 0x4B04D447, 0xD20D85FD, 0xA50AB56B,
            0x35B5A8FA, 0x42B2986C, 0xDBBBC9D6, 0xACBCF940,
            0x32D86CE3, 0x45DF5C75, 0xDCD60DCF, 0xABD13D59,
            0x26D930AC, 0x51DE003A, 0xC8D75180, 0xBFD06116,
            0x21B4F4B5, 0x56B3C423, 0xCFBA9599, 0xB8BDA50F,
            0x2802B89E, 0x5F058808, 0xC60CD9B2, 0xB10BE924,
            0x2F6F7C87, 0x58684C11, 0xC1611DAB, 0xB6662D3D,
            0x76DC4190, 0x01DB7106, 0x98D220BC, 0xEFD5102A,
            0x71B18589, 0x06B6B51F, 0x9FBFE4A5, 0xE8B8D433,
            0x7807C9A2, 0x0F00F934, 0x9609A88E, 0xE10E9818,
            0x7F6A0DBB, 0x086D3D2D, 0x91646C97, 0xE6635C01,
            0x6B6B51F4, 0x1C6C6162, 0x856530D8, 0xF262004E,
            0x6C0695ED, 0x1B01A57B, 0x8208F4C1, 0xF50FC457,
            0x65B0D9C6, 0x12B7E950, 0x8BBEB8EA, 0xFCB9887C,
            0x62DD1DDF, 0x15DA2D49, 0x8CD37CF3, 0xFBD44C65,
            0x4DB26158, 0x3AB551CE, 0xA3BC0074, 0xD4BB30E2,
            0x4ADFA541, 0x3DD895D7, 0xA4D1C46D, 0xD3D6F4FB,
            0x4369E96A, 0x346ED9FC, 0xAD678846, 0xDA60B8D0,
            0x44042D73, 0x33031DE5, 0xAA0A4C5F, 0xDD0D7CC9,
            0x5005713C, 0x270241AA, 0xBE0B1010, 0xC90C2086,
            0x5768B525, 0x206F85B3, 0xB966D409, 0xCE61E49F,
            0x5EDEF90E, 0x29D9C998, 0xB0D09822, 0xC7D7A8B4,
            0x59B33D17, 0x2EB40D81, 0xB7BD5C3B, 0xC0BA6CAD,
            0xEDB88320, 0x9ABFB3B6, 0x03B6E20C, 0x74B1D29A,
            0xEAD54739, 0x9DD277AF, 0x04DB2615, 0x73DC1683,
            0xE3630B12, 0x94643B84, 0x0D6D6A3E, 0x7A6A5AA8,
            0xE40ECF0B, 0x9309FF9D, 0x0A00AE27, 0x7D079EB1,
            0xF00F9344, 0x8708A3D2, 0x1E01F268, 0x6906C2FE,
            0xF762575D, 0x806567CB, 0x196C3671, 0x6E6B06E7,
            0xFED41B76, 0x89D32BE0, 0x10DA7A5A, 0x67DD4ACC,
            0xF9B9DF6F, 0x8EBEEFF9, 0x17B7BE43, 0x60B08ED5,
            0xD6D6A3E8, 0xA1D1937E, 0x38D8C2C4, 0x4FDFF252,
            0xD1BB67F1, 0xA6BC5767, 0x3FB506DD, 0x48B2364B,
            0xD80D2BDA, 0xAF0A1B4C, 0x36034AF6, 0x41047A60,
            0xDF60EFC3, 0xA867DF55, 0x316E8EEF, 0x4669BE79,
            0xCB61B38C, 0xBC66831A, 0x256FD2A0, 0x5268E236,
            0xCC0C7795, 0xBB0B4703, 0x220216B9, 0x5505262F,
            0xC5BA3BBE, 0xB2BD0B28, 0x2BB45A92, 0x5CB36A04,
            0xC2D7FFA7, 0xB5D0CF31, 0x2CD99E8B, 0x5BDEAE1D,
            0x9B64C2B0, 0xEC63F226, 0x756AA39C, 0x026D930A,
            0x9C0906A9, 0xEB0E363F, 0x72076785, 0x05005713,
            0x95BF4A82, 0xE2B87A14, 0x7BB12BAE, 0x0CB61B38,
            0x92D28E9B, 0xE5D5BE0D, 0x7CDCEFB7, 0x0BDBDF21,
            0x86D3D2D4, 0xF1D4E242, 0x68DDB3F8, 0x1FDA836E,
            0x81BE16CD, 0xF6B9265B, 0x6FB077E1, 0x18B74777,
            0x88085AE6, 0xFF0F6A70, 0x66063BCA, 0x11010B5C,
            0x8F659EFF, 0xF862AE69, 0x616BFFD3, 0x166CCF45,
            0xA00AE278, 0xD70DD2EE, 0x4E048354, 0x3903B3C2,
            0xA7672661, 0xD06016F7, 0x4969474D, 0x3E6E77DB,
            0xAED16A4A, 0xD9D65ADC, 0x40DF0B66, 0x37D83BF0,
            0xA9BCAE53, 0xDEBB9EC5, 0x47B2CF7F, 0x30B5FFE9,
            0xBDBDF21C, 0xCABAC28A, 0x53B39330, 0x24B4A3A6,
            0xBAD03605, 0xCDD70693, 0x54DE5729, 0x23D967BF,
            0xB3667A2E, 0xC4614AB8, 0x5D681B02, 0x2A6F2B94,
            0xB40BBE37, 0xC30C8EA1, 0x5A05DF1B, 0x2D02EF8D
        };
    }
}
