using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

// ============================================================
//  Form2.cs  –  GCS_240626  Secondary Display
//  Screen  : 1920 x 1080  (monitor 2, maximized)
//
//  Layout
//  ──────────────────────────────────────────────────────────
//  ┌─[AutoP1][AutoP2][Engine][IPSU][Sen1][Sen2][Navigation][MC]─┐
//  │  NAME (dark-red)  │  VALUE (striped rows, scrollable)      │
//  │  ...              │  ...                                   │
//  ├───────────────────────────┬────────────────────────────────┤
//  │  5 rows  NAME │ VALUE     │  5 rows  NAME │ VALUE          │
//  └───────────────────────────┴────────────────────────────────┘
// ============================================================
namespace GCS_240626
{
    public class Form2 : Form
    {
        // ── Row metrics ───────────────────────────────────────────────────
        private const int ROW_H = 24;
        private const int ROW_STRIDE = 26;   // ROW_H + 2 px gap

        // ── Column widths ─────────────────────────────────────────────────
        // Column widths calculated at runtime from actual font metrics (see BuildTabContent)
        private static int TAB_NW;       // NAME  column — exactly 20 chars of Consolas 12 Bold
        private static int TAB_VW;       // VALUE column — exactly 12 chars of Consolas 12 Bold
        private static int TAB_ROW_W;    // total = TAB_NW + 1 + TAB_VW
        private const int BOT_NW = 220;   // NAME column in bottom strip

        // ── Tab content max visible height (1080p minus title/tabs/strip overhead) ──
        private const int TAB_MAX_H = 850;

        // ── Bottom strip ──────────────────────────────────────────────────
        private const int BOT_ROWS = 5;
        private const int BOT_H = BOT_ROWS * ROW_STRIDE + 10;   // ~140 px

        // ── Colours ───────────────────────────────────────────────────────
        private static readonly Color LIME = Color.Lime;
        private static readonly Color BLACK = Color.Black;
        private static readonly Color WHITE = Color.White;
        private static readonly Color NAME_BG = Color.FromArgb(80, 0, 0);   // dark red
        private static readonly Color HDR_BG = Color.FromArgb(100, 0, 0);
        private static readonly Color ROW_EVEN = Color.FromArgb(15, 15, 15);
        private static readonly Color ROW_ODD = Color.FromArgb(38, 38, 38);
        private static readonly Color SEP_CLR = Color.FromArgb(0, 100, 0);

        // ── Fonts ─────────────────────────────────────────────────────────
        private static readonly Font FNT14 = new Font("Consolas", 14f, FontStyle.Bold);
        private static readonly Font FNT12 = new Font("Consolas", 12f, FontStyle.Bold);
        private static readonly Font FNT11 = new Font("Consolas", 11f, FontStyle.Bold);
        private static readonly Font FNT10 = new Font("Consolas", 10f, FontStyle.Bold);
        private static readonly Font FNT8B = new Font("Consolas",  8f, FontStyle.Bold);   // Flags tab

        // ── Live value labels — keyed by ICD variable name ────────────────
        private readonly Dictionary<string, Label> _valueLabels = new Dictionary<string, Label>();

        // ── Packet-rate labels — keyed by TelemetryData field name ──────────
        private readonly Dictionary<string, Label> _rateLabels = new Dictionary<string, Label>();

        // ── Flag bit labels — keyed by "ByteName.BitName" ────────────────────
        private readonly Dictionary<string, Label> _flagBitLabels = new Dictionary<string, Label>();

        // ── Tab control — stored so BeginUpdate/EndUpdate can target the selected page ──
        private TabControl _tc;

        // ── Live plot panel — 600×400, always visible, right side ─────────────────
        private const int PLOT_W       = 600;
        private const int PLOT_H       = 400;
        private const int PLOT_MAX_PTS = 500;
        private Panel   _plotPanel;
        private Button  _plotToggleBtn;          // always visible; toggles plot panel
        private Chart[] _charts        = new Chart[5];
        private Label[] _chartLabels   = new Label[5];
        private string[] _plotVarSel   = { "Roll7Angle", "Pitch7Angle", "CAS", "AGL", "Yaw7Rate" };
        private Panel   _selectorPanel;
        private ToolTip _chartTip      = new ToolTip { AutomaticDelay = 0, AutoPopDelay = 4000, InitialDelay = 0, ReshowDelay = 0 };

        private static readonly string[][] PLOT_CATS = {
            new[] { "Roll7Rate", "Pitch7Rate", "Yaw7Rate" },
            new[] { "Roll7Angle", "Pitch7Angle", "Angle7of7Attack", "Sideslip7Angle" },
            new[] { "CAS", "TAS", "Hp", "AGL", "Radar_Alt" },
            new[] { "Inner_SB_Ail_Com", "Inner_SB_Ail_Pos", "NW_Com", "NW_Pos",
                    "Throttle_com", "Throttle_pos" },
            new[] { "Phi_Com", "Psi_Com", "CAS_Com", "Alt_Com", "Theta_Cmd" },
        };
        private static readonly string[] PLOT_CAT_NAMES =
            { "RATES", "ANGLES", "AERO", "CONTROLS", "COMMANDS" };
        private static readonly Color[] PLOT_COLORS = {
            Color.FromArgb(0,  255, 136),   // P1 – green
            Color.FromArgb(136,170, 255),   // P2 – blue
            Color.FromArgb(255,170,   0),   // P3 – amber
            Color.FromArgb(255,102, 136),   // P4 – pink
            Color.FromArgb(  0,204, 255),   // P5 – cyan
        };
        private static readonly Dictionary<string, FieldInfo> _fiCache =
            new Dictionary<string, FieldInfo>();

        // ── ICD variable lists per tab ────────────────────────────────────
        private static readonly string[] VARS_AUTOP1 = {
            "FCC7Time","FCC_Clock","Roll7Rate","Pitch7Rate","Yaw7Rate",
            "Roll7Angle","Pitch7Angle","Angle7of7Attack","Sideslip7Angle",
            "Phi_Com","Psi_Com","CAS_Com","Alt_Com","Slew_Rate_Alt_Com",
            "Throttle_com","Throttle_pos","AGL","Q_Cmd","ZAccel_Cmd","Theta_Cmd",
            "ROC_Cmd_AP","ROC_Estimated","H_err","Height7Delta","Speed7Delta",
            "Pitch7Rate7AftLead","Flight7Phase","Flight7Phase7Change7Reason",
            "WakeUp7Miss","Turn7Compensation"
        };
        private static readonly string[] VARS_AUTOP2 = {
            "REFERENCE_L","AIR_KXTrack","AIR_Kpsi","AIR_Kphi",
            "GND_KXTrack_LL","GND_KXTrack_UL","GND_Kpsi","GND_Kr",
            "AIR_Kh","KH_GST","Kp","K_NL_RollControl","Lambda_NL_RollControl",
            "KIPHI","KIH_Enroute","KIH_GST","KTHETA","KQ",
            "ROC_CMD_Limit","NW_CMD_LIMIT","Turn7Radius","dPsiAtDestWP",
            "Loiter7Duration","Loiter7Timer","LOS_Azimuth"
        };
        private static readonly string[] VARS_ENGINE = {
            "Engine_RPM","Engine_RPM_filt","Injection7Time","Fuel7Flow7Rate",
            "CHT7Left","CHT7Right","EGT7Left","EGT7Right",
            "Fuel7Pressure","FuelLevel_Main","FuelLevel_Left","FuelLevel_Right",
            "Mach","Battery7Voltage_ECU","FCC7Temp"
        };
        private static readonly string[] VARS_IPSU = {
            "PFCU7Volts","Servo7Volts","Volts12V","Volts5V","Payload7Volts",
            "IPSU7Temp","Sensor7Volts","IPSU7Status7Byte1","IPSU7Status7Byte2","IPSU_Cmd_FBByte",
            "Relay7Status7Byte1","Relay7Status7Byte2","Relay7Status7Byte3",
            "Batt_Volts","Alt_Volts","AltCurrent","BatCurrent",
            "Sensors7Current","PFCU7Current_LR4","BFCU7Current",
            "Current5V","Current12V","Pitot7Heater7Current","Payload7Current","DLPA7Current",
            "Servo01Current","Servo02Current","Servo03Current","Servo04Current","Servo05Current",
            "Servo06Current","Servo07Current","Servo08Current","Servo09Current","Servo10Current",
            "SystemStatusByte1","SystemStatusByte2","SysID7Status7Byte","System7Status7Byte3","BITStatus",
            "GCSEchoTime","CPU7Temp","Heading_Cmd",
            "GCS_Alt_Cmd","GCS_CAS_Cmd","GCS_Ail_Cmd","GCS_Elev_Cmd","GCS_Rudd_Cmd",
            "GCS_Throttle_Cmd"
        };
        private static readonly string[] VARS_SEN1 = {
            "X7Acceleration","Y7Acceleration","Z7Acceleration",
            "X_mag","Y_mag","Z_mag","Mag_heading",
            "Yaw7Rate7Offset","Roll7Rate7Offset","Pitch7Rate7Offset","Pressure7Alt7Offset",
            "PS","PD","TAS","CAS","Hp","Radar_Alt","Static7Air7Temp"
        };
        private static readonly string[] VARS_SEN2 = {
            "Heading","Latitude","Longitude","GPS_Alt",
            "North7Speed","East7Speed","Up7Speed",
            "Latitude7DeadReck","Longitude7DeadReck",
            "Nspeed7DeadReck","Espeed7DeadReck",
            "Differential7Age","Lat_Std","Lon_Std","Alt_Std",
            "Solution7Status","Satellites","Total7Air7Temp"
        };
        private static readonly string[] VARS_NAV = {
            "Course7Bearing","Required7Bearing","Next7Course7Bearing",
            "Next7WayPoint7Number","Prev7WayPoint7Number",
            "Turning7Dist2WP","Curve7Guidance7Angle",
            "D2Go","D2Go2TouchDownP","Dist_Travelled",
            "Distance7From7Base","Time7Since7TakeOff"
        };
        private static readonly string[] VARS_MC = {
            "MC_Ail_Cmd","MC_Elev_Cmd","MC_Rudd_Cmd","MC_Throt_Cmd","MC_Brake_Cmd",
            "MCFlagByte1","MCFlagByte2","MCFlagByte3","MCFlagByte4","MCFlagByte5"
        };

        // ── Flag bit definitions (ByteFieldName, DisplayName, BitMask, IsMultiBit) ──
        // b1=LSB(0x01) .. b8=MSB(0x80)
        private static readonly (string B, string N, byte M, bool MB)[] FLAG_DEFS =
        {
            // SystemStatusByte1 (HR)
            ("SystemStatusByte1","IMU_DataValid",      0x01,false),
            ("SystemStatusByte1","GPS_Locked",         0x02,false),
            ("SystemStatusByte1","ADS_DataValid",      0x04,false),
            ("SystemStatusByte1","CMD_Link",           0x08,false),
            ("SystemStatusByte1","ECU_DataValid",      0x10,false),
            ("SystemStatusByte1","FailSafe_DataValid", 0x20,false),
            ("SystemStatusByte1","InterFCC_DataValid", 0x40,false),
            ("SystemStatusByte1","IPSU_DataValid",     0x80,false),
            // SystemStatusByte2 (HR)
            ("SystemStatusByte2","Magneto_DataValid",       0x01,false),
            ("SystemStatusByte2","PL_DataValid",            0x02,false),
            ("SystemStatusByte2","RA_DataValid",            0x04,false),
            ("SystemStatusByte2","ServoFB_DataValid",       0x08,false),
            ("SystemStatusByte2","OffsetsCalcDone",         0x10,false),
            ("SystemStatusByte2","Manual_Rx_Fail",          0x20,false),
            ("SystemStatusByte2","DGPS_Corr_DataValid",     0x40,false),
            ("SystemStatusByte2","In_Flight_Mode",          0x80,false),
            // MCFlagByte1 (LR3)
            ("MCFlagByte1","StartCurveGuidance",0x01,false),
            ("MCFlagByte1","LAPointHit",        0x02,false),
            ("MCFlagByte1","RW_Alignment_Flag", 0x04,false),
            ("MCFlagByte1","Deploy_chute",      0x08,false),
            ("MCFlagByte1","Engine_Kill",       0x10,false),
            ("MCFlagByte1","Deploy_Gear",       0x20,false),
            ("MCFlagByte1","FCC_Active",        0x40,false),
            ("MCFlagByte1","Go2NextWayPt",      0x80,false),
            // MCFlagByte2 (LR3)
            ("MCFlagByte2","Go2PrevWayPt",        0x01,false),
            ("MCFlagByte2","MissionWPDummyLand",  0x02,false),
            ("MCFlagByte2","Loiter[b3-5]",        0x1C,true),
            ("MCFlagByte2","Search",              0x20,false),
            ("MCFlagByte2","Dash",                0x40,false),
            ("MCFlagByte2","Descend",             0x80,false),
            // MCFlagByte3 (LR3)
            ("MCFlagByte3","Ascend",              0x01,false),
            ("MCFlagByte3","Return2Base",         0x02,false),
            ("MCFlagByte3","InitLandingApproach", 0x04,false),
            ("MCFlagByte3","Mode[b4-7]",          0x78,true),
            ("MCFlagByte3","SelfTest",            0x80,false),
            // MCFlagByte4 (LR4)
            ("MCFlagByte4","ready2TakeOff",    0x01,false),
            ("MCFlagByte4","TakeOffDone",      0x02,false),
            ("MCFlagByte4","ClimbOutDone",     0x04,false),
            ("MCFlagByte4","Override_Ext",     0x08,false),
            ("MCFlagByte4","UseCurveGuidance", 0x10,false),
            ("MCFlagByte4","InitPitchIDCmd",   0x20,false),
            ("MCFlagByte4","MissionWP1_HIT",   0x40,false),
            ("MCFlagByte4","AltitudeHold",     0x80,false),
            // MCFlagByte5 (LR4)
            ("MCFlagByte5","LatGuidScheme[b1-2]",      0x03,true),
            ("MCFlagByte5","EnableGainTuning_GCS",     0x04,false),
            ("MCFlagByte5","EnableStabilityAug_GCS",   0x08,false),
            ("MCFlagByte5","Wing_Leveler",             0x10,false),
            ("MCFlagByte5","Use_Flaps_as_Ailerons",    0x20,false),
            ("MCFlagByte5","FlapsDown",                0x40,false),
            ("MCFlagByte5","SBUS_Flaps_Down",          0x80,false),
            // SysID7Status7Byte (LR4)
            ("SysID7Status7Byte","SySIDCmdSeq[b1-3]",  0x07,true),
            ("SysID7Status7Byte","SySIDCmd[b4-5]",     0x18,true),
            ("SysID7Status7Byte","SySIDSeqDone",       0x20,false),
            ("SysID7Status7Byte","SySIDSeqApplied",    0x40,false),
            ("SysID7Status7Byte","SySIDEnable",        0x80,false),
            // System7Status7Byte3 (LR4)
            ("System7Status7Byte3","AltComAvgDone",         0x01,false),
            ("System7Status7Byte3","SpeedComAvgDone",        0x02,false),
            ("System7Status7Byte3","DummyLandingInitiated",  0x04,false),
            ("System7Status7Byte3","Use_NL_RollControl",     0x08,false),
            ("System7Status7Byte3","Height_Ctrl_Scheme",     0x10,false),
            ("System7Status7Byte3","AirModesEnabledSwt",     0x20,false),
            ("System7Status7Byte3","Gnd_Crew_ClearanceSwt",  0x40,false),
            ("System7Status7Byte3","ExecuteSLT",             0x80,false),
            // SerChTOByte1 (LR7)
            ("SerChTOByte1","CmdTimeOutFlag",    0x01,false),
            ("SerChTOByte1","VGTimeOutFlag",     0x02,false),
            ("SerChTOByte1","GPSTimeOutFlag",    0x04,false),
            ("SerChTOByte1","MagTimeOutFlag",    0x08,false),
            ("SerChTOByte1","GCS_VCTimeOutFlag", 0x10,false),
            ("SerChTOByte1","ADSTimeOutFlag",    0x20,false),
            ("SerChTOByte1","ECUTimeOutFlag",    0x40,false),
            ("SerChTOByte1","FSTimeOutFlag",     0x80,false),
            // SerChTOByte2 (LR7)
            ("SerChTOByte2","RATimeOutFlag",        0x01,false),
            ("SerChTOByte2","PLTimeOutFlag",        0x02,false),
            ("SerChTOByte2","IPSUTimeOutFlag",      0x04,false),
            ("SerChTOByte2","IFCCTimeOutFlag",      0x08,false),
            ("SerChTOByte2","GCS_MPTimeOutFlag",    0x10,false),
            ("SerChTOByte2","GCS_PLTimeOutFlag",    0x20,false),
            ("SerChTOByte2","SBUS_SerDataTOFlag",   0x40,false),
            ("SerChTOByte2","DGPS_CorrTOFlag",      0x80,false),
            // CRC_Byte1 (LR8)
            ("CRC_Byte1","Cmd_CRCFail", 0x01,false),
            ("CRC_Byte1","VG_CRCFail",  0x02,false),
            ("CRC_Byte1","GPS_CRCFail", 0x04,false),
            ("CRC_Byte1","Mag_CRCFail", 0x08,false),
            ("CRC_Byte1","RA_CRCFail",  0x10,false),
            ("CRC_Byte1","ADS_CRCFail", 0x20,false),
            ("CRC_Byte1","SPAN_CRCFail",0x40,false),
            ("CRC_Byte1","ECU_CRCFail", 0x80,false),
            // CRC_Byte2 (LR8)
            ("CRC_Byte2","GCS_PL_CRCFail",0x01,false),
            ("CRC_Byte2","GCS_VC_CRCFail",0x02,false),
            ("CRC_Byte2","GCS_MP_CRCFail",0x04,false),
            ("CRC_Byte2","IPSU_CRCFail",  0x08,false),
            ("CRC_Byte2","IFCC_CRCFail",  0x10,false),
            ("CRC_Byte2","FS_CRCFail",    0x20,false),
            ("CRC_Byte2","PL_CRCFail",    0x40,false),
            ("CRC_Byte2","SBUS_CRCFail",  0x80,false),
            // MiscFlags1 (LR7)
            ("MiscFlags1","DGPS_Corr_Enabled", 0x01,false),
            ("MiscFlags1","DGPSMode",          0x02,false),
            ("MiscFlags1","SpeedHold",         0x04,false),
            ("MiscFlags1","SelectIncDecParam", 0x08,false),
            ("MiscFlags1","DecrementSpeedCmd", 0x10,false),
            ("MiscFlags1","IncrementSpeedCmd", 0x20,false),
            ("MiscFlags1","DGPS_Corr_CRCValid",0x40,false),
            ("MiscFlags1","Loiter_Completed",  0x80,false),
            // MiscFlags2 (LR7)
            ("MiscFlags2","Enable_Turn_Compensation",0x01,false),
            ("MiscFlags2","Enable_Logging",          0x02,false),
            ("MiscFlags2","Alt_Sensor_in_Use",       0x04,false),
            ("MiscFlags2","AGL_Sensor_in_Use[b4-5]", 0x18,true),
            ("MiscFlags2","Speed_Sensor_in_Use",     0x20,false),
            ("MiscFlags2","Disable_Telemetry",       0x40,false),
            ("MiscFlags2","Enable_PF_Estimation",    0x80,false),
        };

        // Column grouping for the Flags tab layout (4 columns)
        private static readonly string[][] FLAG_COLUMNS =
        {
            new[] { "SystemStatusByte1", "SystemStatusByte2", "MCFlagByte1" },
            new[] { "MCFlagByte2", "MCFlagByte3", "MCFlagByte4", "MCFlagByte5" },
            new[] { "SysID7Status7Byte", "System7Status7Byte3", "SerChTOByte1", "SerChTOByte2" },
            new[] { "CRC_Byte1", "CRC_Byte2", "MiscFlags1", "MiscFlags2" },
        };


        // ════════════════════════════════════════════════════════════════
        //  Constructor
        // ════════════════════════════════════════════════════════════════
        public Form2()
        {
            this.Text = "GCS_240626 — Secondary Display";
            this.BackColor = BLACK;
            this.ForeColor = LIME;
            this.Font = FNT14;
            this.FormBorderStyle = FormBorderStyle.Sizable;

            // ── Place on second monitor if available ─────────────────────
            var screens = Screen.AllScreens;
            if (screens.Length > 1)
            {
                this.StartPosition = FormStartPosition.Manual;
                this.Location = screens[1].Bounds.Location;
            }
            this.WindowState = FormWindowState.Maximized;

            // ── Measure exact column widths from font ────────────────────
            TAB_NW = TextRenderer.MeasureText(new string('W', 20), FNT10).Width;
            TAB_VW = TextRenderer.MeasureText(new string('W', 12), FNT10).Width;
            TAB_ROW_W = TAB_NW + 1 + TAB_VW;

            // Order matters for docking — bottom first, then fill
            BuildBottomStrip();
            BuildTabControl();
            BuildPlotPanel();
            this.Resize += (s, e) => LayoutMainArea();
            this.Load   += (s, e) => LayoutMainArea();
        }

        // ════════════════════════════════════════════════════════════════
        //  Bottom strip  — 5 rows left | 5 rows right
        // ════════════════════════════════════════════════════════════════
        private void BuildBottomStrip()
        {
            int halfW = TAB_NW + 1 + TAB_VW;   // NAME + sep + VALUE
            int stripW = 2 * halfW + 2;           // both halves + 2px gap
            const int B = 3;
            Color CLR = Color.FromArgb(200, 200, 200);

            string[] leftNames = { "Bot L1", "Bot L2", "Bot L3", "Bot L4", "Bot L5" };
            string[] rightNames = { "Bot R1", "Bot R2", "Bot R3", "Bot R4", "Bot R5" };

            // 10px gap between tab area and bottom strip
            this.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 10, BackColor = BLACK });

            // Full-width Dock=Bottom container (black background fills area to the right)
            var pnlOuter = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = BOT_H + 2 * B,
                BackColor = BLACK
            };

            // Fixed-width bordered box — right edge ends exactly after Bot R VALUE column
            var pnlBox = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(stripW + 2 * B, BOT_H + 2 * B),
                BackColor = CLR,          // gray = border (visible through Padding)
                Padding = new Padding(B),
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };

            // Black inner area
            var pnlInner = new Panel { Dock = DockStyle.Fill, BackColor = BLACK };

            var pnlLeft = BuildBottomHalf(leftNames, halfW);
            pnlLeft.Location = new Point(0, 0);
            pnlInner.Controls.Add(pnlLeft);

            var pnlRight = BuildBottomHalf(rightNames, halfW);
            pnlRight.Location = new Point(halfW + 2, 0);
            pnlInner.Controls.Add(pnlRight);

            pnlBox.Controls.Add(pnlInner);
            pnlOuter.Controls.Add(pnlBox);

            // Status box — starts right after the right edge of the Bot L/R box
            var statusBox = BuildStatusBox();
            statusBox.Location = new Point(stripW + 2 * B + 4, 0);
            pnlOuter.Controls.Add(statusBox);

            this.Controls.Add(pnlOuter);
        }

        private Panel BuildBottomHalf(string[] names, int halfW)
        {
            var pnl = new Panel
            {
                Size = new Size(halfW, BOT_H),
                BackColor = BLACK
            };

            for (int i = 0; i < names.Length; i++)
            {
                int y = i * ROW_STRIDE + 4;

                var row = new Panel
                {
                    Location = new Point(0, y),
                    Size = new Size(halfW, ROW_H),
                    BackColor = (i % 2 == 0) ? ROW_EVEN : ROW_ODD
                };

                row.Controls.Add(new Label
                {
                    Text = names[i],
                    Location = new Point(0, 0),
                    Size = new Size(TAB_NW, ROW_H),
                    BackColor = NAME_BG,
                    ForeColor = LIME,
                    Font = FNT10,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(4, 0, 0, 0)
                });
                row.Controls.Add(new Panel
                {
                    Location = new Point(TAB_NW, 0),
                    Size = new Size(1, ROW_H),
                    BackColor = SEP_CLR
                });
                row.Controls.Add(new Label
                {
                    Text = "----",
                    Location = new Point(TAB_NW + 6, 0),
                    Size = new Size(TAB_VW - 6, ROW_H),
                    BackColor = Color.Transparent,
                    ForeColor = WHITE,
                    Font = FNT10,
                    TextAlign = ContentAlignment.MiddleLeft
                });

                pnl.Controls.Add(row);
            }
            return pnl;
        }

        // ════════════════════════════════════════════════════════════════
        //  Status box  — packet-age indicators, 5 cols × 4 rows
        // ════════════════════════════════════════════════════════════════
        private Panel BuildStatusBox()
        {
            const int B = 3;
            Color CLR = Color.FromArgb(200, 200, 200);
            const int COLS = 5;
            const int LBL_W = 72;    // fits "GCS_VC" in Consolas 11 Bold
            const int VAL_W = 36;
            const int ITEM_W = LBL_W + 1 + VAL_W;   // 109
            const int COL_GAP = 2;
            const int PAD = 4;

            // (display label, TelemetryData field name for _rateLabels — null = no live data)
            var items = new (string Lbl, string Key)[]
            {
                ("Cmd",    "CmdPacketRate"),
                ("RA",     "RAPacketRate"),
                ("PL",     "PLPacketRate"),
                ("IPSU",   "IPSUPacketRate"),
                ("SBUS1",  "SBUSPacketRate"),
                ("VG",     "VGPacketRate"),
                ("ADS",    "ADSPacketRate"),
                ("GCS_VC", "GCS_VCPacketRate"),
                ("IFCC",   "IFCCPacketRate"),
                ("SBUS2",  null),                // no separate SBUS2 rate in telemetry
                ("GPS",    "GPSPacketRate"),
                ("ECU",    "ECUPacketRate"),
                ("GCS_MP", "GCS_MPPacketRate"),
                ("MAN_RX", null),                // not in telemetry
                ("SPAN",   null),                // not in telemetry
                ("Mag",    "MagPacketRate"),
                ("FS",     "FSPacketRate"),
                ("GCS_PL", "GCS_PLPacketRate"),
                ("DGPS",   "DGPSCorrRate"),
            };

            int rows = (items.Length + COLS - 1) / COLS;  // 4
            int contentW = COLS * ITEM_W + (COLS - 1) * COL_GAP;
            int contentH = rows * ROW_STRIDE + PAD * 2;
            int outerW = contentW + 2 * PAD + 2 * B;
            int outerH = BOT_H + 2 * B;

            var outer = new Panel
            {
                Size = new Size(outerW, outerH),
                BackColor = Color.FromArgb(12, 12, 12),
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };

            var inner = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(12, 12, 12) };

            for (int i = 0; i < items.Length; i++)
            {
                int col = i % COLS;
                int row = i / COLS;
                int x = PAD + col * (ITEM_W + COL_GAP);
                int y = PAD + row * ROW_STRIDE;

                // Name label
                inner.Controls.Add(new Label
                {
                    Text = items[i].Lbl,
                    Location = new Point(x, y),
                    Size = new Size(LBL_W, ROW_H),
                    BackColor = NAME_BG,
                    ForeColor = LIME,
                    Font = FNT11,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(3, 0, 0, 0)
                });

                // Separator
                inner.Controls.Add(new Panel
                {
                    Location = new Point(x + LBL_W, y),
                    Size = new Size(1, ROW_H),
                    BackColor = SEP_CLR
                });

                // Value label — registered in _rateLabels if a telemetry key exists
                var rateLbl = new Label
                {
                    Text = items[i].Key != null ? "0" : "---",
                    Location = new Point(x + LBL_W + 1, y),
                    Size = new Size(VAL_W, ROW_H),
                    BackColor = Color.FromArgb(50, 50, 50),
                    ForeColor = Color.Red,
                    Font = FNT11,
                    TextAlign = ContentAlignment.MiddleCenter
                };
                if (items[i].Key != null)
                    _rateLabels[items[i].Key] = rateLbl;
                inner.Controls.Add(rateLbl);
            }

            // Content added first → fill
            outer.Controls.Add(inner);

            // Borders — Bottom last = processed first
            outer.Controls.Add(new Panel { Dock = DockStyle.Top, Height = B, BackColor = CLR });
            outer.Controls.Add(new Panel { Dock = DockStyle.Left, Width = B, BackColor = CLR });
            outer.Controls.Add(new Panel { Dock = DockStyle.Right, Width = B, BackColor = CLR });
            outer.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = B, BackColor = CLR });

            return outer;
        }

        // ════════════════════════════════════════════════════════════════
        //  Tab Control  —  8 tabs, top-aligned, each scrollable
        // ════════════════════════════════════════════════════════════════
        private void BuildTabControl()
        {
            _tc = new TabControl
            {
                Font      = FNT11,
                Alignment = TabAlignment.Top,
                DrawMode  = TabDrawMode.OwnerDrawFixed,
                ItemSize  = new Size(110, 28),
                Padding   = new Point(8, 4),
                Anchor    = AnchorStyles.Top | AnchorStyles.Bottom |
                            AnchorStyles.Left | AnchorStyles.Right
            };
            _tc.DrawItem += DrawTab;

            var tabs = new (string Name, string[] Vars)[]
            {
                ("AutoP1",     VARS_AUTOP1),
                ("AutoP2",     VARS_AUTOP2),
                ("Engine",     VARS_ENGINE),
                ("IPSU",       VARS_IPSU),
                ("Sen1",       VARS_SEN1),
                ("Sen2",       VARS_SEN2),
                ("Navigation", VARS_NAV),
                ("MC",         VARS_MC),
            };

            foreach (var (name, vars) in tabs)
            {
                var page = new TabPage(name)
                {
                    BackColor = Color.FromArgb(12, 12, 12),
                    ForeColor = LIME,
                    Padding = new Padding(3)
                };

                page.Controls.Add(Make3DPanel(BuildTabContent(vars), vars.Length));

                _tc.TabPages.Add(page);
            }

            // ── Flags tab — 4-column bit-status layout ───────────────────
            var flagsPage = new TabPage("Flags")
            {
                BackColor = Color.FromArgb(12, 12, 12),
                ForeColor = LIME,
                Padding   = new Padding(3)
            };
            var flagsContent = BuildFlagsContent();
            flagsContent.Dock = DockStyle.Left;   // width = measured content; height fills tab
            flagsPage.Controls.Add(flagsContent);
            _tc.TabPages.Add(flagsPage);

            this.Controls.Add(_tc);
        }

        // ── Scrollable NAME | VALUE content for one tab ───────────────────
        private Panel BuildTabContent(string[] varNames)
        {
            var scroll = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(12, 12, 12)
            };
            scroll.HorizontalScroll.Maximum = 0;
            scroll.HorizontalScroll.Enabled = false;
            scroll.HorizontalScroll.Visible = false;
            scroll.AutoScrollMinSize = new Size(0, 0);

            // ── Column headers ────────────────────────────────────────────
            scroll.Controls.Add(new Label
            {
                Text = "NAME",
                Location = new Point(0, 2),
                Size = new Size(TAB_NW, ROW_H),
                BackColor = HDR_BG,
                ForeColor = WHITE,
                Font = FNT12,
                TextAlign = ContentAlignment.MiddleCenter
            });
            scroll.Controls.Add(new Panel
            {
                Location = new Point(TAB_NW, 2),
                Size = new Size(1, ROW_H),
                BackColor = SEP_CLR
            });
            scroll.Controls.Add(new Label
            {
                Text = "VALUE",
                Location = new Point(TAB_NW + 1, 2),
                Size = new Size(TAB_VW, ROW_H),
                BackColor = Color.FromArgb(50, 50, 50),
                ForeColor = WHITE,
                Font = FNT12,
                TextAlign = ContentAlignment.MiddleCenter
            });

            // ── Data rows ─────────────────────────────────────────────────
            int startY = ROW_H + 6;
            for (int i = 0; i < varNames.Length; i++)
            {
                int y = startY + i * ROW_STRIDE;
                string vName = varNames[i];

                var row = new Panel
                {
                    Location = new Point(0, y),
                    Size = new Size(TAB_ROW_W, ROW_H),
                    BackColor = (i % 2 == 0) ? ROW_EVEN : ROW_ODD
                };

                row.Controls.Add(new Label
                {
                    Text = vName,
                    Location = new Point(0, 0),
                    Size = new Size(TAB_NW, ROW_H),
                    BackColor = NAME_BG,
                    ForeColor = LIME,
                    Font = FNT10,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(4, 0, 0, 0)
                });
                row.Controls.Add(new Panel
                {
                    Location = new Point(TAB_NW, 0),
                    Size = new Size(1, ROW_H),
                    BackColor = SEP_CLR
                });

                // VALUE label — stored in dictionary for live updates
                var valLbl = new Label
                {
                    Text = "----",
                    Location = new Point(TAB_NW + 4, 0),
                    Size = new Size(TAB_VW - 4, ROW_H),
                    BackColor = Color.Transparent,
                    ForeColor = WHITE,
                    Font = FNT10,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                _valueLabels[vName] = valLbl;
                row.Controls.Add(valLbl);

                scroll.Controls.Add(row);
            }
            return scroll;
        }

        // ── 4-column flag status panel ────────────────────────────────────────
        private Panel BuildFlagsContent()
        {
            const int HDR_F_H = 22;   // flag-byte header height
            const int GRP_GAP = 6;    // gap between byte groups
            const int PAD_L   = 6;    // left text padding inside labels
            const int EXTRA_R = 10;   // right breathing room per column
            const int COL_GAP = 6;    // gap between adjacent columns

            // ── First pass: measure max text width for each column ────────
            int[] colW = new int[FLAG_COLUMNS.Length];
            for (int col = 0; col < FLAG_COLUMNS.Length; col++)
            {
                int maxW = 0;
                foreach (string byteName in FLAG_COLUMNS[col])
                {
                    int hw = TextRenderer.MeasureText(byteName, FNT8B).Width;
                    if (hw > maxW) maxW = hw;
                    foreach (var def in FLAG_DEFS)
                    {
                        if (def.B != byteName) continue;
                        int tw = TextRenderer.MeasureText(def.N, FNT8B).Width;
                        if (tw > maxW) maxW = tw;
                    }
                }
                colW[col] = maxW + PAD_L + EXTRA_R;
            }

            // ── Cumulative x positions ────────────────────────────────────
            int[] colXPos = new int[FLAG_COLUMNS.Length];
            colXPos[0] = 2;
            for (int col = 1; col < FLAG_COLUMNS.Length; col++)
                colXPos[col] = colXPos[col - 1] + colW[col - 1] + COL_GAP;
            int totalW = colXPos[FLAG_COLUMNS.Length - 1] + colW[FLAG_COLUMNS.Length - 1] + 4;

            var scroll = new Panel
            {
                Width         = totalW,
                AutoScroll    = true,
                BackColor     = Color.FromArgb(12, 12, 12)
            };
            scroll.HorizontalScroll.Maximum = 0;
            scroll.HorizontalScroll.Enabled = false;
            scroll.HorizontalScroll.Visible = false;

            int[] colY = new int[FLAG_COLUMNS.Length];
            for (int c = 0; c < colY.Length; c++) colY[c] = 4;

            for (int col = 0; col < FLAG_COLUMNS.Length; col++)
            {
                int cx = colXPos[col];
                int cw = colW[col];

                foreach (string byteName in FLAG_COLUMNS[col])
                {
                    // ── Byte header ───────────────────────────────────────
                    scroll.Controls.Add(new Label
                    {
                        Text      = byteName,
                        Location  = new Point(cx, colY[col]),
                        Size      = new Size(cw, HDR_F_H),
                        BackColor = HDR_BG,
                        ForeColor = WHITE,
                        Font      = FNT8B,
                        TextAlign = ContentAlignment.MiddleLeft,
                        Padding   = new Padding(PAD_L, 0, 0, 0)
                    });
                    colY[col] += HDR_F_H;

                    // ── Bit rows ──────────────────────────────────────────
                    int rowIdx = 0;
                    foreach (var def in FLAG_DEFS)
                    {
                        if (def.B != byteName) continue;

                        Color rowBg = (rowIdx % 2 == 0) ? ROW_EVEN : ROW_ODD;

                        var row = new Panel
                        {
                            Location  = new Point(cx, colY[col]),
                            Size      = new Size(cw, ROW_H),
                            BackColor = rowBg
                        };

                        var namLbl = new Label
                        {
                            Text      = def.N,
                            Location  = new Point(0, 0),
                            Size      = new Size(cw, ROW_H),
                            BackColor = Color.FromArgb(180, 0, 0),
                            ForeColor = Color.White,
                            Font      = FNT8B,
                            TextAlign = ContentAlignment.MiddleLeft,
                            Padding   = new Padding(PAD_L, 0, 0, 0)
                        };
                        _flagBitLabels[$"{byteName}.{def.N}"] = namLbl;
                        row.Controls.Add(namLbl);

                        scroll.Controls.Add(row);
                        colY[col] += ROW_STRIDE;
                        rowIdx++;
                    }

                    colY[col] += GRP_GAP;
                }
            }

            int maxH = 4;
            foreach (int y in colY) if (y > maxH) maxH = y;
            scroll.AutoScrollMinSize = new Size(totalW, maxH + 4);

            return scroll;
        }

        // ════════════════════════════════════════════════════════════════
        //  Batch-paint helpers — suppress/restore redraws around bulk updates.
        //  Call BeginUpdate() before the first UpdateValue, EndUpdate() after
        //  the last one; the form repaints once instead of once per label.
        // ════════════════════════════════════════════════════════════════
        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int msg, bool wParam, int lParam);
        private const int WM_SETREDRAW = 11;

        public void BeginUpdate()
        {
            // Target only the visible tab page — never the TabControl itself.
            // The tab header strip is part of the TabControl window, not the TabPage,
            // so it is never frozen and never flashes.
            var page = _tc?.SelectedTab;
            if (page != null && page.IsHandleCreated)
                SendMessage(page.Handle, WM_SETREDRAW, false, 0);
        }

        public void EndUpdate()
        {
            var page = _tc?.SelectedTab;
            if (page != null && page.IsHandleCreated)
            {
                SendMessage(page.Handle, WM_SETREDRAW, true, 0);
                page.Invalidate(true);   // repaint selected page content in one pass
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  Public API — call from telemetry/serial receiver on each decoded value
        //
        //  varName : exact ICD variable name (e.g. "Roll7Angle")
        //  value   : decoded engineering value (float)
        // ════════════════════════════════════════════════════════════════
        public void UpdateValue(string varName, float value)
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => UpdateValue(varName, value)));
                return;
            }
            if (_valueLabels.TryGetValue(varName, out Label lbl))
                lbl.Text = value.ToString("F2");
        }

        // ════════════════════════════════════════════════════════════════
        //  Packet-rate updater — updates status-box value labels
        //  key    : TelemetryData field name (e.g. "CmdPacketRate")
        //  value  : pkt/sec as decoded float
        // ════════════════════════════════════════════════════════════════
        public void UpdateRate(string key, float value)
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => UpdateRate(key, value)));
                return;
            }
            if (_rateLabels.TryGetValue(key, out Label lbl))
            {
                bool active = value >= 1f;
                lbl.Text      = active ? ((int)value).ToString() : "0";
                lbl.ForeColor = active ? Color.Lime : Color.Red;
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  Flag status updater — call from telemetry receive loop each packet.
        //  Extracts each flag bit/field and colours the label:
        //    single bit 1  → Lime     single bit 0 → dim gray
        //    multi-bit     → Yellow (value) or dim gray (zero)
        // ════════════════════════════════════════════════════════════════
        public void UpdateFlags(TelemetryData data)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action(() => UpdateFlags(data))); return; }

            foreach (var def in FLAG_DEFS)
            {
                string key = $"{def.B}.{def.N}";
                if (!_flagBitLabels.TryGetValue(key, out Label lbl)) continue;

                double rawD = GetFieldDouble(data, def.B);
                if (double.IsNaN(rawD))
                {
                    lbl.ForeColor = Color.White;
                    lbl.BackColor = Color.FromArgb(180, 0, 0);
                    continue;
                }

                byte raw       = (byte)(int)rawD;
                int  extracted = raw & def.M;

                if (!def.MB)
                {
                    // Single bit: green bg + black text when set; red bg + white text when clear
                    bool isSet    = extracted != 0;
                    lbl.ForeColor = Color.White;
                    lbl.BackColor = isSet ? Color.FromArgb(0, 180, 0) : Color.FromArgb(180, 0, 0);
                }
                else
                {
                    // Multi-bit: green bg + black text when non-zero; red bg + white text when zero
                    int shift = 0;
                    byte tmp = def.M;
                    while ((tmp & 1) == 0) { tmp >>= 1; shift++; }
                    int val       = extracted >> shift;
                    lbl.ForeColor = Color.White;
                    lbl.BackColor = val != 0 ? Color.FromArgb(0, 180, 0) : Color.FromArgb(180, 0, 0);
                }
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  Raised 3-D border wrapper for tab content
        // ════════════════════════════════════════════════════════════════
        private Panel Make3DPanel(Control content, int rowCount, bool forceDockLeft = false)
        {
            const int B = 3;
            Color CLR = Color.FromArgb(200, 200, 200);

            int contentH = (ROW_H + 6) + rowCount * ROW_STRIDE + 4;
            int naturalH = contentH + 2 * B;
            bool fitsInView = !forceDockLeft && (naturalH <= TAB_MAX_H);

            var outer = new Panel { BackColor = Color.FromArgb(12, 12, 12) };

            if (fitsInView)
            {
                outer.Location = new Point(0, 0);
                outer.Size = new Size(TAB_ROW_W + 2 * B, naturalH);
                outer.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            }
            else
            {
                outer.Dock = DockStyle.Left;
                outer.Width = TAB_ROW_W + 2 * B;
            }

            content.Dock = DockStyle.Fill;
            outer.Controls.Add(content);

            outer.Controls.Add(new Panel { Dock = DockStyle.Top, Height = B, BackColor = CLR });
            outer.Controls.Add(new Panel { Dock = DockStyle.Left, Width = B, BackColor = CLR });
            outer.Controls.Add(new Panel { Dock = DockStyle.Right, Width = B, BackColor = CLR });
            outer.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = B, BackColor = CLR });

            return outer;
        }

        // Empty bordered box placeholder (same dimensions as Make3DPanel)
        private Panel BuildEmptyBox(int rowCount)
        {
            const int B = 3;
            Color CLR = Color.FromArgb(200, 200, 200);

            var outer = new Panel
            {
                Dock = DockStyle.Left,
                Width = TAB_ROW_W + 2 * B,
                BackColor = Color.FromArgb(12, 12, 12)
            };

            // Empty inner area
            outer.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(12, 12, 12) });

            outer.Controls.Add(new Panel { Dock = DockStyle.Top, Height = B, BackColor = CLR });
            outer.Controls.Add(new Panel { Dock = DockStyle.Left, Width = B, BackColor = CLR });
            outer.Controls.Add(new Panel { Dock = DockStyle.Right, Width = B, BackColor = CLR });
            outer.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = B, BackColor = CLR });

            return outer;
        }

        // ════════════════════════════════════════════════════════════════
        //  Layout — positions _tc and _plotPanel on every resize / load
        // ════════════════════════════════════════════════════════════════
        private void LayoutMainArea()
        {
            int w = ClientSize.Width;
            int h = ClientSize.Height;

            int botReserved = BOT_H + 16;
            int tcH = h - botReserved;

            bool plotVisible = _plotPanel != null && _plotPanel.Visible;
            int  tcW = plotVisible ? w - PLOT_W : w;

            _tc.Location = new Point(0, 0);
            _tc.Size     = new Size(tcW, tcH);

            if (_plotPanel != null)
            {
                _plotPanel.Location = new Point(w - PLOT_W, 0);
                _plotPanel.Size     = new Size(PLOT_W, PLOT_H);
            }

            // Toggle button always sits top-right of the form
            if (_plotToggleBtn != null)
                _plotToggleBtn.Location = new Point(w - _plotToggleBtn.Width - 4, 4);
        }

        // ════════════════════════════════════════════════════════════════
        //  Build live-plot panel  (600 × 400, always visible right side)
        // ════════════════════════════════════════════════════════════════
        private void BuildPlotPanel()
        {
            const int HDR_H   = 26;                       // header bar height
            const int CHART_H = (PLOT_H - HDR_H) / 5;    // 74 px per sub-plot

            _plotPanel = new Panel
            {
                Size      = new Size(PLOT_W, PLOT_H),
                BackColor = Color.FromArgb(10, 21, 0),
            };

            // ── Header bar ──────────────────────────────────────────────
            var hdr = new Panel
            {
                Location  = new Point(0, 0),
                Size      = new Size(PLOT_W, HDR_H),
                BackColor = Color.FromArgb(26, 42, 26)
            };
            hdr.Controls.Add(new Label
            {
                Text      = "▶  LIVE PLOT",
                Location  = new Point(6, 0),
                Size      = new Size(200, HDR_H),
                ForeColor = Color.Yellow,
                BackColor = Color.Transparent,
                Font      = new Font("Consolas", 9f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            });
            var selBtn = new Button
            {
                Text      = "⚙ Variables",
                Location  = new Point(PLOT_W - 200, 3),
                Size      = new Size(112, HDR_H - 6),
                ForeColor = Color.Lime,
                BackColor = Color.FromArgb(0, 40, 0),
                FlatStyle = FlatStyle.Flat,
                Font      = new Font("Consolas", 8f, FontStyle.Regular)
            };
            selBtn.FlatAppearance.BorderColor = Color.FromArgb(0, 140, 0);
            selBtn.Click += (s, e) =>
            {
                _selectorPanel.Visible = !_selectorPanel.Visible;
                if (_selectorPanel.Visible) _selectorPanel.BringToFront();
            };
            hdr.Controls.Add(selBtn);

            _plotPanel.Controls.Add(hdr);

            // ── 5 stacked Chart controls (start immediately below header) ──
            int chartTop = HDR_H;
            for (int i = 0; i < 5; i++)
            {
                int idx     = i;
                int actualH = (idx == 4)
                    ? (PLOT_H - chartTop - 4 * CHART_H)   // last gets remainder
                    : CHART_H;

                var chart = new Chart
                {
                    Location  = new Point(0, chartTop + idx * CHART_H),
                    Size      = new Size(PLOT_W, actualH > 0 ? actualH : CHART_H),
                    BackColor = Color.FromArgb(12, 12, 12)
                };

                var ca = new ChartArea("ca" + i)
                {
                    BackColor = Color.FromArgb(13, 26, 10)
                };
                ca.Position.Auto            = false;
                ca.Position.X               = 0f;   ca.Position.Y      = 0f;
                ca.Position.Width           = 100f; ca.Position.Height = 100f;
                ca.InnerPlotPosition.Auto   = false;
                ca.InnerPlotPosition.X      = 22f;  ca.InnerPlotPosition.Y      = 5f;
                ca.InnerPlotPosition.Width  = 73f;  ca.InnerPlotPosition.Height = 80f;
                // X axis
                ca.AxisX.LineColor              = Color.FromArgb(40, 40, 40);
                ca.AxisX.MajorGrid.LineColor     = Color.FromArgb(28, 45, 28);
                ca.AxisX.MajorGrid.LineDashStyle = ChartDashStyle.Dot;
                ca.AxisX.LabelStyle.ForeColor    = Color.FromArgb(90, 90, 90);
                ca.AxisX.LabelStyle.Font         = new Font("Consolas", 6f);
                ca.AxisX.LabelStyle.Enabled      = (idx == 4);          // only bottom
                ca.AxisX.Title                   = (idx == 4) ? "FCC7Time (s)" : "";
                ca.AxisX.TitleFont               = new Font("Consolas", 6.5f);
                ca.AxisX.TitleForeColor          = Color.FromArgb(80, 80, 80);
                // Y axis
                ca.AxisY.LineColor              = Color.FromArgb(40, 40, 40);
                ca.AxisY.MajorGrid.LineColor     = Color.FromArgb(28, 45, 28);
                ca.AxisY.MajorGrid.LineDashStyle = ChartDashStyle.Dot;
                ca.AxisY.LabelStyle.ForeColor    = Color.FromArgb(90, 90, 90);
                ca.AxisY.LabelStyle.Font         = new Font("Consolas", 6f);
                ca.AxisY.LabelStyle.Angle        = 0;
                ca.AxisY.IsStartedFromZero       = false;
                chart.ChartAreas.Add(ca);

                var series = new Series("s" + i)
                {
                    ChartType   = SeriesChartType.FastLine,
                    Color       = PLOT_COLORS[idx],
                    BorderWidth = 1,
                    XValueType  = ChartValueType.Double,
                    YValueType  = ChartValueType.Double
                };
                chart.Series.Add(series);

                // Variable name label overlay (top-left of each sub-plot)
                var lbl = new Label
                {
                    Text      = _plotVarSel[idx],
                    Location  = new Point(28, 2),
                    Size      = new Size(240, 14),
                    ForeColor = Color.FromArgb(110, 110, 110),
                    BackColor = Color.Transparent,
                    Font      = new Font("Consolas", 6.5f),
                    TextAlign = ContentAlignment.MiddleLeft
                };
                _chartLabels[idx] = lbl;
                chart.Controls.Add(lbl);

                _charts[idx] = chart;
                _plotPanel.Controls.Add(chart);

                // ── Cursor value tooltip ──────────────────────────────────
                chart.MouseMove += (s, e) =>
                {
                    try
                    {
                        var pts = chart.Series[0].Points;
                        if (pts.Count < 2) return;

                        var tipCa = chart.ChartAreas[0];
                        double xv = tipCa.AxisX.PixelPositionToValue(e.X);

                        // Binary-search for the closest point by X value
                        int lo = 0, hi = pts.Count - 1, best = 0;
                        while (lo <= hi)
                        {
                            int mid = (lo + hi) / 2;
                            if (Math.Abs(pts[mid].XValue - xv) < Math.Abs(pts[best].XValue - xv))
                                best = mid;
                            if (pts[mid].XValue < xv) lo = mid + 1;
                            else                       hi = mid - 1;
                        }
                        double yv = pts[best].YValues[0];
                        string tip = $"{yv:F4}   t={pts[best].XValue:F2}s";
                        _chartTip.Show(tip, chart, e.X + 14, e.Y - 18, 3000);
                    }
                    catch { /* axis not yet scaled — ignore */ }
                };
                chart.MouseLeave += (s, e) => _chartTip.Hide(chart);
            }

            // ── Selector panel — child of _plotPanel, overlays charts when open ──
            // Positioned at (0, HDR_H): slides over the chart area, never covers the header
            _selectorPanel          = BuildSelectorPanel(PLOT_W, PLOT_H - HDR_H, HDR_H);
            _selectorPanel.Visible  = false;
            _plotPanel.Controls.Add(_selectorPanel);

            this.Controls.Add(_plotPanel);

            // ── Single toggle button — always visible, lives on Form2 ──
            _plotToggleBtn = new Button
            {
                Text      = "▼ Hide Plot",
                Size      = new Size(90, 22),
                ForeColor = Color.Silver,
                BackColor = Color.FromArgb(30, 15, 0),
                FlatStyle = FlatStyle.Flat,
                Font      = new Font("Consolas", 8f, FontStyle.Regular)
            };
            _plotToggleBtn.FlatAppearance.BorderColor = Color.FromArgb(120, 80, 0);
            _plotToggleBtn.Click += (s, e) =>
            {
                bool show = !_plotPanel.Visible;
                _plotPanel.Visible     = show;
                _plotToggleBtn.Text    = show ? "▼ Hide Plot" : "▶ Show Plot";
                _plotToggleBtn.ForeColor = show ? Color.Silver : Color.Lime;
                LayoutMainArea();
            };
            this.Controls.Add(_plotToggleBtn);
            _plotToggleBtn.BringToFront();
        }

        // ════════════════════════════════════════════════════════════════
        //  Variable selector panel — categorised list with radio buttons
        // ════════════════════════════════════════════════════════════════
        // w, h = dimensions; topY = position within parent (_plotPanel)
        private Panel BuildSelectorPanel(int w, int h, int topY)
        {
            int SEL_W  = w;
            int SEL_H  = h;
            const int ROW_H  = 18;
            const int NM_W   = 185;
            const int RD_W   = 60;
            const int HDR_H  = 22;
            const int FOOT_H = 28;

            var outer = new Panel
            {
                Location    = new Point(0, topY),   // just below plot-panel header
                Size        = new Size(SEL_W, SEL_H),
                BackColor   = Color.FromArgb(14, 14, 14),
                BorderStyle = BorderStyle.FixedSingle
            };

            // Column headers
            var colHdr = new Panel
            {
                Location  = new Point(0, 0),
                Size      = new Size(SEL_W, HDR_H),
                BackColor = Color.FromArgb(26, 42, 26)
            };
            colHdr.Controls.Add(new Label
            {
                Text      = "  Variable",
                Location  = new Point(0, 0),
                Size      = new Size(NM_W, HDR_H),
                ForeColor = Color.LightGray,
                BackColor = Color.Transparent,
                Font      = new Font("Consolas", 7.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            });
            for (int p = 0; p < 5; p++)
            {
                colHdr.Controls.Add(new Label
                {
                    Text      = $"P{p + 1}",
                    Location  = new Point(NM_W + p * RD_W + 18, 0),
                    Size      = new Size(RD_W, HDR_H),
                    ForeColor = Color.Yellow,
                    BackColor = Color.Transparent,
                    Font      = new Font("Consolas", 8f, FontStyle.Bold),
                    TextAlign = ContentAlignment.MiddleCenter
                });
            }
            outer.Controls.Add(colHdr);

            // Scrollable variable list
            var scroll = new Panel
            {
                Location          = new Point(0, HDR_H),
                Size              = new Size(SEL_W, SEL_H - HDR_H - FOOT_H),
                AutoScroll        = true,
                BackColor         = Color.FromArgb(12, 12, 12)
            };
            scroll.HorizontalScroll.Enabled = false;
            scroll.HorizontalScroll.Visible = false;

            // One isolated Panel per column so WinForms treats each as its own
            // RadioButton group — otherwise all buttons share one group and only
            // one can ever be selected across the entire list.
            var colPanels = new Panel[5];
            for (int cp = 0; cp < 5; cp++)
            {
                colPanels[cp] = new Panel
                {
                    Location  = new Point(NM_W + cp * RD_W + 18, 0),
                    BackColor = Color.FromArgb(12, 12, 12)
                };
                scroll.Controls.Add(colPanels[cp]);
            }

            int y       = 0;
            int rowIdx  = 0;
            for (int c = 0; c < PLOT_CATS.Length; c++)
            {
                // Category header row
                var catLbl = new Label
                {
                    Text      = $"  ▸  {PLOT_CAT_NAMES[c]}",
                    Location  = new Point(0, y),
                    Size      = new Size(SEL_W - 20, ROW_H),
                    ForeColor = Color.Lime,
                    BackColor = Color.FromArgb(0, 30, 0),
                    Font      = new Font("Consolas", 7.5f, FontStyle.Bold),
                    TextAlign = ContentAlignment.MiddleLeft
                };
                scroll.Controls.Add(catLbl);
                y += ROW_H;

                foreach (var varName in PLOT_CATS[c])
                {
                    string vn    = varName;
                    int    rowY  = y;
                    Color  rowBg = (rowIdx % 2 == 0)
                        ? Color.FromArgb(10, 10, 10)
                        : Color.FromArgb(16, 16, 16);

                    scroll.Controls.Add(new Label
                    {
                        Text      = vn,
                        Location  = new Point(6, rowY),
                        Size      = new Size(NM_W - 6, ROW_H),
                        ForeColor = Color.FromArgb(0, 180, 0),
                        BackColor = rowBg,
                        Font      = new Font("Consolas", 7f),
                        TextAlign = ContentAlignment.MiddleLeft
                    });

                    for (int p = 0; p < 5; p++)
                    {
                        int pi = p;
                        // Location is relative to the column panel, not scroll
                        var rb = new RadioButton
                        {
                            Location  = new Point(0, rowY + 1),
                            Size      = new Size(RD_W - 4, ROW_H - 2),
                            Checked   = (_plotVarSel[pi] == vn),
                            BackColor = rowBg,
                            ForeColor = PLOT_COLORS[pi]
                        };
                        rb.CheckedChanged += (s, e) =>
                        {
                            if (!rb.Checked) return;
                            _plotVarSel[pi] = vn;
                            if (_chartLabels[pi] != null)
                                _chartLabels[pi].Text = vn;
                            if (_charts[pi] != null)
                                _charts[pi].Series[0].Points.Clear();
                        };
                        colPanels[pi].Controls.Add(rb);   // each column = own group
                    }
                    y      += ROW_H;
                    rowIdx++;
                }
            }

            // Now that total height is known, size the column panels
            for (int cp = 0; cp < 5; cp++)
                colPanels[cp].Size = new Size(RD_W - 4, y);

            scroll.AutoScrollMinSize = new Size(SEL_W - 20, y);
            outer.Controls.Add(scroll);

            // Footer — Apply & Close
            var foot = new Button
            {
                Text      = "✔  Apply & Close",
                Location  = new Point(0, SEL_H - FOOT_H),
                Size      = new Size(SEL_W, FOOT_H),
                BackColor = Color.FromArgb(0, 40, 0),
                ForeColor = Color.Lime,
                FlatStyle = FlatStyle.Flat,
                Font      = new Font("Consolas", 8.5f, FontStyle.Bold)
            };
            foot.FlatAppearance.BorderColor = Color.FromArgb(0, 100, 0);
            foot.Click += (s, e) => outer.Visible = false;
            outer.Controls.Add(foot);

            return outer;
        }

        // ════════════════════════════════════════════════════════════════
        //  Public API — call from packet-reception handler on each
        //  decoded TelemetryData packet.  Thread-safe (BeginInvoke).
        // ════════════════════════════════════════════════════════════════
        public void UpdatePlotData(TelemetryData data)
        {
            if (IsDisposed) return;
            if (_plotPanel == null || !_plotPanel.Visible) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => UpdatePlotData(data)));
                return;
            }

            double t = data.FCC7Time;
            for (int i = 0; i < 5; i++)
            {
                double val = GetFieldDouble(data, _plotVarSel[i]);
                if (double.IsNaN(val)) continue;

                var pts = _charts[i].Series[0].Points;
                pts.AddXY(t, val);
                if (pts.Count > PLOT_MAX_PTS) pts.RemoveAt(0);

                if (pts.Count >= 2)
                {
                    var ca = _charts[i].ChartAreas[0];
                    ca.AxisX.Minimum = pts[0].XValue;
                    ca.AxisX.Maximum = t;
                    ca.RecalculateAxesScale();
                }
            }
        }

        // Reflection-based field reader — cached for performance
        private static double GetFieldDouble(TelemetryData data, string name)
        {
            if (!_fiCache.TryGetValue(name, out FieldInfo fi))
            {
                fi = typeof(TelemetryData).GetField(
                    name, BindingFlags.Public | BindingFlags.Instance);
                _fiCache[name] = fi;
            }
            if (fi == null) return double.NaN;
            try   { return Convert.ToDouble(fi.GetValue(data)); }
            catch { return double.NaN; }
        }

        // ════════════════════════════════════════════════════════════════
        //  Owner-draw tab headers  (3-D bevel, top-aligned)
        // ════════════════════════════════════════════════════════════════
        private void DrawTab(object sender, DrawItemEventArgs ev)
        {
            var tab = (TabControl)sender;
            bool sel = (tab.SelectedIndex == ev.Index);
            var g = ev.Graphics;
            var r = ev.Bounds;
            Color face = sel ? Color.FromArgb(65, 65, 65) : Color.FromArgb(22, 22, 22);
            Color hi = Color.FromArgb(125, 125, 125);
            Color sh = Color.FromArgb(10, 10, 10);
            Color fg = sel ? Color.Lime : Color.FromArgb(90, 140, 90);

            g.FillRectangle(new SolidBrush(face), r);

            const int B = 2;
            if (sel)
            {
                // Selected: highlight top + left + right (raised toward viewer)
                using (var pH = new Pen(hi, 1))
                using (var pS = new Pen(sh, 1))
                    for (int i = 0; i < B; i++)
                    {
                        g.DrawLine(pH, r.Left + i, r.Top + i, r.Right - 1 - i, r.Top + i);         // top
                        g.DrawLine(pH, r.Left + i, r.Top + i, r.Left + i, r.Bottom - 1);       // left
                        g.DrawLine(pS, r.Right - 1 - i, r.Top + i, r.Right - 1 - i, r.Bottom - 1);       // right shadow
                    }
            }
            else
            {
                // Unselected: shadow sides, soft highlight on top
                using (var pH = new Pen(hi, 1))
                using (var pS = new Pen(sh, 1))
                    for (int i = 0; i < B; i++)
                    {
                        g.DrawLine(pH, r.Left + i, r.Top + i, r.Right - 1 - i, r.Top + i);          // top highlight
                        g.DrawLine(pS, r.Left + i, r.Top + i, r.Left + i, r.Bottom - 1 - i);   // left shadow
                        g.DrawLine(pS, r.Right - 1 - i, r.Top + i, r.Right - 1 - i, r.Bottom - 1 - i);   // right shadow
                    }
            }

            var fmt = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString(tab.TabPages[ev.Index].Text, tab.Font,
                         new SolidBrush(fg), r, fmt);
        }

    }
}