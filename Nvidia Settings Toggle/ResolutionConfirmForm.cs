using System;
using System.Windows.Forms;

namespace NVCP_Toggle
{
    public partial class ResolutionConfirmForm : Form
    {
        private int countdownSeconds;
        private System.Windows.Forms.Timer countdownTimer = new System.Windows.Forms.Timer();

        public ResolutionConfirmForm(int seconds)
        {
            countdownSeconds = seconds;
            InitializeComponent();
            UpdateLabel();
            countdownTimer.Interval = 1000;
            countdownTimer.Tick += CountdownTimer_Tick;
            countdownTimer.Start();
            this.FormClosing += ResolutionConfirmForm_FormClosing;
        }

        private void CountdownTimer_Tick(object sender, EventArgs e)
        {
            countdownSeconds--;
            UpdateLabel();
            if (countdownSeconds <= 0)
            {
                countdownTimer.Stop();
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            }
        }

        private void UpdateLabel()
        {
            lblCountdown.Text = $"Keep this resolution?\nReverting in {countdownSeconds} seconds...";
        }

        private void btnKeep_Click(object sender, EventArgs e)
        {
            countdownTimer.Stop();
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            countdownTimer.Stop();
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        private void ResolutionConfirmForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (this.DialogResult != DialogResult.OK)
                this.DialogResult = DialogResult.Cancel;
        }

        private Label lblCountdown;
        private Button btnKeep;
        private Button btnCancel;

        private void InitializeComponent()
        {
            this.lblCountdown = new Label();
            this.btnKeep = new Button();
            this.btnCancel = new Button();
            this.SuspendLayout();
            // 
            // lblCountdown
            // 
            this.lblCountdown.AutoSize = true;
            this.lblCountdown.Font = new System.Drawing.Font("Segoe UI", 10F);
            this.lblCountdown.Location = new System.Drawing.Point(30, 20);
            this.lblCountdown.Name = "lblCountdown";
            this.lblCountdown.Size = new System.Drawing.Size(240, 40);
            this.lblCountdown.TabIndex = 0;
            this.lblCountdown.Text = "Keep this resolution?\r\nReverting in X seconds...";
            this.lblCountdown.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // btnKeep
            // 
            this.btnKeep.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.btnKeep.Location = new System.Drawing.Point(30, 80);
            this.btnKeep.Name = "btnKeep";
            this.btnKeep.Size = new System.Drawing.Size(100, 30);
            this.btnKeep.TabIndex = 1;
            this.btnKeep.Text = "Keep";
            this.btnKeep.UseVisualStyleBackColor = true;
            this.btnKeep.Click += new EventHandler(this.btnKeep_Click);
            // 
            // btnCancel
            // 
            this.btnCancel.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.btnCancel.Location = new System.Drawing.Point(150, 80);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(100, 30);
            this.btnCancel.TabIndex = 2;
            this.btnCancel.Text = "Revert";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new EventHandler(this.btnCancel_Click);
            // 
            // ResolutionConfirmForm
            // 
            this.ClientSize = new System.Drawing.Size(300, 130);
            this.Controls.Add(this.lblCountdown);
            this.Controls.Add(this.btnKeep);
            this.Controls.Add(this.btnCancel);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.Text = "Confirm Resolution";
            this.BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
            this.ForeColor = System.Drawing.Color.White;
            foreach (Control ctl in this.Controls)
            {
                ctl.BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
                ctl.ForeColor = System.Drawing.Color.White;
            }
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}
