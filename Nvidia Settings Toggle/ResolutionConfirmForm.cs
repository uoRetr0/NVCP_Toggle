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
            countdownTimer.Interval = 1000; // 1 second
            countdownTimer.Tick += CountdownTimer_Tick;
            countdownTimer.Start();
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
            lblCountdown.Text = $"Do you want to keep this resolution?\nReverting in {countdownSeconds} seconds...";
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
            this.lblCountdown.Location = new System.Drawing.Point(30, 20);
            this.lblCountdown.Name = "lblCountdown";
            this.lblCountdown.Size = new System.Drawing.Size(250, 30);
            this.lblCountdown.TabIndex = 0;
            this.lblCountdown.Text = "Do you want to keep this resolution?\r\nReverting in X seconds...";
            // 
            // btnKeep
            // 
            this.btnKeep.Location = new System.Drawing.Point(30, 70);
            this.btnKeep.Name = "btnKeep";
            this.btnKeep.Size = new System.Drawing.Size(100, 30);
            this.btnKeep.TabIndex = 1;
            this.btnKeep.Text = "Keep";
            this.btnKeep.UseVisualStyleBackColor = true;
            this.btnKeep.Click += new EventHandler(this.btnKeep_Click);
            // 
            // btnCancel
            // 
            this.btnCancel.Location = new System.Drawing.Point(150, 70);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(100, 30);
            this.btnCancel.TabIndex = 2;
            this.btnCancel.Text = "Revert";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new EventHandler(this.btnCancel_Click);
            // 
            // ResolutionConfirmForm
            // 
            this.ClientSize = new System.Drawing.Size(300, 120);
            this.Controls.Add(this.lblCountdown);
            this.Controls.Add(this.btnKeep);
            this.Controls.Add(this.btnCancel);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "ResolutionConfirmForm";
            this.StartPosition = FormStartPosition.CenterParent;
            this.Text = "Confirm Resolution";
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}
