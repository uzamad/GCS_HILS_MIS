using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using AxCombinedLib;
using HeadingIndicator;
using RpmIndicator;
// ============================================================
//  Form1.cs  –  GCS_240626
//  Screen  : 1900 x 1000
//  Font    : Consolas 14pt Bold, Neon Green on Black
//
//  Layout
//  ──────────────────────────────────────────────────────────
//   x=10   PFD (384×372)           │  MAP (1010×452)    │  ENGINE (455×295)
//   x=10   [DEMO btn] (384×40)     │                    │  LINK   (455×230)
//   x=10   [DEMO sliders — hidden] │                    │
//   ─────────────────────────────────────────────────────────
//          FLIGHT BLOCK (1010×522)  bottom-centre
//
//  Demo behaviour
//  ──────────────────────────────────────────────────────────
//  • "DEMO ▶ OFF" button is ALWAYS visible below the PFD.
//  • Click once  → slider panel appears, demo mode ON.
//  • Click again → slider panel hides, live mode resumes.
// ============================================================
namespace GCS_240626
{
    public partial class Form1 : Form
    {
        // ── Serial & timer ───────────────────────────────────────────────
        private PacketReceiver   _receiver;
        private CommandLinkSender _cmdSender;
        private System.Windows.Forms.Timer _displayTimer;
        // ── Controls ─────────────────────────────────────────────────────
        private AxCombined _combined;
        private MapControl _mapCtrl;
        private Form2 _form2;
        private PacketMonitorForm _pktMon;
        private TelemetryData _latestFlags = new TelemetryData(); // accumulates flag bytes across all LR frames
        // ── Map toolbar buttons (upper-right, below PKT MON/SCR2/DEMO) ──
        private Button      _btnMapMission;
        private Button      _btnMapSend;
        private Button      _btnMapCache;
        private CheckBox    _chkSendLog;
        private ProgressBar _barMission;
        private Panel       _toolBar;     // horizontal bar at top-right
        // ── Demo ─────────────────────────────────────────────────────────
        private bool _demoMode = false;
        private Button _btnDemo;          // always visible
        private Panel _pnlSliders;       // hidden until DEMO ON
        private TrackBar _tbAirspeed, _tbAltitude, _tbHeading, _tbPitch, _tbRoll, _tbPosition, _tbCHTLeft, _tbCHTRight;
        private Label _lblDemoAS, _lblDemoAlt, _lblDemoHdg, _lblDemoPitch, _lblDemoRoll, _lblDemoPos, _lblDemoCHT, _lblDemoCHTR;
        private VProgressBar _chtFillGreen,  _chtFillYellow,  _chtFillRed;   // CHT Left
        private VProgressBar _chtRFillGreen, _chtRFillYellow, _chtRFillRed;  // CHT Right
        private Label        _chtBarValue, _chtRBarValue;
        // ── Flight block value labels ─────────────────────────────────────
        private Label _valLat, _valLon, _valD2Base, _valOffTrack, _valGPS, _valWP;
        private Label _valRollAngle, _valRollCmd, _valPitchAngle, _valPitchCmd;
        private Label _valHeading, _valHeadingCmd;
        private HeadingIndicator.HeadingIndicator _hdgIndicator;
        private HeadingFloatForm                  _hdgFloatForm;
        private GcsButton                          _btnHeading;
        private readonly Point _hdgOriginalLocation = new Point(1608, 141);
        private readonly Size  _hdgOriginalSize     = new Size(200, 200);
        private RpmFloatForm                       _rpmFloatForm;
        private GcsButton                          _btnRpm;
        private RpmIndicator.RpmIndicator          _rpmIndicator;
        private readonly Point _rpmOriginalLocation = new Point(1608, 405);
        private readonly Size  _rpmOriginalSize     = new Size(150, 150);
        private Label _valAileron, _valFlap;
        private Label _valHMSL, _valHCmd, _valHAGL, _valRA;
        private Label _valElevator, _valROC;
        private Label _valSpeed, _valSpeedCmd, _valThrottle;
        // ── Engine & Power value labels ──────────────────────────────────
        private Label _valRPM, _valFuelMain;
        private Label _valBattV, _valAltV, _valCHTL, _valCHTR;
        // ── Reset actions — each entry restores one control to its default state ──
        private readonly List<Action> _resetActions = new List<Action>();
        // ── Link Health value labels ─────────────────────────────────────
        private Label _valCRCFail, _valGPSMiss, _valVGMiss;
        private Label _valGPSRate, _valVGRate, _valPktNum;
        // ── Flight status (temporary debug displays) ─────────────────────
        private Label _valMode, _valPhase;
        // ── Fonts & colours ──────────────────────────────────────────────
        private static readonly string[] MODE_NAMES = {
            "AUTO", "SELFTEST", "RUNUP", "STANDBY",
            "MANUAL_EC", "MANUAL_GCS", "SEMIAUTO_HDG", "SEMIAUTO_ROLL",
            "SIGHTLINE_SLV"
        };
        private static readonly string[] PHASE_NAMES = {
            "WAIT", "FT_TAXI", "HS_TAXI", "TO_TAXI",
            "TAKEOFF", "ENROUTE", "LOITER", "SEARCH",
            "DASH", "LAPROACH", "GS_TRACKING", "DUMMY_LAND",
            "FLARE", "LANDING_TAXI", "ABORT_TAXI", "ABORT_LANDING",
            "GND_LAND_TEST", "CLIMB_OUT", "FLY_PAST", "MANUAL"
        };
        private static readonly Font FNT16B = new Font("Consolas", 16f, FontStyle.Bold);
        private static readonly Font FNT15  = new Font("Consolas", 15f, FontStyle.Bold);
        private static readonly Font FNT14 = new Font("Consolas", 14f, FontStyle.Bold);
        private static readonly Font FNT12 = new Font("Consolas", 12f, FontStyle.Bold);
        private static readonly Font FNT11 = new Font("Consolas", 11f, FontStyle.Bold);
        private static readonly Font FNT10  = new Font("Consolas", 10f);
        private static readonly Font FNT10B = new Font("Consolas", 10f, FontStyle.Bold);
        private static readonly Font FNT_TAB = new Font("Arial Rounded MT Bold", 10f, FontStyle.Bold);    // tab-box buttons & labels
        private static readonly Font FNT8B   = new Font("Consolas", 8f, FontStyle.Bold);
        private static readonly Color LIME = Color.Lime;
        private static readonly Color WHITE = Color.White;
        private static readonly Color CYAN = Color.Cyan;
        private static readonly Color BLACK = Color.Black;
        // ── Flight Block column constants ─────────────────────────────────
        private const int FL_NX = 5;
        private const int FL_NW = 137;
        private const int FL_VX = 145;
        private const int FL_VW = 95;
        private const int FL_R_NX = 250;
        private const int FL_R_NW = 105;
        private const int FL_R_VX = 350;
        private const int FL_R_VW = 130;
        private const int FL_UX = 603;
        private const int FL_H = 24;
        private const int FL_ROW = 27;
        // ── GPS section (12pt, compressed) ──────────────────────────────
        private const int GPS_ROW = 27;
        private const int GPS_H = 24;
        // ── Constructor ──────────────────────────────────────────────────
        public Form1()
        {
            InitializeComponent();
            this.Load += Form1_Load;
            this.FormClosing += Form1_FormClosing;
            BuildUI();
        }
        private void Form1_Load(object sender, EventArgs e)
        {
            _receiver = new PacketReceiver();
            _receiver.Open("COM9", 115200);

            _cmdSender = new CommandLinkSender(_receiver);
            _cmdSender.State.ApplyHilsDefaults();
            // Command link starts STOPPED — user presses CMD LINK ON in the tab.

            _displayTimer = new System.Windows.Forms.Timer();
            _displayTimer.Interval = 50;   // 50 ms → 20 Hz; was 100 ms (10 Hz)
            _displayTimer.Tick += UpdateDisplay;
            _displayTimer.Start();

            // Auto-load last mission (fixed copy saved after every successful load)
            if (File.Exists(MapControl.FixedMissionPath))
                _mapCtrl.AutoLoadLastMission(MapControl.FixedMissionPath);
        }
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_displayTimer != null) _displayTimer.Stop();
            _cmdSender?.Dispose();
            if (_receiver != null) _receiver.Close();
        }
        // ════════════════════════════════════════════════════════════════
        //  Demo toggle
        // ════════════════════════════════════════════════════════════════
        private void BtnDemo_Click(object sender, EventArgs e)
        {
            _demoMode = !_demoMode;
            if (_demoMode)
            {
                _btnDemo.Text = "DEMO  ■  ON   (click to hide sliders)";
                _btnDemo.BackColor = Color.FromArgb(0, 150, 0);
                _btnDemo.ForeColor = WHITE;
            }
            else
            {
                _btnDemo.Text = "DEMO  ▶  OFF  (click to show sliders)";
                _btnDemo.BackColor = Color.FromArgb(40, 40, 40);
                _btnDemo.ForeColor = LIME;
            }
            _pnlSliders.Visible = _demoMode;
        }
        // ════════════════════════════════════════════════════════════════
        //  Display update  (100 ms timer)
        // ════════════════════════════════════════════════════════════════
        private void UpdateDisplay(object sender, EventArgs e)
        {
            if (_demoMode)
            {
                _combined.Airspeed = _tbAirspeed.Value;
                _combined.Altitude = _tbAltitude.Value;
                _combined.Heading = _tbHeading.Value;
                _combined.Pitch = _tbPitch.Value;
                _combined.Roll = _tbRoll.Value;
                _combined.Refresh();
                _hdgIndicator.CurrentHeading = _tbHeading.Value;
                _valRollAngle.Text = _tbRoll.Value + "°";
                _valPitchAngle.Text = _tbPitch.Value + "°";
                _valHeading.Text = _tbHeading.Value + "°";
                _valHMSL.Text = _tbAltitude.Value + " m";
                _valSpeed.Text = _tbAirspeed.Value + " m/s";
                _valCHTL.Text = _tbCHTLeft.Value + " °C";
                _valCHTR.Text = _tbCHTRight.Value + " °C";
                UpdateCHTBar(_tbCHTLeft.Value);
                UpdateCHTRBar(_tbCHTRight.Value);
                return;
            }
            // ── Live telemetry ───────────────────────────────────────────
            // Take one consistent locked snapshot so the receive thread can't
            // overwrite a buffer while we are decoding it.
            var (snap, recv) = PacketReceiver.TakeSnapshot();
            if (!recv[0]) return;
            _lastTelemetryTime = DateTime.UtcNow;

            // Decode each received packet exactly once; reuse for both the
            // main display and the Form2 push (eliminates double-decode).
            TelemetryData[] td = new TelemetryData[8];
            td[0] = TelemetryDecoder.Decode(snap[0]);
            for (int i = 1; i < 8; i++)
                if (recv[i]) td[i] = TelemetryDecoder.Decode(snap[i]);

            TelemetryData t1 = td[0];

            _combined.Roll = (int)t1.Roll7Angle;
            _combined.Pitch = (int)t1.Pitch7Angle;
            _combined.Airspeed = (int)t1.CAS;
            _combined.Altitude = (int)t1.Hp;
            _combined.Heading = (int)t1.Heading;
            _combined.Invalidate();   // async repaint — don't block UpdateDisplay
            _hdgIndicator.CurrentHeading = (int)t1.Heading;
            _mapCtrl.UpdatePosition(t1.Latitude, t1.Longitude, t1.Heading);
            _mapCtrl.SetActiveWaypoint((int)t1.Next7WayPoint7Number);
            _valLat.Text = t1.Latitude.ToString("F4") + "°";
            _valLon.Text = t1.Longitude.ToString("F4") + "°";
            _valRollAngle.Text = t1.Roll7Angle.ToString("F1") + "°";
            _valRollCmd.Text = t1.Phi_Com.ToString("F1") + "°";
            _valPitchAngle.Text = t1.Pitch7Angle.ToString("F1") + "°";
            _valPitchCmd.Text = t1.Theta_Cmd.ToString("F1") + "°";
            _valHeading.Text = t1.Heading.ToString("F0") + "°";
            _valHeadingCmd.Text = t1.Psi_Com.ToString("F0") + "°";
            _valAileron.Text = t1.Inner_SB_Ail_Com.ToString("F1") + "°";
            _valFlap.Text = "N/A";
            _valHMSL.Text = t1.Hp.ToString("F0") + " m";
            _valHCmd.Text = t1.Alt_Com.ToString("F0") + " m";
            _valHAGL.Text = t1.AGL.ToString("F1") + " m";
            _valRA.Text = t1.Radar_Alt.ToString("F1") + " m";
            _valElevator.Text = t1.Top_SB_RV_Com.ToString("F1") + "°";
            _valSpeed.Text = t1.CAS.ToString("F1") + " m/s";
            _valSpeedCmd.Text = t1.CAS_Com.ToString("F1") + " m/s";
            _valThrottle.Text = (t1.Throttle_pos * 100.0).ToString("F0") + " %";
            _valRPM.Text = t1.Engine_RPM.ToString("F0") + " rpm";
            if (_rpmIndicator != null) _rpmIndicator.CurrentRPM = (int)t1.Engine_RPM;
            _valBattV.Text = t1.Batt_Volts.ToString("F1") + " V";
            _valAltV.Text = t1.Alt_Volts.ToString("F1") + " V";
            if (td[1] != null)
            {
                _valD2Base.Text = td[1].Distance7From7Base.ToString("F1") + " km";
                _valOffTrack.Text = td[1].OffTrack.ToString("F1") + " m";
                _valWP.Text = (td[1].D2Go / 1000.0).ToString("F0");
            }
            if (td[2] != null)
            {
                _valROC.Text = td[2].ROC_Estimated.ToString("F1") + " m/s";
                int modeIdx  = (td[2].MCFlagByte3 & 0x78) >> 3;
                int phaseIdx = (int)td[2].Flight7Phase;
                _valMode.Text  = modeIdx  < MODE_NAMES.Length  ? MODE_NAMES[modeIdx]   : modeIdx.ToString();
                _valPhase.Text = phaseIdx < PHASE_NAMES.Length ? PHASE_NAMES[phaseIdx] : phaseIdx.ToString();
            }
            if (td[4] != null)
                _valGPS.Text = td[4].Solution7Status > 0 ? "DIFF" : "SINGLE";
            if (td[5] != null)
            {
                _valFuelMain.Text = (td[5].FuelLevel_Main * 100).ToString("F0") + " %";
                _valCHTL.Text = td[5].CHT7Left.ToString("F0") + " °C";
                _valCHTR.Text = td[5].CHT7Right.ToString("F0") + " °C";
                UpdateCHTBar(td[5].CHT7Left);
                UpdateCHTRBar(td[5].CHT7Right);
            }
            _valCRCFail.Text = PacketReceiver.CrcFailCounter.ToString();
            this.Text = $"GCS — RxBytes:{PacketReceiver.RawBytesCount}  CRCFail:{PacketReceiver.CrcFailCounter}";
            _valPktNum.Text = t1.PacketNumber.ToString();
            if (td[6] != null)
            {
                _valGPSMiss.Text = td[6].GPSMissPacketsCount.ToString();
                _valVGMiss.Text = td[6].VGMissPacketsCount.ToString();
            }
            if (td[7] != null)
            {
                _valGPSRate.Text = td[7].GPSPacketRate.ToString("F0") + " pkt/s";
                _valVGRate.Text = td[7].VGPacketRate.ToString("F0") + " pkt/s";
            }

            // ── CMD LINK TX / RX rate label ──────────────────────────────
            if (_lblCmdRate != null)
            {
                string rxPart = (td[7] != null)
                    ? $"   RX {td[7].CmdPacketRate:F0} pkt/s"
                    : "";

                if (_cmdSender != null && _cmdSender.IsRunning)
                {
                    bool rxOk = td[7] != null && td[7].CmdPacketRate >= 5.0;
                    _lblCmdRate.Text      = $"CMD LINK: ON  ▶  TX {_cmdSender.TxRatePps} pkt/s{rxPart}";
                    _lblCmdRate.ForeColor = rxOk ? Color.FromArgb(0, 200, 0) : Color.Orange;
                }
                else
                {
                    _lblCmdRate.Text      = $"CMD LINK: OFF{rxPart}";
                    _lblCmdRate.ForeColor = Color.Gray;
                }
            }

            // ── Push all decoded fields to Form2 secondary display ───────
            // Skip entirely if Form2 is not open/visible.
            // Wrap in BeginUpdate/EndUpdate so 177+ label changes trigger
            // one repaint instead of one per label.
            if (_form2 != null && !_form2.IsDisposed && _form2.Visible)
            {
                _form2.BeginUpdate();

                PushForm2_FullRate(td[0]);
                PushForm2_HR1(td[0]);
                PushForm2_LR1(td[0]);
                if (td[1] != null) { PushForm2_HR2(td[1]); PushForm2_LR2(td[1]); }
                if (td[2] != null) PushForm2_LR3(td[2]);
                if (td[3] != null) PushForm2_LR4(td[3]);
                if (td[4] != null) PushForm2_LR5(td[4]);
                if (td[5] != null) PushForm2_LR6(td[5]);
                if (td[6] != null) PushForm2_LR7(td[6]);
                if (td[7] != null) PushForm2_LR8(td[7]);
                _form2.EndUpdate();
                _form2.UpdatePlotData(td[0]);  // feeds all 5 live plots

                // Merge flag bytes from each frame into the accumulator, then push once.
                // (Flag bytes arrive on different low-rate frames so no single td[n] has them all.)
                _latestFlags.SystemStatusByte1  = td[0].SystemStatusByte1;
                _latestFlags.SystemStatusByte2  = td[0].SystemStatusByte2;
                if (td[2] != null) { _latestFlags.MCFlagByte1 = td[2].MCFlagByte1; _latestFlags.MCFlagByte2 = td[2].MCFlagByte2; _latestFlags.MCFlagByte3 = td[2].MCFlagByte3; }
                if (td[3] != null) { _latestFlags.MCFlagByte4 = td[3].MCFlagByte4; _latestFlags.MCFlagByte5 = td[3].MCFlagByte5; _latestFlags.SysID7Status7Byte = td[3].SysID7Status7Byte; _latestFlags.System7Status7Byte3 = td[3].System7Status7Byte3; }
                if (td[6] != null) { _latestFlags.SerChTOByte1 = td[6].SerChTOByte1; _latestFlags.SerChTOByte2 = td[6].SerChTOByte2; _latestFlags.MiscFlags1 = td[6].MiscFlags1; _latestFlags.MiscFlags2 = td[6].MiscFlags2; }
                if (td[7] != null) { _latestFlags.CRC_Byte1 = td[7].CRC_Byte1; _latestFlags.CRC_Byte2 = td[7].CRC_Byte2; }
                _form2.UpdateFlags(_latestFlags);
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  Form2 push helpers
        // ════════════════════════════════════════════════════════════════
        private void Upd(string n, double v) => _form2?.UpdateValue(n, (float)v);
        private void Upd(string n, byte   v) => _form2?.UpdateValue(n, (float)v);
        private void UpdRate(string n, double v) => _form2?.UpdateRate(n, (float)v);

        private void PushForm2_FullRate(TelemetryData d)
        {
            Upd("FCC7Time",           d.FCC7Time);
            Upd("FCC_Clock",          d.FCC_Clock);
            Upd("Roll7Rate",          d.Roll7Rate);
            Upd("Pitch7Rate",         d.Pitch7Rate);
            Upd("Yaw7Rate",           d.Yaw7Rate);
            Upd("X7Acceleration",     d.X7Acceleration);
            Upd("Y7Acceleration",     d.Y7Acceleration);
            Upd("Z7Acceleration",     d.Z7Acceleration);
            Upd("Inner_SB_Ail_Com",   d.Inner_SB_Ail_Com);
            Upd("Inner_SB_Ail_Pos",   d.Inner_SB_Ail_Pos);
            Upd("Outer_SB_Ail_Com",   d.Outer_SB_Ail_Com);
            Upd("Outer_SB_Ail_Pos",   d.Outer_SB_Ail_Pos);
            Upd("Inner_Port_Ail_Com", d.Inner_Port_Ail_Com);
            Upd("Inner_Port_Ail_Pos", d.Inner_Port_Ail_Pos);
            Upd("Outer_Port_Ail_Com", d.Outer_Port_Ail_Com);
            Upd("Outer_Port_Ail_Pos", d.Outer_Port_Ail_Pos);
            Upd("Top_SB_RV_Com",      d.Top_SB_RV_Com);
            Upd("Top_SB_RV_Pos",      d.Top_SB_RV_Pos);
            Upd("Bottom_SB_RV_Com",   d.Bottom_SB_RV_Com);
            Upd("Bottom_SB_RV_Pos",   d.Bottom_SB_RV_Pos);
            Upd("Top_Port_RV_Com",    d.Top_Port_RV_Com);
            Upd("Top_Port_RV_Pos",    d.Top_Port_RV_Pos);
            Upd("Bottom_Port_RV_Com", d.Bottom_Port_RV_Com);
            Upd("Bottom_Port_RV_Pos", d.Bottom_Port_RV_Pos);
            Upd("NW_Com",             d.NW_Com);
            Upd("NW_Pos",             d.NW_Pos);
            Upd("Roll7Angle",         d.Roll7Angle);
            Upd("Pitch7Angle",        d.Pitch7Angle);
            Upd("Angle7of7Attack",    d.Angle7of7Attack);
            Upd("Sideslip7Angle",     d.Sideslip7Angle);
            Upd("Phi_Com",            d.Phi_Com);
            Upd("LOS_Azimuth",        d.LOS_Azimuth);
            Upd("LOS_Elev",           d.LOS_Elev);
            Upd("FoV",                d.FoV);
            Upd("Batt_Volts",         d.Batt_Volts);
            Upd("Alt_Volts",          d.Alt_Volts);
            Upd("AltCurrent",         d.AltCurrent);
            Upd("BatCurrent",         d.BatCurrent);
            Upd("Q_Cmd",              d.Q_Cmd);
            Upd("ZAccel_Cmd",         d.ZAccel_Cmd);
            Upd("Theta_Cmd",          d.Theta_Cmd);
        }

        private void PushForm2_HR1(TelemetryData d)
        {
            Upd("Heading",           d.Heading);
            Upd("Latitude",          d.Latitude);
            Upd("Longitude",         d.Longitude);
            Upd("CAS",               d.CAS);
            Upd("PS",                d.PS);
            Upd("PD",                d.PD);
            Upd("TAS",               d.TAS);
            Upd("Hp",                d.Hp);
            Upd("Throttle_com",      d.Throttle_com);
            Upd("Throttle_pos",      d.Throttle_pos);
            Upd("Radar_Alt",         d.Radar_Alt);
            Upd("Psi_Com",           d.Psi_Com);
            Upd("CAS_Com",           d.CAS_Com);
            Upd("Alt_Com",           d.Alt_Com);
            Upd("Slew_Rate_Alt_Com", d.Slew_Rate_Alt_Com);
            Upd("AGL",               d.AGL);
        }

        private void PushForm2_HR2(TelemetryData d)
        {
            Upd("Servo01Current",    d.Servo01Current);
            Upd("Servo02Current",    d.Servo02Current);
            Upd("Servo03Current",    d.Servo03Current);
            Upd("Servo04Current",    d.Servo04Current);
            Upd("Servo05Current",    d.Servo05Current);
            Upd("Servo06Current",    d.Servo06Current);
            Upd("Servo07Current",    d.Servo07Current);
            Upd("Servo08Current",    d.Servo08Current);
            Upd("Servo09Current",    d.Servo09Current);
            Upd("Servo10Current",    d.Servo10Current);
            Upd("OffTrack",          d.OffTrack);
            Upd("GCS_Ail_Cmd",       d.GCS_Ail_Cmd);
            Upd("GCS_Elev_Cmd",      d.GCS_Elev_Cmd);
            Upd("GCS_Rudd_Cmd",      d.GCS_Rudd_Cmd);
            Upd("GCS_Throttle_Cmd",  d.GCS_Throttle_Cmd);
            Upd("GCS_Brake_Cmd",     d.GCS_Brake_Cmd);
            Upd("GCS_NW_Cmd",        d.GCS_NW_Cmd);
            Upd("GCS_Roll_Cmd",      d.GCS_Roll_Cmd);
            Upd("MC_Elev_Cmd",       d.MC_Elev_Cmd);
            Upd("MC_Ail_Cmd",        d.MC_Ail_Cmd);
            Upd("MC_Rudd_Cmd",       d.MC_Rudd_Cmd);
            Upd("MC_Throt_Cmd",      d.MC_Throt_Cmd);
            Upd("GPS_Alt",           d.GPS_Alt);
            Upd("SystemStatusByte1", d.SystemStatusByte1);
            Upd("SystemStatusByte2", d.SystemStatusByte2);
            Upd("OffGlide",          d.OffGlide);
        }

        private void PushForm2_LR1(TelemetryData d)
        {
            Upd("Mach",                  d.Mach);
            Upd("Static7Air7Temp",       d.Static7Air7Temp);
            Upd("Total7Air7Temp",        d.Total7Air7Temp);
            Upd("X_mag",                 d.X_mag);
            Upd("Y_mag",                 d.Y_mag);
            Upd("Z_mag",                 d.Z_mag);
            Upd("Mag_heading",           d.Mag_heading);
            Upd("Yaw7Rate7Offset",       d.Yaw7Rate7Offset);
            Upd("Roll7Rate7Offset",      d.Roll7Rate7Offset);
            Upd("Pitch7Rate7Offset",     d.Pitch7Rate7Offset);
            Upd("Pressure7Alt7Offset",   d.Pressure7Alt7Offset);
            Upd("Engine_RPM",            d.Engine_RPM);
            Upd("Engine_RPM_filt",       d.Engine_RPM_filt);
            Upd("REFERENCE_L",           d.REFERENCE_L);
            Upd("AIR_KXTrack",           d.AIR_KXTrack);
            Upd("AIR_Kpsi",              d.AIR_Kpsi);
            Upd("AIR_Kphi",              d.AIR_Kphi);
            Upd("GND_KXTrack_LL",        d.GND_KXTrack_LL);
            Upd("GND_KXTrack_UL",        d.GND_KXTrack_UL);
            Upd("GND_Kpsi",              d.GND_Kpsi);
            Upd("GND_Kr",                d.GND_Kr);
        }

        private void PushForm2_LR2(TelemetryData d)
        {
            Upd("North7Speed",           d.North7Speed);
            Upd("East7Speed",            d.East7Speed);
            Upd("Up7Speed",              d.Up7Speed);
            Upd("D2Go",                  d.D2Go);
            Upd("Course7Bearing",        d.Course7Bearing);
            Upd("Required7Bearing",      d.Required7Bearing);
            Upd("D2Go2TouchDownP",       d.D2Go2TouchDownP);
            Upd("Dist_Travelled",        d.Dist_Travelled);
            Upd("dPsiAtDestWP",          d.dPsiAtDestWP);
            Upd("Distance7From7Base",    d.Distance7From7Base);
            Upd("NW_CMD_LIMIT",          d.NW_CMD_LIMIT);
            Upd("AIR_Kh",                d.AIR_Kh);
            Upd("KH_GST",                d.KH_GST);
            Upd("Kp",                    d.Kp);
            Upd("K_NL_RollControl",      d.K_NL_RollControl);
            Upd("Lambda_NL_RollControl", d.Lambda_NL_RollControl);
            Upd("KIPHI",                 d.KIPHI);
            Upd("KIH_Enroute",           d.KIH_Enroute);
            Upd("KIH_GST",               d.KIH_GST);
            Upd("KTHETA",                d.KTHETA);
            Upd("KQ",                    d.KQ);
            Upd("ROC_CMD_Limit",         d.ROC_CMD_Limit);
        }

        private void PushForm2_LR3(TelemetryData d)
        {
            Upd("Next7Course7Bearing",        d.Next7Course7Bearing);
            Upd("Next7WayPoint7Number",       d.Next7WayPoint7Number);
            Upd("Prev7WayPoint7Number",       d.Prev7WayPoint7Number);
            Upd("Turning7Dist2WP",            d.Turning7Dist2WP);
            Upd("Curve7Guidance7Angle",       d.Curve7Guidance7Angle);
            Upd("Turn7Radius",                d.Turn7Radius);
            Upd("Time7Since7TakeOff",         d.Time7Since7TakeOff);
            Upd("MCFlagByte1",                d.MCFlagByte1);
            Upd("MCFlagByte2",                d.MCFlagByte2);
            Upd("MCFlagByte3",                d.MCFlagByte3);
            Upd("Flight7Phase",               d.Flight7Phase);
            Upd("Flight7Phase7Change7Reason", d.Flight7Phase7Change7Reason);
            Upd("WakeUp7Miss",                d.WakeUp7Miss);
            Upd("Differential7Age",           d.Differential7Age);
            Upd("Height7Delta",               d.Height7Delta);
            Upd("Speed7Delta",                d.Speed7Delta);
            Upd("ROC_Cmd_AP",                 d.ROC_Cmd_AP);
            Upd("ROC_Estimated",              d.ROC_Estimated);
            Upd("Pitch7Rate7AftLead",         d.Pitch7Rate7AftLead);
            Upd("H_err",                      d.H_err);
        }

        private void PushForm2_LR4(TelemetryData d)
        {
            Upd("MCFlagByte4",           d.MCFlagByte4);
            Upd("MCFlagByte5",           d.MCFlagByte5);
            Upd("SysID7Status7Byte",     d.SysID7Status7Byte);
            Upd("System7Status7Byte3",   d.System7Status7Byte3);
            Upd("Sensors7Current",       d.Sensors7Current);
            Upd("PFCU7Current_LR4",      d.PFCU7Current_LR4);
            Upd("BFCU7Current",          d.BFCU7Current);
            Upd("Current5V",             d.Current5V);
            Upd("Current12V",            d.Current12V);
            Upd("Pitot7Heater7Current",  d.Pitot7Heater7Current);
            Upd("Payload7Current",       d.Payload7Current);
            Upd("DLPA7Current",          d.DLPA7Current);
            Upd("Sensor7Volts",          d.Sensor7Volts);
            Upd("Brake7Servo7Pos",       d.Brake7Servo7Pos);
            Upd("NLG7Pos",               d.NLG7Pos);
            Upd("Abort7Taxi7Reason",     d.Abort7Taxi7Reason);
            Upd("Abort7Landing7Reason",  d.Abort7Landing7Reason);
            Upd("Turn7Compensation",     d.Turn7Compensation);
        }

        private void PushForm2_LR5(TelemetryData d)
        {
            Upd("PFCU7Volts",        d.PFCU7Volts);
            Upd("Servo7Volts",       d.Servo7Volts);
            Upd("Volts12V",          d.Volts12V);
            Upd("Volts5V",           d.Volts5V);
            Upd("Payload7Volts",     d.Payload7Volts);
            Upd("IPSU7Temp",         d.IPSU7Temp);
            Upd("Relay7Status7Byte1",d.Relay7Status7Byte1);
            Upd("Relay7Status7Byte2",d.Relay7Status7Byte2);
            Upd("Relay7Status7Byte3",d.Relay7Status7Byte3);
            Upd("IPSU7Status7Byte1", d.IPSU7Status7Byte1);
            Upd("IPSU7Status7Byte2", d.IPSU7Status7Byte2);
            Upd("IPSU_Cmd_FBByte",   d.IPSU_Cmd_FBByte);
            Upd("Heading_Cmd",       d.Heading_Cmd);
            Upd("CPU7Temp",          d.CPU7Temp);
            Upd("Solution7Status",   d.Solution7Status);
            Upd("Satellites",        d.Satellites);
            Upd("BITStatus",         d.BITStatus);
            Upd("GCSEchoTime",       d.GCSEchoTime);
        }

        private void PushForm2_LR6(TelemetryData d)
        {
            Upd("GCS_Alt_Cmd",          d.GCS_Alt_Cmd);
            Upd("GCS_CAS_Cmd",          d.GCS_CAS_Cmd);
            Upd("Injection7Time",       d.Injection7Time);
            Upd("Battery7Voltage_ECU",  d.Battery7Voltage_ECU);
            Upd("CHT7Left",             d.CHT7Left);
            Upd("CHT7Right",            d.CHT7Right);
            Upd("EGT7Left",             d.EGT7Left);
            Upd("EGT7Right",            d.EGT7Right);
            Upd("Fuel7Pressure",        d.Fuel7Pressure);
            Upd("FuelLevel_Main",       d.FuelLevel_Main);
            Upd("FuelLevel_Left",       d.FuelLevel_Left);
            Upd("FuelLevel_Right",      d.FuelLevel_Right);
            Upd("Lat_Std",              d.Lat_Std);
            Upd("Lon_Std",              d.Lon_Std);
            Upd("Alt_Std",              d.Alt_Std);
            Upd("Latitude7DeadReck",    d.Latitude7DeadReck);
            Upd("Longitude7DeadReck",   d.Longitude7DeadReck);
        }

        private void PushForm2_LR7(TelemetryData d)
        {
            Upd("Fuel7Flow7Rate",         d.Fuel7Flow7Rate);
            Upd("MC_Brake_Cmd",           d.MC_Brake_Cmd);
            Upd("FailSafeContFlagByte",   d.FailSafeContFlagByte);
            Upd("FCC7Temp",               d.FCC7Temp);
            Upd("SerChTOByte1",           d.SerChTOByte1);
            Upd("SerChTOByte2",           d.SerChTOByte2);
            Upd("CmdMissPacketsCount",    d.CmdMissPacketsCount);
            Upd("GCS_VCMissPacketsCount", d.GCS_VCMissPacketsCount);
            Upd("GCS_PLMissPacketsCount", d.GCS_PLMissPacketsCount);
            Upd("VGMissPacketsCount",     d.VGMissPacketsCount);
            Upd("GPSMissPacketsCount",    d.GPSMissPacketsCount);
            Upd("MagMissPacketsCount",    d.MagMissPacketsCount);
            Upd("GCS_MPMissPacketsCount", d.GCS_MPMissPacketsCount);
            Upd("ADSMissPacketsCount",    d.ADSMissPacketsCount);
            Upd("ECUMissPacketsCount",    d.ECUMissPacketsCount);
            Upd("FSMissPacketsCount",     d.FSMissPacketsCount);
            Upd("RAMissPacketsCount",     d.RAMissPacketsCount);
            Upd("PLMissPacketsCount",     d.PLMissPacketsCount);
            Upd("IPSUMissPacketsCount",   d.IPSUMissPacketsCount);
            Upd("IFCCMissPacketsCount",   d.IFCCMissPacketsCount);
            Upd("SBUSMissPacketsCount",   d.SBUSMissPacketsCount);
            Upd("DGPS_Corr_MPC",          d.DGPS_Corr_MPC);
            Upd("Telemetry_MPC",          d.Telemetry_MPC);
            Upd("MiscFlags1",             d.MiscFlags1);
            Upd("Loiter7Duration",        d.Loiter7Duration);
            Upd("Loiter7Timer",           d.Loiter7Timer);
            Upd("Nspeed7DeadReck",        d.Nspeed7DeadReck);
            Upd("Espeed7DeadReck",        d.Espeed7DeadReck);
            Upd("MiscFlags2",             d.MiscFlags2);
            Upd("GCS_Packet_Number",      d.GCS_Packet_Number);
        }

        private void PushForm2_LR8(TelemetryData d)
        {
            UpdRate("CmdPacketRate",    d.CmdPacketRate);
            UpdRate("VGPacketRate",     d.VGPacketRate);
            UpdRate("GPSPacketRate",    d.GPSPacketRate);
            UpdRate("MagPacketRate",    d.MagPacketRate);
            UpdRate("GCS_VCPacketRate", d.GCS_VCPacketRate);
            UpdRate("GCS_MPPacketRate", d.GCS_MPPacketRate);
            UpdRate("GCS_PLPacketRate", d.GCS_PLPacketRate);
            UpdRate("ADSPacketRate",    d.ADSPacketRate);
            UpdRate("ECUPacketRate",    d.ECUPacketRate);
            UpdRate("FSPacketRate",     d.FSPacketRate);
            UpdRate("RAPacketRate",     d.RAPacketRate);
            UpdRate("PLPacketRate",     d.PLPacketRate);
            UpdRate("IPSUPacketRate",   d.IPSUPacketRate);
            UpdRate("IFCCPacketRate",   d.IFCCPacketRate);
            UpdRate("SBUSPacketRate",   d.SBUSPacketRate);
            UpdRate("DGPSCorrRate",     d.DGPSCorrRate);
            Upd("DGPS7Packet7Counter", d.DGPS7Packet7Counter);
            Upd("DGPS7Packet7Number",  d.DGPS7Packet7Number);
        }
        // ════════════════════════════════════════════════════════════════
        //  BuildUI
        // ════════════════════════════════════════════════════════════════
        private void BuildUI()
        {
            this.Text = "GCS_240626 — Ground Control Station";
            this.ClientSize = new Size(1900, 1100);
            this.MinimumSize = new Size(1900, 1140);
            this.BackColor = BLACK;
            this.ForeColor = LIME;
            this.Font = FNT14;
            _combined = new AxCombined();
            _combined.Location = new Point(690, 51);
            _combined.Size = new Size(530, 437);
            ((System.ComponentModel.ISupportInitialize)_combined).BeginInit();
            this.Controls.Add(_combined);
            ((System.ComponentModel.ISupportInitialize)_combined).EndInit();
            BuildDemoButton();
            BuildDemoSliders();
            BuildMapPanel();
            BuildFlightBlock();
            BuildSidePanels();   // must run first — sets _btnHeading/_btnRpm used by BuildRightPanel
            BuildRpmIndicator();
            BuildRightPanel();
            BuildCHTBar();
            BuildCHTRBar();
            BuildCHTRRuler();
        }
        // ════════════════════════════════════════════════════════════════
        //  Top-bar button helper (shared style)
        // ════════════════════════════════════════════════════════════════
        private Button MakeTopBtn(string text, int x, int y, Color fg, Color bg)
        {
            var btn = new Button
            {
                Text      = text,
                Location  = new Point(x, y),
                Size      = new Size(90, 26),
                BackColor = bg,
                ForeColor = fg,
                Font      = new Font("Consolas", 9f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
            };
            btn.FlatAppearance.BorderColor = Color.FromArgb(0, 120, 0);
            btn.FlatAppearance.BorderSize  = 1;
            return btn;
        }

        // ════════════════════════════════════════════════════════════════
        //  DEMO button
        // ════════════════════════════════════════════════════════════════
        private void BuildDemoButton()
        {
            // ── Horizontal toolbar — full-width at top of form ───────────
            const int TB_X  = 0;
            const int TB_W  = 1900;
            const int TB_H  = 40;
            const int BTN_H = 26;
            const int BTN_Y_REL = (TB_H - BTN_H) / 2;  // = 4

            _toolBar = new Panel
            {
                Location  = new Point(TB_X, 0),
                Size      = new Size(TB_W, TB_H),
                BackColor = Color.FromArgb(20, 25, 20),
                Anchor    = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            this.Controls.Add(_toolBar);

            // ── Right-aligned buttons — positions computed from right edge ─
            // Order right→left: DEMO | SCR2 | PKT MON
            const int R_MARGIN  = 5;
            const int BTN_GAP   = 4;
            const int W_DEMO    = 55;
            const int W_SCR2    = 55;
            const int W_PKTMON  = 72;

            int xDemo   = TB_W - R_MARGIN - W_DEMO;
            int xScr2   = xDemo   - BTN_GAP - W_SCR2;
            int xPktMon = xScr2   - BTN_GAP - W_PKTMON;

            // ── PKT MON ───────────────────────────────────────────────────
            var btnPktMon = new Button
            {
                Text      = "PKT MON",
                Location  = new Point(xPktMon, BTN_Y_REL),
                Size      = new Size(W_PKTMON, BTN_H),
                BackColor = Color.FromArgb(0, 160, 0),
                ForeColor = Color.White,
                Font      = new Font("Consolas", 9f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Anchor    = AnchorStyles.Top | AnchorStyles.Right
            };
            btnPktMon.FlatAppearance.BorderColor = Color.FromArgb(0xC0, 0xC0, 0xC0);
            btnPktMon.FlatAppearance.BorderSize  = 1;
            btnPktMon.Click += (s, e) =>
            {
                if (_pktMon == null || _pktMon.IsDisposed)
                {
                    _pktMon = new PacketMonitorForm();
                    _pktMon.Show();
                }
                else
                {
                    _pktMon.BringToFront();
                }
            };
            _toolBar.Controls.Add(btnPktMon);

            // ── SCR2 ──────────────────────────────────────────────────────
            var btnScr2 = new Button
            {
                Text      = "SCR2",
                Location  = new Point(xScr2, BTN_Y_REL),
                Size      = new Size(W_SCR2, BTN_H),
                BackColor = Color.FromArgb(0, 160, 0),
                ForeColor = Color.White,
                Font      = new Font("Consolas", 11f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Anchor    = AnchorStyles.Top | AnchorStyles.Right
            };
            btnScr2.FlatAppearance.BorderColor = Color.FromArgb(0xC0, 0xC0, 0xC0);
            btnScr2.FlatAppearance.BorderSize  = 1;
            btnScr2.Click += (s, e) =>
            {
                if (_form2 == null || _form2.IsDisposed)
                {
                    _form2 = new Form2();
                    _form2.Show();
                }
                else
                {
                    _form2.BringToFront();
                }
            };
            _toolBar.Controls.Add(btnScr2);

            // ── DEMO ──────────────────────────────────────────────────────
            _btnDemo = new Button
            {
                Text      = "DEMO",
                Location  = new Point(xDemo, BTN_Y_REL),
                Size      = new Size(W_DEMO, BTN_H),
                BackColor = Color.FromArgb(0, 160, 0),
                ForeColor = Color.White,
                Font      = new Font("Consolas", 11f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Anchor    = AnchorStyles.Top | AnchorStyles.Right
            };
            _btnDemo.FlatAppearance.BorderColor = Color.FromArgb(0xC0, 0xC0, 0xC0);
            _btnDemo.FlatAppearance.BorderSize  = 1;
            _btnDemo.Click += BtnDemo_Click;
            _toolBar.Controls.Add(_btnDemo);
        }
        // ════════════════════════════════════════════════════════════════
        //  DEMO slider panel
        // ════════════════════════════════════════════════════════════════
        private void BuildDemoSliders()
        {
            _pnlSliders = new Panel
            {
                Location = new Point(690, 508),
                Size = new Size(530, 568),
                BackColor = Color.FromArgb(18, 18, 18),
                BorderStyle = BorderStyle.FixedSingle,
                Visible = false
            };
            _pnlSliders.Controls.Add(new Label
            {
                Text = "── DEMO INPUTS ──",
                Location = new Point(0, 4),
                Size = new Size(380, 22),
                ForeColor = Color.Magenta,
                BackColor = Color.Transparent,
                Font = FNT11,
                TextAlign = ContentAlignment.MiddleCenter
            });
            int y = 30;
            _tbAirspeed = DemoSlider(_pnlSliders, "Airspeed (m/s)", 0, 100, 0, y, out _lblDemoAS); y += 66;
            _tbAltitude = DemoSlider(_pnlSliders, "Altitude   (m)", -1000, 11000, 0, y, out _lblDemoAlt); y += 66;
            _tbHeading = DemoSlider(_pnlSliders, "Heading  (deg)", 0, 360, 0, y, out _lblDemoHdg); y += 66;
            _tbPitch = DemoSlider(_pnlSliders, "Pitch    (deg)", -30, 30, 0, y, out _lblDemoPitch); y += 66;
            _tbRoll = DemoSlider(_pnlSliders, "Roll     (deg)", -60, 60, 0, y, out _lblDemoRoll); y += 66;
            _tbCHTLeft  = DemoSlider(_pnlSliders, "CHT Left  (°C)", 0, 300, 0, y, out _lblDemoCHT);  y += 66;
            _tbCHTRight = DemoSlider(_pnlSliders, "CHT Right (°C)", 0, 300, 0, y, out _lblDemoCHTR);
            this.Controls.Add(_pnlSliders);
        }
        // ════════════════════════════════════════════════════════════════
        //  Map
        // ════════════════════════════════════════════════════════════════
        private void BuildMapPanel()
        {
            _mapCtrl = new MapControl
            {
                Location = new Point(10, 510),
                Size = new Size(1350, 535)   // right edge = 1360; ends 75px from form bottom
            };
            this.Controls.Add(_mapCtrl);

            // Wire mission serial upload + download
            _mapCtrl.MissionSendRequested += OnMissionSendRequested;

            // ── Mission toolbar — right-aligned inside _toolBar ──────────────
            // Right-to-left order: DEMO | SCR2 | PKT MON | [gap] | Send log | Cache | Send | Mission
            // Mirrors the constants used in BuildDemoButton:
            //   TB_W=1900, xPktMon = 1900-5-55-4-55-4-72 = 1705
            const int MTB_Y   = 7;    // button y inside toolbar
            const int MTB_H   = 26;   // button height
            const int MTB_GAP = 4;    // gap between map buttons
            const int GRP_GAP = 8;    // extra gap between map group and PKT MON group

            const int X_PKTMON   = 1705;   // set by BuildDemoButton; repeated here for alignment
            int xSendLog = X_PKTMON  - GRP_GAP - 82;   // 1615
            int xCache   = xSendLog  - MTB_GAP - 72;   // 1539
            int xSend    = xCache    - MTB_GAP - 72;   // 1463
            int xMission = xSend     - MTB_GAP - 82;   // 1377

            _btnMapMission = new Button
            {
                Text      = "Mission",
                Location  = new Point(xMission, MTB_Y),
                Size      = new Size(82, MTB_H),
                BackColor = Color.FromArgb(0, 160, 0),
                ForeColor = Color.White,
                Font      = new Font("Consolas", 9f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Anchor    = AnchorStyles.Top | AnchorStyles.Right
            };
            _btnMapMission.FlatAppearance.BorderColor = Color.FromArgb(0xC0, 0xC0, 0xC0);
            _btnMapMission.FlatAppearance.BorderSize  = 1;
            _btnMapMission.Click += (s, e) => _mapCtrl.InvokeLoadMission();

            _btnMapSend = new Button
            {
                Text      = "Send",
                Location  = new Point(xSend, MTB_Y),
                Size      = new Size(72, MTB_H),
                BackColor = Color.FromArgb(0, 160, 0),
                ForeColor = Color.White,
                Font      = new Font("Consolas", 9f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Enabled   = false,
                Anchor    = AnchorStyles.Top | AnchorStyles.Right
            };
            _btnMapSend.FlatAppearance.BorderColor = Color.FromArgb(0xC0, 0xC0, 0xC0);
            _btnMapSend.FlatAppearance.BorderSize  = 1;
            _btnMapSend.Click += (s, e) => _mapCtrl.InvokeSendMission();

            _btnMapCache = new Button
            {
                Text      = "Cache",
                Location  = new Point(xCache, MTB_Y),
                Size      = new Size(72, MTB_H),
                BackColor = Color.FromArgb(0, 160, 0),
                ForeColor = Color.White,
                Font      = new Font("Consolas", 9f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Anchor    = AnchorStyles.Top | AnchorStyles.Right
            };
            _btnMapCache.FlatAppearance.BorderColor = Color.FromArgb(0xC0, 0xC0, 0xC0);
            _btnMapCache.FlatAppearance.BorderSize  = 1;
            _btnMapCache.Click += (s, e) => _mapCtrl.InvokeCacheArea();

            _chkSendLog = new CheckBox
            {
                Text      = "Send log",
                Checked   = false,
                Location  = new Point(xSendLog, MTB_Y + 3),
                Size      = new Size(82, 20),
                BackColor = Color.Transparent,
                ForeColor = Color.White,
                Font      = new Font("Consolas", 8f, FontStyle.Bold),
                Cursor    = Cursors.Hand,
                Anchor    = AnchorStyles.Top | AnchorStyles.Right
            };
            _chkSendLog.CheckedChanged += (s, e) => _mapCtrl.ShowSendWindow = _chkSendLog.Checked;

            // Wire enable/disable events back from MapControl
            _mapCtrl.SendEnabledChanged  += on => _btnMapSend.Enabled  = on;
            _mapCtrl.CacheEnabledChanged += on =>
            {
                if (_btnMapCache.InvokeRequired)
                    _btnMapCache.BeginInvoke(new Action(() => _btnMapCache.Enabled = on));
                else
                    _btnMapCache.Enabled = on;
            };

            _toolBar.Controls.Add(_btnMapMission);
            _toolBar.Controls.Add(_btnMapSend);
            _toolBar.Controls.Add(_btnMapCache);
            _toolBar.Controls.Add(_chkSendLog);

            // ── Progress bar — sits just below toolbar on the form ────────
            _barMission = new ProgressBar
            {
                Location  = new Point(1463, 40),   // below Send (1463) .. Cache (1611)
                Size      = new Size(148, 8),
                Minimum   = 0,
                Maximum   = 100,
                Value     = 0,
                Style     = ProgressBarStyle.Continuous,
                Visible   = false,
            };
            this.Controls.Add(_barMission);
        }

        // ── Mission upload handler ─────────────────────────────────────────
        private bool     _uploadInProgress;
        private DateTime _lastTelemetryTime = DateTime.MinValue;

        private void OnMissionSendRequested(MissionData md, byte vehicleId)
        {
            // Workaround: old sgap.lib has VEHICLE_ID=(02)=2 (octal literal, decimal 2).
            // Mission file may carry a different ID — force 2 to match.
            vehicleId = 2;

            // Pre-send connectivity check — abort if no telemetry in the last 2 s.
            if ((DateTime.UtcNow - _lastTelemetryTime).TotalSeconds > 2.0)
            {
                MessageBox.Show(
                    "No Connection",
                    "Not Ready",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (_uploadInProgress)
            {
                MessageBox.Show("Upload already in progress.", "Mission Upload",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _uploadInProgress = true;

            int totalPkts = MissionPacketEncoder.PacketCount(md);
            bool showBar  = _chkSendLog.Checked;   // progress bar only when "Send log" is checked

            var progress = new Progress<MissionUploadProgress>(rpt =>
            {
                _mapCtrl.ReportUploadStatus(
                    $"TX [{rpt.PacketsSent}/{rpt.PacketsTotal}] {rpt.Status}");
                if (showBar && rpt.PacketsTotal > 0)
                {
                    _barMission.Maximum = rpt.PacketsTotal;
                    _barMission.Value   = Math.Min(rpt.PacketsSent, rpt.PacketsTotal);
                    _barMission.Visible = true;
                }
            });

            System.Threading.Tasks.Task.Run(async () =>
            {
                bool ok = await MissionUploader.UploadAsync(
                    md, vehicleId, _receiver, progress).ConfigureAwait(false);

                BeginInvoke(new Action(() =>
                {
                    _uploadInProgress = false;
                    if (ok) _mapCtrl.OnMissionUploaded(md);
                    _mapCtrl.ReportUploadStatus(ok
                        ? $"Upload complete ✓  '{md.Name}'"
                        : $"Upload FAILED  '{md.Name}'");
                    // Keep bar visible for 2 s then hide
                    if (showBar)
                    {
                        var t = new System.Windows.Forms.Timer { Interval = 2000 };
                        t.Tick += (ts, te) => { t.Stop(); t.Dispose(); _barMission.Value = 0; _barMission.Visible = false; };
                        t.Start();
                    }
                }));
            });
        }
        // ════════════════════════════════════════════════════════════════
        //  Flight Block
        // ════════════════════════════════════════════════════════════════
        private void BuildFlightBlock()
        {
            var pnl = new Panel
            {
                Location = new Point(10, 46),
                Size = new Size(580, 442),
                BackColor = BLACK,
                BorderStyle = BorderStyle.FixedSingle
            };
            this.Controls.Add(pnl);
            int y = 8;
            // ── GPS / NAV ─────────────────────────────────────────────────
            FLg(pnl, "Lat:", 5, y, 58, GPS_H, LIME, ContentAlignment.MiddleLeft);
            _valLat = FLg(pnl, "----", 65, y, 100, GPS_H, WHITE, ContentAlignment.MiddleLeft);
            FLg(pnl, "D2BASE:", 163, y, 110, GPS_H, LIME, ContentAlignment.MiddleLeft);
            _valD2Base = FLg(pnl, "----", 280, y, 105, GPS_H, WHITE, ContentAlignment.MiddleLeft);
            FLg(pnl, "GPS:", 385, y, 50, GPS_H, LIME, ContentAlignment.MiddleLeft);
            _valGPS = FLg(pnl, "----", 435, y, 130, GPS_H, WHITE, ContentAlignment.MiddleLeft);
            // FLg(pnl, "[Diff/Single]", 668, y, 200, GPS_H, CYAN, ContentAlignment.MiddleLeft);
            y += GPS_ROW;
            FLg(pnl, "Lon:", 5, y, 58, GPS_H, LIME, ContentAlignment.MiddleLeft);
            _valLon = FLg(pnl, "----", 65, y, 95, GPS_H, WHITE, ContentAlignment.MiddleLeft);
            FLg(pnl, "OFFTRACK:", 163, y, 105, GPS_H, LIME, ContentAlignment.MiddleLeft);
            _valOffTrack = FLg(pnl, "----", 276, y, 110, GPS_H, WHITE, ContentAlignment.MiddleLeft);
            FLg(pnl, "WP:", 385, y, 60, GPS_H, LIME, ContentAlignment.MiddleLeft);
            _valWP = FLg(pnl, "----", 445, y, 130, GPS_H, WHITE, ContentAlignment.MiddleLeft);
            y += GPS_ROW + 3;
            Sep(pnl, 5, y, 568);
            // ── ATTITUDE ─────────────────────────────────────────────────
            y += 7;
            FL(pnl, "Sensor in Use", FL_R_NX, y, 150, FL_H, CYAN);
            FL(pnl, "[VG440]", FL_R_NX + 145, y, 120, FL_H, WHITE);
            FLn(pnl, "Roll Angle:", FL_NX, y, FL_NW, FL_H, LIME, ContentAlignment.MiddleLeft);
            _valRollAngle = FL(pnl, "----", FL_VX, y, FL_VW, FL_H, WHITE);
            y += FL_ROW;
            FLn(pnl, "Roll CMD:", FL_NX, y, FL_NW, FL_H, LIME, ContentAlignment.MiddleLeft);
            _valRollCmd = FL(pnl, "----", FL_VX, y, FL_VW, FL_H, WHITE);
            FLn(pnl, "Aileron:", FL_R_NX, y, FL_R_NW, FL_H, LIME, ContentAlignment.MiddleLeft);
            _valAileron = FL(pnl, "----", FL_R_VX, y, FL_R_VW, FL_H, WHITE);
            y += FL_ROW;
            FLn(pnl, "Pitch Angle:", FL_NX, y, FL_NW, FL_H, LIME, ContentAlignment.MiddleLeft);
            _valPitchAngle = FL(pnl, "----", FL_VX, y, FL_VW, FL_H, WHITE);
            FLn(pnl, "Flap:", FL_R_NX, y, FL_R_NW, FL_H, LIME, ContentAlignment.MiddleLeft);
            _valFlap = FL(pnl, "----", FL_R_VX, y, FL_R_VW, FL_H, WHITE);
            y += FL_ROW;
            FLn(pnl, "Pitch CMD:", FL_NX, y, FL_NW, FL_H, LIME, ContentAlignment.MiddleLeft);
            _valPitchCmd = FL(pnl, "----", FL_VX, y, FL_VW, FL_H, WHITE);
            y += FL_ROW;
            FLn(pnl, "Heading:", FL_NX, y, FL_NW, FL_H, LIME, ContentAlignment.MiddleLeft);
            _valHeading = FL(pnl, "----", FL_VX, y, FL_VW, FL_H, WHITE);
            FL(pnl, "Sensor in Use:", FL_R_NX, y, 158, FL_H, CYAN);
            FL(pnl, "[GPS/Magneto]", FL_R_NX + 161, y, 200, FL_H, WHITE);
            y += FL_ROW;
            FLn(pnl, "Heading CMD:", FL_NX, y, FL_NW, FL_H, LIME, ContentAlignment.MiddleLeft);
            _valHeadingCmd = FL(pnl, "----", FL_VX, y, FL_VW, FL_H, WHITE);
            y += FL_ROW + 4; Sep(pnl, 5, y, 568);
            // ── HEIGHT ───────────────────────────────────────────────────
            y += 7;
            FLn(pnl, "Height (MSL):", FL_NX, y, FL_NW, FL_H, LIME, ContentAlignment.MiddleLeft);
            _valHMSL = FL(pnl, "----", FL_VX, y, FL_VW, FL_H, WHITE);
            FL(pnl, "Sensor in Use:", FL_R_NX, y, 150, FL_H, CYAN);
            FL(pnl, "[GPS/ADS]", FL_R_NX + 145, y, 160, FL_H, WHITE);
            y += FL_ROW;
            FLn(pnl, "Height CMD:", FL_NX, y, FL_NW, FL_H, LIME, ContentAlignment.MiddleLeft);
            _valHCmd = FL(pnl, "----", FL_VX, y, FL_VW, FL_H, WHITE);
            FLn(pnl, "Elevator:", FL_R_NX, y, FL_R_NW, FL_H, LIME, ContentAlignment.MiddleLeft);
            _valElevator = FL(pnl, "----", FL_R_VX, y, FL_R_VW, FL_H, WHITE);
            y += FL_ROW;
            FLn(pnl, "Height AGL:", FL_NX, y, FL_NW, FL_H, LIME, ContentAlignment.MiddleLeft);
            _valHAGL = FL(pnl, "----", FL_VX, y, FL_VW, FL_H, WHITE);
            FLn(pnl, "ROC:", FL_R_NX, y, FL_R_NW, FL_H, LIME, ContentAlignment.MiddleLeft);
            _valROC = FL(pnl, "----", FL_R_VX, y, FL_R_VW, FL_H, WHITE);
            y += FL_ROW;
            FLn(pnl, "RA:", FL_NX, y, FL_NW, FL_H, LIME, ContentAlignment.MiddleLeft);
            _valRA = FL(pnl, "----", FL_VX, y, FL_VW, FL_H, WHITE);
            y += FL_ROW + 4; Sep(pnl, 5, y, 568);
            // ── SPEED ────────────────────────────────────────────────────
            y += 7;
            FLn(pnl, "Speed:", FL_NX, y, FL_NW, FL_H, LIME, ContentAlignment.MiddleLeft);
            _valSpeed = FL(pnl, "----", FL_VX, y, FL_VW, FL_H, WHITE);
            FL(pnl, "Sensor in Use:", FL_R_NX, y, 150, FL_H, CYAN);
            FL(pnl, "[ADS]", FL_R_NX + 145, y, 100, FL_H, WHITE);
            y += FL_ROW;
            FLn(pnl, "Speed CMD:", FL_NX, y, FL_NW, FL_H, LIME, ContentAlignment.MiddleLeft);
            _valSpeedCmd = FL(pnl, "----", FL_VX, y, FL_VW, FL_H, WHITE);
            y += FL_ROW;
            FLn(pnl, "Throttle:", FL_NX, y, FL_NW, FL_H, LIME, ContentAlignment.MiddleLeft);
            _valThrottle = FL(pnl, "----", FL_VX, y, FL_VW, FL_H, WHITE);
        }
        // ════════════════════════════════════════════════════════════════
        //  Right column: Engine & Power + Link Health
        //  ROW=24, H=24 → last ENGINE row bottom = 34 + 7×24 + 24 = 226
        //  ENGINE height = 230  (226 + 4 margin)
        //  LINK y        = 10 + 230 + 6 = 246
        //  LINK last row bottom = 34 + 5×24 + 24 = 178
        //  LINK height   = 182  (178 + 4 margin)
        // ════════════════════════════════════════════════════════════════
        // ════════════════════════════════════════════════════════════════
        //  Side button panels — left and right of the PFD
        //  Left  panel : x=592, w=96  (gap between FlightBlock x+w=590 and PFD x=690)
        //  Right panel : x=1222, w=106 (gap between PFD x+w=1220 and ENGINE x=1330)
        //  Both share same height as PFD (h=442). Buttons: 80×55, 6 rows, centred.
        // ════════════════════════════════════════════════════════════════
        private void BuildSidePanels()
        {
            const int PNL_H = 442;
            const int BW = 80, BH = 55;
            const int ROWS = 6;
            int vGap = (PNL_H - ROWS * BH) / (ROWS + 1);  // = 16 — equal spacing top/between/bottom
            // ── Left panel ───────────────────────────────────────────────
            var pnlL = new Panel
            {
                Location = new Point(592, 46),
                Size = new Size(96, PNL_H),
                BackColor = Color.FromArgb(12, 12, 12),
                BorderStyle = BorderStyle.FixedSingle
            };
            int lbx = (96 - BW) / 2;   // = 8
            for (int r = 0; r < ROWS; r++)
            {
                int by = vGap + r * (BH + vGap);
                pnlL.Controls.Add(new GcsButton
                {
                    Text    = $"L{r + 1}",
                    Location = new Point(lbx, by),
                    Size    = new Size(BW, BH),
                    ForeColor = LIME,
                    Font    = FNT11,
                    ShowLed = false
                });
            }
            this.Controls.Add(pnlL);
            // ── Right panel ──────────────────────────────────────────────
            var pnlR = new Panel
            {
                Location = new Point(1222, 46),
                Size = new Size(106, PNL_H),
                BackColor = Color.FromArgb(12, 12, 12),
                BorderStyle = BorderStyle.FixedSingle
            };
            int rbx = (106 - BW) / 2;  // = 13
            for (int r = 0; r < ROWS; r++)
            {
                int by = vGap + r * (BH + vGap);
                var rBtn = new GcsButton
                {
                    Text     = (r == 0) ? "RPM"
                             : (r == 5) ? "Heading"
                             : $"R{r + 1}",
                    Location = new Point(rbx, by),
                    Size     = new Size(BW, BH),
                    ForeColor = LIME,
                    Font     = FNT11,
                    ShowLed  = false
                };
                pnlR.Controls.Add(rBtn);
                if (r == 0) _btnRpm     = rBtn;   // R1 → drives RPM float
                if (r == 5) _btnHeading = rBtn;   // R6 → drives heading float
            }
            this.Controls.Add(pnlR);
        }
        // ════════════════════════════════════════════════════════════════
        //  RPM Indicator — floatable, toggled by "RPM" button (R1)
        //  Same pattern as HeadingIndicator / HeadingFloatForm.
        // ════════════════════════════════════════════════════════════════
        private void BuildRpmIndicator()
        {
            _rpmIndicator = new RpmIndicator.RpmIndicator
            {
                Location   = _rpmOriginalLocation,
                Size       = _rpmOriginalSize,
                CurrentRPM = 0,
                Visible    = false
            };
            this.Controls.Add(_rpmIndicator);

            _btnRpm.Click += (s, e) =>
            {
                if (_btnRpm.IsOn)
                {
                    // Button just latched ON → open float window
                    if (_rpmFloatForm == null || _rpmFloatForm.IsDisposed)
                    {
                        _rpmFloatForm = new RpmFloatForm();
                        _rpmFloatForm.FloatClosed += () =>
                        {
                            _btnRpm.SetOn(false);
                            _rpmIndicator.Dock     = DockStyle.None;
                            _rpmIndicator.Location = _rpmOriginalLocation;
                            _rpmIndicator.Size     = _rpmOriginalSize;
                            this.Controls.Add(_rpmIndicator);
                            _rpmIndicator.Visible  = false;
                        };
                    }
                    this.Controls.Remove(_rpmIndicator);
                    _rpmIndicator.Dock    = DockStyle.Fill;
                    _rpmIndicator.Visible = true;
                    _rpmFloatForm.Controls.Add(_rpmIndicator);
                    _rpmFloatForm.Show(this);
                }
                else
                {
                    // Button just unlatched → close float, reparent back (hidden)
                    if (_rpmFloatForm != null && !_rpmFloatForm.IsDisposed)
                    {
                        _rpmFloatForm.Controls.Remove(_rpmIndicator);
                        _rpmFloatForm.Hide();
                    }
                    _rpmIndicator.Dock     = DockStyle.None;
                    _rpmIndicator.Location = _rpmOriginalLocation;
                    _rpmIndicator.Size     = _rpmOriginalSize;
                    this.Controls.Add(_rpmIndicator);
                    _rpmIndicator.Visible  = false;
                }
            };
        }

        private void BuildRightPanel()
        {
            // NW sized to longest label "Last Pkt #:" (11 chars × ~7.6px Consolas 10B ≈ 84px + 4px pad)
            const int ROW = 20, H = 20, NX = 8, NW = 88, VX = 100, VW = 72;
            const int PANEL_W = VX + VW + NX;   // = 180
            // Pushed down so Link Health bottom aligns with pnlR bottom (36+442=478).
            // CHT_BAR_Y (=210) is used as the ENGINE & POWER top so CHT bars align.
            int ENG_Y  = CHT_BAR_Y;            // 210
            int ENG_H  = 130;
            int LINK_Y = ENG_Y + ENG_H + 4;    // 344
            int LINK_H = 488 - LINK_Y;          // pnlR bottom = 46+442=488
            var grpEng = MakeGroupBox("ENGINE & POWER", 1330, ENG_Y, PANEL_W, ENG_H, Color.Orange, FNT12);
            EngRow(grpEng, "RPM:", NX, 30, NW, H, VX, VW, out _valRPM, FNT14);  // placed independently above Fuel
            int y = 62;   // pushed down to create gap after RPM
            EngRow(grpEng, "Fuel:", NX, y, NW, H, VX, VW, out _valFuelMain); y += ROW;
            EngRow(grpEng, "Batt V:", NX, y, NW, H, VX, VW, out _valBattV); y += ROW;
            EngRow(grpEng, "Alt V:", NX, y, NW, H, VX, VW, out _valAltV);
            // CHT Left / Right removed from box — keep fields as silent labels for CHT bar updates
            _valCHTL = new Label();
            _valCHTR = new Label();
            this.Controls.Add(grpEng);
            // grpLink: starts at R2-bottom+gap, ends flush with R5 bottom
            // 6 rows compacted to ROW_LNK=16 so last row bottom = 30+5*16+16 = 126 < LINK_H
            const int ROW_LNK = 16;
            var grpLink = MakeGroupBox("LINK HEALTH", 1330, LINK_Y, PANEL_W, LINK_H, Color.Yellow, FNT12);
            y = 30;
            EngRow(grpLink, "CRC Fails:", NX, y, NW, H, VX, VW, out _valCRCFail); y += ROW_LNK;
            EngRow(grpLink, "GPS Miss:", NX, y, NW, H, VX, VW, out _valGPSMiss); y += ROW_LNK;
            EngRow(grpLink, "VG Miss:", NX, y, NW, H, VX, VW, out _valVGMiss); y += ROW_LNK;
            EngRow(grpLink, "GPS Rate:", NX, y, NW, H, VX, VW, out _valGPSRate); y += ROW_LNK;
            EngRow(grpLink, "VG Rate:", NX, y, NW, H, VX, VW, out _valVGRate); y += ROW_LNK;
            EngRow(grpLink, "Last Pkt #:", NX, y, NW, H, VX, VW, out _valPktNum);
            this.Controls.Add(grpLink);

            // ── Flight Status — inside toolbar, aligned with Mission/Send/Cache row ─
            // CMD rate label occupies x=10..370; Mission button starts at x=1377.
            // Place both pairs in the gap: x=375..880, y=BTN_Y_REL=4, h=BTN_H=26.
            const int FS_BTN_W    = 80;   // button width (fits "FPhase" at 14pt)
            const int FS_TB_W     = 190;  // textbox width
            const int FS_GAP      = 4;    // gap between button and textbox within a pair
            const int FS_PAIR_GAP = 8;    // gap between pairs / between FPhase pair and Mission
            const int FS_BTN_Y    = 7;    // same as MTB_Y
            const int FS_BTN_H    = 28;   //.. 26;   // same as MTB_H
            // Anchor right-to-left from Mission button (x=1377, must match BuildMapPanel):
            //   [Mode btn][Mode TB][FS_PAIR_GAP][FPhase btn][FPhase TB][FS_PAIR_GAP][Mission]
            const int X_MISSION = 1377;
            int fsPhaseX = X_MISSION  - FS_PAIR_GAP - (FS_BTN_W + FS_GAP + FS_TB_W); // 1095
            int fsModeX  = fsPhaseX   - FS_PAIR_GAP - (FS_BTN_W + FS_GAP + FS_TB_W); // 813

            var btnMode = new Button
            {
                Text      = "Mode",
                Location  = new Point(fsModeX, FS_BTN_Y),
                Size      = new Size(FS_BTN_W, FS_BTN_H),
                BackColor = Color.FromArgb(0, 60, 120),
                ForeColor = Color.White,
                Font      = FNT14,
                FlatStyle = FlatStyle.Flat,
                TabStop   = false
            };
            btnMode.FlatAppearance.BorderColor = Color.FromArgb(0, 100, 200);
            btnMode.FlatAppearance.BorderSize  = 1;
            _toolBar.Controls.Add(btnMode);

            _valMode = new Label
            {
                Location  = new Point(fsModeX + FS_BTN_W + FS_GAP, 5),
                Size      = new Size(FS_TB_W, 30),
                AutoSize  = false,
                Font      = FNT14,
                BackColor = Color.Black,
                ForeColor = Color.Cyan,
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = ContentAlignment.MiddleLeft,
                Text      = "---",
            };
            _toolBar.Controls.Add(_valMode);

            var btnPhase = new Button
            {
                Text      = "FPhase",
                Location  = new Point(fsPhaseX, FS_BTN_Y),
                Size      = new Size(FS_BTN_W, FS_BTN_H),
                BackColor = Color.FromArgb(80, 50, 0),
                ForeColor = Color.White,
                Font      = FNT14,
                FlatStyle = FlatStyle.Flat,
                TabStop   = false
            };
            btnPhase.FlatAppearance.BorderColor = Color.FromArgb(160, 100, 0);
            btnPhase.FlatAppearance.BorderSize  = 1;
            _toolBar.Controls.Add(btnPhase);

            _valPhase = new Label
            {
                Location  = new Point(fsPhaseX + FS_BTN_W + FS_GAP, 5),
                Size      = new Size(FS_TB_W, 30),
                AutoSize  = false,
                Font      = FNT14,
                BackColor = Color.Black,
                ForeColor = Color.Yellow,
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign   = ContentAlignment.MiddleLeft,
                Text        = "---",
            };
            _toolBar.Controls.Add(_valPhase);

            // ── CMD link status label — top-left of toolbar ───────────────
            _lblCmdRate = new Label
            {
                Text      = "CMD LINK: OFF",
                Location  = new Point(10, 4),
                Size      = new Size(360, 26),
                ForeColor = Color.Gray,
                BackColor = Color.Transparent,
                Font      = FNT10B,
                TextAlign = ContentAlignment.MiddleLeft,
                Anchor    = AnchorStyles.Top | AnchorStyles.Left
            };
            _toolBar.Controls.Add(_lblCmdRate);
            // ── Command Tabs (below LINK HEALTH) ─────────────────────────
            BuildCommandTabs();
            // ── Heading Indicator — x=1608, y=131, 250×250 ───────────────
            // Shifted 28px right to make room for CHT ruler at x=1580.
            _hdgIndicator = new HeadingIndicator.HeadingIndicator
            {
                Location       = new Point(1608, 141),
                Size           = new Size(250, 250),
                CurrentHeading = 0,
                Visible        = false,   // hidden until "HI" is ticked
            };
            this.Controls.Add(_hdgIndicator);

            // ── "Heading" button (R6 side panel) — click to toggle floating window ──
            _btnHeading.Click += (s, e) =>
            {
                if (_btnHeading.IsOn)
                {
                    // OnMouseUp toggles _isOn BEFORE firing Click, so IsOn==true means
                    // the button just latched ON → open the float.
                    if (_hdgFloatForm == null || _hdgFloatForm.IsDisposed)
                    {
                        _hdgFloatForm = new HeadingFloatForm();
                        _hdgFloatForm.FloatClosed += () =>
                        {
                            // User closed float via X — reset button to OFF
                            _btnHeading.SetOn(false);
                            // Reparent indicator back to main form (hidden)
                            _hdgIndicator.Dock     = DockStyle.None;
                            _hdgIndicator.Location = _hdgOriginalLocation;
                            _hdgIndicator.Size     = _hdgOriginalSize;
                            this.Controls.Add(_hdgIndicator);
                            _hdgIndicator.Visible  = false;
                        };
                    }
                    this.Controls.Remove(_hdgIndicator);
                    _hdgIndicator.Dock    = DockStyle.Fill;
                    _hdgIndicator.Visible = true;
                    _hdgFloatForm.Controls.Add(_hdgIndicator);
                    _hdgFloatForm.Show(this);
                }
                else
                {
                    // IsOn is FALSE → button just unlatched, close the float
                    if (_hdgFloatForm != null && !_hdgFloatForm.IsDisposed)
                    {
                        _hdgFloatForm.Controls.Remove(_hdgIndicator);
                        _hdgFloatForm.Hide();
                    }
                    _hdgIndicator.Dock     = DockStyle.None;
                    _hdgIndicator.Location = _hdgOriginalLocation;
                    _hdgIndicator.Size     = _hdgOriginalSize;
                    this.Controls.Add(_hdgIndicator);
                    _hdgIndicator.Visible  = false;
                }
            };
        }
        // ════════════════════════════════════════════════════════════════
        //  VProgressBar — vertical ProgressBar with custom ForeColor
        //  PBS_VERTICAL (0x04) fills bottom-to-top.
        //  SetWindowTheme(" "," ") disables Aero/UxTheme so ForeColor works.
        // ════════════════════════════════════════════════════════════════
        private class VProgressBar : ProgressBar
        {
            [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
            private static extern int SetWindowTheme(IntPtr hwnd, string app, string id);

            protected override CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    cp.Style |= 0x04;   // PBS_VERTICAL
                    return cp;
                }
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                SetWindowTheme(Handle, " ", " ");   // classic rendering → ForeColor respected
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  CHT Left vertical temperature bar  (x=1518, beside ENGINE & POWER)
        //  Ranges : Green 0–150 °C | Yellow 150–210 °C | Red 210–300 °C
        //  Track  : 120 px tall  → Green 60 px | Yellow 24 px | Red 36 px
        // ════════════════════════════════════════════════════════════════
        private const int    CHT_BAR_X       = 1518;
        private const int    CHT_R_BAR_X    = 1518 + 28 + 32; // = 1578 (ruler occupies the 28px gap)
        private const int    CHT_BAR_Y       = 220;   // aligned with ENGINE & POWER group box top
        private const int    CHT_BAR_W       = 28;
        private const int    CHT_BAR_TRACK_H = 90;    // trimmed to fit within grpEng height (130px)
        private const double CHT_MAX         = 300.0;

        // Zone pixel heights (must sum to CHT_BAR_TRACK_H = 90)
        private const int CHT_GREEN_H  = 45;   // 0   – 150 °C  (bottom, 50%)
        private const int CHT_YELLOW_H = 18;   // 150 – 210 °C  (middle, 20%)
        private const int CHT_RED_H    = 27;   // 210 – 300 °C  (top,    30%)

        private void BuildCHTBar()
        {
            // ── Title ────────────────────────────────────────────────────────
            this.Controls.Add(new Label
            {
                Text      = "CHT L",
                Location  = new Point(CHT_BAR_X, CHT_BAR_Y),
                Size      = new Size(CHT_BAR_W + 4, 16),
                ForeColor = Color.Orange,
                BackColor = Color.Transparent,
                Font      = new Font("Consolas", 7f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            });

            // ── Track container ──────────────────────────────────────────────
            var track = new Panel
            {
                Location    = new Point(CHT_BAR_X, CHT_BAR_Y + 18),
                Size        = new Size(CHT_BAR_W, CHT_BAR_TRACK_H),
                BackColor   = Color.FromArgb(40, 40, 40),
                BorderStyle = BorderStyle.FixedSingle
            };
            this.Controls.Add(track);

            // ── Three VProgressBar segments stacked top-to-bottom inside track ──
            // Each fills bottom-to-top within its own zone.
            // Red   zone : y=0,              height=CHT_RED_H
            // Yellow zone: y=CHT_RED_H,       height=CHT_YELLOW_H
            // Green  zone: y=RED+YELLOW,       height=CHT_GREEN_H
            _chtFillRed = new VProgressBar
            {
                Location  = new Point(0, 0),
                Size      = new Size(CHT_BAR_W, CHT_RED_H),
                Minimum   = 0, Maximum = 90,
                Value     = 0,
                ForeColor = Color.FromArgb(200, 0, 0),
                BackColor = Color.FromArgb(40, 40, 40)
            };
            _chtFillYellow = new VProgressBar
            {
                Location  = new Point(0, CHT_RED_H),
                Size      = new Size(CHT_BAR_W, CHT_YELLOW_H),
                Minimum   = 0, Maximum = 60,
                Value     = 0,
                ForeColor = Color.FromArgb(180, 150, 0),
                BackColor = Color.FromArgb(40, 40, 40)
            };
            _chtFillGreen = new VProgressBar
            {
                Location  = new Point(0, CHT_RED_H + CHT_YELLOW_H),
                Size      = new Size(CHT_BAR_W, CHT_GREEN_H),
                Minimum   = 0, Maximum = 150,
                Value     = 0,
                ForeColor = Color.FromArgb(0, 180, 0),
                BackColor = Color.FromArgb(40, 40, 40)
            };
            track.Controls.Add(_chtFillRed);
            track.Controls.Add(_chtFillYellow);
            track.Controls.Add(_chtFillGreen);

            // ── Value label below bar ────────────────────────────────────────
            _chtBarValue = new Label
            {
                Text      = "-- °C",
                Location  = new Point(CHT_BAR_X - 4, CHT_BAR_Y + 18 + CHT_BAR_TRACK_H + 2),
                Size      = new Size(CHT_BAR_W + 12, 16),
                ForeColor = Color.Orange,
                BackColor = Color.Transparent,
                Font      = new Font("Consolas", 7f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };
            this.Controls.Add(_chtBarValue);
        }

        private void UpdateCHTBar(double tempC)
        {
            if (_chtFillGreen == null || _chtBarValue == null) return;

            double t = Math.Max(0, Math.Min(tempC, CHT_MAX));

            // Green  0 – 150 °C   (Maximum = 150)
            _chtFillGreen.Value = (int)Math.Round(Math.Min(t, 150.0));

            // Yellow 150 – 210 °C (Maximum = 60)
            _chtFillYellow.Value = t > 150.0 ? (int)Math.Round(Math.Min(t - 150.0, 60.0)) : 0;

            // Red    210 – 300 °C (Maximum = 90)
            _chtFillRed.Value = t > 210.0 ? (int)Math.Round(Math.Min(t - 210.0, 90.0)) : 0;

            // Label colour follows the highest active tier
            Color labelCol = t > 210.0 ? Color.FromArgb(200, 0, 0)
                           : t > 150.0 ? Color.FromArgb(180, 150, 0)
                                       : Color.FromArgb(0, 180, 0);
            _chtBarValue.Text      = ((int)tempC).ToString() + "°C";
            _chtBarValue.ForeColor = labelCol;
        }

        // ════════════════════════════════════════════════════════════════
        //  CHT Right vertical temperature bar  (x=1550, parallel to CHT Left)
        //  Identical layout/ranges to CHT Left.
        // ════════════════════════════════════════════════════════════════
        private void BuildCHTRBar()
        {
            this.Controls.Add(new Label
            {
                Text      = "CHT R",
                Location  = new Point(CHT_R_BAR_X, CHT_BAR_Y),
                Size      = new Size(CHT_BAR_W + 4, 16),
                ForeColor = Color.Orange,
                BackColor = Color.Transparent,
                Font      = new Font("Consolas", 7f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            });

            var track = new Panel
            {
                Location    = new Point(CHT_R_BAR_X, CHT_BAR_Y + 18),
                Size        = new Size(CHT_BAR_W, CHT_BAR_TRACK_H),
                BackColor   = Color.FromArgb(40, 40, 40),
                BorderStyle = BorderStyle.FixedSingle
            };
            this.Controls.Add(track);

            _chtRFillRed = new VProgressBar
            {
                Location  = new Point(0, 0),
                Size      = new Size(CHT_BAR_W, CHT_RED_H),
                Minimum   = 0, Maximum = 90,
                Value     = 0,
                ForeColor = Color.FromArgb(200, 0, 0),
                BackColor = Color.FromArgb(40, 40, 40)
            };
            _chtRFillYellow = new VProgressBar
            {
                Location  = new Point(0, CHT_RED_H),
                Size      = new Size(CHT_BAR_W, CHT_YELLOW_H),
                Minimum   = 0, Maximum = 60,
                Value     = 0,
                ForeColor = Color.FromArgb(180, 150, 0),
                BackColor = Color.FromArgb(40, 40, 40)
            };
            _chtRFillGreen = new VProgressBar
            {
                Location  = new Point(0, CHT_RED_H + CHT_YELLOW_H),
                Size      = new Size(CHT_BAR_W, CHT_GREEN_H),
                Minimum   = 0, Maximum = 150,
                Value     = 0,
                ForeColor = Color.FromArgb(0, 180, 0),
                BackColor = Color.FromArgb(40, 40, 40)
            };
            track.Controls.Add(_chtRFillRed);
            track.Controls.Add(_chtRFillYellow);
            track.Controls.Add(_chtRFillGreen);

            _chtRBarValue = new Label
            {
                Text      = "-- °C",
                Location  = new Point(CHT_R_BAR_X - 4, CHT_BAR_Y + 18 + CHT_BAR_TRACK_H + 2),
                Size      = new Size(CHT_BAR_W + 12, 16),
                ForeColor = Color.Orange,
                BackColor = Color.Transparent,
                Font      = new Font("Consolas", 7f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };
            this.Controls.Add(_chtRBarValue);
        }

        private void UpdateCHTRBar(double tempC)
        {
            if (_chtRFillGreen == null || _chtRBarValue == null) return;

            double t = Math.Max(0, Math.Min(tempC, CHT_MAX));

            _chtRFillGreen.Value  = (int)Math.Round(Math.Min(t, 150.0));
            _chtRFillYellow.Value = t > 150.0 ? (int)Math.Round(Math.Min(t - 150.0, 60.0)) : 0;
            _chtRFillRed.Value    = t > 210.0 ? (int)Math.Round(Math.Min(t - 210.0, 90.0)) : 0;

            Color labelCol = t > 210.0 ? Color.FromArgb(200, 0, 0)
                           : t > 150.0 ? Color.FromArgb(180, 150, 0)
                                       : Color.FromArgb(0, 180, 0);
            _chtRBarValue.Text      = ((int)tempC).ToString() + "°C";
            _chtRBarValue.ForeColor = labelCol;
        }

        // ════════════════════════════════════════════════════════════════
        //  CHT ruler — placed BETWEEN CHT Left and CHT Right bars
        //  Layout within 28px ruler width:
        //    x=0..4   left tick  (points toward CHT Left)
        //    x=6..21  label text ("50".."250")
        //    x=23..27 right tick (points toward CHT Right)
        //  y positions (ruler top = 300°C, ruler bottom = 0°C):
        //    250°C = 20 px | 200°C = 40 px | 150°C = 60 px
        //    100°C = 80 px |  50°C = 100 px
        // ════════════════════════════════════════════════════════════════
        private void BuildCHTRRuler()
        {
            // Ruler sits in the gap between CHT Left right edge and CHT Right left edge
            const int RX = CHT_BAR_X + CHT_BAR_W + 2;   // 1548 (2px right of CHT Left)
            const int RY = CHT_BAR_Y + 18;                // 54
            const int RW = CHT_R_BAR_X - (CHT_BAR_X + CHT_BAR_W) - 4; // 28px

            var ruler = new Panel
            {
                Location  = new Point(RX, RY),
                Size      = new Size(RW, CHT_BAR_TRACK_H),
                BackColor = Color.Black
            };

            var tickFont  = new Font("Consolas", 6f, FontStyle.Bold);
            var tickPen   = new Pen(Color.FromArgb(160, 160, 160), 1);
            var tickBrush = new SolidBrush(Color.FromArgb(200, 200, 200));

            ruler.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;

                for (int deg = 50; deg <= 250; deg += 50)
                {
                    // y=0 at top = 300°C, y=120 at bottom = 0°C
                    int y = CHT_BAR_TRACK_H - (int)Math.Round((double)deg / CHT_MAX * CHT_BAR_TRACK_H);

                    g.DrawLine(tickPen, 0, y, 5, y);          // left tick  → CHT Left
                    g.DrawLine(tickPen, RW - 5, y, RW, y);    // right tick → CHT Right
                    g.DrawString(deg.ToString(), tickFont, tickBrush, 6, y - 5); // label
                }
            };

            this.Controls.Add(ruler);
        }

        // ════════════════════════════════════════════════════════════════
        //  Command Tab Panel  (x=1330, below LINK HEALTH at y=424)
        // ════════════════════════════════════════════════════════════════
        // ── CMD LINK tab state ───────────────────────────────────────────
        private Label _lblCmdRate;   // CMD link status — lives in top toolbar (left side)

        private void BuildCommandTabs()
        {
            // ── Layout constants ──────────────────────────────────────────
            // TC_W = 540 → tab at x=1360, right edge=1900 (flush with DEMO button).
            // Map ends at x=1360 (width=1350).
            // BW fills the tab content area → buttons and tab labels span identical widths,
            // so the tab strip (bottom) sits directly under the button block.
            const int NUM_TABS   = 6;
            const int TC_BORDER  = 4;                           // ~2 px border each side
            const int COLS       = 4;
            const int ROWS       = 5;
            const int H_GAP      = 4;                           // gap between buttons
            const int V_GAP      = 4;                           // vertical gap between rows
            const int TAB_LBL_H  = 22;                         // reduced height for 8pt font
            // TC_W = T1_OFFSET_X(5) + H_GAP(4) + 3*(GB_BW(100)+H_GAP(4)) + TC_BORDER(4) = 325
            const int TC_W       = 340;                         // right edge flush with Tab 1 content + 15px
            const int TC_INNER   = TC_W - TC_BORDER;           // = 321 px client width
            const int TAB_LBL_W  = TC_W / NUM_TABS;            // = 54 px per label
            const int MARGIN     = 4;                           // inner margin each side
            const int BW         = (TC_INNER - (COLS + 1) * H_GAP) / COLS; // = 75 px per button
            const int BH         = 58;                          // button height
            const int ROW_STRIDE = BH + V_GAP;                 // = 62
            const int START_Y    = 12;
            const int HEADING_H  = 40;
            const int BTN_Y      = START_Y + HEADING_H;        // = 52 — first button row Y
            const int FULL_W     = COLS * BW + (COLS - 1) * H_GAP;   // button block width
            const int TAB_INNER_W = TC_INNER - 2 * MARGIN;    // = 313 px safe content width

            // ── Tab control ───────────────────────────────────────────────
            var tc = new TabControl
            {
                Location   = new Point(1363, 510),
                Size       = new Size(TC_W, 535),
                Font       = FNT8B,
                Appearance = TabAppearance.Normal,
                Alignment  = TabAlignment.Bottom,
                DrawMode   = TabDrawMode.OwnerDrawFixed,
                Multiline  = false,                             // single row — no wrap risk
                ItemSize   = new Size(TAB_LBL_W, TAB_LBL_H),
                Padding    = new Point(0, 0)
            };

            // ── Owner-draw: 3-D beveled tab headers ──────────────────────
            tc.DrawItem += (sender, ev) =>
            {
                var tab = (TabControl)sender;
                bool sel = (tab.SelectedIndex == ev.Index);
                var g = ev.Graphics;
                // WinForms expands the selected tab's bounds slightly outside the control edge —
                // clip to ClientRectangle so the first/last tab never bleeds past the border.
                var r = Rectangle.Intersect(ev.Bounds, tab.ClientRectangle);
                Color face = sel ? Color.FromArgb(65, 65, 65) : Color.FromArgb(22, 22, 22);
                Color hi   = Color.FromArgb(125, 125, 125);
                Color sh   = Color.FromArgb(10, 10, 10);
                Color fg   = sel ? Color.Lime : Color.FromArgb(90, 140, 90);
                g.FillRectangle(new SolidBrush(face), r);
                const int B = 2;
                if (sel)
                {
                    using (var p = new Pen(hi, 1))
                        for (int i = 0; i < B; i++)
                        {
                            g.DrawLine(p, r.Left + i,     r.Top, r.Left + i,         r.Bottom - 1 - i);
                            g.DrawLine(p, r.Left + i,     r.Bottom - 1 - i, r.Right - 1 - i, r.Bottom - 1 - i);
                            g.DrawLine(p, r.Right - 1 - i, r.Top, r.Right - 1 - i,   r.Bottom - 1 - i);
                        }
                }
                else
                {
                    using (var pHi = new Pen(hi, 1))
                    using (var pSh = new Pen(sh, 1))
                        for (int i = 0; i < B; i++)
                        {
                            g.DrawLine(pSh, r.Left + i,     r.Top, r.Left + i,         r.Bottom - 1 - i);
                            g.DrawLine(pHi, r.Left + i,     r.Bottom - 1 - i, r.Right - 1 - i, r.Bottom - 1 - i);
                            g.DrawLine(pSh, r.Right - 1 - i, r.Top, r.Right - 1 - i,   r.Bottom - 1 - i);
                        }
                }
                var fmt = new StringFormat
                {
                    Alignment     = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                g.DrawString(tab.TabPages[ev.Index].Text,
                             new Font(tab.Font.FontFamily, 8f, FontStyle.Bold),
                             new SolidBrush(fg), r, fmt);
            };

            // ── Shared helpers ────────────────────────────────────────────
            // Underlined full-sentence heading at the top of a tab page
            void AddHeading(TabPage page, string title)
            {
                page.Controls.Add(new Label
                {
                    Text      = title.ToUpper(),
                    Location  = new Point(0, 2),
                    Size      = new Size(TC_INNER - 2 * MARGIN, 28),
                    Font      = new Font("Arial Rounded MT Bold", 13f, FontStyle.Regular),
                    ForeColor = LIME,
                    BackColor = Color.Transparent,
                    TextAlign = ContentAlignment.MiddleCenter
                });
            }

            // Toggle button: label, column, row, initial state, callback, optional active colour
            GcsButton MakeToggle(string label, int col, int row, bool initOn,
                                  Action<bool> onToggle, Color onColor = default)
            {
                if (onColor == default) onColor = Color.FromArgb(0, 150, 0);
                Color offColor = Color.FromArgb(40, 40, 40);
                var btn = new GcsButton
                {
                    Text      = label,
                    Location  = new Point(H_GAP + col * (BW + H_GAP), BTN_Y + row * ROW_STRIDE),
                    Size      = new Size(BW, BH),
                    ForeColor = LIME,
                    Font      = FNT_TAB,
                    Tag       = initOn
                };
                if (initOn) btn.SetOn(true);
                btn.Click += (s, e) =>
                {
                    bool now  = !(bool)btn.Tag;
                    btn.Tag   = now;
                    onToggle(now);
                };
                _resetActions.Add(() => { btn.Tag = initOn; btn.SetOn(initOn); onToggle(initOn); });
                return btn;
            }

            // Placeholder button: dimmed, no callback
            GcsButton MakePlaceholder(string label, int col, int row) => new GcsButton
            {
                Text      = label,
                Location  = new Point(H_GAP + col * (BW + H_GAP), BTN_Y + row * ROW_STRIDE),
                Size      = new Size(BW, BH),
                ForeColor = LIME,
                Font      = FNT_TAB
            };

            // Fill rows [fromRow..toRow] with sequentially-labelled placeholders.
            // startN controls the first number suffix (default 1).
            void FillRows(TabPage page, string prefix, int fromRow, int toRow, int startN = 1)
            {
                int n = startN;
                for (int row = fromRow; row <= toRow; row++)
                    for (int col = 0; col < COLS; col++)
                        page.Controls.Add(MakePlaceholder($"{prefix}{n++:D2}", col, row));
            }

            // ── Free-positioned button helpers (used by Tabs 4/5/6) ───────
            // Create a GcsButton at an absolute (x,y) with explicit size/font.
            GcsButton FreeBtn(string lbl, int x, int y, int w, int h, Font f) => new GcsButton
            {
                Text      = lbl,
                Location  = new Point(x, y),
                Size      = new Size(w, h),
                ForeColor = LIME,
                Font      = f,
                Tag       = false,
                OffColor  = Color.FromArgb(64, 64, 64)
            };
            // Wire an independent on/off toggle (logical state in Tag, visual auto-latched).
            void WireToggle(GcsButton b, bool initOn, Action<bool> cb)
            {
                b.Tag = initOn;
                if (initOn) b.SetOn(true);
                b.Click += (s, e) => { bool now = !(bool)b.Tag; b.Tag = now; cb(now); };
                _resetActions.Add(() => { b.Tag = initOn; b.SetOn(initOn); cb(initOn); });
            }
            // Wire a mutually-exclusive set — exactly one ON at a time. onSel gets the index.
            void WireExclusive(GcsButton[] set, int defIdx, Action<int> onSel)
            {
                for (int i = 0; i < set.Length; i++)
                {
                    int idx = i; var b = set[i];
                    b.Tag = (i == defIdx);
                    if (i == defIdx) b.SetOn(true);
                    b.Click += (s, e) =>
                    {
                        foreach (var x in set) { x.Tag = false; x.SetOn(false); }
                        b.Tag = true; b.SetOn(true);
                        onSel(idx);
                    };
                }
                _resetActions.Add(() =>
                {
                    foreach (var x in set) { x.Tag = false; x.SetOn(false); }
                    set[defIdx].Tag = true; set[defIdx].SetOn(true);
                    onSel(defIdx);
                });
            }

            // ── TAB 1 — Vehicle Management ────────────────────────────────
            //   3-column layout, button size 100×40.
            //   Rows 0-2 : 9 mutually exclusive VehicleMode buttons (3×3)
            //              AUTO | SELF TEST | RUNUP
            //              STANDBY | MANUAL EC | MANUAL GCS
            //              SEMIAUTO HDG | SEMIAUTO ROLL | SIGHTLINE SLV
            //   Row 3    : Pilot Aug | Full Throttle | Search
            //   GroupBox "Altitude Sensor In Use": GPS | Pres Alt
            //   GroupBox "Speed Sensor In Use":    GPS | ADS
            //   Brakes button (below sensor groupboxes)
            {
                var page = new TabPage("Vehicle")
                {
                    BackColor = Color.FromArgb(12, 12, 12),
                    ForeColor = LIME
                };

                AddHeading(page, "Vehicle Management");

                // Button size matches left side panel (BW=80, BH=55)
                const int T1C  = 3;
                const int T1W  = 100;
                const int T1H  = 50;             // increased to fill tab content height
                const int T1S  = T1H + V_GAP;   // stride = 54
                const int T1BY = BTN_Y - 10;     // below heading, shifted up 10px
                const int GB_BW = 100;
                const int GB_H  = 22 + T1H + 4; // title(22) + button(50) + bottom pad(4) = 76
                var t1Font = new Font("Arial Rounded MT Bold", 10f, FontStyle.Bold);

                // ── Local helpers ─────────────────────────────────────────
                const int T1_OFFSET_X = 5;   // right shift for all Tab 1 buttons

                GcsButton T1Tog(string lbl, int col, int row, bool initOn,
                                Action<bool> cb, Color onClr = default, int extraY = 0)
                {
                    if (onClr == default) onClr = Color.FromArgb(0, 150, 0);
                    var b = new GcsButton
                    {
                        Text      = lbl,
                        Location  = new Point(T1_OFFSET_X + H_GAP + col * (T1W + H_GAP), T1BY + row * T1S + extraY),
                        Size      = new Size(T1W, T1H),
                        ForeColor = LIME,
                        Font      = t1Font,
                        Tag       = initOn,
                        OffColor  = Color.FromArgb(64, 64, 64)
                    };
                    if (initOn) b.SetOn(true);
                    b.Click += (s, e) => { bool now = !(bool)b.Tag; b.Tag = now; cb(now); };
                    _resetActions.Add(() => { b.Tag = initOn; b.SetOn(initOn); cb(initOn); });
                    return b;
                }
                GcsButton T1Phd(string lbl, int col, int row, int extraY = 0) => new GcsButton
                {
                    Text      = lbl,
                    Location  = new Point(T1_OFFSET_X + H_GAP + col * (T1W + H_GAP), T1BY + row * T1S + extraY),
                    Size      = new Size(T1W, T1H),
                    ForeColor = LIME,
                    Font      = t1Font
                };
                GcsButton GbTog(string lbl, int col, bool initOn, Action<bool> cb, Color onClr = default)
                {
                    if (onClr == default) onClr = Color.FromArgb(0, 150, 0);
                    var b = new GcsButton
                    {
                        Text      = lbl,
                        Location  = new Point(H_GAP + col * (GB_BW + H_GAP), 20),
                        Size      = new Size(GB_BW, T1H),
                        ForeColor = LIME,
                        Font      = t1Font,
                        Tag       = initOn,
                        OffColor  = Color.FromArgb(64, 64, 64)
                    };
                    if (initOn) b.SetOn(true);
                    b.Click += (s, e) => { bool now = !(bool)b.Tag; b.Tag = now; cb(now); };
                    _resetActions.Add(() => { b.Tag = initOn; b.SetOn(initOn); cb(initOn); });
                    return b;
                }
                GcsButton GbPhd(string lbl, int col) => new GcsButton
                {
                    Text      = lbl,
                    Location  = new Point(H_GAP + col * (GB_BW + H_GAP), 20),
                    Size      = new Size(GB_BW, T1H),
                    ForeColor = LIME,
                    Font      = t1Font
                };
                // Momentary nudge button — held down = active, released = inactive
                // Momentary nudge button — GcsButton appearance, active while held
                GcsButton MakeNudgeBtn(string lbl, int x, int y, int w, int h, Action<bool> cb)
                {
                    var b = new GcsButton
                    {
                        Text      = lbl,
                        Location  = new Point(x, y),
                        Size      = new Size(w, h),
                        ForeColor = LIME,
                        Font      = new Font("Arial Rounded MT Bold", 14f, FontStyle.Bold),
                        Tag       = false,
                        OffColor  = Color.FromArgb(64, 64, 64)
                    };
                    b.MouseDown += (s, e) => { b.SetOn(true);  cb(true);  };
                    b.MouseUp   += (s, e) => { b.SetOn(false); cb(false); };
                    return b;
                }

                // ── Vehicle Mode — Rows 0-2 (3×3 mutually exclusive) ─────
                //   AUTO=0  SELF TEST=1  RUNUP=2
                //   STANDBY=3  MANUAL EC=4  MANUAL GCS=5
                //   SEMIAUTO HDG=6  SEMIAUTO ROLL=7  SIGHTLINE SLV=8
                var modeNames = new[]
                {
                    "AUTO",          "SELF TEST",      "RUNUP",
                    "STANDBY",       "MANUAL EC",      "MANUAL GCS",
                    "SEMIAUTO HDG",  "SEMIAUTO ROLL",  "SIGHTLINE SLV"
                };
                var modeBtns = new GcsButton[9];
                for (int mi = 0; mi < 9; mi++)
                {
                    int m = mi;
                    modeBtns[m] = new GcsButton
                    {
                        Text      = modeNames[m],
                        Location  = new Point(T1_OFFSET_X + H_GAP + (m % 3) * (T1W + H_GAP),
                                              T1BY + (m / 3) * T1S),
                        Size      = new Size(T1W, T1H),
                        ForeColor = LIME,
                        Font      = t1Font,
                        Tag       = (m == 3),   // STANDBY is default
                        OffColor  = Color.FromArgb(64, 64, 64)
                    };
                }
                modeBtns[3].SetOn(true);   // STANDBY ON at startup

                for (int mi = 0; mi < 9; mi++)
                {
                    int m = mi;
                    modeBtns[m].Click += (s, e) =>
                    {
                        if ((bool)modeBtns[m].Tag) { modeBtns[m].SetOn(true); return; }
                        for (int j = 0; j < 9; j++) { modeBtns[j].Tag = false; modeBtns[j].SetOn(false); }
                        modeBtns[m].Tag = true; modeBtns[m].SetOn(true);
                        _cmdSender.State.VehicleMode = (byte)m;
                    };
                    page.Controls.Add(modeBtns[m]);
                }
                _resetActions.Add(() =>
                {
                    for (int j = 0; j < 9; j++) { modeBtns[j].Tag = false; modeBtns[j].SetOn(false); }
                    modeBtns[3].Tag = true; modeBtns[3].SetOn(true);
                    _cmdSender.State.VehicleMode = 3;
                });

                // ── Row 3 — Pilot Aug / Full Throttle / Search ────────────
                // initOn reflects default conditions: Pilot Aug=ON, Full Throttle=ON, Search=OFF
                page.Controls.Add(T1Tog("Pilot Aug",     0, 3, true,
                    on => _cmdSender.State.PilotAugmentationOff = !on,   // inverted: ON=aug enabled=bit 0
                    extraY: 5));
                page.Controls.Add(T1Tog("Full Throttle", 1, 3, true,
                    on => _cmdSender.State.FullThrottle = on,
                    Color.FromArgb(200, 60, 0), extraY: 5));
                page.Controls.Add(T1Tog("Search",        2, 3, false,
                    on => _cmdSender.State.Search = on,
                    extraY: 5));

                // ── Sensor GroupBoxes (below the 4 button rows) ───────────
                int gbY = T1BY + 4 * T1S + 4 + 5;   // row 3 extraY=5

                int gbW = H_GAP + T1C * (GB_BW + H_GAP);   // = 4 + 3*104 = 316 — matches button span
                int gbWAlt = H_GAP + 2 * (GB_BW + H_GAP);   // 2-button width = 212px
                var grpAlt = new GroupBox
                {
                    Text      = "Altitude Sensor In Use",
                    Location  = new Point(T1_OFFSET_X, gbY),
                    Size      = new Size(gbWAlt, GB_H),
                    ForeColor = LIME,
                    Font      = new Font("Arial Rounded MT Bold", 10f, FontStyle.Regular)
                };
                // Mutually exclusive alt sensor selection — GPS is default ON
                var btnAltGps  = new GcsButton
                {
                    Text      = "GPS",
                    Location  = new Point(H_GAP + 0 * (GB_BW + H_GAP), 20),
                    Size      = new Size(GB_BW, T1H),
                    ForeColor = LIME,
                    Font      = t1Font,
                    Tag       = true,
                    OffColor  = Color.FromArgb(64, 64, 64)
                };
                var btnAltPres = new GcsButton
                {
                    Text      = "Pres Alt",
                    Location  = new Point(H_GAP + 1 * (GB_BW + H_GAP), 20),
                    Size      = new Size(GB_BW, T1H),
                    ForeColor = LIME,
                    Font      = t1Font,
                    Tag       = false,
                    OffColor  = Color.FromArgb(64, 64, 64)
                };
                btnAltGps.SetOn(true);   // GPS is default ON at startup

                btnAltGps.Click += (s, e) =>
                {
                    if ((bool)btnAltGps.Tag) { btnAltGps.SetOn(true); return; }  // already ON — restore visual
                    btnAltGps.Tag  = true;  btnAltGps.SetOn(true);
                    _cmdSender.State.UseGpsAlt  = true;
                    btnAltPres.Tag = false; btnAltPres.SetOn(false);
                    _cmdSender.State.UsePresAlt = false;
                };
                btnAltPres.Click += (s, e) =>
                {
                    if ((bool)btnAltPres.Tag) { btnAltPres.SetOn(true); return; } // already ON — restore visual
                    btnAltPres.Tag = true;  btnAltPres.SetOn(true);
                    _cmdSender.State.UsePresAlt = true;
                    btnAltGps.Tag  = false; btnAltGps.SetOn(false);
                    _cmdSender.State.UseGpsAlt  = false;
                };
                grpAlt.Controls.Add(btnAltGps);
                grpAlt.Controls.Add(btnAltPres);
                page.Controls.Add(grpAlt);
                _resetActions.Add(() =>
                {
                    btnAltGps.Tag  = true;  btnAltGps.SetOn(true);  _cmdSender.State.UseGpsAlt  = true;
                    btnAltPres.Tag = false; btnAltPres.SetOn(false); _cmdSender.State.UsePresAlt = false;
                });

                // ── Alt nudge +/- (right of grpAlt) ──────────────────────
                // Aligned within the inner button area of the groupbox (below title, same bottom as GPS/Pres Alt)
                int nudgeX = T1_OFFSET_X + gbWAlt + H_GAP;  // right edge of groupbox + gap
                int nudgeW = GB_BW / 2;                      // 50px = half of GPS/Pres Alt width
                int nudgeH = (T1H - V_GAP) / 2;             // (50-4)/2 = 23px; two buttons + gap = T1H
                int nudgeInnerY = 5;// 20;                        // offset below groupbox title bar
                page.Controls.Add(MakeNudgeBtn("+", nudgeX, gbY + nudgeInnerY + 2,                    nudgeW, nudgeH + 6 + 6,
                                               on => _cmdSender.State.GcsAscend  = on));
               // page.Controls.Add(MakeNudgeBtn("-", nudgeX, gbY + nudgeInnerY + nudgeH + V_GAP,   nudgeW, nudgeH + 6,
               //                                on => _cmdSender.State.GcsDescend = on));
                page.Controls.Add(MakeNudgeBtn("-", nudgeX, gbY + nudgeInnerY + nudgeH + 14, nudgeW, nudgeH + 6 + 6,
                                               on => _cmdSender.State.GcsDescend = on));

                var grpSpd = new GroupBox
                {
                    Text      = "Speed Sensor In Use",
                    Location  = new Point(T1_OFFSET_X, gbY + GB_H + 4),
                    Size      = new Size(gbWAlt, GB_H),   // same 2-button width as Alt groupbox
                    ForeColor = LIME,
                    Font      = new Font("Arial Rounded MT Bold", 10f, FontStyle.Regular)
                };
                // Mutually exclusive speed sensor selection — GPS is default ON
                var btnSpdGps = new GcsButton
                {
                    Text      = "GPS",
                    Location  = new Point(H_GAP + 0 * (GB_BW + H_GAP), 20),
                    Size      = new Size(GB_BW, T1H),
                    ForeColor = LIME,
                    Font      = t1Font,
                    Tag       = true,
                    OffColor  = Color.FromArgb(64, 64, 64)
                };
                var btnSpdAds = new GcsButton
                {
                    Text      = "ADS",
                    Location  = new Point(H_GAP + 1 * (GB_BW + H_GAP), 20),
                    Size      = new Size(GB_BW, T1H),
                    ForeColor = LIME,
                    Font      = t1Font,
                    Tag       = false,
                    OffColor  = Color.FromArgb(64, 64, 64)
                };
                btnSpdGps.SetOn(true);   // GPS is default ON at startup

                // Speed sensor is a single bit: Use_GPSSpeed (VC bit 0): 1 = GPS, 0 = ADS.
                btnSpdGps.Click += (s, e) =>
                {
                    if ((bool)btnSpdGps.Tag) { btnSpdGps.SetOn(true); return; }  // already ON — restore visual
                    btnSpdGps.Tag  = true;  btnSpdGps.SetOn(true);
                    _cmdSender.State.UseGpsSpeed = true;                          // 1 = GPS
                    btnSpdAds.Tag  = false; btnSpdAds.SetOn(false);
                };
                btnSpdAds.Click += (s, e) =>
                {
                    if ((bool)btnSpdAds.Tag) { btnSpdAds.SetOn(true); return; }  // already ON — restore visual
                    btnSpdAds.Tag  = true;  btnSpdAds.SetOn(true);
                    _cmdSender.State.UseGpsSpeed = false;                         // 0 = ADS
                    btnSpdGps.Tag  = false; btnSpdGps.SetOn(false);
                };
                grpSpd.Controls.Add(btnSpdGps);
                grpSpd.Controls.Add(btnSpdAds);
                page.Controls.Add(grpSpd);
                _resetActions.Add(() =>
                {
                    btnSpdGps.Tag = true;  btnSpdGps.SetOn(true);  _cmdSender.State.UseGpsSpeed = true;
                    btnSpdAds.Tag = false; btnSpdAds.SetOn(false);
                });

                // ── Speed nudge +/- (right of grpSpd) ────────────────────
                int spdGbY = gbY + GB_H + 4;   // same Y offset used for grpSpd
                page.Controls.Add(MakeNudgeBtn("+", nudgeX, spdGbY + nudgeInnerY + 2, nudgeW, nudgeH + 6 + 6,
                                               on => _cmdSender.State.GcsIncrementSpeed = on));
               // page.Controls.Add(MakeNudgeBtn("-", nudgeX, spdGbY + nudgeInnerY + nudgeH + V_GAP,   nudgeW, nudgeH + 6 + 6,
               //                                on => _cmdSender.State.GcsDecrementSpeed = on));
                page.Controls.Add(MakeNudgeBtn("-", nudgeX, spdGbY + nudgeInnerY + nudgeH + 14, nudgeW, nudgeH + 6 + 6,
                                               on => _cmdSender.State.GcsDecrementSpeed = on));

                // ── Brakes button (bottom) ────────────────────────────────
                // Tag=true/ON = brakes applied → ReleaseBrakes=false (not released)
                int brakesY = gbY + 2 * GB_H + 4 + 20;
                var brakesBtn = new GcsButton
                {
                    Text      = "Brakes",
                    Location  = new Point(T1_OFFSET_X + H_GAP, brakesY),
                    Size      = new Size(T1W, T1H),
                    ForeColor = LIME,
                    Font      = FNT_TAB,
                    Tag       = true,      // brakes applied by default
                    OffColor  = Color.FromArgb(64, 64, 64)
                };
                brakesBtn.SetOn(true);
                brakesBtn.Click += (s, e) =>
                {
                    bool now = !(bool)brakesBtn.Tag;
                    brakesBtn.Tag = now;
                    brakesBtn.SetOn(now);
                    _cmdSender.State.ReleaseBrakes = !now;   // ON→brakes applied→NOT released
                };
                _resetActions.Add(() =>
                {
                    brakesBtn.Tag = true; brakesBtn.SetOn(true);
                    _cmdSender.State.ReleaseBrakes = false;
                });
                page.Controls.Add(brakesBtn);


                tc.TabPages.Add(page);
            }

            // ── TAB 2 — Flight Automation ─────────────────────────────────
            //   3-column layout, button size 100×50 — matches Tab 1 sizing.
            //   Row 0 : Ht Err Lead  | Init Pitch    | Init Rudder
            //   Row 1 : Init Aileron | PR Ld Filter  | Def Lat Gains
            //   Row 2 : Def Lon Gains| Air Mode      | SLT
            //   Row 3 : Flaps Down   | NonLin Roll   | Flaps/Ailrn
            //   Row 4 : Alt Hold     | Gnd Clear     | Tel Logging
            //   Row 5 : Speed Hold
            //   Height Control Scheme groupbox : Pitch Angle* | ROC Control*
            //   * Pitch Angle / ROC Control mutually exclusive (HeightControlScheme)
            {
                var page = new TabPage("Autop")
                {
                    BackColor = Color.FromArgb(12, 12, 12),
                    ForeColor = LIME
                };

                AddHeading(page, "Flight Automation");

                // Button geometry — same as Tab 1
                const int T2W        = 100;
                const int T2H        = 50;
                const int T2S        = T2H + V_GAP;          // stride = 54
                const int T2BY       = BTN_Y - 10;           // = 42, matches T1BY
                const int T2_OFFSET  = 5;                    // left margin, matches T1_OFFSET_X
                var t2Font = new Font("Arial Rounded MT Bold", 10f, FontStyle.Bold);

                GcsButton T2Tog(string lbl, int col, int row, bool initOn, Action<bool> cb)
                {
                    var b = new GcsButton
                    {
                        Text      = lbl,
                        Location  = new Point(T2_OFFSET + H_GAP + col * (T2W + H_GAP), T2BY + row * T2S),
                        Size      = new Size(T2W, T2H),
                        ForeColor = LIME,
                        Font      = t2Font,
                        Tag       = initOn,
                        OffColor  = Color.FromArgb(64, 64, 64)
                    };
                    if (initOn) b.SetOn(true);
                    b.Click += (s, e) => { bool now = !(bool)b.Tag; b.Tag = now; cb(now); };
                    _resetActions.Add(() => { b.Tag = initOn; b.SetOn(initOn); cb(initOn); });
                    return b;
                }
                GcsButton T2Ph(string lbl, int col, int row) => new GcsButton
                {
                    Text      = lbl,
                    Location  = new Point(T2_OFFSET + H_GAP + col * (T2W + H_GAP), T2BY + row * T2S),
                    Size      = new Size(T2W, T2H),
                    ForeColor = LIME,
                    Font      = t2Font
                };

                // Row 0
                page.Controls.Add(T2Tog("Ht Err Lead",   0, 0, false, on => { }));
                page.Controls.Add(T2Tog("Init Pitch",    1, 0, false, on => { }));
                page.Controls.Add(T2Tog("Init Rudder",   2, 0, false, on => { }));

                // Row 1
                page.Controls.Add(T2Tog("Init Aileron",  0, 1, false, on => { }));
                page.Controls.Add(T2Tog("PR Ld Filter",  1, 1, false, on => { }));
                page.Controls.Add(T2Tog("Def Lat Gains", 2, 1, true,  on => _cmdSender.State.SetDefaultLatGains = on));

                // Row 2
                page.Controls.Add(T2Tog("Def Lon Gains", 0, 2, true,  on => _cmdSender.State.SetDefaultLonGains = on));
                page.Controls.Add(T2Tog("Air Mode",      1, 2, false, on => _cmdSender.State.AirModesEnabledSwt = on));
                page.Controls.Add(T2Tog("SLT",           2, 2, false, on => { }));

                // Row 3
                page.Controls.Add(T2Tog("Flaps Down",    0, 3, false, on => _cmdSender.State.FlapsDown = on));
                page.Controls.Add(T2Tog("NonLin Roll",   1, 3, false, on => _cmdSender.State.UseNonLinearRollControl = on));
                page.Controls.Add(T2Tog("Flaps/Ailrn",   2, 3, false, on => _cmdSender.State.UseFlapsAsAilerons = on));

                // Row 4
                page.Controls.Add(T2Tog("Alt Hold",      0, 4, false, on => _cmdSender.State.AltitudeHold = on));
                page.Controls.Add(T2Tog("Gnd Clear",     1, 4, false, on => _cmdSender.State.GndCrewClearanceSwt = on));
                page.Controls.Add(T2Tog("Tel Logging",   2, 4, false, on => _cmdSender.State.EnableLogging = on));

                // Row 5
                page.Controls.Add(T2Tog("Speed Hold",    0, 5, false, on => _cmdSender.State.SpeedHold = on));

                // ── Height Control Scheme GroupBox (moved down below row 5) ───
                //   Pitch Angle / ROC Control mutually exclusive (HeightControlScheme)
                //   Pitch Angle ON → HeightControlScheme = false (default)
                //   ROC Control ON → HeightControlScheme = true
                int hcsY = T2BY + 6 * T2S + 8;                  // below row 5 + extra gap
                int hcsW = H_GAP + 2 * (T2W + H_GAP);          // 2-button width = 212px
                int hcsH = 22 + T2H + 4;                        // title + button + pad = 76
                var grpHcs = new GroupBox
                {
                    Text      = "Height Control Scheme",
                    Location  = new Point(T2_OFFSET, hcsY),
                    Size      = new Size(hcsW, hcsH),
                    ForeColor = LIME,
                    Font      = new Font("Arial Rounded MT Bold", 8f, FontStyle.Regular)
                };

                GcsButton btnPitchAngle = null, btnRocControl = null;
                btnPitchAngle = new GcsButton
                {
                    Text      = "Pitch Angle",
                    Location  = new Point(H_GAP + 0 * (T2W + H_GAP), 20),
                    Size      = new Size(T2W, T2H),
                    ForeColor = LIME,
                    Font      = t2Font,
                    Tag       = true,
                    OffColor  = Color.FromArgb(64, 64, 64)
                };
                btnRocControl = new GcsButton
                {
                    Text      = "ROC Control",
                    Location  = new Point(H_GAP + 1 * (T2W + H_GAP), 20),
                    Size      = new Size(T2W, T2H),
                    ForeColor = LIME,
                    Font      = t2Font,
                    Tag       = false,
                    OffColor  = Color.FromArgb(64, 64, 64)
                };
                btnPitchAngle.SetOn(true);   // Pitch Angle default ON
                btnPitchAngle.Click += (s, e) =>
                {
                    if ((bool)btnPitchAngle.Tag) { btnPitchAngle.SetOn(true); return; }
                    btnPitchAngle.Tag = true;  btnPitchAngle.SetOn(true);
                    btnRocControl.Tag = false; btnRocControl.SetOn(false);
                    _cmdSender.State.HeightControlScheme = false;
                };
                btnRocControl.Click += (s, e) =>
                {
                    if ((bool)btnRocControl.Tag) { btnRocControl.SetOn(true); return; }
                    btnRocControl.Tag = true;  btnRocControl.SetOn(true);
                    btnPitchAngle.Tag = false; btnPitchAngle.SetOn(false);
                    _cmdSender.State.HeightControlScheme = true;
                };
                _resetActions.Add(() =>
                {
                    btnPitchAngle.Tag = true;  btnPitchAngle.SetOn(true);
                    btnRocControl.Tag = false; btnRocControl.SetOn(false);
                    _cmdSender.State.HeightControlScheme = false;
                });
                grpHcs.Controls.Add(btnPitchAngle);
                grpHcs.Controls.Add(btnRocControl);
                page.Controls.Add(grpHcs);

                // ── Gain Tune & Stability Augmentation — checkboxes at bottom ─
                var chkFont2 = new Font("Arial Rounded MT Bold", 10f, FontStyle.Regular);
                int chkY2    = hcsY + hcsH + 6;    // below the groupbox + gap

                var chkGainTune = new CheckBox
                {
                    Text      = "Gain Tune",
                    Location  = new Point(H_GAP, chkY2),
                    Size      = new Size(160, 22),
                    ForeColor = LIME,
                    BackColor = Color.Transparent,
                    Font      = chkFont2,
                    Checked   = false
                };
                chkGainTune.CheckedChanged += (s, e) =>
                    _cmdSender.State.EnableGainTuning = chkGainTune.Checked;
                page.Controls.Add(chkGainTune);

                var chkStabAug = new CheckBox
                {
                    Text      = "Stability Augmentation",
                    Location  = new Point(H_GAP, chkY2 + 26),
                    Size      = new Size(220, 22),
                    ForeColor = LIME,
                    BackColor = Color.Transparent,
                    Font      = chkFont2,
                    Checked   = false
                };
                chkStabAug.CheckedChanged += (s, e) =>
                    _cmdSender.State.EnableStabilityAugmentation = chkStabAug.Checked;
                page.Controls.Add(chkStabAug);

                tc.TabPages.Add(page);
            }

            // ── TAB 3 — Landing Control ───────────────────────────────────
            //   3-column layout, button size 100×50 — matches Tab 1/2 sizing.
            //   Row 0 : Radar Alt      | Abort Landing  | Emergency Landing
            //   Row 1 : Dummy Landing  | Deny Hit Crit  | Landing
            //   GroupBox "Landing Direction" : End1→End2 / End2→End1 (radio)
            //   Row 2 : Retract        | Deploy         (mutually exclusive)
            //   GroupBox "Takeoff Direction" : End1→End2 / End2→End1 (radio)
            {
                var page = new TabPage("Landing")
                {
                    BackColor = Color.FromArgb(12, 12, 12),
                    ForeColor = LIME
                };
                AddHeading(page, "Landing Control");

                const int T3W       = 100;
                const int T3H       = 50;
                const int T3S       = T3H + V_GAP;         // stride = 54
                const int T3BY      = BTN_Y - 10;          // = 42
                const int T3_OFFSET = 5;
                var t3Font   = new Font("Arial Rounded MT Bold", 10f, FontStyle.Bold);
                var grpFont  = new Font("Arial Rounded MT Bold", 10f, FontStyle.Regular);
                var radFont  = new Font("Arial Rounded MT Bold", 9f,  FontStyle.Regular);

                GcsButton T3Tog(string lbl, int col, int row, bool initOn, Action<bool> cb, int extraY = 0)
                {
                    var b = new GcsButton
                    {
                        Text      = lbl,
                        Location  = new Point(T3_OFFSET + H_GAP + col * (T3W + H_GAP), T3BY + row * T3S + extraY),
                        Size      = new Size(T3W, T3H),
                        ForeColor = LIME,
                        Font      = t3Font,
                        Tag       = initOn,
                        OffColor  = Color.FromArgb(64, 64, 64)
                    };
                    if (initOn) b.SetOn(true);
                    b.Click += (s, e) => { bool now = !(bool)b.Tag; b.Tag = now; cb(now); };
                    _resetActions.Add(() => { b.Tag = initOn; b.SetOn(initOn); cb(initOn); });
                    return b;
                }

                // Row 0
                page.Controls.Add(T3Tog("Radar Alt",     0, 0, false, on => _cmdSender.State.UseRadarAlt      = on));
                page.Controls.Add(T3Tog("Abort Landing", 1, 0, false, on => _cmdSender.State.AbortLanding     = on));
                page.Controls.Add(T3Tog("Emrg Landing",  2, 0, false, on => _cmdSender.State.EmergencyLanding = on));

                // Row 1
                page.Controls.Add(T3Tog("Dummy Landing", 0, 1, false, on => _cmdSender.State.DummyLanding    = on));
                page.Controls.Add(T3Tog("Deny Hit Crit", 1, 1, false, on => _cmdSender.State.DenyHitCriteria = on));
                // Landing — placed underneath Dummy Landing (col 0, row 2)
                page.Controls.Add(T3Tog("Landing",       0, 2, false, on => _cmdSender.State.Landing         = on));

                // ── Landing Direction groupbox (radio: End1→End2 default) ─────
                //   LandingDirection = false → End1→End2 ; true → End2→End1
                int ldY = T3BY + 3 * T3S + 10;
                const int RAD_W  = 152;                    // radio button width (fits text)
                const int GRP_W  = 12 + RAD_W + 12;        // groupbox snug to text = 176px
                var grpLandDir = new GroupBox
                {
                    Text      = "Landing Direction",
                    Location  = new Point(T3_OFFSET, ldY),
                    Size      = new Size(GRP_W, 72),
                    ForeColor = LIME,
                    Font      = grpFont
                };
                var radLd12 = new RadioButton
                {
                    Text = "From End1 to End2", Location = new Point(12, 22),
                    Size = new Size(RAD_W, 22), ForeColor = LIME, BackColor = Color.Transparent,
                    Font = radFont, Checked = true
                };
                var radLd21 = new RadioButton
                {
                    Text = "From End2 to End1", Location = new Point(12, 46),
                    Size = new Size(RAD_W, 22), ForeColor = LIME, BackColor = Color.Transparent,
                    Font = radFont, Checked = false
                };
                radLd21.CheckedChanged += (s, e) => _cmdSender.State.LandingDirection = radLd21.Checked;
                _resetActions.Add(() => { radLd12.Checked = true; _cmdSender.State.LandingDirection = false; });
                grpLandDir.Controls.Add(radLd12);
                grpLandDir.Controls.Add(radLd21);
                page.Controls.Add(grpLandDir);

                // ── Retract / Deploy (mutually exclusive) ─────────────────────
                //   NLRetractionCmd (Landing byte bit 7): Retract=true, Deploy=false (default deployed)
                int rdRow = ldY + 72 + 20;   // below Landing Direction groupbox
                GcsButton btnRetract = null, btnDeploy = null;
                btnRetract = new GcsButton
                {
                    Text = "Retract", Location = new Point(T3_OFFSET + H_GAP + 0 * (T3W + H_GAP), rdRow),
                    Size = new Size(T3W, T3H), ForeColor = LIME, Font = t3Font,
                    Tag = false, OffColor = Color.FromArgb(64, 64, 64)
                };
                btnDeploy = new GcsButton
                {
                    Text = "Deploy", Location = new Point(T3_OFFSET + H_GAP + 1 * (T3W + H_GAP), rdRow),
                    Size = new Size(T3W, T3H), ForeColor = LIME, Font = t3Font,
                    Tag = true, OffColor = Color.FromArgb(64, 64, 64)
                };
                btnDeploy.SetOn(true);   // gear deployed by default
                btnRetract.Click += (s, e) =>
                {
                    if ((bool)btnRetract.Tag) { btnRetract.SetOn(true); return; }
                    btnRetract.Tag = true;  btnRetract.SetOn(true);
                    btnDeploy.Tag  = false; btnDeploy.SetOn(false);
                    _cmdSender.State.NLRetractionCmd = true;   // Landing byte bit 7 (spec: RetractNoseLandingGearCMD)
                };
                btnDeploy.Click += (s, e) =>
                {
                    if ((bool)btnDeploy.Tag) { btnDeploy.SetOn(true); return; }
                    btnDeploy.Tag  = true;  btnDeploy.SetOn(true);
                    btnRetract.Tag = false; btnRetract.SetOn(false);
                    _cmdSender.State.NLRetractionCmd = false;   // Landing byte bit 7 (spec: RetractNoseLandingGearCMD)
                };
                _resetActions.Add(() =>
                {
                    btnDeploy.Tag  = true;  btnDeploy.SetOn(true);
                    btnRetract.Tag = false; btnRetract.SetOn(false);
                    _cmdSender.State.NLRetractionCmd = false;   // Landing byte bit 7 (spec: RetractNoseLandingGearCMD)
                });
                page.Controls.Add(btnRetract);
                page.Controls.Add(btnDeploy);

                // ── Takeoff Direction groupbox (radio: End1→End2 default) ─────
                //   TODirection = false → End1→End2 ; true → End2→End1
                int toY = rdRow + T3H + 20;
                var grpToDir = new GroupBox
                {
                    Text      = "Takeoff Direction",
                    Location  = new Point(T3_OFFSET, toY),
                    Size      = new Size(GRP_W, 72),
                    ForeColor = LIME,
                    Font      = grpFont
                };
                var radTo12 = new RadioButton
                {
                    Text = "From End1 to End2", Location = new Point(12, 22),
                    Size = new Size(RAD_W, 22), ForeColor = LIME, BackColor = Color.Transparent,
                    Font = radFont, Checked = true
                };
                var radTo21 = new RadioButton
                {
                    Text = "From End2 to End1", Location = new Point(12, 46),
                    Size = new Size(RAD_W, 22), ForeColor = LIME, BackColor = Color.Transparent,
                    Font = radFont, Checked = false
                };
                radTo21.CheckedChanged += (s, e) => _cmdSender.State.TODirection = radTo21.Checked;
                _resetActions.Add(() => { radTo12.Checked = true; _cmdSender.State.TODirection = false; });
                grpToDir.Controls.Add(radTo12);
                grpToDir.Controls.Add(radTo21);
                page.Controls.Add(grpToDir);

                tc.TabPages.Add(page);
            }

            // ── TAB 4 — System ID & FCC ───────────────────────────────────
            //   Row 0 : Deny Sensors Fail | (gap)        | Payload Video
            //   Row 1 : Deny Abort Taxi   | Radio Silence | Backup Link
            //   GroupBox "FCC Selection"  : Default | Master | Slave (exclusive)
            //   GroupBox "System ID Byte" : Reset/DC..Control Surface Test (exclusive)
            //   Ctrl-surface : Elevator | Aileron | Reset ; Rudder | Throttle | [☑ System ID]
            //   Bottom : DGPS Corrections | OVI Data | Payload
            {
                var page = new TabPage("SysID")
                {
                    BackColor = Color.FromArgb(12, 12, 12),
                    ForeColor = LIME
                };
                AddHeading(page, "System ID & FCC");

                const int W   = 100;      // button width
                const int H   = 40;       // button height (compact — many rows)
                const int S   = H + 4;    // row stride
                const int OFF = 5;        // left margin
                const int G   = 4;        // gap
                int Cx(int col) => OFF + G + col * (W + G);   // column x
                var bFont = new Font("Arial Rounded MT Bold", 8.5f, FontStyle.Bold);
                var gFont = new Font("Arial Rounded MT Bold", 8f, FontStyle.Regular);
                int fullW = G + 3 * (W + G);   // 3-col span = 316

                int y = BTN_Y - 12;   // first row

                // ── Row 0 / Row 1 top toggles ─────────────────────────────
                // ("Deny Sensors Fail" removed — no matching command in the spec)
                var bPayVid = FreeBtn("Payload Video", Cx(2), y, W, H, bFont);
                WireToggle(bPayVid, false, on => { });           // no field
                page.Controls.Add(bPayVid);

                y += S;
                var bDenyTaxi = FreeBtn("Deny Abort Taxi", Cx(0), y, W, H, bFont);
                WireToggle(bDenyTaxi, false, on => _cmdSender.State.DenyAbortTaxi = on);
                page.Controls.Add(bDenyTaxi);
                var bRadSil = FreeBtn("Radio Silence", Cx(1), y, W, H, bFont);
                WireToggle(bRadSil, false, on => { });           // no field
                page.Controls.Add(bRadSil);
                var bBupLink = FreeBtn("Backup Link", Cx(2), y, W, H, bFont);
                WireToggle(bBupLink, false, on => _cmdSender.State.Switch2BupLink = on);
                page.Controls.Add(bBupLink);

                // ── FCC Selection groupbox (Master / Slave) ───────────────
                //   Spec (Auxiliary sheet, "Use FCC", byte 15 bit 6): 0 = Master, 1 = Slave.
                //   Default = Master.
                y += S + 6;
                int fccW = G + 2 * (W + G);   // 2-button span — borders align with Master/Slave ends
                var grpFcc = new GroupBox
                {
                    Text = "FCC Selection", Location = new Point(OFF, y),
                    Size = new Size(fccW, 18 + H + 6), ForeColor = LIME, Font = gFont
                };
                var fccMaster = FreeBtn("Master", G + 0 * (W + G), 18, W, H, bFont);
                var fccSlave  = FreeBtn("Slave",  G + 1 * (W + G), 18, W, H, bFont);
                // Master → UseFcc = false (0) ; Slave → UseFcc = true (1)
                WireExclusive(new[] { fccMaster, fccSlave }, 0, idx =>
                    _cmdSender.State.UseFcc = (idx == 1));
                grpFcc.Controls.Add(fccMaster);
                grpFcc.Controls.Add(fccSlave);
                page.Controls.Add(grpFcc);

                // ── System ID Byte groupbox ───────────────────────────────
                //   3×3 matrix at HALF height (Hide checkbox in empty slot),
                //   then the control-surface rows are enclosed in the SAME boundary.
                int sidH = H / 2;        // 20px button height (half)
                int sidS = sidH + 4;     // 24px row stride
                int Rx(int col) => G + col * (W + G);   // groupbox-relative column x
                int matrixBot = 18 + 3 * sidS;          // below the 3 matrix rows
                int cr0 = matrixBot + 10;               // control-surface row 0
                int cr1 = cr0 + S;                      // control-surface row 1
                int sidGrpH = cr1 + H + 8;              // full groupbox height
                y += 18 + H + 6 + 6;
                var grpSid = new GroupBox
                {
                    Text = "System ID Byte", Location = new Point(OFF, y),
                    Size = new Size(fullW, sidGrpH), ForeColor = LIME, Font = gFont
                };
                var sidBtns = new[]
                {
                    FreeBtn("Reset/DC",     Rx(0), 18 + 0 * sidS, W, sidH, bFont),
                    FreeBtn("Chirp 1",      Rx(1), 18 + 0 * sidS, W, sidH, bFont),
                    FreeBtn("Chirp 2",      Rx(2), 18 + 0 * sidS, W, sidH, bFont),
                    FreeBtn("Chirp 3",      Rx(0), 18 + 1 * sidS, W, sidH, bFont),
                    FreeBtn("Doublet 1",    Rx(1), 18 + 1 * sidS, W, sidH, bFont),
                    FreeBtn("Doublet 2",    Rx(2), 18 + 1 * sidS, W, sidH, bFont),
                    FreeBtn("Doublet 3",    Rx(0), 18 + 2 * sidS, W, sidH, bFont),
                    FreeBtn("Ctrl Surf Test", Rx(1), 18 + 2 * sidS, W, sidH, bFont),
                };
                // NOTE: SLT/SysID byte is NYI in the encoder — selection is visual only.
                WireExclusive(sidBtns, 0, idx => { });
                foreach (var b in sidBtns) grpSid.Controls.Add(b);

                // Checkbox in the empty slot (col2, row2): hides the matrix buttons.
                // Default CHECKED → matrix buttons hidden at startup.
                var chkHide = new CheckBox
                {
                    Text = "Hide",
                    Location = new Point(Rx(2), 18 + 2 * sidS),
                    Size = new Size(W, 20), ForeColor = LIME, BackColor = Color.Transparent,
                    Font = gFont, Checked = true
                };
                chkHide.CheckedChanged += (s, e) =>
                {
                    foreach (var b in sidBtns) b.Visible = !chkHide.Checked;
                };
                foreach (var b in sidBtns) b.Visible = false;   // hidden by default
                grpSid.Controls.Add(chkHide);

                // Control-surface rows inside the same boundary
                var csElevator = FreeBtn("Elevator", Rx(0), cr0, W, H, bFont);
                var csAileron  = FreeBtn("Aileron",  Rx(1), cr0, W, H, bFont);
                var csReset    = FreeBtn("Reset",    Rx(2), cr0, W, H, bFont);
                var csRudder   = FreeBtn("Rudder",   Rx(0), cr1, W, H, bFont);
                var csThrottle = FreeBtn("Throttle", Rx(1), cr1, W, H, bFont);
                WireExclusive(new[] { csElevator, csAileron, csRudder, csThrottle, csReset }, 4, idx => { });
                grpSid.Controls.Add(csElevator); grpSid.Controls.Add(csAileron);
                grpSid.Controls.Add(csReset);    grpSid.Controls.Add(csRudder);
                grpSid.Controls.Add(csThrottle);

                var chkSysId = new CheckBox
                {
                    Text = "System ID", Location = new Point(Rx(2) + 4, cr1 + 10),
                    Size = new Size(100, 22), ForeColor = LIME, BackColor = Color.Transparent,
                    Font = gFont, Checked = false
                };
                grpSid.Controls.Add(chkSysId);
                page.Controls.Add(grpSid);

                // ── Bottom row : DGPS Corrections | OVI Data | Payload ────
                y += sidGrpH + 10;
                var bDgps = FreeBtn("DGPS Corrections", Cx(0), y, W, H, bFont);
                WireToggle(bDgps, false, on => _cmdSender.State.DgpsCorrectionsEnabled = on);
                page.Controls.Add(bDgps);
                var bOvi = FreeBtn("OVI Data", Cx(1), y, W, H, bFont);
                WireToggle(bOvi, false, on => { });     // no field
                page.Controls.Add(bOvi);
                var bPayload = FreeBtn("Payload", Cx(2), y, W, H, bFont);
                WireToggle(bPayload, false, on => { }); // no field
                page.Controls.Add(bPayload);

                tc.TabPages.Add(page);
            }

            // ── TAB 5 — Navigation Management ────────────────────────────
            //   Row 0 : Dash R2 Base | R2 Base | Loiter
            //   Row 1 : Use Curve Guidance
            //   GroupBox "Way Point"        : Next | Previous
            //   GroupBox "Track Correction" : Left | Right
            //   Lat scheme : Offtrack Linear | Offtrack Nonlinear | Pursuit (exclusive)
            {
                var page = new TabPage("Nav")
                {
                    BackColor = Color.FromArgb(12, 12, 12),
                    ForeColor = LIME
                };
                AddHeading(page, "Navigation Management");

                const int W   = 100;
                const int H   = 50;
                const int S   = H + 4;
                const int OFF = 5;
                const int G   = 4;
                int Cx(int col) => OFF + G + col * (W + G);
                var bFont = new Font("Arial Rounded MT Bold", 9f, FontStyle.Bold);
                var gFont = new Font("Arial Rounded MT Bold", 8f, FontStyle.Regular);

                int y = BTN_Y - 10;

                // ── Row 0 ─────────────────────────────────────────────────
                var bDashR2 = FreeBtn("Dash R2 Base", Cx(0), y, W, H, bFont);
                WireToggle(bDashR2, false, on => _cmdSender.State.DashR2Base = on);
                page.Controls.Add(bDashR2);
                var bR2 = FreeBtn("R2 Base", Cx(1), y, W, H, bFont);
                WireToggle(bR2, false, on => _cmdSender.State.GcsR2Base = on);
                page.Controls.Add(bR2);
                var bLoiter = FreeBtn("Loiter", Cx(2), y, W, H, bFont);
                WireToggle(bLoiter, false, on => _cmdSender.State.Loiter = on);
                page.Controls.Add(bLoiter);

                // ── Row 1 ─────────────────────────────────────────────────
                y += S;
                var bCurve = FreeBtn("Use Curve Guidance", Cx(0), y, W, H, bFont);
                WireToggle(bCurve, false, on => _cmdSender.State.UseCurveGuidance = on);
                page.Controls.Add(bCurve);

                // ── Way Point groupbox (Next / Previous) ──────────────────
                y += S + 8;
                int twoW = G + 2 * (W + G);   // 2-button span (aligns border to button ends)
                var grpWp = new GroupBox
                {
                    Text = "Way Point", Location = new Point(OFF, y),
                    Size = new Size(twoW, 18 + H + 6), ForeColor = LIME, Font = gFont
                };
                // Momentary "send-once": the spec warns waypoints increment every
                // packet the flag is held. A click sets the bit, sends ONE packet,
                // then clears it — so it can never latch.
                void WireOneShot(GcsButton b, Action<bool> set)
                {
                    b.Click += (s, e) =>
                    {
                        b.SetOn(false);                       // never latch on
                        set(true);
                        if (_cmdSender.IsRunning) _cmdSender.SendNow();
                        set(false);
                    };
                }
                var bNext = FreeBtn("Next", G + 0 * (W + G), 18, W, H, bFont);
                WireOneShot(bNext, on => _cmdSender.State.GoToNextWP = on);
                var bPrev = FreeBtn("Previous", G + 1 * (W + G), 18, W, H, bFont);
                WireOneShot(bPrev, on => _cmdSender.State.GoToPrevWP = on);
                grpWp.Controls.Add(bNext);
                grpWp.Controls.Add(bPrev);
                page.Controls.Add(grpWp);

                // ── Track Correction groupbox (Left / Right) ──────────────
                y += 18 + H + 6 + 8;
                var grpTc = new GroupBox
                {
                    Text = "Track Correction", Location = new Point(OFF, y),
                    Size = new Size(twoW, 18 + H + 6), ForeColor = LIME, Font = gFont
                };
                var bLeft = FreeBtn("Left", G + 0 * (W + G), 18, W, H, bFont);
                WireToggle(bLeft, false, on => _cmdSender.State.LeftXTrackCorrection = on);
                var bRight = FreeBtn("Right", G + 1 * (W + G), 18, W, H, bFont);
                WireToggle(bRight, false, on => _cmdSender.State.RightXTrackCorrection = on);
                grpTc.Controls.Add(bLeft);
                grpTc.Controls.Add(bRight);
                page.Controls.Add(grpTc);

                // ── Lateral guidance scheme (exclusive: 0/1/2) ────────────
                y += 18 + H + 6 + 8;
                var gsLinear = FreeBtn("Offtrk Linear",    Cx(0), y, W, H, bFont);
                var gsNonLin = FreeBtn("Offtrk NonLinear", Cx(1), y, W, H, bFont);
                var gsPursuit = FreeBtn("Pursuit Guid",    Cx(2), y, W, H, bFont);
                WireExclusive(new[] { gsLinear, gsNonLin, gsPursuit }, 0,
                              idx => _cmdSender.State.LateralGuidanceScheme = idx);
                page.Controls.Add(gsLinear);
                page.Controls.Add(gsNonLin);
                page.Controls.Add(gsPursuit);

                tc.TabPages.Add(page);
            }

            // ── TAB 6 — Power Management ──────────────────────────────────
            //   GroupBox "Servo": Port Servos (2 rows) + Starboard Servos (2 rows)
            //   Power buttons (4 rows) : Weapon..ECU
            //   Telemetry text: Brakes Actuation / NLG Retraction
            //
            //   NOTE: the 24 servo/power buttons map to RelayControlByte1/2/3
            //   (pkt[26-28]).  Bit order below is ASSUMED SEQUENTIAL and should
            //   be confirmed against the IPSU relay spec.
            {
                var page = new TabPage("Power")
                {
                    BackColor = Color.FromArgb(12, 12, 12),
                    ForeColor = LIME
                };
                AddHeading(page, "Power Management");

                const int W   = 98;
                const int H   = 34;
                const int S   = H + 4;    // stride = 38
                const int OFF = 5;
                const int G   = 4;
                int Cx(int col) => OFF + G + col * (W + G);   // page-relative x
                int Gx(int col) => 6 + col * (W + G);         // groupbox-relative x
                var bFont = new Font("Arial Rounded MT Bold", 8f, FontStyle.Bold);
                var gFont = new Font("Arial Rounded MT Bold", 8f, FontStyle.Regular);
                var subFont = new Font("Arial Rounded MT Bold", 8f, FontStyle.Bold);
                int fullW = G + 3 * (W + G);   // = 310

                // Set/clear one bit of a relay byte in CommandState.
                // Spec (RelayCntrlByte sheets): 0 = ON/Enable, 1 = OFF/Disable.
                // So a button turned ON CLEARS its bit; OFF SETS it.
                void SetRelay(int byteNo, int bit, bool on)
                {
                    byte m = (byte)(1 << bit);
                    var st = _cmdSender.State;
                    bool setBit = !on;   // ON → 0, OFF → 1
                    if (byteNo == 1) st.RelayControlByte1 = (byte)(setBit ? (st.RelayControlByte1 | m) : (st.RelayControlByte1 & ~m));
                    else if (byteNo == 2) st.RelayControlByte2 = (byte)(setBit ? (st.RelayControlByte2 | m) : (st.RelayControlByte2 & ~m));
                    else                  st.RelayControlByte3 = (byte)(setBit ? (st.RelayControlByte3 | m) : (st.RelayControlByte3 & ~m));
                }
                // Create + wire a relay power button, add it to the given parent.
                // NOTE: do NOT touch _cmdSender here — tabs are built in the ctor,
                // before _cmdSender exists. Default relay bytes are seeded in
                // CommandState (RelayControlByte1/2/3 initializers) instead.
                void PwrBtn(Control parent, string lbl, int x, int y2, bool initOn, int byteNo, int bit)
                {
                    var b = FreeBtn(lbl, x, y2, W, H, bFont);
                    WireToggle(b, initOn, on => SetRelay(byteNo, bit, on));
                    parent.Controls.Add(b);
                }

                int y = BTN_Y - 12;

                // ── Servo groupbox ────────────────────────────────────────
                int servoH = 16 + 14 + 2 * S + 14 + 2 * S + 6;   // title+sub+2rows+sub+2rows
                var grpServo = new GroupBox
                {
                    Text = "Servo", Location = new Point(OFF, y),
                    Size = new Size(fullW, servoH), ForeColor = LIME, Font = gFont
                };
                int gy = 16;
                grpServo.Controls.Add(new Label
                {
                    Text = "Port Servos", Location = new Point(6, gy), Size = new Size(fullW - 12, 14),
                    ForeColor = LIME, BackColor = Color.Transparent, Font = subFont
                });
                // Byte/bit per RelayCntrlByte1/2/3 sheets; polarity handled in SetRelay (0=ON).
                gy += 14;
                PwrBtn(grpServo, "Aileron",    Gx(0), gy, true,  1, 7);   // Port_Aileron
                PwrBtn(grpServo, "Flap",       Gx(1), gy, true,  1, 1);   // Port_Flap
                PwrBtn(grpServo, "B Rudder",   Gx(2), gy, true,  1, 6);   // Port_Bottom_Ruddervator
                gy += S;
                PwrBtn(grpServo, "Top Rudder", Gx(0), gy, true,  1, 0);   // Port_Top_Ruddervator
                PwrBtn(grpServo, "Video Rec",  Gx(1), gy, false, 1, 3);   // Video_Recorder (UI default OFF)
                PwrBtn(grpServo, "Strobe",     Gx(2), gy, false, 1, 2);   // Strobe_Light  (UI default OFF)
                gy += S;
                grpServo.Controls.Add(new Label
                {
                    Text = "Star Board Servos", Location = new Point(6, gy), Size = new Size(fullW - 12, 14),
                    ForeColor = LIME, BackColor = Color.Transparent, Font = subFont
                });
                gy += 14;
                PwrBtn(grpServo, "Aileron",    Gx(0), gy, true,  1, 4);   // StarBoard_Aileron
                PwrBtn(grpServo, "Flap",       Gx(1), gy, true,  2, 6);   // StarBoard_Flap
                PwrBtn(grpServo, "B Rudder",   Gx(2), gy, true,  1, 5);   // StarBoard_Bottom_Ruddervator
                gy += S;
                PwrBtn(grpServo, "Top Rudder", Gx(0), gy, true,  2, 7);   // StarBoard_Top_Ruddervator
                PwrBtn(grpServo, "N Wheel",    Gx(1), gy, true,  2, 5);   // Nose_Wheel
                PwrBtn(grpServo, "Servo12",    Gx(2), gy, true,  2, 4);   // Servo12_Cntrl
                page.Controls.Add(grpServo);

                // ── Power buttons (4 rows) ────────────────────────────────
                y += servoH + 16;            // gap after 4th (last servo) row
                PwrBtn(page, "Weapon",      Cx(0), y, false, 2, 3);   // Weapon_Cntrl (dft OFF)
                PwrBtn(page, "RETRC",       Cx(1), y, false, 3, 3);   // Retraction   (dft Disable)
                PwrBtn(page, "ComLink",     Cx(2), y, true,  3, 7);   // ComLnk
                y += S;
                PwrBtn(page, "Camera OPT12", Cx(0), y, true,  3, 6);  // Camera/Opt12
                PwrBtn(page, "Pitot Heater", Cx(1), y, false, 2, 1);  // Pitot Heater_Cntrl (dft OFF)
                PwrBtn(page, "Payload",      Cx(2), y, false, 2, 2);  // Payload_Cntrl (dft OFF)
                // ── Rows 7 & 8 enclosed in a boundary groupbox ────────────
                y += S + 14;                 // gap after 6th row
                int grp2H = 10 + H + 4 + H + 8;   // top pad + 2 rows + gaps
                var grpPwr2 = new GroupBox
                {
                    Text = "", Location = new Point(OFF, y - 6),
                    Size = new Size(fullW, grp2H), ForeColor = LIME, Font = gFont
                };
                int gr7 = 10, gr8 = 10 + S;
                PwrBtn(grpPwr2, "AUX 1",       Gx(0), gr7, false, 3, 0);   // Aux1 (dft OFF)
                PwrBtn(grpPwr2, "OPT5V",       Gx(1), gr7, true,  3, 1);   // Opt5v
                PwrBtn(grpPwr2, "Brake Power", Gx(2), gr7, false, 3, 2);   // Brakes (dft Disable)
                PwrBtn(grpPwr2, "ALTERNATOR",  Gx(0), gr8, true,  3, 4);   // Alternator
                PwrBtn(grpPwr2, "AUX 2",       Gx(1), gr8, false, 3, 5);   // Aux2 (dft OFF)
                PwrBtn(grpPwr2, "ECU",         Gx(2), gr8, true,  2, 0);   // ECU_Cntrl
                page.Controls.Add(grpPwr2);

                // ── Telemetry readouts (text, live values TBD) ────────────
                y += grp2H - 6 + 6;
                page.Controls.Add(new Label
                {
                    Text = "Brakes Actuation: 0", Location = new Point(Cx(0), y),
                    Size = new Size(fullW, 18), ForeColor = LIME, BackColor = Color.Transparent, Font = gFont
                });
                page.Controls.Add(new Label
                {
                    Text = "NLG Retraction: 0", Location = new Point(Cx(0), y + 20),
                    Size = new Size(fullW, 18), ForeColor = LIME, BackColor = Color.Transparent, Font = gFont
                });

                tc.TabPages.Add(page);
            }

            // Clip all painting (including native COMCTL32 selection border) to the control bounds.
            // Without this, the selected first tab bleeds 1-2 px left past the control edge.
            tc.Region = new System.Drawing.Region(new Rectangle(0, 0, tc.Width, tc.Height));

            this.Controls.Add(tc);

            // ── "COMMAND" group — RESET above SEND, bottom-right of form ──
            // Matches ENGINE & POWER / LINK HEALTH style: GroupBox, Consolas 12pt Bold.
            const int SB_W   = 160;   // button width
            const int SB_H   = 60;    // button height
            const int SB_GAP = 8;     // gap between buttons
            const int SB_PAD = 8;     // inner padding left / right
            const int SB_TY  = 28;    // top offset inside GroupBox (clears title text)
            const int SB_BOT = 6;     // bottom padding

            int grpW = SB_PAD + SB_W + SB_PAD;                            // 176
            int grpH = SB_TY + SB_H + SB_GAP + SB_H + SB_BOT;            // 162
            int grpX = 1900 - grpW - 10;                                   // 1714
            int grpY = 1045 - grpH - 5;                                    // 878

            var grpCmd = MakeGroupBox("COMMAND", grpX, grpY, grpW, grpH, LIME, FNT14);

            var btnReset = new GcsButton
            {
                Text      = "RESET",
                Location  = new Point(SB_PAD, SB_TY),
                Size      = new Size(SB_W, SB_H),
                ForeColor = Color.FromArgb(220, 100, 0),
                Font      = new Font("Arial Rounded MT Bold", 13f, FontStyle.Bold),
                OffColor  = Color.FromArgb(60, 30, 0)
            };
            btnReset.Click += (s, e) =>
            {
                _cmdSender?.State.ApplyHilsDefaults();
                foreach (var act in _resetActions) act();
            };

            var btnSend = new GcsButton
            {
                Text      = "SEND",
                Location  = new Point(SB_PAD, SB_TY + SB_H + SB_GAP),
                Size      = new Size(SB_W, SB_H),
                ForeColor = Color.FromArgb(0, 220, 220),
                Font      = new Font("Arial Rounded MT Bold", 13f, FontStyle.Bold),
                OffColor  = Color.FromArgb(0, 50, 60)
            };
            btnSend.Click += (s, e) => _cmdSender?.SendNow();

            grpCmd.Controls.Add(btnReset);
            grpCmd.Controls.Add(btnSend);
            this.Controls.Add(grpCmd);
        }
        // ════════════════════════════════════════════════════════════════
        //  Helpers
        // ════════════════════════════════════════════════════════════════
        private Label FL(Control p, string t, int x, int y, int w, int h, Color fg)
        {
            var l = new Label
            {
                Text = t,
                Location = new Point(x, y),
                Size = new Size(w, h),
                ForeColor = fg,
                BackColor = Color.Transparent,
                Font = FNT14
            };
            p.Controls.Add(l);
            return l;
        }
        private Label FLn(Control p, string t, int x, int y, int w, int h,
                           Color fg, ContentAlignment a)
        {
            var l = new Label
            {
                Text = t,
                Location = new Point(x, y),
                Size = new Size(w, h),
                ForeColor = fg,
                BackColor = Color.Transparent,
                Font = FNT14,
                TextAlign = a
            };
            p.Controls.Add(l);
            return l;
        }
        private Label FLg(Control p, string t, int x, int y, int w, int h,
                           Color fg, ContentAlignment a)
        {
            var l = new Label
            {
                Text = t,
                Location = new Point(x, y),
                Size = new Size(w, h),
                ForeColor = fg,
                BackColor = Color.Transparent,
                Font = FNT14,
                TextAlign = a
            };
            p.Controls.Add(l);
            return l;
        }
        private void Sep(Control p, int x, int y, int w)
        {
            p.Controls.Add(new Panel
            {
                Location = new Point(x, y),
                Size = new Size(w, 2),
                BackColor = Color.FromArgb(0, 140, 0)
            });
        }
        private void EngRow(Control parent, string name, int nx, int y, int nw, int h,
                             int vx, int vw, out Label valLabel, Font rowFont = null)
        {
            var f = rowFont ?? FNT10B;
            parent.Controls.Add(new Label
            {
                Text = name,
                Location = new Point(nx, y),
                Size = new Size(nw, h),
                ForeColor = LIME,
                BackColor = Color.Transparent,
                Font = f,
                TextAlign = ContentAlignment.MiddleLeft
            });
            var val = new Label
            {
                Text = "----",
                Location = new Point(vx, y),
                Size = new Size(vw, h),
                ForeColor = WHITE,
                BackColor = Color.Transparent,
                Font = f
            };
            parent.Controls.Add(val);
            valLabel = val;
        }
        private GroupBox MakeGroupBox(string title, int x, int y, int w, int h,
                                       Color tc, Font f)
        {
            return new GroupBox
            {
                Text = title,
                Location = new Point(x, y),
                Size = new Size(w, h),
                ForeColor = tc,
                BackColor = Color.FromArgb(12, 12, 12),
                Font = f
            };
        }
        private TrackBar DemoSlider(Control parent, string name, int min, int max,
                                     int sv, int y, out Label valueLabel)
        {
            parent.Controls.Add(new Label
            {
                Text = name,
                Location = new Point(6, y),
                Size = new Size(220, 20),
                ForeColor = Color.Silver,
                BackColor = Color.Transparent,
                Font = FNT10
            });
            var vl = new Label
            {
                Text = sv.ToString(),
                Location = new Point(228, y),
                Size = new Size(148, 20),
                ForeColor = WHITE,
                BackColor = Color.Transparent,
                Font = FNT11,
                TextAlign = ContentAlignment.MiddleLeft
            };
            parent.Controls.Add(vl);
            var tb = new TrackBar
            {
                Minimum = min,
                Maximum = max,
                Value = sv,
                TickFrequency = Math.Max(1, (max - min) / 10),
                SmallChange = 1,
                LargeChange = Math.Max(1, (max - min) / 20),
                Location = new Point(4, y + 22),
                Size = new Size(372, 36),
                BackColor = Color.FromArgb(30, 30, 30)
            };
            Label cap = vl;
            tb.ValueChanged += (s, ev) => cap.Text = ((TrackBar)s).Value.ToString();
            parent.Controls.Add(tb);
            valueLabel = vl;
            return tb;
        }
        // ════════════════════════════════════════════════════════════════
        //  GcsButton — beveled panel-style toggle button
        //  First click → latches ON (sunken, green).
        //  Second click → releases OFF (raised, dark grey).
        // ════════════════════════════════════════════════════════════════
        private class GcsButton : Button
        {
            private bool _isOn = false;  // latched toggle state
            private bool _mouseHeld = false;  // transient hold visual
            private static readonly Color CLR_OFF = Color.FromArgb(64, 64, 64);
            private static readonly Color CLR_ON  = Color.FromArgb(0, 130, 0);
            public Color OffColor { get; set; } = CLR_OFF;   // per-instance override
            private static readonly Color CLR_HI = Color.FromArgb(130, 130, 130);
            private static readonly Color CLR_SH = Color.FromArgb(15, 15, 15);
            // ── LED images (loaded once, shared across all instances) ─────
            private static readonly Image _ledOff;
            private static readonly Image _ledOn;
            private const int LED_SIZE = 14;  // drawn size in px (scaled from 100×100 source)
            private const int LED_PAD  = 3;   // inset from top-right edge
            static GcsButton()
            {
                try
                {
                    string res = System.IO.Path.Combine(Application.StartupPath, "Resources");
                    _ledOff = Image.FromFile(System.IO.Path.Combine(res, "red-led-off-th.png"));
                    _ledOn  = Image.FromFile(System.IO.Path.Combine(res, "red-led-on-th.png"));
                }
                catch { /* images not found — LED silently omitted */ }
            }
            public bool IsOn  => _isOn;
            public bool ShowLed { get; set; } = false;
            /// <summary>Programmatically set latch state (e.g. when float form is closed externally).</summary>
            public void SetOn(bool value) { _isOn = value; Invalidate(); }
            public GcsButton()
            {
                SetStyle(ControlStyles.UserPaint |
                         ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.DoubleBuffer, true);
            }
            protected override void OnMouseDown(MouseEventArgs e)
            {
                _mouseHeld = true;
                Invalidate();
                base.OnMouseDown(e);
            }
            protected override void OnMouseUp(MouseEventArgs e)
            {
                _mouseHeld = false;
                _isOn = !_isOn;   // latch / unlatch on release
                Invalidate();
                base.OnMouseUp(e);
            }
            protected override void OnMouseLeave(EventArgs e)
            {
                if (_mouseHeld) { _mouseHeld = false; Invalidate(); }
                base.OnMouseLeave(e);
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                // Show pressed look while held OR while latched ON
                bool showPressed = _isOn || _mouseHeld;
                var g = e.Graphics;
                var r = ClientRectangle;
                const int B = 3;
                g.FillRectangle(new SolidBrush(showPressed ? CLR_ON : OffColor), r);
                Color hi = showPressed ? CLR_SH : CLR_HI;
                Color sh = showPressed ? CLR_HI : CLR_SH;
                using (var pHi = new Pen(hi, 1))
                using (var pSh = new Pen(sh, 1))
                {
                    for (int i = 0; i < B; i++)
                    {
                        g.DrawLine(pHi, i, i, r.Width - 1 - i, i);
                        g.DrawLine(pHi, i, i, i, r.Height - 1 - i);
                        g.DrawLine(pSh, i, r.Height - 1 - i, r.Width - 1 - i, r.Height - 1 - i);
                        g.DrawLine(pSh, r.Width - 1 - i, i, r.Width - 1 - i, r.Height - 1 - i);
                    }
                }
                int d = showPressed ? 1 : 0;
                var fmt = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                // Reserve right-side space for LED so text doesn't overlap it
                int textW = (ShowLed && _ledOff != null) ? r.Width - LED_SIZE - LED_PAD * 2 : r.Width;
                // Text is dark gray when unpressed, normal ForeColor when pressed/latched ON
                Color txtClr = showPressed ? ForeColor : Color.FromArgb(210, 210, 210);
                g.DrawString(Text, Font, new SolidBrush(txtClr),
                             new RectangleF(r.X + d, r.Y + d, textW, r.Height), fmt);
                // Draw LED top-right corner (only when ShowLed is true)
                if (ShowLed)
                {
                    Image led = _isOn ? _ledOn : _ledOff;
                    if (led != null)
                        g.DrawImage(led, new Rectangle(r.Width - LED_SIZE - LED_PAD - 2,
                                                       LED_PAD + 2, LED_SIZE, LED_SIZE));
                }
            }
        }
    }
}
