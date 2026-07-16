namespace Sledge.BspEditor.Editing.Components.Compile
{
    partial class BuildParametersPanel
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

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.txtPreviewText = new System.Windows.Forms.TextBox();
            this.SuspendLayout();
            // 
            // txtPreviewText
            // 
            this.txtPreviewText.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtPreviewText.Location = new System.Drawing.Point(0, 0);
            this.txtPreviewText.Multiline = true;
            this.txtPreviewText.Name = "txtPreviewText";
            this.txtPreviewText.ReadOnly = false;
            this.txtPreviewText.Size = new System.Drawing.Size(541, 407);
            this.txtPreviewText.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtPreviewText.TabIndex = 1;
            this.txtPreviewText.Text = "one line\r\ntwo line\r\nthree line";
            // 
            // BuildParametersPanel
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.txtPreviewText);
            this.Name = "BuildParametersPanel";
            this.Size = new System.Drawing.Size(541, 407);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TextBox txtPreviewText;
    }
}
