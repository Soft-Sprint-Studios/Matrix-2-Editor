namespace Sledge.BspEditor.Editing.Components.Properties.Tabs
{
    public sealed partial class FlagsTab
    {
        private void InitializeComponent()
        {
            this.FlagsTable = new System.Windows.Forms.CheckedListBox();
            this.ClearAllButton = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // FlagsTable
            // 
            this.FlagsTable.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.FlagsTable.CheckOnClick = true;
            this.FlagsTable.FormattingEnabled = true;
            this.FlagsTable.IntegralHeight = false;
            this.FlagsTable.Location = new System.Drawing.Point(3, 3);
            this.FlagsTable.Name = "FlagsTable";
            this.FlagsTable.Size = new System.Drawing.Size(673, 335);
            this.FlagsTable.TabIndex = 1;
            this.FlagsTable.ItemCheck += new System.Windows.Forms.ItemCheckEventHandler(this.FlagsTableChanged);
            // 
            // ClearAllButton
            // 
            this.ClearAllButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.ClearAllButton.Location = new System.Drawing.Point(3, 344);
            this.ClearAllButton.Name = "ClearAllButton";
            this.ClearAllButton.Size = new System.Drawing.Size(120, 25);
            this.ClearAllButton.TabIndex = 2;
            this.ClearAllButton.Text = "Clear All";
            this.ClearAllButton.UseVisualStyleBackColor = true;
            this.ClearAllButton.Click += new System.EventHandler(this.ClearAllButtonClicked);
            // 
            // FlagsTab
            // 
            this.BackColor = System.Drawing.SystemColors.ControlLightLight;
            this.Controls.Add(this.ClearAllButton);
            this.Controls.Add(this.FlagsTable);
            this.Name = "FlagsTab";
            this.Size = new System.Drawing.Size(679, 378);
            this.ResumeLayout(false);

        }

        private System.Windows.Forms.CheckedListBox FlagsTable;
        private System.Windows.Forms.Button ClearAllButton;
    }
}
