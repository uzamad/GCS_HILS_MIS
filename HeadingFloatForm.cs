// ================================================================
//  HeadingFloatForm.cs  –  GCS_240626
//
//  Bare container form for the reparented HeadingIndicator.
//  When the user clicks X it hides (not disposes) and fires
//  FloatClosed so Form1 can uncheck the Float checkbox and
//  reparent the indicator back to the main form.
// ================================================================

using System;
using System.Drawing;
using System.Windows.Forms;

namespace GCS_240626
{
    public class HeadingFloatForm : Form
    {
        /// <summary>Fired when the user closes the window via the X button.</summary>
        public event Action FloatClosed;

        public HeadingFloatForm()
        {
            this.Text            = "Heading Indicator";
            this.FormBorderStyle = FormBorderStyle.SizableToolWindow;
            this.BackColor       = Color.Black;
            this.ClientSize      = new Size(220, 220);
            this.MinimumSize     = new Size(210, 210);
            this.StartPosition   = FormStartPosition.Manual;
            this.Location        = new Point(100, 100);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;   // don't dispose — just hide
                this.Hide();
                FloatClosed?.Invoke();   // tell Form1 to uncheck the checkbox
            }
            else
            {
                base.OnFormClosing(e);
            }
        }
    }
}
