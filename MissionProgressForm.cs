using System;
using System.Drawing;
using System.Windows.Forms;

// ================================================================
//  MissionProgressForm.cs  –  GCS_240626
//
//  Modeless window shown during mission upload or download.
//  Displays:
//    • Link status  — whether COM9 is receiving telemetry (proves
//                     the serial link to sgsim is alive)
//    • Packet log   — each packet sent + the ACK/response received
//    • Progress bar — packets sent vs total
//    • Final result — green "SUCCESS" or red "FAILED"
//
//  Usage (Form1):
//    var frm = new MissionProgressForm("Upload — SG_05_65KM", 26);
//    frm.Show(this);
//    // feed progress via IProgress<MissionUploadProgress> that calls:
//    frm.ReportProgress(rpt);
//    // on completion:
//    frm.SetFinalResult(success: true);
// ================================================================

namespace GCS_240626
{
    public class MissionProgressForm : Form
    {
        // ── Layout ────────────────────────────────────────────────────────
        private readonly Label          _lblLink;
        private readonly Label          _lblPackets;
        private readonly ProgressBar    _bar;
        private readonly RichTextBox    _log;
        private readonly Label          _lblResult;
        private readonly System.Windows.Forms.Timer _linkTimer;

        // ── Colours ───────────────────────────────────────────────────────
        private static readonly Color BG       = Color.FromArgb(15, 15, 15);
        private static readonly Color CLR_OK   = Color.FromArgb(0, 220, 80);
        private static readonly Color CLR_WARN = Color.Orange;
        private static readonly Color CLR_ERR  = Color.FromArgb(255, 60, 60);
        private static readonly Color CLR_INFO = Color.FromArgb(160, 200, 255);
        private static readonly Color CLR_DIM  = Color.FromArgb(100, 100, 100);

        private int _lastRawCount;   // used to detect telemetry flow

        // ─────────────────────────────────────────────────────────────────
        public MissionProgressForm(string operationTitle, int totalPackets)
        {
            Text            = operationTitle;
            ClientSize      = new Size(540, 460);
            MinimumSize     = new Size(400, 300);
            BackColor       = BG;
            ForeColor       = Color.White;
            Font            = new Font("Consolas", 9f);
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            StartPosition   = FormStartPosition.CenterParent;

            // ── Link status strip ─────────────────────────────────────────
            var pnlTop = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 30,
                BackColor = Color.FromArgb(25, 25, 25),
            };

            _lblLink = new Label
            {
                Text      = "●  Checking link…",
                ForeColor = CLR_DIM,
                Location  = new Point(8, 6),
                AutoSize  = true,
                Font      = new Font("Consolas", 9f, FontStyle.Bold),
            };

            _lblPackets = new Label
            {
                Text      = $"0 / {totalPackets} packets",
                ForeColor = CLR_DIM,
                AutoSize  = true,
                Font      = new Font("Consolas", 9f),
            };
            _lblPackets.Location = new Point(
                ClientSize.Width - _lblPackets.PreferredWidth - 10, 8);
            _lblPackets.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            pnlTop.Controls.Add(_lblLink);
            pnlTop.Controls.Add(_lblPackets);

            // ── Progress bar ──────────────────────────────────────────────
            _bar = new ProgressBar
            {
                Dock    = DockStyle.Top,
                Height  = 10,
                Minimum = 0,
                Maximum = Math.Max(totalPackets, 1),
                Value   = 0,
                Style   = ProgressBarStyle.Continuous,
            };

            // ── Scrolling log ─────────────────────────────────────────────
            _log = new RichTextBox
            {
                Dock       = DockStyle.Fill,
                BackColor  = Color.FromArgb(10, 10, 10),
                ForeColor  = Color.White,
                Font       = new Font("Consolas", 9f),
                ReadOnly   = true,
                WordWrap   = false,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
            };

            // ── Result banner ─────────────────────────────────────────────
            _lblResult = new Label
            {
                Dock      = DockStyle.Bottom,
                Height    = 32,
                Text      = "",
                TextAlign = ContentAlignment.MiddleCenter,
                Font      = new Font("Consolas", 11f, FontStyle.Bold),
                BackColor = Color.FromArgb(25, 25, 25),
                ForeColor = Color.White,
                Visible   = false,
            };

            // ── Close button ──────────────────────────────────────────────
            var btnClose = new Button
            {
                Text      = "Close",
                Dock      = DockStyle.Bottom,
                Height    = 28,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(40, 40, 40),
                ForeColor = Color.White,
            };
            btnClose.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);
            btnClose.Click += (s, e) => Close();

            Controls.Add(_log);
            Controls.Add(_bar);
            Controls.Add(pnlTop);
            Controls.Add(_lblResult);
            Controls.Add(btnClose);

            // ── Link-check timer (1 Hz) ───────────────────────────────────
            _lastRawCount = PacketReceiver.RawBytesCount;
            _linkTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _linkTimer.Tick += OnLinkTick;
            _linkTimer.Start();

            AppendLog("── Operation started ──────────────────────────────", CLR_DIM);
        }

        // ════════════════════════════════════════════════════════════════
        //  Public API
        // ════════════════════════════════════════════════════════════════

        /// <summary>Feed each progress update from MissionUploader/Downloader.</summary>
        public void ReportProgress(MissionUploadProgress rpt)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action(() => ReportProgress(rpt))); return; }

            Color c = rpt.IsError ? CLR_ERR : CLR_OK;

            // Colour-code the status line
            if (rpt.Status.Contains("OK") || rpt.Status.Contains("complete"))
                c = CLR_OK;
            else if (rpt.Status.Contains("timeout") || rpt.Status.Contains("NACK") || rpt.IsError)
                c = CLR_ERR;
            else if (rpt.Status.Contains("Sent") || rpt.Status.Contains("Requesting"))
                c = CLR_INFO;

            AppendLog(
                $"  [{rpt.PacketsSent,3}/{rpt.PacketsTotal,3}]  {rpt.Status}",
                c);

            if (rpt.PacketsTotal > 0)
            {
                _bar.Maximum = rpt.PacketsTotal;
                _bar.Value   = Math.Min(rpt.PacketsSent, rpt.PacketsTotal);
                _lblPackets.Text = $"{rpt.PacketsSent} / {rpt.PacketsTotal} packets";
            }
        }

        /// <summary>Call once the upload/download loop finishes.</summary>
        public void SetFinalResult(bool success)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action(() => SetFinalResult(success))); return; }

            _linkTimer.Stop();

            AppendLog("── Operation ended ────────────────────────────────", CLR_DIM);

            if (success)
            {
                _lblResult.Text      = "✓  SUCCESS";
                _lblResult.ForeColor = CLR_OK;
                _lblResult.BackColor = Color.FromArgb(0, 40, 10);
            }
            else
            {
                _lblResult.Text      = "✗  FAILED";
                _lblResult.ForeColor = CLR_ERR;
                _lblResult.BackColor = Color.FromArgb(50, 0, 0);
            }
            _lblResult.Visible = true;
        }

        // ════════════════════════════════════════════════════════════════
        //  Link check — runs every 1 s while form is open
        // ════════════════════════════════════════════════════════════════
        private void OnLinkTick(object sender, EventArgs e)
        {
            int current = PacketReceiver.RawBytesCount;
            int delta   = current - _lastRawCount;
            _lastRawCount = current;

            if (delta > 0)
            {
                _lblLink.Text      = $"●  COM9 LIVE  (+{delta} B/s)";
                _lblLink.ForeColor = CLR_OK;
            }
            else
            {
                _lblLink.Text      = "●  COM9 — no telemetry";
                _lblLink.ForeColor = CLR_WARN;
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  Log helper
        // ════════════════════════════════════════════════════════════════
        private void AppendLog(string text, Color color)
        {
            _log.SelectionStart  = _log.TextLength;
            _log.SelectionLength = 0;
            _log.SelectionColor  = color;
            _log.AppendText(text + "\n");
            _log.ScrollToCaret();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _linkTimer.Stop();
            _linkTimer.Dispose();
            base.OnFormClosed(e);
        }
    }
}
