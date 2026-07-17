// TelemetryDecoder.cs
// Decodes all telemetry fields from a 164-byte packet per GCS ICD-Telemetry1.xlsx
//
// Packet layout (sgsim actual — differs from ICD section boundaries):
//   [0-3]    Header    : AA 55 DD XX  (XX = packet number 01..08)
//   [4-71]   Full Rate : 68 bytes (ICD fields end at [61]; [62-71] are sgsim extras / spare)
//   [72-109] Half Rate : 38 bytes, Frame 1 in odd packets (1,3,5,7)
//                                  Frame 2 in even packets (2,4,6,8)
//   [110-159] Low Rate : Frame N in packet N (50 bytes; ICD fields span ~30 bytes)
//   [160-163] CRC-32   : little-endian
//
// Encoding: little-endian unsigned integer
// Eng Value = Raw * (Max - Min) / (2^(N*8) - 1) + Min

using System;

namespace GCS_240626
{
    // ── All decoded engineering-unit values ───────────────────────────────
    public class TelemetryData
    {
        public int PacketNumber;    // 1..8

        // ── Full Rate (every packet) ──────────────────────────────────────
        public double FCC7Time;             // sec       [4-7]
        public double Roll7Rate;            // deg/sec   [8-9]
        public double Pitch7Rate;           // deg/sec   [10-11]
        public double Yaw7Rate;             // deg/sec   [12-13]
        public double X7Acceleration;       // m/s²      [14-15]
        public double Y7Acceleration;       // m/s²      [16-17]
        public double Z7Acceleration;       // m/s²      [18-19]
        public double Inner_SB_Ail_Com;     // deg       [20]
        public double Inner_SB_Ail_Pos;     // deg       [21]
        public double Outer_SB_Ail_Com;     // deg       [22]
        public double Outer_SB_Ail_Pos;     // deg       [23]
        public double Inner_Port_Ail_Com;   // deg       [24]
        public double Inner_Port_Ail_Pos;   // deg       [25]
        public double Outer_Port_Ail_Com;   // deg       [26]
        public double Outer_Port_Ail_Pos;   // deg       [27]
        public double Top_SB_RV_Com;        // deg       [28]
        public double Top_SB_RV_Pos;        // deg       [29]
        public double Bottom_SB_RV_Com;     // deg       [30]
        public double Bottom_SB_RV_Pos;     // deg       [31]
        public double Top_Port_RV_Com;      // deg       [32]
        public double Top_Port_RV_Pos;      // deg       [33]
        public double Bottom_Port_RV_Com;   // deg       [34]
        public double Bottom_Port_RV_Pos;   // deg       [35]
        public double NW_Com;               // deg       [36]
        public double NW_Pos;               // deg       [37]
        public double Roll7Angle;           // deg       [38-39]
        public double Pitch7Angle;          // deg       [40-41]
        public double Angle7of7Attack;      // deg       [42]
        public double Sideslip7Angle;       // deg       [43]
        public double Phi_Com;              // deg       [44]
        public double LOS_Azimuth;          // deg       [45-46]
        public double LOS_Elev;             // deg       [47-48]
        public double FoV;                  // deg       [49-50]
        public double Batt_Volts;           // V         [51]
        public double Alt_Volts;            // V         [52]
        public double AltCurrent;           // A         [53]
        public double BatCurrent;           // A         [54]
        public double FCC_Clock;            // msec      [55-58]
        public double Q_Cmd;               // deg/sec   [59]
        public double ZAccel_Cmd;           // m/s/s     [60]
        public double Theta_Cmd;            // deg       [61]

        // ── Half Rate Frame 1 (packets 1, 3, 5, 7) ───────────────────────
        public double Heading;              // deg       [72-73]
        public double Latitude;             // deg       [74-77]
        public double Longitude;            // deg       [78-81]
        public double CAS;                  // m/s       [82]
        public double PS;                   // Pa        [83-84]
        public double PD;                   // Pa        [85]
        public double TAS;                  // m/s       [86]
        public double Hp;                   // m         [87-88]
        public double Throttle_com;         // per unit  [89]
        public double Throttle_pos;         // per unit  [90]
        public double Radar_Alt;            // m         [91-92]
        public double Psi_Com;              // deg       [93-94]
        public double CAS_Com;              // m/s       [95]
        public double Alt_Com;              // m         [96]
        // [97] spare = 0
        public double AGL;                  // m         [98-99]
        public double Slew_Rate_Alt_Com;    // m         [100-101] (2 B in sgsim)

        // ── Half Rate Frame 2 (packets 2, 4, 6, 8) ───────────────────────
        public double Servo01Current;       // A         [72]
        public double Servo02Current;       // A         [73]
        public double Servo03Current;       // A         [74]
        public double Servo04Current;       // A         [75]
        public double Servo05Current;       // A         [76]
        public double Servo06Current;       // A         [77]
        public double Servo07Current;       // A         [78]
        public double Servo08Current;       // A         [79]
        public double Servo09Current;       // A         [80]
        public double Servo10Current;       // A         [81]
        public double OffTrack;             // m         [82-83]
        public double GCS_Ail_Cmd;          // deg       [84]
        public double GCS_Elev_Cmd;         // deg       [85]
        public double GCS_Rudd_Cmd;         // deg       [86]
        public double GCS_Throttle_Cmd;     // per unit  [87]
        public double GCS_Brake_Cmd;        // per unit  [88]
        public double GCS_NW_Cmd;           // deg       [89]
        public double GCS_Roll_Cmd;         // deg       [90]
        public double MC_Elev_Cmd;          // deg       [91]
        public double MC_Ail_Cmd;           // deg       [92]
        public double MC_Rudd_Cmd;          // deg       [93]
        public double MC_Throt_Cmd;         // per unit  [94]
        public double GPS_Alt;              // m         [95-96]
        public byte   SystemStatusByte1;    // flags     [97]
        public byte   SystemStatusByte2;    // flags     [98]
        public double OffGlide;             // m         [99]

        // ── Low Rate Frame 1 (packet 1) ───────────────────────────────────
        public double Mach;                 // -         [110]
        public double Static7Air7Temp;      // deg C     [111]
        public double Total7Air7Temp;       // deg C     [112]
        public double X_mag;               // Gauss     [93]
        public double Y_mag;               // Gauss     [94]
        public double Z_mag;               // Gauss     [95]
        public double Mag_heading;          // deg       [96]
        public double Yaw7Rate7Offset;      // deg/s     [97]
        public double Roll7Rate7Offset;     // deg/s     [98]
        public double Pitch7Rate7Offset;    // deg/s     [99]
        public double Pressure7Alt7Offset;  // m         [100]
        public double Engine_RPM;           // rpm       [101]
        public double Engine_RPM_filt;      // rpm       [102]
        public double REFERENCE_L;          // m         [103]
        public double AIR_KXTrack;          //           [104]
        public double AIR_Kpsi;             //           [105]
        public double AIR_Kphi;             //           [106]
        public double GND_KXTrack_LL;       //           [107]
        public double GND_KXTrack_UL;       //           [108]
        public double GND_Kpsi;             //           [109]
        public double GND_Kr;               //           [110]
        public double RW_Align_Dist_TD;     // m         [111-112]
        public double RW_Align_ALT;         // m         [113]
        public double GST_Start_Dist_TD;    // m         [114]
        public double GST_Start_ALT;        // m         [115]
        public double FLARE_Start_Dist_TD;  // m         [116]
        public double FLARE_Start_ALT;      // m         [117]
        public double RW_EndOne_Dist_TD;    // m         [118]
        public double RW_EndTwo_Dist_TD;    // m         [119]

        // ── Low Rate Frame 2 (packet 2) ───────────────────────────────────
        public double North7Speed;          // m/s       [90]
        public double East7Speed;           // m/s       [91]
        public double Up7Speed;             // m/s       [92]
        public double D2Go;                 // km        [93-94]
        public double Course7Bearing;       // deg       [95]
        public double Required7Bearing;     // deg       [96]
        public double D2Go2TouchDownP;      // m         [98]
        public double Dist_Travelled;       // km        [99-100]
        public double dPsiAtDestWP;         // deg       [102]
        public double Distance7From7Base;   // km        [103-104]
        public double NW_CMD_LIMIT;         //           [105]
        public double AIR_Kh;               //           [107]
        public double KH_GST;               //           [108]
        public double Kp;                   //           [109]
        public double K_NL_RollControl;     //           [110]
        public double Lambda_NL_RollControl;//           [111]
        public double KIPHI;                //           [112]
        public double KIH_Enroute;          //           [113]
        public double KIH_GST;              //           [114]
        public double KTHETA;               //           [115]
        public double KQ;                   //           [116]
        public double ROC_CMD_Limit;        // ft        [117]

        // ── Low Rate Frame 3 (packet 3) ───────────────────────────────────
        public double Next7Course7Bearing;  // deg       [90]
        public double Next7WayPoint7Number; //           [91]
        public double Prev7WayPoint7Number; //           [92]
        public double Turning7Dist2WP;      // km        [93]
        public double Curve7Guidance7Angle; // deg       [94]
        public double Turn7Radius;          // km        [95]
        public double Time7Since7TakeOff;   // sec       [96-97]
        public byte   MCFlagByte1;          // flags     [98]
        public byte   MCFlagByte2;          // flags     [99]
        public byte   MCFlagByte3;          // flags     [100]
        public double Flight7Phase;         //           [101]
        public double Flight7Phase7Change7Reason; //     [102]
        public double WakeUp7Miss;          //           [103]
        public double Differential7Age;     // sec       [104]
        public double Height7Delta;         // m         [105-106]
        public double Speed7Delta;          // m         [107]
        public double ROC_Cmd_AP;           // m         [108]
        public double ROC_Estimated;        // m         [109]
        public double Pitch7Rate7AftLead;   // deg/sec   [110]
        public double H_err;               // m         [111]

        // ── Low Rate Frame 4 (packet 4) ───────────────────────────────────
        public byte   MCFlagByte4;          // flags     [90]
        public byte   MCFlagByte5;          // flags     [91]
        public byte   SysID7Status7Byte;    // flags     [92]
        public byte   System7Status7Byte3;  // flags     [93]
        public double Sensors7Current;      // A         [94]
        public double PFCU7Current_LR4;     // A         [95]
        public double BFCU7Current;         // A         [96]
        public double Current5V;            // A         [97]
        public double Current12V;           // A         [98]
        public double Pitot7Heater7Current; // A         [99]
        public double Payload7Current;      // A         [100]
        public double DLPA7Current;         // A         [101]
        public double Sensor7Volts;         // V         [102]
        public double Brake7Servo7Pos;      // per unit  [103]
        public double NLG7Pos;              // per unit  [104]
        public double Abort7Taxi7Reason;    //           [107]
        public double Abort7Landing7Reason; //           [108]
        public double Turn7Compensation;    // deg       [109]

        // ── Low Rate Frame 5 (packet 5) ───────────────────────────────────
        public double PFCU7Volts;           // V         [90]
        public double Servo7Volts;          // V         [91]
        public double Volts12V;             // V         [92]
        public double Volts5V;              // V         [93]
        public double Payload7Volts;        // V         [94]
        public double IPSU7Temp;            // deg C     [95]
        public byte   Relay7Status7Byte1;   // flags     [96]
        public byte   Relay7Status7Byte2;   // flags     [97]
        public byte   Relay7Status7Byte3;   // flags     [98]
        public byte   IPSU7Status7Byte1;    // flags     [99]
        public byte   IPSU7Status7Byte2;    // flags     [100]
        public byte   IPSU_Cmd_FBByte;      // flags     [101]
        public double Heading_Cmd;          // deg       [102]
        public double CPU7Temp;             // deg C     [103]
        public double Solution7Status;      //           [104]
        public double Satellites;           //           [105]
        public byte   BITStatus;            // flags     [106]
        public double GCSEchoTime;          // msec      [107-110]

        // ── Low Rate Frame 6 (packet 6) ───────────────────────────────────
        public double GCS_Alt_Cmd;          // m         [90]
        public double GCS_CAS_Cmd;          // m/s       [91]
        public double Injection7Time;       // usec      [92-95]
        public double Battery7Voltage_ECU;  // V         [96]
        public double CHT7Left;             // deg C     [97]
        public double CHT7Right;            // deg C     [98]
        public double EGT7Left;             // deg C     [99]
        public double EGT7Right;            // deg C     [100]
        public double Fuel7Pressure;        // mBar      [101-102]
        public double FuelLevel_Main;       // per unit  [103]
        public double FuelLevel_Left;       // per unit  [104]
        public double FuelLevel_Right;      // per unit  [105]
        public double Lat_Std;              //           [106]
        public double Lon_Std;              //           [107]
        public double Alt_Std;              //           [108]
        public double Latitude7DeadReck;    // deg       [109-112]
        public double Longitude7DeadReck;   // deg       [113-116]

        // ── Low Rate Frame 7 (packet 7) ───────────────────────────────────
        public double Fuel7Flow7Rate;       // lit/hr    [90]
        public double MC_Brake_Cmd;         // per unit  [91]
        public byte   FailSafeContFlagByte; // flags     [92]
        public double FCC7Temp;             // deg C     [93]
        public byte   SerChTOByte1;         // flags     [94]
        public byte   SerChTOByte2;         // flags     [95]
        public double CmdMissPacketsCount;  //           [96]
        public double GCS_VCMissPacketsCount;  //        [97]
        public double GCS_PLMissPacketsCount;  //        [98]
        public double VGMissPacketsCount;   //           [99]
        public double GPSMissPacketsCount;  //           [100]
        public double MagMissPacketsCount;  //           [101]
        public double GCS_MPMissPacketsCount;  //        [102]
        public double ADSMissPacketsCount;  //           [103]
        public double ECUMissPacketsCount;  //           [104]
        public double FSMissPacketsCount;   //           [105]
        public double RAMissPacketsCount;   //           [106]
        public double PLMissPacketsCount;   //           [107]
        public double IPSUMissPacketsCount; //           [108]
        public double IFCCMissPacketsCount; //           [109]
        public double SBUSMissPacketsCount; //           [110]
        public double DGPS_Corr_MPC;        //           [111]
        public double Telemetry_MPC;        //           [112]
        public byte   MiscFlags1;           // flags     [113]
        public double Loiter7Duration;      // sec       [114]
        public double Loiter7Timer;         // sec       [115]
        public double Nspeed7DeadReck;      // m/s       [116]
        public double Espeed7DeadReck;      // m/s       [117]
        public byte   MiscFlags2;           // flags     [118]
        public double GCS_Packet_Number;    //           [119]

        // ── Low Rate Frame 8 (packet 8) ───────────────────────────────────
        public double CmdPacketRate;        // pkt/sec   [90]
        public double VGPacketRate;         // pkt/sec   [91]
        public double GPSPacketRate;        // pkt/sec   [92]
        public double MagPacketRate;        // pkt/sec   [93]
        public double GCS_VCPacketRate;     // pkt/sec   [94]
        public double GCS_MPPacketRate;     // pkt/sec   [95]
        public double GCS_PLPacketRate;     // pkt/sec   [96]
        public double ADSPacketRate;        // pkt/sec   [97]
        public double ECUPacketRate;        // pkt/sec   [98]
        public double FSPacketRate;         // pkt/sec   [99]
        public double RAPacketRate;         // pkt/sec   [100]
        public double PLPacketRate;         // pkt/sec   [101]
        public double IPSUPacketRate;       // pkt/sec   [102]
        public double IFCCPacketRate;       // pkt/sec   [103]
        public byte   CRC_Byte1;            // flags     [104]
        public byte   CRC_Byte2;            // flags     [105]
        public double SBUSPacketRate;       // pkt/sec   [106]
        public double DGPSCorrRate;         // pkt/sec   [107]
        public double DGPS7Packet7Counter;  //           [108]
        public double DGPS7Packet7Number;   //           [109]
    }

    // ── Decoder ───────────────────────────────────────────────────────────
    public static class TelemetryDecoder
    {
        private const int LR = 110;  // Low Rate start offset (sgsim: pkt[110..159] = 50 B)

        /// <summary>
        /// Decodes all engineering-unit values from a validated 164-byte packet.
        /// Call this after PacketReceiver confirms CRC is good.
        /// </summary>
        public static TelemetryData Decode(byte[] pkt)
        {
            var d = new TelemetryData();
            d.PacketNumber = pkt[3];

            DecodeFullRate(pkt, d);

            if (d.PacketNumber % 2 == 1)
                DecodeHalfRate_Frame1(pkt, d);
            else
                DecodeHalfRate_Frame2(pkt, d);

            switch (d.PacketNumber)
            {
                case 1: DecodeLowRate_Frame1(pkt, d); break;
                case 2: DecodeLowRate_Frame2(pkt, d); break;
                case 3: DecodeLowRate_Frame3(pkt, d); break;
                case 4: DecodeLowRate_Frame4(pkt, d); break;
                case 5: DecodeLowRate_Frame5(pkt, d); break;
                case 6: DecodeLowRate_Frame6(pkt, d); break;
                case 7: DecodeLowRate_Frame7(pkt, d); break;
                case 8: DecodeLowRate_Frame8(pkt, d); break;
            }

            return d;
        }

        // ── Full Rate [4-61] ──────────────────────────────────────────────
        private static void DecodeFullRate(byte[] p, TelemetryData d)
        {
            d.FCC7Time           = U(p,  4, 4,      0,  262144);
            d.Roll7Rate          = U(p,  8, 2,   -400,     400);
            d.Pitch7Rate         = U(p, 10, 2,   -400,     400);
            d.Yaw7Rate           = U(p, 12, 2,   -400,     400);
            d.X7Acceleration     = U(p, 14, 2,   -100,     100);
            d.Y7Acceleration     = U(p, 16, 2,   -100,     100);
            d.Z7Acceleration     = U(p, 18, 2,   -100,     100);
            d.Inner_SB_Ail_Com   = U(p, 20, 1,    -20,      20);
            d.Inner_SB_Ail_Pos   = U(p, 21, 1,    -20,      20);
            d.Outer_SB_Ail_Com   = U(p, 22, 1,    -20,      20);
            d.Outer_SB_Ail_Pos   = U(p, 23, 1,    -20,      20);
            d.Inner_Port_Ail_Com = U(p, 24, 1,    -20,      20);
            d.Inner_Port_Ail_Pos = U(p, 25, 1,    -20,      20);
            d.Outer_Port_Ail_Com = U(p, 26, 1,    -20,      20);
            d.Outer_Port_Ail_Pos = U(p, 27, 1,    -20,      20);
            d.Top_SB_RV_Com      = U(p, 28, 1,    -20,      20);
            d.Top_SB_RV_Pos      = U(p, 29, 1,    -20,      20);
            d.Bottom_SB_RV_Com   = U(p, 30, 1,    -20,      20);
            d.Bottom_SB_RV_Pos   = U(p, 31, 1,    -20,      20);
            d.Top_Port_RV_Com    = U(p, 32, 1,    -20,      20);
            d.Top_Port_RV_Pos    = U(p, 33, 1,    -20,      20);
            d.Bottom_Port_RV_Com = U(p, 34, 1,    -20,      20);
            d.Bottom_Port_RV_Pos = U(p, 35, 1,    -20,      20);
            d.NW_Com             = U(p, 36, 1,    -20,      20);
            d.NW_Pos             = U(p, 37, 1,    -20,      20);
            d.Roll7Angle         = U(p, 38, 2,   -180,     180);
            d.Pitch7Angle        = U(p, 40, 2,   -180,     180);
            d.Angle7of7Attack    = U(p, 42, 1,    -25,      25);
            d.Sideslip7Angle     = U(p, 43, 1,    -25,      25);
            d.Phi_Com            = U(p, 44, 1,    -45,      45);
            d.LOS_Azimuth        = U(p, 45, 2,      0,     360);
            d.LOS_Elev           = U(p, 47, 2,    -90,      90);
            d.FoV                = U(p, 49, 2,      0,      60);
            d.Batt_Volts         = U(p, 51, 1,      0,      40);
            d.Alt_Volts          = U(p, 52, 1,      0,      40);
            d.AltCurrent         = U(p, 53, 1,      0,      50);
            d.BatCurrent         = U(p, 54, 1,      0,      50);
            d.FCC_Clock          = U(p, 55, 4,      0, 429496729.6);
            d.Q_Cmd              = U(p, 59, 1,   -100,     100);
            d.ZAccel_Cmd         = U(p, 60, 1,    -50,      50);
            d.Theta_Cmd          = U(p, 61, 1,    -45,      45);
        }

        // ── Half Rate Frame 1 [72-109] — packets 1, 3, 5, 7 ─────────────
        // sgsim copies HalfRate1Buff[0..37] → pkt[72..109] (38 B).
        // ICD showed HR1 at pkt[62]; sgsim is +10 bytes later.
        // Slew_Rate_Alt_Com is 2 B in sgsim (buff[28..29] = pkt[100..101]);
        // pkt[97] (buff[25]) is a spare byte always = 0.
        private static void DecodeHalfRate_Frame1(byte[] p, TelemetryData d)
        {
            d.Heading           = U(p,  72, 2,     0,    360);
            d.Latitude          = U(p,  74, 4,   -90,     90);
            d.Longitude         = U(p,  78, 4,  -180,    180);
            d.CAS               = U(p,  82, 1,     0,    100);
            d.PS                = U(p,  83, 2, 22600, 108000);
            d.PD                = U(p,  85, 1,     0,   6400);
            d.TAS               = U(p,  86, 1,     0,    100);
            d.Hp                = U(p,  87, 2, -1000,  11000);
            d.Throttle_com      = U(p,  89, 1,     0,      1);
            d.Throttle_pos      = U(p,  90, 1,     0,      1);
            d.Radar_Alt         = U(p,  91, 2,     0,    100);
            d.Psi_Com           = U(p,  93, 2,     0,    360);
            d.CAS_Com           = U(p,  95, 1,     0,    100);
            d.Alt_Com           = U(p,  96, 1,     0,  11000);
            // pkt[97] = spare (0 always)
            d.AGL               = U(p,  98, 2,     0,   1000);
            d.Slew_Rate_Alt_Com = U(p, 100, 2,     0,  11000); // 2 B in sgsim
        }

        // ── Half Rate Frame 2 [72-109] — packets 2, 4, 6, 8 ─────────────
        // sgsim copies HalfRate2Buff[0..37] → pkt[72..109]; all offsets +10.
        // Note: sgsim GCS_Ail/Elev/Rudd/NW commands encoded as -15..15 deg
        //       (scale 8.5) but ICD range is -20..20; values are correct in sign
        //       and approximate magnitude (+/-15° maps to +/-12°, not +/-20°).
        private static void DecodeHalfRate_Frame2(byte[] p, TelemetryData d)
        {
            d.Servo01Current    = U(p, 72, 1, 0, 3);
            d.Servo02Current    = U(p, 73, 1, 0, 3);
            d.Servo03Current    = U(p, 74, 1, 0, 3);
            d.Servo04Current    = U(p, 75, 1, 0, 3);
            d.Servo05Current    = U(p, 76, 1, 0, 3);
            d.Servo06Current    = U(p, 77, 1, 0, 3);
            d.Servo07Current    = U(p, 78, 1, 0, 3);
            d.Servo08Current    = U(p, 79, 1, 0, 3);
            d.Servo09Current    = U(p, 80, 1, 0, 3);
            d.Servo10Current    = U(p, 81, 1, 0, 3);
            d.OffTrack          = U(p, 82, 2, -1000, 1000);
            d.GCS_Ail_Cmd       = U(p, 84, 1,   -20,   20);
            d.GCS_Elev_Cmd      = U(p, 85, 1,   -20,   20);
            d.GCS_Rudd_Cmd      = U(p, 86, 1,   -20,   20);
            d.GCS_Throttle_Cmd  = U(p, 87, 1,     0,    1);
            d.GCS_Brake_Cmd     = U(p, 88, 1,     0,    1);
            d.GCS_NW_Cmd        = U(p, 89, 1,   -20,   20);
            d.GCS_Roll_Cmd      = U(p, 90, 1,   -45,   45);
            d.MC_Elev_Cmd       = U(p, 91, 1,   -20,   20);
            d.MC_Ail_Cmd        = U(p, 92, 1,   -20,   20);
            d.MC_Rudd_Cmd       = U(p, 93, 1,   -20,   20);
            d.MC_Throt_Cmd      = U(p, 94, 1,     0,    1);
            d.GPS_Alt           = U(p, 95, 2, -1000, 11000);
            d.SystemStatusByte1 = p[97];
            d.SystemStatusByte2 = p[98];
            d.OffGlide          = U(p, 99, 1,  -100,  100);
        }

        // ── Low Rate Frame 1 [110+] — packet 1 ──────────────────────────
        private static void DecodeLowRate_Frame1(byte[] p, TelemetryData d)
        {
            d.Mach                 = U(p, LR+0,  1,     0,     0.3);
            d.Static7Air7Temp      = U(p, LR+1,  1,   -60,      70);
            d.Total7Air7Temp       = U(p, LR+2,  1,   -60,      70);
            d.X_mag                = U(p, LR+3,  1,    -2,       2);
            d.Y_mag                = U(p, LR+4,  1,    -2,       2);
            d.Z_mag                = U(p, LR+5,  1,    -2,       2);
            d.Mag_heading          = U(p, LR+6,  1,     0,     360);
            d.Yaw7Rate7Offset      = U(p, LR+7,  1,    -3,       3);
            d.Roll7Rate7Offset     = U(p, LR+8,  1,    -3,       3);
            d.Pitch7Rate7Offset    = U(p, LR+9,  1,    -3,       3);
            d.Pressure7Alt7Offset  = U(p, LR+10, 1,  -200,     200);
            d.Engine_RPM           = U(p, LR+11, 1,     0,    8000);
            d.Engine_RPM_filt      = U(p, LR+12, 1,     0,    8000);
            d.REFERENCE_L          = U(p, LR+13, 1,   200,     700);
            d.AIR_KXTrack          = U(p, LR+14, 1,0.0005,    0.01);
            d.AIR_Kpsi             = U(p, LR+15, 1,   0.3,     1.4);
            d.AIR_Kphi             = U(p, LR+16, 1,     0,       1);
            d.GND_KXTrack_LL       = U(p, LR+17, 1, 0.005,    0.02);
            d.GND_KXTrack_UL       = U(p, LR+18, 1,  0.01,    0.04);
            d.GND_Kpsi             = U(p, LR+19, 1,  0.05,       1);
            d.GND_Kr               = U(p, LR+20, 1, 0.005,       1);
            d.RW_Align_Dist_TD     = U(p, LR+21, 2,  1000,   15000);
            d.RW_Align_ALT         = U(p, LR+23, 1,     0,    1000);
            d.GST_Start_Dist_TD    = U(p, LR+24, 1,   100,   10000);
            d.GST_Start_ALT        = U(p, LR+25, 1,     0,    1000);
            d.FLARE_Start_Dist_TD  = U(p, LR+26, 1,     0,    1000);
            d.FLARE_Start_ALT      = U(p, LR+27, 1,     0,     100);
            d.RW_EndOne_Dist_TD    = U(p, LR+28, 1,     0,    1000);
            d.RW_EndTwo_Dist_TD    = U(p, LR+29, 1,     0,    3000);
        }

        // ── Low Rate Frame 2 [110+]   — packet 2 ─────────────────────────
        private static void DecodeLowRate_Frame2(byte[] p, TelemetryData d)
        {
            d.North7Speed          = U(p, LR+0,  1, -100,  100);
            d.East7Speed           = U(p, LR+1,  1, -100,  100);
            d.Up7Speed             = U(p, LR+2,  1, -100,  100);
            d.D2Go                 = U(p, LR+3,  2,    0,  250);
            d.Course7Bearing       = U(p, LR+5,  1,    0,  360);
            d.Required7Bearing     = U(p, LR+6,  1,    0,  360);
            // LR+7 = Spare
            d.D2Go2TouchDownP      = U(p, LR+8,  1,    0, 10000);
            d.Dist_Travelled       = U(p, LR+9,  2,    0, 10000);
            // LR+11 = Spare
            d.dPsiAtDestWP         = U(p, LR+12, 1,    0,   360);
            d.Distance7From7Base   = U(p, LR+13, 2,    0,   300);
            d.NW_CMD_LIMIT         = U(p, LR+15, 1,  0.4,     1);
            // LR+16 = Spare
            d.AIR_Kh               = U(p, LR+17, 1, 0.005,    1);
            d.KH_GST               = U(p, LR+18, 1, 0.005, 0.06);
            d.Kp                   = U(p, LR+19, 1, 0.005,  0.5);
            d.K_NL_RollControl     = U(p, LR+20, 1,  0.01,  1.5);
            d.Lambda_NL_RollControl= U(p, LR+21, 1,     1,   15);
            d.KIPHI                = U(p, LR+22, 1,     0, 0.05);
            d.KIH_Enroute          = U(p, LR+23, 1,     0, 0.006);
            d.KIH_GST              = U(p, LR+24, 1,     0, 0.006);
            d.KTHETA               = U(p, LR+25, 1,   0.1,  0.8);
            d.KQ                   = U(p, LR+26, 1,  0.01,  0.1);
            d.ROC_CMD_Limit        = U(p, LR+27, 1,     1,   10);
        }

        // ── Low Rate Frame 3 [110+]   — packet 3 ─────────────────────────
        private static void DecodeLowRate_Frame3(byte[] p, TelemetryData d)
        {
            d.Next7Course7Bearing        = U(p, LR+0,  1,    0,  360);
            d.Next7WayPoint7Number       = U(p, LR+1,  1,    0,  255);
            d.Prev7WayPoint7Number       = U(p, LR+2,  1,    0,  255);
            d.Turning7Dist2WP            = U(p, LR+3,  1,    0,   10);
            d.Curve7Guidance7Angle       = U(p, LR+4,  1,    0,  360);
            d.Turn7Radius                = U(p, LR+5,  1,    0,   10);
            d.Time7Since7TakeOff         = U(p, LR+6,  2,    0, 86400);
            d.MCFlagByte1                = p[LR+8];
            d.MCFlagByte2                = p[LR+9];
            d.MCFlagByte3                = p[LR+10];
            d.Flight7Phase               = U(p, LR+11, 1,    0,  255);
            d.Flight7Phase7Change7Reason = U(p, LR+12, 1,    0,  255);
            d.WakeUp7Miss                = U(p, LR+13, 1,    0,  255);
            d.Differential7Age           = U(p, LR+14, 1,    0,  100);
            d.Height7Delta               = U(p, LR+15, 2,-1000, 1000);
            d.Speed7Delta                = U(p, LR+17, 1,  -50,   50);
            d.ROC_Cmd_AP                 = U(p, LR+18, 1,  -50,   50);
            d.ROC_Estimated              = U(p, LR+19, 1, -100,  100);
            d.Pitch7Rate7AftLead         = U(p, LR+20, 1, -100,  100);
            d.H_err                      = U(p, LR+21, 1,-1000, 1000);
        }

        // ── Low Rate Frame 4 [110+]   — packet 4 ─────────────────────────
        private static void DecodeLowRate_Frame4(byte[] p, TelemetryData d)
        {
            d.MCFlagByte4           = p[LR+0];
            d.MCFlagByte5           = p[LR+1];
            d.SysID7Status7Byte     = p[LR+2];
            d.System7Status7Byte3   = p[LR+3];
            d.Sensors7Current       = U(p, LR+4,  1,  0,  10);
            d.PFCU7Current_LR4      = U(p, LR+5,  1,  0,  10);
            d.BFCU7Current          = U(p, LR+6,  1,  0,  10);
            d.Current5V             = U(p, LR+7,  1,  0,  10);
            d.Current12V            = U(p, LR+8,  1,  0,  10);
            d.Pitot7Heater7Current  = U(p, LR+9,  1,  0,  20);
            d.Payload7Current       = U(p, LR+10, 1,  0,  20);
            d.DLPA7Current          = U(p, LR+11, 1,  0,  10);
            d.Sensor7Volts          = U(p, LR+12, 1,  0,  40);
            d.Brake7Servo7Pos       = U(p, LR+13, 1,  0,   1);
            d.NLG7Pos               = U(p, LR+14, 1,  0,   1);
            // LR+15,16 = 2-byte spare
            d.Abort7Taxi7Reason     = U(p, LR+17, 1,  0,  50);
            d.Abort7Landing7Reason  = U(p, LR+18, 1,  0,  50);
            d.Turn7Compensation     = U(p, LR+19, 1, -5,   5);
        }

        // ── Low Rate Frame 5 [110+]   — packet 5 ─────────────────────────
        private static void DecodeLowRate_Frame5(byte[] p, TelemetryData d)
        {
            d.PFCU7Volts        = U(p, LR+0,  1,   0,  40);
            d.Servo7Volts       = U(p, LR+1,  1,   0,  40);
            d.Volts12V          = U(p, LR+2,  1,   0,  25);
            d.Volts5V           = U(p, LR+3,  1,   0,  20);
            d.Payload7Volts     = U(p, LR+4,  1,   0,  40);
            d.IPSU7Temp         = U(p, LR+5,  1, -60, 100);
            d.Relay7Status7Byte1= p[LR+6];
            d.Relay7Status7Byte2= p[LR+7];
            d.Relay7Status7Byte3= p[LR+8];
            d.IPSU7Status7Byte1 = p[LR+9];
            d.IPSU7Status7Byte2 = p[LR+10];
            d.IPSU_Cmd_FBByte   = p[LR+11];
            d.Heading_Cmd       = U(p, LR+12, 1,   0, 360);
            d.CPU7Temp          = U(p, LR+13, 1, -60, 100);
            d.Solution7Status   = U(p, LR+14, 1,   0,  50);
            d.Satellites        = U(p, LR+15, 1,   0,  50);
            d.BITStatus         = p[LR+16];
            d.GCSEchoTime       = U(p, LR+17, 4,   0, 4294967296);
        }

        // ── Low Rate Frame 6 [110+]   — packet 6 ─────────────────────────
        private static void DecodeLowRate_Frame6(byte[] p, TelemetryData d)
        {
            d.GCS_Alt_Cmd         = U(p, LR+0,  1,    0,  10000);
            d.GCS_CAS_Cmd         = U(p, LR+1,  1,    0,    100);
            d.Injection7Time      = U(p, LR+2,  4,    0, 4294967296);
            d.Battery7Voltage_ECU = U(p, LR+6,  1,    0,     20);
            d.CHT7Left            = U(p, LR+7,  1,    0,    200);
            d.CHT7Right           = U(p, LR+8,  1,    0,    200);
            d.EGT7Left            = U(p, LR+9,  1,    0,    200);
            d.EGT7Right           = U(p, LR+10, 1,    0,    200);
            d.Fuel7Pressure       = U(p, LR+11, 2,    0,  65535);
            d.FuelLevel_Main      = U(p, LR+13, 1,    0,      1);
            d.FuelLevel_Left      = U(p, LR+14, 1,    0,      1);
            d.FuelLevel_Right     = U(p, LR+15, 1,    0,      1);
            d.Lat_Std             = U(p, LR+16, 1,    0,     10);
            d.Lon_Std             = U(p, LR+17, 1,    0,     10);
            d.Alt_Std             = U(p, LR+18, 1,    0,     10);
            d.Latitude7DeadReck   = U(p, LR+19, 4,  -90,     90);
            d.Longitude7DeadReck  = U(p, LR+23, 4, -180,    180);
        }

        // ── Low Rate Frame 7 [110+]   — packet 7 ─────────────────────────
        private static void DecodeLowRate_Frame7(byte[] p, TelemetryData d)
        {
            d.Fuel7Flow7Rate          = U(p, LR+0,  1,    0,   20);
            d.MC_Brake_Cmd            = U(p, LR+1,  1,    0,    1);
            d.FailSafeContFlagByte    = p[LR+2];
            d.FCC7Temp                = U(p, LR+3,  1,  -50,  100);
            d.SerChTOByte1            = p[LR+4];
            d.SerChTOByte2            = p[LR+5];
            d.CmdMissPacketsCount     = U(p, LR+6,  1,    0,  255);
            d.GCS_VCMissPacketsCount  = U(p, LR+7,  1,    0,  255);
            d.GCS_PLMissPacketsCount  = U(p, LR+8,  1,    0,  255);
            d.VGMissPacketsCount      = U(p, LR+9,  1,    0,  255);
            d.GPSMissPacketsCount     = U(p, LR+10, 1,    0,  255);
            d.MagMissPacketsCount     = U(p, LR+11, 1,    0,  255);
            d.GCS_MPMissPacketsCount  = U(p, LR+12, 1,    0,  255);
            d.ADSMissPacketsCount     = U(p, LR+13, 1,    0,  255);
            d.ECUMissPacketsCount     = U(p, LR+14, 1,    0,  255);
            d.FSMissPacketsCount      = U(p, LR+15, 1,    0,  255);
            d.RAMissPacketsCount      = U(p, LR+16, 1,    0,  255);
            d.PLMissPacketsCount      = U(p, LR+17, 1,    0,  255);
            d.IPSUMissPacketsCount    = U(p, LR+18, 1,    0,  255);
            d.IFCCMissPacketsCount    = U(p, LR+19, 1,    0,  255);
            d.SBUSMissPacketsCount    = U(p, LR+20, 1,    0,  255);
            d.DGPS_Corr_MPC           = U(p, LR+21, 1,    0,  255);
            d.Telemetry_MPC           = U(p, LR+22, 1,    0,  255);
            d.MiscFlags1              = p[LR+23];
            d.Loiter7Duration         = U(p, LR+24, 1,    0,  600);
            d.Loiter7Timer            = U(p, LR+25, 1,    0,  600);
            d.Nspeed7DeadReck         = U(p, LR+26, 1, -100,  100);
            d.Espeed7DeadReck         = U(p, LR+27, 1, -100,  100);
            d.MiscFlags2              = p[LR+28];
            d.GCS_Packet_Number       = U(p, LR+29, 1,    0,  255);
        }

        // ── Low Rate Frame 8 [110+]   — packet 8 ─────────────────────────
        private static void DecodeLowRate_Frame8(byte[] p, TelemetryData d)
        {
            d.CmdPacketRate         = U(p, LR+0,  1,    0,  255);
            d.VGPacketRate          = U(p, LR+1,  1,    0,  255);
            d.GPSPacketRate         = U(p, LR+2,  1,    0,  255);
            d.MagPacketRate         = U(p, LR+3,  1,    0,  255);
            d.GCS_VCPacketRate      = U(p, LR+4,  1,    0,   50);
            d.GCS_MPPacketRate      = U(p, LR+5,  1,    0,   50);
            d.GCS_PLPacketRate      = U(p, LR+6,  1,    0,   50);
            d.ADSPacketRate         = U(p, LR+7,  1,    0, 1000);
            d.ECUPacketRate         = U(p, LR+8,  1,    0,  255);
            d.FSPacketRate          = U(p, LR+9,  1,    0,  255);
            d.RAPacketRate          = U(p, LR+10, 1,    0,  255);
            d.PLPacketRate          = U(p, LR+11, 1,    0,  255);
            d.IPSUPacketRate        = U(p, LR+12, 1,    0,  255);
            d.IFCCPacketRate        = U(p, LR+13, 1,    0,  255);
            d.CRC_Byte1             = p[LR+14];
            d.CRC_Byte2             = p[LR+15];
            d.SBUSPacketRate        = U(p, LR+16, 1,    0,  255);
            d.DGPSCorrRate          = U(p, LR+17, 1,    0,   10);
            d.DGPS7Packet7Counter   = U(p, LR+18, 1,    0,  255);
            d.DGPS7Packet7Number    = U(p, LR+19, 1,    0,  255);
        }

        // ── Scale helper ──────────────────────────────────────────────────
        // Reads n bytes (little-endian unsigned) and converts to engineering units.
        // Formula: EngValue = Raw / MaxRaw * (maxEng - minEng) + minEng
        private static double U(byte[] p, int offset, int n, double minEng, double maxEng)
        {
            ulong raw = 0;
            for (int i = 0; i < n; i++)
                raw |= (ulong)p[offset + i] << (8 * i);
            double maxRaw = (1UL << (n * 8)) - 1;
            return raw / maxRaw * (maxEng - minEng) + minEng;
        }
    }
}
