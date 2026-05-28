
namespace TSSmartSchool
{
    partial class frmPrinterUsb
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.btnRestart = new System.Windows.Forms.Button();
            this.btnInstallDriver = new System.Windows.Forms.Button();
            this.btnGwReset = new System.Windows.Forms.Button();
            this.btnLogCopy = new System.Windows.Forms.Button();
            this.lstvNode = new System.Windows.Forms.ListView();
            this.btnLogClear = new System.Windows.Forms.Button();
            this.btnLoadJson = new System.Windows.Forms.Button();
            this.rtbLog = new System.Windows.Forms.RichTextBox();
            this.btnOff = new System.Windows.Forms.Button();
            this.btnOn = new System.Windows.Forms.Button();
            this.btnDisconnect = new System.Windows.Forms.Button();
            this.btnConnect = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // btnRestart
            // 
            this.btnRestart.Location = new System.Drawing.Point(349, 12);
            this.btnRestart.Name = "btnRestart";
            this.btnRestart.Size = new System.Drawing.Size(114, 81);
            this.btnRestart.TabIndex = 43;
            this.btnRestart.Text = "ReStart";
            this.btnRestart.UseVisualStyleBackColor = true;
            // 
            // btnInstallDriver
            // 
            this.btnInstallDriver.Location = new System.Drawing.Point(738, 13);
            this.btnInstallDriver.Name = "btnInstallDriver";
            this.btnInstallDriver.Size = new System.Drawing.Size(114, 80);
            this.btnInstallDriver.TabIndex = 42;
            this.btnInstallDriver.Text = "USB동글 설정";
            this.btnInstallDriver.UseVisualStyleBackColor = true;
            // 
            // btnGwReset
            // 
            this.btnGwReset.Location = new System.Drawing.Point(1027, 10);
            this.btnGwReset.Name = "btnGwReset";
            this.btnGwReset.Size = new System.Drawing.Size(102, 83);
            this.btnGwReset.TabIndex = 41;
            this.btnGwReset.Text = "장치 초기화";
            this.btnGwReset.UseVisualStyleBackColor = true;
            // 
            // btnLogCopy
            // 
            this.btnLogCopy.Location = new System.Drawing.Point(1135, 10);
            this.btnLogCopy.Name = "btnLogCopy";
            this.btnLogCopy.Size = new System.Drawing.Size(87, 31);
            this.btnLogCopy.TabIndex = 40;
            this.btnLogCopy.Text = "로그복사";
            this.btnLogCopy.UseVisualStyleBackColor = true;
            // 
            // lstvNode
            // 
            this.lstvNode.HideSelection = false;
            this.lstvNode.Location = new System.Drawing.Point(9, 103);
            this.lstvNode.Name = "lstvNode";
            this.lstvNode.Size = new System.Drawing.Size(244, 591);
            this.lstvNode.TabIndex = 39;
            this.lstvNode.UseCompatibleStateImageBehavior = false;
            // 
            // btnLogClear
            // 
            this.btnLogClear.Location = new System.Drawing.Point(1135, 50);
            this.btnLogClear.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.btnLogClear.Name = "btnLogClear";
            this.btnLogClear.Size = new System.Drawing.Size(87, 43);
            this.btnLogClear.TabIndex = 38;
            this.btnLogClear.Text = "LogClear";
            this.btnLogClear.UseVisualStyleBackColor = true;
            // 
            // btnLoadJson
            // 
            this.btnLoadJson.Location = new System.Drawing.Point(870, 12);
            this.btnLoadJson.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.btnLoadJson.Name = "btnLoadJson";
            this.btnLoadJson.Size = new System.Drawing.Size(151, 81);
            this.btnLoadJson.TabIndex = 37;
            this.btnLoadJson.Text = "Json파일 가져오기";
            this.btnLoadJson.UseVisualStyleBackColor = true;
            // 
            // rtbLog
            // 
            this.rtbLog.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(15)))), ((int)(((byte)(15)))), ((int)(((byte)(25)))));
            this.rtbLog.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(180)))), ((int)(((byte)(230)))), ((int)(((byte)(180)))));
            this.rtbLog.Location = new System.Drawing.Point(259, 103);
            this.rtbLog.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.rtbLog.Name = "rtbLog";
            this.rtbLog.Size = new System.Drawing.Size(1022, 592);
            this.rtbLog.TabIndex = 36;
            this.rtbLog.Text = "";
            // 
            // btnOff
            // 
            this.btnOff.Location = new System.Drawing.Point(169, 47);
            this.btnOff.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.btnOff.Name = "btnOff";
            this.btnOff.Size = new System.Drawing.Size(154, 46);
            this.btnOff.TabIndex = 35;
            this.btnOff.Text = "Off";
            this.btnOff.UseVisualStyleBackColor = true;
            // 
            // btnOn
            // 
            this.btnOn.Location = new System.Drawing.Point(9, 47);
            this.btnOn.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.btnOn.Name = "btnOn";
            this.btnOn.Size = new System.Drawing.Size(154, 46);
            this.btnOn.TabIndex = 34;
            this.btnOn.Text = "On";
            this.btnOn.UseVisualStyleBackColor = true;
            // 
            // btnDisconnect
            // 
            this.btnDisconnect.Location = new System.Drawing.Point(169, 10);
            this.btnDisconnect.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.btnDisconnect.Name = "btnDisconnect";
            this.btnDisconnect.Size = new System.Drawing.Size(154, 25);
            this.btnDisconnect.TabIndex = 33;
            this.btnDisconnect.Text = "연결해제";
            this.btnDisconnect.UseVisualStyleBackColor = true;
            // 
            // btnConnect
            // 
            this.btnConnect.Location = new System.Drawing.Point(9, 10);
            this.btnConnect.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.btnConnect.Name = "btnConnect";
            this.btnConnect.Size = new System.Drawing.Size(154, 25);
            this.btnConnect.TabIndex = 32;
            this.btnConnect.Text = "연결";
            this.btnConnect.UseVisualStyleBackColor = true;
            // 
            // frmPrinterUsb
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1293, 705);
            this.Controls.Add(this.btnRestart);
            this.Controls.Add(this.btnInstallDriver);
            this.Controls.Add(this.btnGwReset);
            this.Controls.Add(this.btnLogCopy);
            this.Controls.Add(this.lstvNode);
            this.Controls.Add(this.btnLogClear);
            this.Controls.Add(this.btnLoadJson);
            this.Controls.Add(this.rtbLog);
            this.Controls.Add(this.btnOff);
            this.Controls.Add(this.btnOn);
            this.Controls.Add(this.btnDisconnect);
            this.Controls.Add(this.btnConnect);
            this.Name = "frmPrinterUsb";
            this.Text = "frmPrinterUsb";
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Button btnRestart;
        private System.Windows.Forms.Button btnInstallDriver;
        private System.Windows.Forms.Button btnGwReset;
        private System.Windows.Forms.Button btnLogCopy;
        private System.Windows.Forms.ListView lstvNode;
        private System.Windows.Forms.Button btnLogClear;
        private System.Windows.Forms.Button btnLoadJson;
        private System.Windows.Forms.RichTextBox rtbLog;
        private System.Windows.Forms.Button btnOff;
        private System.Windows.Forms.Button btnOn;
        private System.Windows.Forms.Button btnDisconnect;
        private System.Windows.Forms.Button btnConnect;
    }
}