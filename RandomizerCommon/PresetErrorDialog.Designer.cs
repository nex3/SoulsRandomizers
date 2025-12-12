namespace RandomizerCommon
{
    partial class PresetErrorDialog
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
            errorMessage = new System.Windows.Forms.Label();
            yaml = new System.Windows.Forms.RichTextBox();
            button1 = new System.Windows.Forms.Button();
            panel2 = new System.Windows.Forms.Panel();
            panel1 = new System.Windows.Forms.Panel();
            panel2.SuspendLayout();
            panel1.SuspendLayout();
            SuspendLayout();
            // 
            // errorMessage
            // 
            errorMessage.Dock = System.Windows.Forms.DockStyle.Fill;
            errorMessage.Location = new System.Drawing.Point(0, 0);
            errorMessage.Name = "errorMessage";
            errorMessage.Size = new System.Drawing.Size(1109, 78);
            errorMessage.TabIndex = 0;
            errorMessage.Text = "label1";
            // 
            // yaml
            // 
            yaml.Dock = System.Windows.Forms.DockStyle.Fill;
            yaml.Font = new System.Drawing.Font("Consolas", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            yaml.Location = new System.Drawing.Point(0, 0);
            yaml.Name = "yaml";
            yaml.ReadOnly = true;
            yaml.Size = new System.Drawing.Size(1109, 542);
            yaml.TabIndex = 1;
            yaml.Text = "";
            // 
            // button1
            // 
            button1.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            button1.DialogResult = System.Windows.Forms.DialogResult.OK;
            button1.Location = new System.Drawing.Point(950, 634);
            button1.Name = "button1";
            button1.Size = new System.Drawing.Size(150, 46);
            button1.TabIndex = 2;
            button1.Text = "Ok";
            button1.UseVisualStyleBackColor = true;
            // 
            // panel2
            // 
            panel2.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            panel2.Controls.Add(yaml);
            panel2.Location = new System.Drawing.Point(0, 78);
            panel2.Name = "panel2";
            panel2.Size = new System.Drawing.Size(1109, 542);
            panel2.TabIndex = 4;
            // 
            // panel1
            // 
            panel1.Controls.Add(errorMessage);
            panel1.Location = new System.Drawing.Point(0, 2);
            panel1.Name = "panel1";
            panel1.Size = new System.Drawing.Size(1109, 78);
            panel1.TabIndex = 2;
            // 
            // PresetErrorDialog
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(13F, 32F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(1112, 692);
            Controls.Add(panel1);
            Controls.Add(button1);
            Controls.Add(panel2);
            Name = "PresetErrorDialog";
            Text = "Failed to parse enemy preset";
            Load += PresetErrorDialog_Load;
            panel2.ResumeLayout(false);
            panel1.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion
        private System.Windows.Forms.Label errorMessage;
        private System.Windows.Forms.RichTextBox yaml;
        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.Panel panel2;
        private System.Windows.Forms.Panel panel1;
    }
}