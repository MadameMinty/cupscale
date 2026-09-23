namespace Cupscale.Forms
{
    partial class ModelMultiSelectForm
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
            this.modelTree = new System.Windows.Forms.TreeView();
            this.tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
            this.tableLayoutPanel2 = new System.Windows.Forms.TableLayoutPanel();
            this.confirmBtn = new HTAlt.WinForms.HTButton();
            this.allBtn = new HTAlt.WinForms.HTButton();
            this.noneBtn = new HTAlt.WinForms.HTButton();
            this.cancelBtn = new HTAlt.WinForms.HTButton();
            this.tableLayoutPanel1.SuspendLayout();
            this.tableLayoutPanel2.SuspendLayout();
            this.SuspendLayout();
            //
            // modelTree
            //
            this.modelTree.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(48)))), ((int)(((byte)(48)))), ((int)(((byte)(48)))));
            this.modelTree.CheckBoxes = true;
            this.modelTree.Dock = System.Windows.Forms.DockStyle.Fill;
            this.modelTree.ForeColor = System.Drawing.Color.White;
            this.modelTree.Location = new System.Drawing.Point(6, 6);
            this.modelTree.Margin = new System.Windows.Forms.Padding(6);
            this.modelTree.Name = "modelTree";
            this.modelTree.Size = new System.Drawing.Size(708, 658);
            this.modelTree.TabIndex = 0;
            this.modelTree.AfterCheck += new System.Windows.Forms.TreeViewEventHandler(this.modelTree_AfterCheck);
            //
            // tableLayoutPanel1
            //
            this.tableLayoutPanel1.ColumnCount = 1;
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanel1.Controls.Add(this.modelTree, 0, 0);
            this.tableLayoutPanel1.Controls.Add(this.tableLayoutPanel2, 0, 1);
            this.tableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tableLayoutPanel1.Location = new System.Drawing.Point(0, 0);
            this.tableLayoutPanel1.Name = "tableLayoutPanel1";
            this.tableLayoutPanel1.RowCount = 2;
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 50F));
            this.tableLayoutPanel1.Size = new System.Drawing.Size(720, 720);
            this.tableLayoutPanel1.TabIndex = 1;
            //
            // tableLayoutPanel2
            //
            this.tableLayoutPanel2.ColumnCount = 4;
            this.tableLayoutPanel2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.tableLayoutPanel2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.tableLayoutPanel2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.tableLayoutPanel2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.tableLayoutPanel2.Controls.Add(this.confirmBtn, 0, 0);
            this.tableLayoutPanel2.Controls.Add(this.allBtn, 1, 0);
            this.tableLayoutPanel2.Controls.Add(this.noneBtn, 2, 0);
            this.tableLayoutPanel2.Controls.Add(this.cancelBtn, 3, 0);
            this.tableLayoutPanel2.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tableLayoutPanel2.Location = new System.Drawing.Point(3, 673);
            this.tableLayoutPanel2.Name = "tableLayoutPanel2";
            this.tableLayoutPanel2.RowCount = 1;
            this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanel2.Size = new System.Drawing.Size(714, 44);
            this.tableLayoutPanel2.TabIndex = 15;
            //
            // confirmBtn
            //
            this.confirmBtn.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.confirmBtn.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            this.confirmBtn.FlatAppearance.BorderSize = 0;
            this.confirmBtn.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.confirmBtn.ForeColor = System.Drawing.Color.White;
            this.confirmBtn.Margin = new System.Windows.Forms.Padding(12, 6, 12, 6);
            this.confirmBtn.Name = "confirmBtn";
            this.confirmBtn.Size = new System.Drawing.Size(154, 32);
            this.confirmBtn.TabIndex = 12;
            this.confirmBtn.Text = "OK";
            this.confirmBtn.UseVisualStyleBackColor = false;
            this.confirmBtn.Click += new System.EventHandler(this.confirmBtn_Click);
            //
            // allBtn
            //
            this.allBtn.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.allBtn.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            this.allBtn.FlatAppearance.BorderSize = 0;
            this.allBtn.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.allBtn.ForeColor = System.Drawing.Color.White;
            this.allBtn.Margin = new System.Windows.Forms.Padding(12, 6, 12, 6);
            this.allBtn.Name = "allBtn";
            this.allBtn.Size = new System.Drawing.Size(154, 32);
            this.allBtn.TabIndex = 13;
            this.allBtn.Text = "All";
            this.allBtn.UseVisualStyleBackColor = false;
            this.allBtn.Click += new System.EventHandler(this.allBtn_Click);
            //
            // noneBtn
            //
            this.noneBtn.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.noneBtn.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            this.noneBtn.FlatAppearance.BorderSize = 0;
            this.noneBtn.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.noneBtn.ForeColor = System.Drawing.Color.White;
            this.noneBtn.Margin = new System.Windows.Forms.Padding(12, 6, 12, 6);
            this.noneBtn.Name = "noneBtn";
            this.noneBtn.Size = new System.Drawing.Size(154, 32);
            this.noneBtn.TabIndex = 14;
            this.noneBtn.Text = "None";
            this.noneBtn.UseVisualStyleBackColor = false;
            this.noneBtn.Click += new System.EventHandler(this.noneBtn_Click);
            //
            // cancelBtn
            //
            this.cancelBtn.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.cancelBtn.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            this.cancelBtn.FlatAppearance.BorderSize = 0;
            this.cancelBtn.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.cancelBtn.ForeColor = System.Drawing.Color.White;
            this.cancelBtn.Margin = new System.Windows.Forms.Padding(12, 6, 12, 6);
            this.cancelBtn.Name = "cancelBtn";
            this.cancelBtn.Size = new System.Drawing.Size(156, 32);
            this.cancelBtn.TabIndex = 15;
            this.cancelBtn.Text = "Cancel";
            this.cancelBtn.UseVisualStyleBackColor = false;
            this.cancelBtn.Click += new System.EventHandler(this.cancelBtn_Click);
            //
            // ModelMultiSelectForm
            //
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(32)))), ((int)(((byte)(32)))), ((int)(((byte)(32)))));
            this.ClientSize = new System.Drawing.Size(720, 720);
            this.Controls.Add(this.tableLayoutPanel1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.KeyPreview = true;
            this.Name = "ModelMultiSelectForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "ModelMultiSelectForm";
            this.Load += new System.EventHandler(this.ModelMultiSelectForm_Load);
            this.KeyPress += new System.Windows.Forms.KeyPressEventHandler(this.ModelMultiSelectForm_KeyPress);
            this.tableLayoutPanel1.ResumeLayout(false);
            this.tableLayoutPanel2.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.TreeView modelTree;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel2;
        private HTAlt.WinForms.HTButton confirmBtn;
        private HTAlt.WinForms.HTButton allBtn;
        private HTAlt.WinForms.HTButton noneBtn;
        private HTAlt.WinForms.HTButton cancelBtn;
    }
}
