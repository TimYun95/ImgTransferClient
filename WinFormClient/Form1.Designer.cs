namespace WinFormClient
{
    partial class Form1
    {
        /// <summary>
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.IBShow = new Emgu.CV.UI.ImageBox();
            this.beginBtn = new System.Windows.Forms.Button();
            this.stopBtn = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.IBShow)).BeginInit();
            this.SuspendLayout();
            // 
            // IBShow
            // 
            this.IBShow.FunctionalMode = Emgu.CV.UI.ImageBox.FunctionalModeOption.Minimum;
            this.IBShow.Location = new System.Drawing.Point(12, 12);
            this.IBShow.Name = "IBShow";
            this.IBShow.Size = new System.Drawing.Size(643, 497);
            this.IBShow.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.IBShow.TabIndex = 4;
            this.IBShow.TabStop = false;
            // 
            // beginBtn
            // 
            this.beginBtn.Font = new System.Drawing.Font("微软雅黑", 15F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.beginBtn.Location = new System.Drawing.Point(661, 12);
            this.beginBtn.Name = "beginBtn";
            this.beginBtn.Size = new System.Drawing.Size(165, 45);
            this.beginBtn.TabIndex = 5;
            this.beginBtn.Text = "Begin";
            this.beginBtn.UseVisualStyleBackColor = true;
            this.beginBtn.Click += new System.EventHandler(this.beginBtn_Click);
            // 
            // stopBtn
            // 
            this.stopBtn.Font = new System.Drawing.Font("微软雅黑", 15F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.stopBtn.Location = new System.Drawing.Point(661, 63);
            this.stopBtn.Name = "stopBtn";
            this.stopBtn.Size = new System.Drawing.Size(165, 45);
            this.stopBtn.TabIndex = 7;
            this.stopBtn.Text = "Stop";
            this.stopBtn.UseVisualStyleBackColor = true;
            this.stopBtn.Click += new System.EventHandler(this.stopBtn_Click);
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(838, 521);
            this.Controls.Add(this.stopBtn);
            this.Controls.Add(this.beginBtn);
            this.Controls.Add(this.IBShow);
            this.Name = "Form1";
            this.Text = "Form1";
            ((System.ComponentModel.ISupportInitialize)(this.IBShow)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private Emgu.CV.UI.ImageBox IBShow;
        private System.Windows.Forms.Button beginBtn;
        private System.Windows.Forms.Button stopBtn;
    }
}

